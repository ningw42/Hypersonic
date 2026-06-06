using System.Text.Json;

using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Helpers;

namespace Hypersonic.Services.Cache;

/// <summary>
/// Drives declarative payload-folder migration for any
/// <see cref="SqliteCacheStoreBase{TEntry}"/>, symmetric with
/// <see cref="SqliteCacheSchemaDriver"/> for schema. Runs before the SQLite
/// connection opens (the DB lives inside the folder being moved) and before the
/// per-store payload folder is created.
///
/// <para>
/// State store is a per-entity JSON marker (<c>&lt;entity&gt;.json</c>) at the cache
/// parent. The driver moves the whole on-disk directory as one unit via
/// <c>Directory.Move</c>, then bumps the marker. Returns <c>false</c> only when it
/// must fail the init attempt (a <c>Directory.Move</c> threw, per-step preflight
/// detected an existing downstream subfolder, or a pre-final-target conflict was
/// hit) — cases that would otherwise strand cached data while the store opens a
/// different folder. Everything else (identity steps, fresh-install no-ops,
/// "already migrated externally" recovery, marker-write failures, final-target
/// conflict warnings) returns <c>true</c>.
/// </para>
/// </summary>
internal static class SqliteCacheFolderMigrator
{
    public static async Task<bool> EnsurePayloadFolderAsync(
        IAppFolderLocator folderLocator,
        string payloadSubfolderName,
        string metadataFileName,
        IReadOnlyList<PayloadFolderMigrationStep> steps,
        ILogger log,
        CancellationToken cancellationToken)
    {
        if (steps.Count == 0)
        {
            // Greenfield: no folder migration, no marker file, no on-disk cost.
            return true;
        }

        // Validate declarations on every init — a malformed step list or unsafe leaf name is a
        // programming error in the subclass and must surface loudly, not silently mis-migrate.
        ValidateLeafName(metadataFileName, nameof(metadataFileName));
        ValidateSteps(payloadSubfolderName, steps);

        cancellationToken.ThrowIfCancellationRequested();

        var parent = folderLocator.GetCacheFolder();

        // The unpackaged parent (%LOCALAPPDATA%\Hypersonic\Cache) may not exist on first
        // launch, and we run before the per-store payload folder is created. Creating the
        // parent does not mask the migration signal — the state machine checks subfolders.
        // A failure here (a file occupies the parent path, ACLs deny access) is handled under
        // the same failure contract as a Directory.Move failure: fail this init attempt so
        // callers keep their cache-unavailable safe defaults rather than seeing the I/O throw.
        try
        {
            Directory.CreateDirectory(parent);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Warning(
                "Cache folder migration could not create the cache parent; failing this init attempt",
                new { Parent = parent },
                ex);
            return false;
        }

        var orderedSteps = steps.OrderBy(step => step.TargetVersion).ToArray();
        var markerPath = Path.Combine(parent, metadataFileName);
        var currentVersion = await ReadMarkerVersionAsync(markerPath, cancellationToken).ConfigureAwait(false);

        var highestVersion = orderedSteps[^1].TargetVersion;
        if (currentVersion > highestVersion)
        {
            // The marker claims a version beyond every declared step — a downgrade after a newer
            // version migrated further, or a corrupt/tampered marker. Trusting it would skip the
            // state table and could strand legacy data in an unmigrated source while the store
            // opens an empty target. Treat the marker as untrustworthy (same posture as an
            // unparseable one): degrade to 0 and re-evaluate. The state table is idempotent and
            // self-heals — already-migrated-externally and move branches reconcile the real
            // on-disk state — and the marker is rewritten down to the highest declared version.
            log.Warning(
                "Cache folder migration marker is ahead of the highest declared step; re-evaluating from scratch",
                new { MarkerPath = markerPath, MarkerVersion = currentVersion, HighestDeclaredVersion = highestVersion });
            currentVersion = 0;
        }

        for (var i = 0; i < orderedSteps.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var step = orderedSteps[i];
            if (step.TargetVersion <= currentVersion)
            {
                continue;
            }

            var source = Path.Combine(parent, step.SourceSubfolder);
            var target = Path.Combine(parent, step.TargetSubfolder);

            // Identity step (subclass declared source == target): nothing to move.
            if (string.Equals(step.SourceSubfolder, step.TargetSubfolder, StringComparison.OrdinalIgnoreCase))
            {
                currentVersion = await BumpMarkerAsync(markerPath, step.TargetVersion, log, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var sourceExists = Directory.Exists(source);
            var targetExists = Directory.Exists(target);

            if (!sourceExists)
            {
                // Fresh install (target absent) or already migrated externally / marker lost
                // (target present): nothing to move, just advance the marker.
                currentVersion = await BumpMarkerAsync(markerPath, step.TargetVersion, log, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (!targetExists)
            {
                // Per-step preflight: if a strictly-later step's target already exists, completing
                // this move would advance the marker into a downstream folder that already exists,
                // stranding the freshly-moved data. Abort the whole init attempt instead.
                var blockingDownstream = FindExistingDownstreamTargets(parent, orderedSteps, i);
                if (blockingDownstream.Count > 0)
                {
                    log.Warning(
                        "Cache folder migration aborted: ambiguous chain state — downstream target folder(s) already exist",
                        new { Parent = parent, Source = source, Target = target, ExistingDownstream = blockingDownstream });
                    return false;
                }

                // Directory.Move itself can't be interrupted mid-flight, but bail before starting it.
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    Directory.Move(source, target);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Leave the marker unchanged and the target uncreated; fail this init attempt.
                    // The next cache call (or next launch) re-runs the migrator and may succeed.
                    log.Warning(
                        "Cache folder migration move failed; leaving source intact and failing this init attempt",
                        new { Parent = parent, Source = source, Target = target },
                        ex);
                    return false;
                }

                currentVersion = await BumpMarkerAsync(markerPath, step.TargetVersion, log, cancellationToken).ConfigureAwait(false);
                continue;
            }

            // sourceExists && targetExists: a conflict.
            if (string.Equals(step.TargetSubfolder, payloadSubfolderName, StringComparison.OrdinalIgnoreCase))
            {
                // Conflict on the final target is recoverable: the store opens the existing final
                // folder and serves whatever lives there; the warning re-appears until the operator
                // removes the stale source. Do not touch either folder, do not bump the marker.
                log.Warning(
                    "Cache folder migration conflict on the final target; leaving both folders untouched and opening the existing target",
                    new { Parent = parent, Source = source, Target = target });
                return true;
            }

            // Conflict on a pre-final target: letting init continue would create/open the still-empty
            // final folder, stranding data in both the conflicted source and intermediate target.
            // Fail this init attempt and leave the source intact for the operator to resolve.
            log.Warning(
                "Cache folder migration conflict on a pre-final target; failing this init attempt and leaving folders intact",
                new { Parent = parent, Source = source, Target = target });
            return false;
        }

        return true;
    }

    /// <summary>
    /// Validates that <paramref name="value"/> is a safe single path segment: non-empty, no
    /// embedded separators, not rooted, not <c>"."</c>/<c>".."</c>, and free of platform-invalid
    /// filename characters. Subclasses declare leaf names only — this blocks typos like
    /// <c>"..\\Images"</c>, <c>"."</c>, or a rooted path from escaping the cache parent. Shared
    /// with <see cref="SqliteCacheStoreBase{TEntry}"/> for <c>PayloadSubfolderName</c>.
    /// </summary>
    internal static void ValidateLeafName(string value, string paramName)
    {
        if (string.IsNullOrEmpty(value)
            || Path.GetFileName(value) != value
            || Path.IsPathRooted(value)
            || value == "."
            || value == ".."
            || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException(
                $"'{paramName}' must be a safe leaf name (no path separators, not rooted, not '.' or '..', "
                    + $"no invalid filename characters). Got: '{value}'.",
                paramName);
        }
    }

    /// <summary>
    /// Validates the declared step list: positive and unique <c>TargetVersion</c>s, safe leaf
    /// names, and — for non-empty lists — final-target match, chain continuity, and final-target
    /// uniqueness. Empty and single-step lists trivially satisfy the structural rules.
    /// </summary>
    internal static void ValidateSteps(string payloadSubfolderName, IReadOnlyList<PayloadFolderMigrationStep> steps)
    {
        if (steps.Count == 0)
        {
            return;
        }

        var seenVersions = new HashSet<int>();
        foreach (var step in steps)
        {
            if (step.TargetVersion <= 0)
            {
                throw new ArgumentException(
                    $"Migration step TargetVersion must be positive; got {step.TargetVersion}.",
                    nameof(steps));
            }

            if (!seenVersions.Add(step.TargetVersion))
            {
                throw new ArgumentException(
                    $"Migration steps must have unique TargetVersion values; {step.TargetVersion} is duplicated.",
                    nameof(steps));
            }

            ValidateLeafName(step.SourceSubfolder, nameof(step.SourceSubfolder));
            ValidateLeafName(step.TargetSubfolder, nameof(step.TargetSubfolder));
        }

        var ordered = steps.OrderBy(step => step.TargetVersion).ToArray();

        var highest = ordered[^1];
        if (!string.Equals(highest.TargetSubfolder, payloadSubfolderName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"The highest-version migration step must target the current payload subfolder "
                    + $"'{payloadSubfolderName}'; got '{highest.TargetSubfolder}'.",
                nameof(steps));
        }

        for (var i = 1; i < ordered.Length; i++)
        {
            if (!string.Equals(ordered[i].SourceSubfolder, ordered[i - 1].TargetSubfolder, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Migration step chain is discontinuous: step v{ordered[i].TargetVersion} source "
                        + $"'{ordered[i].SourceSubfolder}' must equal the previous step's target "
                        + $"'{ordered[i - 1].TargetSubfolder}'.",
                    nameof(steps));
            }
        }

        for (var i = 0; i < ordered.Length - 1; i++)
        {
            if (string.Equals(ordered[i].TargetSubfolder, payloadSubfolderName, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Only the highest-version migration step may target the current payload subfolder "
                        + $"'{payloadSubfolderName}'; step v{ordered[i].TargetVersion} also targets it.",
                    nameof(steps));
            }
        }
    }

    private static List<string> FindExistingDownstreamTargets(
        string parent,
        PayloadFolderMigrationStep[] orderedSteps,
        int currentIndex)
    {
        // orderedSteps is sorted ascending by TargetVersion (validated unique), so every index
        // after currentIndex is strictly later in the chain.
        var existing = new List<string>();
        for (var j = currentIndex + 1; j < orderedSteps.Length; j++)
        {
            var downstream = Path.Combine(parent, orderedSteps[j].TargetSubfolder);
            if (Directory.Exists(downstream))
            {
                existing.Add(downstream);
            }
        }

        return existing;
    }

    private static async Task<int> ReadMarkerVersionAsync(string markerPath, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(markerPath))
            {
                return 0;
            }

            var text = await File.ReadAllTextAsync(markerPath, cancellationToken).ConfigureAwait(false);
            var metadata = JsonSerializer.Deserialize<PayloadFolderMetadata>(text, Json.Options);
            return metadata?.FolderVersion ?? 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Missing, unreadable, unparseable, or field-less all degrade to "haven't migrated
            // yet" — the safe default for the state machine.
            return 0;
        }
    }

    private static async Task<int> BumpMarkerAsync(string markerPath, int version, ILogger log, CancellationToken cancellationToken)
    {
        var tempPath = markerPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(new PayloadFolderMetadata(version), Json.Options);
            await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, markerPath, overwrite: true);
        }
        catch (OperationCanceledException)
        {
            TryDeleteFile(tempPath);
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A failed marker write does not strand data: the move (if any) already completed,
            // and the next launch re-evaluates the state table and recovers without re-moving.
            log.Warning(
                "Cache folder migration marker write failed; next launch will re-evaluate",
                new { MarkerPath = markerPath, Version = version },
                ex);
            TryDeleteFile(tempPath);
        }

        // Advance the in-memory version regardless: the on-disk move (if any) already happened.
        return version;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort.
        }
    }
}
