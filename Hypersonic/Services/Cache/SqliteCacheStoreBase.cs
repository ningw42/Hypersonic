using Microsoft.Data.Sqlite;

using Hypersonic.Core.Contracts.Services;

namespace Hypersonic.Services.Cache;

/// <summary>
/// Generic per-entity SQLite cache base. Owns connection lifecycle, schema migration,
/// self-heal on corruption, a single non-reentrant gate, dispose drain, and the
/// payload-file lifecycle (atomic write+upsert, eviction, reconcile, negative-cache).
///
/// Each entity (image/lyrics/audio) ships its own subclass that supplies the schema
/// SQL, column mapping, entity-specific UPSERT/select queries, and the
/// <c>ResolvePayloadFolder</c> override for path resolution.
///
/// Locking convention: every subclass DB method comes in two forms.
/// <list type="bullet">
///   <item><c>Foo*Async</c> — acquires the gate, ensures init, delegates to <c>Foo*LockedAsync</c>.
///     Used by service-layer callers. Catches <see cref="SqliteException"/> to preserve
///     "DB hiccup → safe default" semantics.</item>
///   <item><c>Foo*LockedAsync</c> — assumes the gate is held; takes the open
///     <see cref="SqliteConnection"/> the base already validated. Used by the base's
///     payload-lifecycle methods. Throws on DB failure so the base can decide per
///     call-site recovery.</item>
/// </list>
/// </summary>
public abstract class SqliteCacheStoreBase<TEntry> : IDisposable
    where TEntry : ICacheEntry
{
    internal const string DatabaseFileName = "cache.db";

    private const int ConnectionDefaultTimeoutSeconds = 2;
    private static readonly TimeSpan DisposeDrainTimeout = TimeSpan.FromSeconds(5);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly ILogger _log;
    private readonly CacheOptionsBase _options;

    private string? _cacheFolderPath;
    private string? _databasePath;
    private SqliteConnection? _connection;
    private bool _initialized;
    private bool _disposed;
    private int _inFlightStreams;

    protected SqliteCacheStoreBase(CacheOptionsBase options, ILoggingService logService)
    {
        _options = options;
        _log = logService.GetLogger(GetType().Name);
    }

    /// <summary>
    /// Gets the resolved payload folder for this cache entity.
    /// </summary>
    public string CacheFolderPath => EnsureCacheFolderPath();

    /// <summary>The single SQLite table backing this cache (e.g. <c>"Entries"</c>).</summary>
    protected abstract string TableName { get; }

    /// <summary>
    /// Resolves the full path of the per-entity payload folder. Subclass owns this so
    /// existing entities (image) can preserve their MSIX-vs-unpackaged path mapping
    /// verbatim while greenfield entities use a uniform layout.
    /// </summary>
    protected abstract string ResolvePayloadFolder();

    /// <summary>Current schema version emitted into <c>PRAGMA user_version</c>.</summary>
    protected abstract int CurrentSchemaVersion { get; }

    /// <summary>
    /// Ordered delta migrations applied on top of <c>v1</c>. Each step targets a
    /// version greater than 1 and contains the SQL needed to advance from
    /// <c>step.TargetVersion - 1</c> to <c>step.TargetVersion</c>. Empty list means
    /// the current schema is <c>v1</c> and no deltas exist yet.
    ///
    /// The v0→v1 path is special-cased: the base runs <see cref="CreateCurrentSchemaSql"/>
    /// instead, because greenfield <c>v1</c> entities would otherwise duplicate the
    /// full schema in both this property and <see cref="CreateCurrentSchemaSql"/>.
    /// </summary>
    protected abstract IReadOnlyList<MigrationStep> MigrationSteps { get; }

    /// <summary>
    /// The complete current schema: <c>CREATE TABLE</c> + every index + every trigger.
    /// Used by the v0→current shortcut and by the future-version rebuild path.
    /// Must always represent the latest schema; do not split table and indexes across
    /// separate properties.
    /// </summary>
    protected abstract string CreateCurrentSchemaSql { get; }

    /// <summary>
    /// Filename predicate used by <see cref="ClearAsync"/> and <c>ReconcileAsync</c>.
    /// Must exclude in-flight streamed-write temp files (<c>&lt;hex&gt;.&lt;guid&gt;.tmp</c>).
    /// </summary>
    protected abstract bool IsKnownPayloadFileName(string fileName);

    /// <summary>
    /// Sums <c>Length</c> and counts rows. Returns <c>(0, 0)</c> on SQLite failure so
    /// callers (Settings UI, post-write eviction) never crash on cache flakiness.
    /// </summary>
    public async Task<CacheStoreStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connection = await EnsureInitializedLockedAsync(cancellationToken).ConfigureAwait(false);
            if (connection is null)
            {
                return new CacheStoreStats(0, 0);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COALESCE(SUM(Length), 0), COUNT(*) FROM {TableName};";

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return new CacheStoreStats(0, 0);
            }

            var totalBytes = reader.IsDBNull(0) ? 0L : reader.GetInt64(0);
            var count = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
            return new CacheStoreStats(totalBytes, count);
        }
        catch (SqliteException ex)
        {
            LogWarning("Cache GetStatsAsync failed; returning zero stats", ex);
            return new CacheStoreStats(0, 0);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Reads a single entry by key hash. Returns <c>null</c> on miss; also returns
    /// <c>null</c> on SQLite failure (warn-and-default contract).
    /// </summary>
    public async Task<TEntry?> GetEntryAsync(string keyHash, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connection = await EnsureInitializedLockedAsync(cancellationToken).ConfigureAwait(false);
            if (connection is null)
            {
                return default;
            }

            return await GetEntryLockedAsync(connection, keyHash, cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException ex)
        {
            LogWarning("Cache GetEntryAsync failed; returning miss", ex);
            return default;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Bumps <c>LastAccessUtc</c> on an existing row. No-op on miss; warn-and-noop on SQLite failure.</summary>
    public async Task TouchEntryAsync(string keyHash, DateTimeOffset lastAccessUtc, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connection = await EnsureInitializedLockedAsync(cancellationToken).ConfigureAwait(false);
            if (connection is null)
            {
                return;
            }

            await TouchEntryLockedAsync(connection, keyHash, lastAccessUtc, cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException ex)
        {
            LogWarning("Cache TouchEntryAsync failed; ignoring", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Inserts or replaces an entry. Warn-and-noop on SQLite failure (today's image cache
    /// already treats UPSERT failure as "row may be orphaned until next reconcile").
    /// </summary>
    public async Task UpsertEntryAsync(string keyHash, TEntry entry, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connection = await EnsureInitializedLockedAsync(cancellationToken).ConfigureAwait(false);
            if (connection is null)
            {
                return;
            }

            await UpsertEntryLockedAsync(connection, keyHash, entry, cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException ex)
        {
            LogWarning("Cache UpsertEntryAsync failed; entry may be orphaned until next reconcile", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Removes an entry by key hash. <paramref name="flushImmediately"/> is preserved
    /// for signature compatibility — SQLite commits each mutation, so there's nothing
    /// deferred to flush.
    /// </summary>
    public async Task RemoveEntryAsync(
        string keyHash,
        bool flushImmediately = false,
        CancellationToken cancellationToken = default)
    {
        _ = flushImmediately;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connection = await EnsureInitializedLockedAsync(cancellationToken).ConfigureAwait(false);
            if (connection is null)
            {
                return;
            }

            await RemoveEntryLockedAsync(connection, keyHash, cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException ex)
        {
            LogWarning("Cache RemoveEntryAsync failed; ignoring", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Returns LRU-ordered eviction candidates, optionally skipping a key the caller
    /// wants protected (typically the entry just written). Excludes negative-cache
    /// rows (empty <c>FileName</c>). Returns empty list on SQLite failure.
    /// </summary>
    public async Task<IReadOnlyList<(string KeyHash, TEntry Entry)>> GetEvictionCandidatesAsync(
        string? protectedKeyHash = null,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connection = await EnsureInitializedLockedAsync(cancellationToken).ConfigureAwait(false);
            if (connection is null)
            {
                return [];
            }

            return await GetEvictionCandidatesLockedAsync(connection, protectedKeyHash, cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException ex)
        {
            LogWarning("Cache GetEvictionCandidatesAsync failed; returning empty list", ex);
            return [];
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Returns every row — pinned, negative, ordinary — with no filtering. Used by
    /// <c>ReconcileAsync</c>; the base catches enumeration failures there and aborts
    /// the reconcile rather than treating a SQLite hiccup as "all files are orphans."
    /// This public wrapper preserves the warn-and-default contract for direct callers.
    /// </summary>
    public async Task<IReadOnlyList<(string KeyHash, TEntry Entry)>> EnumerateAllEntriesAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connection = await EnsureInitializedLockedAsync(cancellationToken).ConfigureAwait(false);
            if (connection is null)
            {
                return [];
            }

            return await EnumerateAllEntriesLockedAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException ex)
        {
            LogWarning("Cache EnumerateAllEntriesAsync failed; returning empty list", ex);
            return [];
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Deletes every row and every payload file matching <see cref="IsKnownPayloadFileName"/>
    /// in the payload folder. Non-matching files (user notes, sidecar artifacts) are
    /// left alone. Warn-and-noop on SQLite failure.
    /// </summary>
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connection = await EnsureInitializedLockedAsync(cancellationToken).ConfigureAwait(false);
            if (connection is null)
            {
                return;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = $"DELETE FROM {TableName};";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            if (_cacheFolderPath is not null && Directory.Exists(_cacheFolderPath))
            {
                foreach (var file in Directory.EnumerateFiles(_cacheFolderPath))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!IsKnownPayloadFileName(Path.GetFileName(file)))
                    {
                        continue;
                    }

                    TryDeleteFile(file);
                }
            }
        }
        catch (SqliteException ex)
        {
            LogWarning("Cache ClearAsync failed", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Forces a passive WAL checkpoint so the main DB file reflects all committed
    /// writes. Warn-and-noop on failure.
    /// </summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection is not { State: System.Data.ConnectionState.Open } connection)
            {
                return;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA wal_checkpoint(PASSIVE);";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException ex)
        {
            LogWarning("Cache FlushAsync (WAL checkpoint) failed", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Resolves a key hash to the on-disk URI of its payload file, if one exists right
    /// now. Returns <c>null</c> in three cases:
    /// <list type="bullet">
    ///   <item>No row matches the key hash.</item>
    ///   <item>The row exists but its payload file is missing — the orphaned row is
    ///     auto-removed before returning.</item>
    ///   <item>The row is a negative-cache entry (empty <c>FileName</c>). The row is
    ///     left in place; the service layer reads negative entries via
    ///     <see cref="GetEntryAsync"/>.</item>
    /// </list>
    /// <para>
    /// Does <b>not</b> bump <c>LastAccessUtc</c> and does <b>not</b> apply TTL — that
    /// is service-layer policy. The non-null return only proves "a payload file exists
    /// for this row right now," not "policy-allowed hit."
    /// </para>
    /// </summary>
    public async Task<Uri?> TryGetPayloadUriAsync(string keyHash, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connection = await EnsureInitializedLockedAsync(cancellationToken).ConfigureAwait(false);
            if (connection is null)
            {
                return null;
            }

            var entry = await GetEntryLockedAsync(connection, keyHash, cancellationToken).ConfigureAwait(false);
            if (entry is null)
            {
                return null;
            }

            if (string.IsNullOrEmpty(entry.FileName))
            {
                // Negative-cache entry — surfaced via GetEntryAsync, not as a payload URI.
                return null;
            }

            var fullPath = Path.Combine(_cacheFolderPath!, entry.FileName);
            if (!File.Exists(fullPath))
            {
                // Orphan-row cleanup. Don't propagate failure — best-effort.
                try
                {
                    await RemoveEntryLockedAsync(connection, keyHash, cancellationToken).ConfigureAwait(false);
                }
                catch (SqliteException ex)
                {
                    LogWarning("Cache orphan-row removal failed in TryGetPayloadUriAsync", ex);
                }

                return null;
            }

            return new Uri(fullPath);
        }
        catch (SqliteException ex)
        {
            LogWarning("Cache TryGetPayloadUriAsync failed; returning null", ex);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Buffered atomic write: temp file → move → delete prior file (if FileName changed)
    /// → UPSERT → post-write eviction.
    ///
    /// <para>
    /// Returns the final payload URI on success; returns <c>null</c> if the inner
    /// <see cref="UpsertEntryLockedAsync"/> throws <see cref="SqliteException"/> — the
    /// file is left on disk and will be swept by the next reconcile (per the design's
    /// "atomic-on-success, self-healing-on-failure" contract).
    /// </para>
    ///
    /// <para>
    /// File I/O failures (<see cref="IOException"/> / <see cref="UnauthorizedAccessException"/>)
    /// propagate so the service layer can react (e.g. fall back to stale).
    /// Cancellation and disposal propagate.
    /// </para>
    /// </summary>
    public async Task<Uri?> WritePayloadAsync(
        string keyHash,
        ReadOnlyMemory<byte> bytes,
        TEntry entry,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(entry.FileName))
        {
            throw new ArgumentException(
                "WritePayloadAsync requires entry.FileName. Use WriteNegativeAsync for negative-cache entries.",
                nameof(entry));
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? tempPath = null;
        try
        {
            var connection = await EnsureInitializedLockedAsync(cancellationToken).ConfigureAwait(false);
            if (connection is null)
            {
                return null;
            }

            TEntry? oldEntry;
            try
            {
                oldEntry = await GetEntryLockedAsync(connection, keyHash, cancellationToken).ConfigureAwait(false);
            }
            catch (SqliteException ex)
            {
                LogWarning("Cache WritePayloadAsync: prior-entry lookup failed; treating as miss", ex);
                oldEntry = default;
            }

            Directory.CreateDirectory(_cacheFolderPath!);

            var finalPath = Path.Combine(_cacheFolderPath!, entry.FileName);
            tempPath = Path.Combine(_cacheFolderPath!, $"{keyHash}.{Guid.NewGuid():N}.tmp");

            // 1. Temp write.
            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();

            // 2. Atomic move.
            File.Move(tempPath, finalPath, overwrite: true);
            tempPath = null;

            // 3. Cleanup of prior file when FileName changed.
            if (oldEntry is not null &&
                !string.IsNullOrEmpty(oldEntry.FileName) &&
                !string.Equals(oldEntry.FileName, entry.FileName, StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteFile(Path.Combine(_cacheFolderPath!, oldEntry.FileName));
            }

            // 4. UPSERT. If this throws, leave the new file on disk for reconcile to sweep.
            try
            {
                await UpsertEntryLockedAsync(connection, keyHash, entry, cancellationToken).ConfigureAwait(false);
            }
            catch (SqliteException ex)
            {
                LogWarning("Cache WritePayloadAsync UPSERT failed; new file will be reconciled later", ex);
                return null;
            }

            // 5. Post-write eviction.
            await EvictIfNeededLockedAsync(connection, protectedKeyHash: keyHash, cancellationToken).ConfigureAwait(false);

            return new Uri(finalPath);
        }
        catch (OperationCanceledException)
        {
            if (tempPath is not null)
            {
                TryDeleteFile(tempPath);
            }
            throw;
        }
        catch
        {
            if (tempPath is not null)
            {
                TryDeleteFile(tempPath);
            }
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Streamed atomic write — for payloads too large to buffer in memory (audio).
    ///
    /// <para>
    /// The writer runs against a temp file <b>outside</b> the gate so concurrent
    /// reads and small buffered writes can progress while the streamed payload is
    /// landing on disk. After the writer completes the base re-acquires the gate
    /// to do the atomic rename + UPSERT + post-write eviction.
    /// </para>
    ///
    /// <para>
    /// In-flight streamed writes are tracked via <c>_inFlightStreams</c> + a linked
    /// cancellation source so disposal can cancel them and wait for their temp
    /// files to be cleaned up before tearing down the connection. The
    /// <paramref name="entryFactory"/> is responsible for setting <c>FileName</c>
    /// on the returned entry — the base uses that as the rename target.
    /// </para>
    /// </summary>
    public async Task<Uri?> WriteStreamedPayloadAsync(
        string keyHash,
        Func<Stream, CancellationToken, Task> writer,
        Func<PayloadWriteContext, TEntry> entryFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(entryFactory);

        if (_disposed)
        {
            throw new ObjectDisposedException(GetType().Name);
        }

        // Warm-up init: must complete before we open a temp file, because the startup
        // temp-file sweep runs inside init and would otherwise delete a live writer's
        // temp out from under it. We take the gate briefly here, then release it so
        // the writer runs uncontended.
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var warmupConnection = await EnsureInitializedLockedAsync(cancellationToken).ConfigureAwait(false);
            if (warmupConnection is null)
            {
                return null;
            }
        }
        finally
        {
            _gate.Release();
        }

        Directory.CreateDirectory(_cacheFolderPath!);

        var tempPath = Path.Combine(_cacheFolderPath!, $"{keyHash}.{Guid.NewGuid():N}.tmp");
        Interlocked.Increment(ref _inFlightStreams);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
        var writerToken = linkedCts.Token;

        try
        {
            // 1. Run the writer outside the gate.
            long writtenLength;
            try
            {
                await using var stream = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 81920,
                    useAsync: true);

                await writer(stream, writerToken).ConfigureAwait(false);
                await stream.FlushAsync(writerToken).ConfigureAwait(false);
                writtenLength = stream.Length;
            }
            catch
            {
                TryDeleteFile(tempPath);
                tempPath = null!;
                throw;
            }

            // 2. If dispose fired during the writer, bail without committing.
            if (_disposed || writerToken.IsCancellationRequested)
            {
                TryDeleteFile(tempPath);
                tempPath = null!;
                writerToken.ThrowIfCancellationRequested();
                return null;
            }

            // 3. Build the entry now that Length is known. Factory provides FileName.
            var writtenAt = DateTimeOffset.UtcNow;
            var entry = entryFactory(new PayloadWriteContext(writtenLength, writtenAt));
            if (string.IsNullOrEmpty(entry.FileName))
            {
                TryDeleteFile(tempPath);
                tempPath = null!;
                throw new InvalidOperationException(
                    "entryFactory returned an entry with empty FileName. Streamed writes require a file name.");
            }

            var finalPath = Path.Combine(_cacheFolderPath, entry.FileName);

            // 4. Re-acquire the gate for the commit. Dispose drains in-flight streams
            //    before acquiring the gate itself, so we cannot deadlock here as long
            //    as our in-flight counter is still incremented (it is — the outer
            //    finally decrements it).
            await _gate.WaitAsync(writerToken).ConfigureAwait(false);
            try
            {
                if (_disposed)
                {
                    TryDeleteFile(tempPath);
                    tempPath = null!;
                    return null;
                }

                var connection = await EnsureInitializedLockedAsync(writerToken).ConfigureAwait(false);
                if (connection is null)
                {
                    TryDeleteFile(tempPath);
                    tempPath = null!;
                    return null;
                }

                TEntry? oldEntry;
                try
                {
                    oldEntry = await GetEntryLockedAsync(connection, keyHash, writerToken).ConfigureAwait(false);
                }
                catch (SqliteException ex)
                {
                    LogWarning("Cache WriteStreamedPayloadAsync: prior-entry lookup failed; treating as miss", ex);
                    oldEntry = default;
                }

                // 5. Atomic move.
                File.Move(tempPath, finalPath, overwrite: true);
                tempPath = null!;

                // 6. Cleanup of prior file when FileName changed.
                if (oldEntry is not null &&
                    !string.IsNullOrEmpty(oldEntry.FileName) &&
                    !string.Equals(oldEntry.FileName, entry.FileName, StringComparison.OrdinalIgnoreCase))
                {
                    TryDeleteFile(Path.Combine(_cacheFolderPath, oldEntry.FileName));
                }

                // 7. UPSERT.
                try
                {
                    await UpsertEntryLockedAsync(connection, keyHash, entry, writerToken).ConfigureAwait(false);
                }
                catch (SqliteException ex)
                {
                    LogWarning("Cache WriteStreamedPayloadAsync UPSERT failed; new file will be reconciled later", ex);
                    return null;
                }

                // 8. Post-write eviction.
                await EvictIfNeededLockedAsync(connection, protectedKeyHash: keyHash, writerToken).ConfigureAwait(false);

                return new Uri(finalPath);
            }
            finally
            {
                _gate.Release();
            }
        }
        finally
        {
            if (tempPath is not null)
            {
                TryDeleteFile(tempPath);
            }
            Interlocked.Decrement(ref _inFlightStreams);
        }
    }

    /// <summary>
    /// Public LRU eviction trigger. Service-layer callers use this for explicit
    /// "free space now" operations; the buffered/streamed write paths invoke the
    /// internal locked variant automatically.
    /// </summary>
    public async Task EvictIfNeededAsync(string? protectedKeyHash = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connection = await EnsureInitializedLockedAsync(cancellationToken).ConfigureAwait(false);
            if (connection is null)
            {
                return;
            }

            await EvictIfNeededLockedAsync(connection, protectedKeyHash, cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException ex)
        {
            LogWarning("Cache EvictIfNeededAsync failed; ignoring", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Sweeps DB rows whose payload file is missing (removes them) and payload files
    /// matching <see cref="IsKnownPayloadFileName"/> whose row is missing (deletes
    /// them). Negative-cache rows (empty <c>FileName</c>) are skipped on both passes.
    ///
    /// <para>
    /// Uses <see cref="EnumerateAllEntriesLockedAsync"/> so pinned and negative rows
    /// are visible to the reconcile and not mistaken for orphans. If enumeration
    /// throws <see cref="SqliteException"/>, the reconcile <b>aborts</b> without
    /// deleting any files — interpreting a SQLite hiccup as "all files are orphans"
    /// would wipe the entire cache.
    /// </para>
    /// </summary>
    public async Task ReconcileAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connection = await EnsureInitializedLockedAsync(cancellationToken).ConfigureAwait(false);
            if (connection is null)
            {
                return;
            }

            IReadOnlyList<(string KeyHash, TEntry Entry)> entries;
            try
            {
                entries = await EnumerateAllEntriesLockedAsync(connection, cancellationToken).ConfigureAwait(false);
            }
            catch (SqliteException ex)
            {
                LogWarning(
                    "Cache reconcile aborted: row enumeration failed (refusing to treat as empty and delete every payload)",
                    ex);
                return;
            }

            Directory.CreateDirectory(_cacheFolderPath!);

            var knownFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var changed = false;

            // Pass 1: sweep rows whose file is missing.
            foreach (var (keyHash, entry) in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrEmpty(entry.FileName))
                {
                    // Negative-cache row — no payload to verify.
                    continue;
                }

                knownFiles.Add(entry.FileName);
                if (File.Exists(Path.Combine(_cacheFolderPath!, entry.FileName)))
                {
                    continue;
                }

                try
                {
                    await RemoveEntryLockedAsync(connection, keyHash, cancellationToken).ConfigureAwait(false);
                    changed = true;
                }
                catch (SqliteException ex)
                {
                    LogWarning("Cache reconcile row removal failed; continuing", ex);
                }
            }

            // Pass 2: sweep payload-shaped files whose row is missing.
            foreach (var file in Directory.EnumerateFiles(_cacheFolderPath!))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var name = Path.GetFileName(file);
                if (!IsKnownPayloadFileName(name))
                {
                    continue;
                }

                if (knownFiles.Contains(name))
                {
                    continue;
                }

                TryDeleteFile(file);
            }

            if (changed)
            {
                try
                {
                    await using var checkpoint = connection.CreateCommand();
                    checkpoint.CommandText = "PRAGMA wal_checkpoint(PASSIVE);";
                    await checkpoint.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (SqliteException ex)
                {
                    LogWarning("Cache reconcile WAL checkpoint failed; ignoring", ex);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// LRU eviction down to <see cref="CacheOptionsBase.MaximumBytes"/>. Disabled
    /// when <c>MaximumBytes &lt;= 0</c>. Negative-cache rows are skipped (empty
    /// <c>FileName</c> means no file to delete, and the row itself is tiny).
    /// The candidate query also excludes the protected key.
    ///
    /// <para>
    /// Internally <b>never throws <see cref="SqliteException"/></b> — the buffered and
    /// streamed write paths invoke this after their UPSERT commits, and a DB hiccup
    /// during eviction must not turn a successful write into a user-visible exception.
    /// All DB calls (stats, candidate enumeration, row removal, WAL checkpoint) are
    /// caught and logged; <see cref="OperationCanceledException"/> still propagates.
    /// </para>
    /// </summary>
    protected virtual async Task EvictIfNeededLockedAsync(
        SqliteConnection connection,
        string? protectedKeyHash,
        CancellationToken cancellationToken)
    {
        var maximumBytes = _options.MaximumBytes;
        if (maximumBytes <= 0)
        {
            return;
        }

        long totalBytes;
        try
        {
            await using var statsCommand = connection.CreateCommand();
            statsCommand.CommandText = $"SELECT COALESCE(SUM(Length), 0) FROM {TableName};";
            var result = await statsCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            totalBytes = result is long v ? v : 0L;
        }
        catch (SqliteException ex)
        {
            LogWarning("Cache eviction stats query failed; skipping eviction this cycle", ex);
            return;
        }

        if (totalBytes <= maximumBytes)
        {
            return;
        }

        IReadOnlyList<(string KeyHash, TEntry Entry)> candidates;
        try
        {
            candidates = await GetEvictionCandidatesLockedAsync(connection, protectedKeyHash, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (SqliteException ex)
        {
            LogWarning("Cache eviction candidate enumeration failed; skipping eviction this cycle", ex);
            return;
        }

        var changed = false;
        foreach (var (keyHash, entry) in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (totalBytes <= maximumBytes)
            {
                break;
            }

            // Belt-and-suspenders: subclasses' candidate queries already filter empty
            // FileName, but skip here too so a subclass that forgets doesn't delete
            // the wrong row or no-op the file delete.
            if (string.IsNullOrEmpty(entry.FileName))
            {
                continue;
            }

            TryDeleteFile(Path.Combine(_cacheFolderPath!, entry.FileName));

            try
            {
                await RemoveEntryLockedAsync(connection, keyHash, cancellationToken).ConfigureAwait(false);
            }
            catch (SqliteException ex)
            {
                LogWarning("Cache eviction row delete failed; continuing", ex);
                continue;
            }

            totalBytes -= entry.Length;
            changed = true;
        }

        if (changed)
        {
            try
            {
                await using var checkpoint = connection.CreateCommand();
                checkpoint.CommandText = "PRAGMA wal_checkpoint(PASSIVE);";
                await checkpoint.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (SqliteException ex)
            {
                LogWarning("Cache eviction WAL checkpoint failed; ignoring", ex);
            }
        }
    }

    /// <summary>
    /// Writes a negative-cache row: <c>Length=0</c> and empty <c>FileName</c>. No
    /// payload file is created. Service-layer reads negative entries via
    /// <see cref="GetEntryAsync"/>; <see cref="TryGetPayloadUriAsync"/> always returns
    /// <c>null</c> for them. Warn-and-noop on SQLite failure.
    /// </summary>
    public async Task WriteNegativeAsync(string keyHash, TEntry entry, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(entry.FileName))
        {
            throw new ArgumentException(
                "WriteNegativeAsync requires entry.FileName to be empty.",
                nameof(entry));
        }

        if (entry.Length != 0)
        {
            throw new ArgumentException(
                "WriteNegativeAsync requires entry.Length to be 0.",
                nameof(entry));
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connection = await EnsureInitializedLockedAsync(cancellationToken).ConfigureAwait(false);
            if (connection is null)
            {
                return;
            }

            await UpsertEntryLockedAsync(connection, keyHash, entry, cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException ex)
        {
            LogWarning("Cache WriteNegativeAsync failed; ignoring", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    // ----- Subclass DB surface (gate-held; throw on SqliteException so the base
    // can decide per-call-site recovery in payload-lifecycle methods). -----

    protected abstract Task<TEntry?> GetEntryLockedAsync(
        SqliteConnection connection,
        string keyHash,
        CancellationToken cancellationToken);

    protected abstract Task TouchEntryLockedAsync(
        SqliteConnection connection,
        string keyHash,
        DateTimeOffset lastAccessUtc,
        CancellationToken cancellationToken);

    protected abstract Task UpsertEntryLockedAsync(
        SqliteConnection connection,
        string keyHash,
        TEntry entry,
        CancellationToken cancellationToken);

    protected abstract Task RemoveEntryLockedAsync(
        SqliteConnection connection,
        string keyHash,
        CancellationToken cancellationToken);

    protected abstract Task<IReadOnlyList<(string KeyHash, TEntry Entry)>> GetEvictionCandidatesLockedAsync(
        SqliteConnection connection,
        string? protectedKeyHash,
        CancellationToken cancellationToken);

    protected abstract Task<IReadOnlyList<(string KeyHash, TEntry Entry)>> EnumerateAllEntriesLockedAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // 1. Signal in-flight streamed writers to abort. Buffered writes (and reads /
        //    upserts / evictions) are gated, so they will finish on their own once we
        //    wait on the gate below. Streamed writes run their writer outside the
        //    gate — they observe cancellation here and clean up their temp files in
        //    their own finally blocks.
        try
        {
            _disposeCts.Cancel();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or AggregateException)
        {
            LogWarning("Cache dispose cancellation failed", ex);
        }

        // 2. Drain in-flight streamed writers. Each one decrements _inFlightStreams
        //    in its finally block. We poll because Dispose is sync and the writers
        //    run as Tasks. Bounded by DisposeDrainTimeout — if a writer ignores its
        //    cancellation token, its temp file will be cleaned by the next launch's
        //    startup sweep.
        var drainDeadline = Environment.TickCount64 + (long)DisposeDrainTimeout.TotalMilliseconds;
        while (Volatile.Read(ref _inFlightStreams) > 0)
        {
            if (Environment.TickCount64 >= drainDeadline)
            {
                LogWarning(
                    "Cache dispose timed out waiting for streamed writers to drain; tearing down anyway",
                    new TimeoutException($"In-flight streamed writers did not drain within {DisposeDrainTimeout.TotalSeconds:F0}s."));
                break;
            }

            Thread.Sleep(10);
        }

        // 3. Drain the gate so any in-flight read/buffered-write completes before we
        //    tear down the connection. We do NOT dispose the semaphore — queued waiters
        //    may still wake up and observe _disposed, and disposing it under them would
        //    surface ObjectDisposedException on a benign release path.
        var gateAcquired = false;
        try
        {
            gateAcquired = _gate.Wait(DisposeDrainTimeout);
            if (!gateAcquired)
            {
                LogWarning(
                    "Cache dispose timed out draining the gate; tearing down connection anyway",
                    new TimeoutException($"Gate did not drain within {DisposeDrainTimeout.TotalSeconds:F0}s."));
            }

            try
            {
                if (_connection is not null)
                {
                    _connection.Dispose();
                    _connection = null;
                }
            }
            catch (Exception ex)
            {
                LogWarning("Cache connection dispose failed", ex);
            }

            try
            {
                SqliteConnection.ClearAllPools();
            }
            catch
            {
                // Best-effort.
            }
        }
        finally
        {
            if (gateAcquired)
            {
                _gate.Release();
            }

            try
            {
                _disposeCts.Dispose();
            }
            catch
            {
                // Best-effort.
            }
        }
    }

    private async Task<SqliteConnection?> EnsureInitializedLockedAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return null;
        }

        if (_initialized && _connection is { State: System.Data.ConnectionState.Open })
        {
            return _connection;
        }

        var cacheFolderPath = EnsureCacheFolderPath();

        try
        {
            Directory.CreateDirectory(cacheFolderPath);
            OnBeforeOpenConnection(cacheFolderPath, _databasePath!);
            _connection = await OpenAndPrepareConnectionAsync(cancellationToken).ConfigureAwait(false);
            _initialized = true;
            return _connection;
        }
        catch (SqliteException ex)
        {
            LogWarning("Cache database initialization failed; rebuilding from scratch", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogWarning("Cache database initialization I/O failure; rebuilding from scratch", ex);
        }

        // Self-heal: dispose, delete cache.db + sidecars, retry once.
        await TryDisposeConnectionAsync().ConfigureAwait(false);
        TryDeleteDatabaseFiles();

        try
        {
            _connection = await OpenAndPrepareConnectionAsync(cancellationToken).ConfigureAwait(false);
            _initialized = true;
            return _connection;
        }
        catch (OperationCanceledException)
        {
            await TryDisposeConnectionAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            LogWarning("Cache database rebuild failed; cache will be unavailable", ex);
            await TryDisposeConnectionAsync().ConfigureAwait(false);
            return null;
        }
    }

    private string EnsureCacheFolderPath()
    {
        if (_cacheFolderPath is null)
        {
            _cacheFolderPath = ResolvePayloadFolder();
            _databasePath = Path.Combine(_cacheFolderPath, DatabaseFileName);
        }

        return _cacheFolderPath;
    }

    /// <summary>
    /// Hook for subclass-specific one-off init work (e.g. legacy artifact discard)
    /// that must run after the folder exists but before the connection opens.
    /// Default no-op.
    /// </summary>
    protected virtual void OnBeforeOpenConnection(string cacheFolderPath, string databasePath)
    {
    }

    private async Task<SqliteConnection> OpenAndPrepareConnectionAsync(CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath!,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            DefaultTimeout = ConnectionDefaultTimeoutSeconds,
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ApplyConnectionPragmasAsync(connection, cancellationToken).ConfigureAwait(false);
            await SqliteCacheSchemaDriver.EnsureSchemaAsync(
                connection,
                CurrentSchemaVersion,
                TableName,
                CreateCurrentSchemaSql,
                MigrationSteps,
                cancellationToken).ConfigureAwait(false);

            // One-shot startup sweep of abandoned streamed-write temp files. Safe to
            // run here because no streamed writer can be in flight before init
            // completes — they call EnsureInitializedLockedAsync themselves and queue
            // behind the init gate. After this point any .tmp file is a live writer's
            // and must not be touched until reconcile / next launch.
            SweepAbandonedTempFiles();

            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private void SweepAbandonedTempFiles()
    {
        if (_cacheFolderPath is null || !Directory.Exists(_cacheFolderPath))
        {
            return;
        }

        var swept = 0;
        foreach (var file in Directory.EnumerateFiles(_cacheFolderPath, "*.tmp"))
        {
            // Narrow to the streamed-writer pattern so unrelated .tmp files the user
            // (or another tool) may have stashed in the folder are left alone.
            if (!IsStreamedWriteTempFileName(Path.GetFileName(file)))
            {
                continue;
            }

            if (TryDeleteFile(file))
            {
                swept++;
            }
        }

        if (swept > 0)
        {
            try
            {
                _log.Info(
                    "Cache startup swept abandoned streamed-write temp files",
                    new { CacheFolder = _cacheFolderPath, SweptCount = swept });
            }
            catch
            {
                // Logging failures are non-fatal during init.
            }
        }
    }

    /// <summary>
    /// Recognises the exact filename shape produced by <see cref="WriteStreamedPayloadAsync"/>:
    /// <c>&lt;keyHash&gt;.&lt;guid-N&gt;.tmp</c> — 64 lower-case hex chars, dot,
    /// 32 lower-case hex chars, dot, "tmp". Any deviation (different segment lengths,
    /// non-hex chars, upper-case) is considered unrelated and left alone by the sweep.
    /// </summary>
    private static bool IsStreamedWriteTempFileName(string fileName)
    {
        // <64-hex>.<32-hex>.tmp → 64 + 1 + 32 + 1 + 3 = 101 chars exactly.
        const int ExpectedLength = 101;
        if (fileName.Length != ExpectedLength)
        {
            return false;
        }

        if (fileName[64] != '.' || fileName[97] != '.')
        {
            return false;
        }

        if (!fileName.AsSpan(98).Equals("tmp", StringComparison.Ordinal))
        {
            return false;
        }

        if (!IsLowerHex(fileName.AsSpan(0, 64)))
        {
            return false;
        }

        if (!IsLowerHex(fileName.AsSpan(65, 32)))
        {
            return false;
        }

        return true;

        static bool IsLowerHex(ReadOnlySpan<char> span)
        {
            foreach (var c in span)
            {
                var isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
                if (!isHex)
                {
                    return false;
                }
            }
            return true;
        }
    }

    private static async Task ApplyConnectionPragmasAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task TryDisposeConnectionAsync()
    {
        if (_connection is null)
        {
            return;
        }

        try
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Best-effort.
        }
        finally
        {
            _connection = null;
        }

        try
        {
            SqliteConnection.ClearAllPools();
        }
        catch
        {
            // Best-effort.
        }
    }

    private void TryDeleteDatabaseFiles()
    {
        if (_databasePath is null)
        {
            return;
        }

        TryDeleteFile(_databasePath);
        TryDeleteFile(_databasePath + "-wal");
        TryDeleteFile(_databasePath + "-shm");
    }

    private static bool TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                return true;
            }
        }
        catch
        {
            // Best-effort.
        }

        return false;
    }

    private void LogWarning(string message, Exception exception)
    {
        try
        {
            _log.Warning(message, exception: exception);
        }
        catch
        {
            System.Diagnostics.Debug.WriteLine($"[{GetType().Name}] {message}: {exception}");
        }
    }
}
