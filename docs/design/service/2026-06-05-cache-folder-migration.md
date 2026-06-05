# Cache Folder Migration — Design

Add a generic, declarative payload-folder migration mechanism to `SqliteCacheStoreBase<TEntry>`, symmetric with the existing `PRAGMA user_version` schema migration. First use is renaming the MSIX image cache from `ImageCache` to `Images` so that packaged and unpackaged installs share the same subfolder name across all cache entities.

Bundled with this: a small `IAppFolderLocator` service that becomes the single authoritative source for the cache parent folder (replacing per-store `RootPath` overrides) and a starting point for migrating other per-runtime application folders later.

---

## What we're building

1. **`IAppFolderLocator`** — a tiny DI'd service that owns the MSIX-vs-unpackaged branch for application folders. Initial surface is one method, `GetCacheFolder()`. Other folders (logs, data) can be added one method at a time later without affecting cache store wiring.
2. **`SqliteCacheStoreBase` ownership of the cache parent.** The base computes the per-entity payload folder as `IAppFolderLocator.GetCacheFolder()` + `PayloadSubfolderName`. Subclasses declare only the subfolder name; the parent is single-sourced.
3. **`CacheOptionsBase.RootPath` removed.** With the parent owned authoritatively by the locator, per-store overrides become a source of inconsistency. Test isolation moves to injecting a test `IAppFolderLocator`.
4. **A folder-migration driver in the base**, declarative via `PayloadFolderMigrationSteps`, idempotent via a per-store JSON metadata file (`<entity>.json` at the cache parent). Runs before the SQLite connection opens.
5. **Image store migration step** moving `ImageCache` → `Images`. After this lands, both MSIX and unpackaged installs use `Images` as their image-cache subfolder.

No data is lost. The whole on-disk directory (DB + sidecars + payload files) is moved as one unit via `Directory.Move`, then the SQLite connection opens at the new path with `PRAGMA user_version` unchanged.

---

## Pivotal decisions

### One authoritative cache parent, not per-store overrides

`CacheOptionsBase.RootPath` is removed. Today every store could in principle resolve to a different parent folder; nothing in the design enforces a single root. That's an inconsistency risk that grows as lyrics and audio stores arrive — two stores fighting over the parent would have ambiguous metadata-file ownership and confusing diagnostics.

The replacement is `IAppFolderLocator.GetCacheFolder()`, registered as a singleton in DI. Every store reads the same value, by construction. Test isolation is preserved by injecting a test implementation that returns a per-fixture temp directory — same shape as other test seams in the project.

### `IAppFolderLocator` is scoped to application folders, not cache-specific

There's already a pattern in the project — `FileSink`, `LocalSettingsService`, and `AppStateService` each have their own MSIX-vs-unpackaged branch for their respective folders. A `ICacheLocationProvider` named only for caches would lock us out of the natural generalization.

The interface is scoped to "per-runtime application folders" so future folders (`GetLogsFolder()`, `GetDataFolder()`) land as new methods on the same interface. **This PR ships only `GetCacheFolder()`**; retrofitting the other services is out of scope and tracked separately. The interface shape just makes those follow-ups additive.

### Base owns the cache parent; subclass owns only the subfolder name

`ResolvePayloadFolder()` stops being abstract:

```csharp
private string ResolvePayloadFolder()
    => Path.Combine(_folderLocator.GetCacheFolder(), PayloadSubfolderName);
```

The subclass surface becomes:

```csharp
protected abstract string PayloadSubfolderName { get; }   // e.g. "Images", "Lyrics", "Audio"
```

Two consequences:

- `ImageCachePath.cs` is deleted. The MSIX branch it contained moves into `AppFolderLocator`.
- Subclasses cannot accidentally diverge on the parent. They name themselves and nothing else.

### Folder migration is symmetric with SQL migration

The framework mirrors the shape of `SqliteCacheSchemaDriver` so the mental model stays consistent:

| | SQL migration | Folder migration |
|---|---|---|
| State store | `PRAGMA user_version` inside the DB | `<entity>.json` at the cache parent |
| Step list | `IReadOnlyList<MigrationStep>` (target version + SQL) | `IReadOnlyList<PayloadFolderMigrationStep>` (target version + source/target subfolder) |
| Current version | `int CurrentSchemaVersion` | derived (`steps.Max(s => s.TargetVersion)` or 0 if empty) |
| Driver location | `SqliteCacheSchemaDriver.EnsureSchemaAsync` | new `EnsurePayloadFolderAsync` in the base |
| When it runs | After connection opens, before reads | Before connection opens, before `Directory.CreateDirectory(target)` |
| On failure | Rolls back transaction; `user_version` unchanged | Marker not bumped; next launch retries |

Greenfield stores (lyrics, audio) leave `PayloadFolderMigrationSteps` empty, write no marker file, and pay no on-disk cost for a mechanism they don't use.

### Driver runs before the connection opens, before `Directory.CreateDirectory(target)`

Placement inside `EnsureInitializedLockedAsync` is fixed by two constraints:

- **The DB lives inside the folder being moved.** Migration has to complete before `OpenAndPrepareConnectionAsync` so SQLite opens at the final path.
- **`Directory.CreateDirectory(target)` would mask the "must migrate" signal.** If we created the empty target first, the driver's "source exists && target absent" check would never fire.

New init order:

1. `EnsureCacheFolderPath()` — resolves `_cacheFolderPath` and `_databasePath`.
2. **`EnsurePayloadFolderAsync()`** — folder migration driver runs.
3. `Directory.CreateDirectory(_cacheFolderPath)` — unchanged.
4. `OnBeforeOpenConnection(...)` — unchanged (image store's legacy-manifest discard still runs at the post-migration location).
5. `OpenAndPrepareConnectionAsync(...)` — unchanged. Includes `PRAGMA` setup, SQL schema migration via `SqliteCacheSchemaDriver`, and startup temp-file sweep.

Folder migration and SQL migration are fully decoupled: different state stores, different timing, different failure handling.

### Metadata file: per-entity JSON at the cache parent

One file per store, named after the entity, sitting next to the entity folder:

```
<cache-parent>/Images.json
<cache-parent>/Lyrics.json
<cache-parent>/Audio.json
```

Format — minimal, extensible:

```json
{ "FolderVersion": 1 }
```

Serialized via the project's `Hypersonic.Core.Helpers.Json` wrapper, which uses default PascalCase property names. The DTO is a plain record:

```csharp
public sealed record PayloadFolderMetadata(int FolderVersion);
```

- **Per-store, not shared.** No cross-store contention, no shared file lock, no schema for a multi-store document. Cost is one small file per entity — negligible.
- **At the parent, not inside the entity folder.** Renaming the folder would otherwise lose the marker.
- **Named after the subfolder.** Defaults to `PayloadSubfolderName + ".json"` via an overridable virtual. One place to change if a subclass renames itself.
- **Read failure ⇒ version 0.** Missing, unreadable, unparseable, or missing-field all degrade to "haven't migrated yet" — safe default for the state machine.
- **Atomic writes.** Temp file in the same parent, `File.Move(temp, marker, overwrite: true)`. Failed marker writes are logged; next launch re-evaluates the state table, which is designed to handle a stale marker without re-moving anything.
- **Parent directory bootstrap.** The driver calls `Directory.CreateDirectory(parent)` once at entry, before any marker read/write or temp-file write. This is needed because the unpackaged parent (`%LOCALAPPDATA%\Hypersonic\Cache`) may not exist on first launch, and the driver runs before the per-store payload folder is created. Creating the parent does not mask the source/target migration signal — the state machine checks the *subfolders*, not the parent.

JSON instead of a raw integer because the shape is naturally extensible — future per-store metadata (last-migrated timestamp, schema-driver telemetry) lives in the same file without a format change. We add fields only when something needs them.

### Step shape: subfolder-name pairs, computed by the driver

```csharp
public sealed record PayloadFolderMigrationStep(
    int TargetVersion,
    string SourceSubfolder,
    string TargetSubfolder);
```

The driver joins each step's `SourceSubfolder` and `TargetSubfolder` with `IAppFolderLocator.GetCacheFolder()` internally. The subclass never computes or even sees the parent path — the declaration is platform-agnostic by construction.

Why not absolute paths? An absolute-path API would force every subclass to call the locator itself, which (a) leaks the parent path into per-store code we're trying to centralize and (b) means a subclass that resolves the parent incorrectly silently breaks migration. Subfolder pairs make platform-independence the only declarable shape.

**Leaf-name validation.** `EnsureCacheFolderPath()` validates `PayloadSubfolderName` lazily, immediately before the first `Path.Combine(parent, PayloadSubfolderName)`, and caches the resolved path the same way it does today. Because `_cacheFolderPath` is set on the first call and reused thereafter, validation runs exactly once per store instance — and crucially, runs *after* construction has finished, avoiding the C# footgun of reading an abstract/virtual member from a base constructor (where the derived class isn't yet fully initialized and an override could observe undefined state). The migrator validates `SourceSubfolder`, `TargetSubfolder`, and `MetadataFileName` at its entry point on every init call. All four fields use the same safe-leaf check — the conjunction of:

- `!string.IsNullOrEmpty(x)`
- `Path.GetFileName(x) == x` (catches embedded separators like `foo/bar` or `foo\bar`)
- `!Path.IsPathRooted(x)` (catches `C:\foo` and `\foo`)
- `x != "."` and `x != ".."` (catches the directory-traversal escapes that pass the `GetFileName` check)
- `x.IndexOfAny(Path.GetInvalidFileNameChars()) < 0` (catches platform-invalid filename characters)

Any violation throws `ArgumentException` naming the offending field. This blocks typos like `"..\\Images"`, `"."`, or accidental rooted paths from escaping the cache parent, since the design contract is "subclasses declare leaf names only." The public `CacheFolderPath` property goes through `EnsureCacheFolderPath()`, so it's safe to read even when no init has run — the validation fires on first access, not eagerly at construction.

**Step list validation.** `TargetVersion` values must be positive (`> 0`) and unique across the step list. Non-positive values are meaningless (the marker can only ever bump *up*, and `0` is the implicit pre-migration baseline); duplicates cause silent skips once the marker advances past the duplicated value, masking a real bug. Both throw `ArgumentException` at the migrator entry point.

When the step list is non-empty, three additional invariants must hold:

1. **Final-target match.** The highest-`TargetVersion` step's `TargetSubfolder` must equal `PayloadSubfolderName`. The last step is by definition "the rename that lands data where the current store opens it" — if they diverge, the driver will successfully `Directory.Move` data into one folder and bump the marker, while the store opens a different folder and silently sees an empty cache (the migrated data is stranded next to it, invisible to reads but visible on disk).
2. **Chain continuity.** After ordering by `TargetVersion`, every step after the first must have `SourceSubfolder == previous.TargetSubfolder`. Otherwise an intermediate hop can leave data in a folder that no subsequent step picks up. Concrete failure: steps `[(1, "A", "B"), (2, "X", "Current")]` pass the final-target check on their own, but an existing user at `A` is moved to `B` by step 1, the marker advances to `1`, step 2's source `X` is absent so the driver no-ops and bumps to `2`, and the store opens `Current` while the data sits in `B`. Chain continuity rules this out: the step-2 source would have to be `B`, so the second move actually carries the data forward.
3. **Final-target uniqueness in the chain.** Only the highest-`TargetVersion` step may have `TargetSubfolder == PayloadSubfolderName`. A non-highest step that targets `PayloadSubfolderName` (e.g. a contrived chain `A → Current` then `Current → Current`) would qualify as "conflict on final target" in the state machine at the wrong step and silently mis-classify the conflict policy. Identity steps elsewhere in the chain are fine — only the equality with the current read folder is forbidden outside the final step.

All three throw `ArgumentException` at the migrator entry point on mismatch. Empty step lists have no final target and no chain to validate.

### Driver state machine

For each `step` in `PayloadFolderMigrationSteps` ordered by `TargetVersion`, where `step.TargetVersion > currentMarkerVersion`:

**Per-step move preflight (multi-step chains only).** Before the driver would execute a `Directory.Move(source, target)` (i.e. before evaluating the "source present, target absent" row), it inspects subfolders *strictly later* in the chain — every `TargetSubfolder` of a step with a higher `TargetVersion` than the current step. If **any** of those folders exists on disk (populated or empty — same `Directory.Exists` predicate the rest of the state machine uses), the move is unsafe: completing it would advance the marker past the current step into a downstream folder that already exists, and the later step would then hit the conflict row (continuing init against the pre-existing downstream folder) while the freshly-moved data sits stranded in the intermediate target. The driver instead logs an "ambiguous chain state" warning naming every existing downstream folder it found and fails the init attempt — same posture as a `Directory.Move` failure, so the source is left intact for the operator to resolve. Concrete failures this catches: chain `A → B → Current` with `{A exists, B absent, Current exists}` (Current may be populated *or* empty — either way, without preflight, step 1 would move `A` to `B`, step 2 would hit the final-target conflict row, and the store would open the pre-existing `Current` while the older `A` data sits in `B`). Preflight aborts before any move. Single-step chains have no later subfolders to scan, so preflight is a no-op for the image cache.

| `source` exists | `target` exists | `source == target` | `target == PayloadSubfolderName` | Action | Bump marker to `step.TargetVersion`? | Abort remaining steps? | Init outcome |
|---|---|---|---|---|---|---|---|
| — | — | yes | — | No-op (subclass declared an identity step) | yes | no | continue |
| no | no | no | — | No-op (fresh install — nothing to move) | yes | no | continue |
| no | yes | no | — | No-op (already migrated externally or marker was lost) | yes | no | continue |
| yes | no | no | — | **Preflight check**: if any strictly-later step's `TargetSubfolder` exists on disk → fail this init attempt. Otherwise `Directory.Move(source, target)`; on success bump marker | yes (only on `Move` success) | yes if preflight fails or `Move` throws | **fail this init attempt** if preflight fails or `Move` throws |
| yes | yes | no | yes (final step) | **Conflict on final target**: log warning naming both paths; do not touch either folder | **no** | yes | continue (store opens the existing final target — whatever lives there) |
| yes | yes | no | no (pre-final step) | **Conflict on pre-final target**: log warning naming both paths; do not touch either folder | **no** | yes | **fail this init attempt** |

Properties:

- **Single-step atomicity.** Each step is a single `Directory.Move` (or no-op) followed by a marker bump. The marker bump is a separate file write that can fail independently; if it does, next launch sees `source missing && target exists && marker low` → "already migrated externally" branch → bumps marker, no re-move. Robust to a crash between move and marker write.
- **No partial chain.** Conflict, preflight failure, or `Directory.Move` failure aborts the rest of the step list. The marker reflects the highest successfully completed step.
- **Conflict policy depends on whether the conflicted target is the final target.** Conflict on the final target (`step.TargetSubfolder == PayloadSubfolderName`) is recoverable: the store opens the existing final folder and serves whatever lives there; the warning re-appears every launch until the operator removes the stale source. Conflict on a *pre-final* step would otherwise let init continue and create/open the still-empty final folder, stranding data in both the conflicted source and conflicted intermediate target — so pre-final conflicts fail the init attempt instead, leaving the source intact for the operator to resolve. The chain-continuity invariant (every later step's source equals the previous step's target) guarantees pre-final targets are never equal to `PayloadSubfolderName`, so these two cases partition cleanly. The image cache's single step is final-only, so this distinction is dormant today and activates only when a future migration adds intermediate steps.
- **`Directory.Move` failure, preflight failure, and pre-final conflict all fail this initialization attempt.** When the move throws, when preflight detects an existing downstream subfolder, or when a pre-final-target conflict is encountered, the driver propagates the failure and `EnsureInitializedLockedAsync` returns `null` (cache unavailable until init succeeds) — matching the existing self-heal-failed contract. Critically, this means the target folder is **not** created and **not** written during the failed attempt; otherwise the next attempt would observe whatever state the partial migration left behind and could mis-classify it on the next pass. Because `_initialized` is left false, the next call into the cache re-runs init (and the migrator); a transient lock or a since-resolved conflict that clears before the next call recovers in-session, while a persistent failure waits until the next launch.

### `Directory.Move` failures: fail the run, don't create the target

`Directory.Move` only — no copy-then-delete fallback. By construction the driver always joins source and target under the same cache parent (`IAppFolderLocator.GetCacheFolder()`), so cross-volume cases can't arise from the design itself; the realistic failure modes are transient I/O errors (file locked, antivirus scan, permission denied).

When the move throws, the driver logs the failure with both paths and the exception, leaves the marker unchanged, **does not create the target folder**, and returns a failure signal. `EnsureInitializedLockedAsync` treats this the same as a self-heal failure: returns `null`, leaves `_initialized` false, cache is unavailable until init succeeds; every cache method returns its safe default (miss / zero stats / no-op) until then. The next call into the cache re-runs init and the migrator; if the move succeeds then, the cache initializes normally.

The alternative — letting init proceed against a freshly created empty target — was the original draft. It strands the source data: writes during the failed-migration session populate the target, and next launch sees both folders populated, hits the conflict branch, and warns forever without resolving.

---

## Architecture

### New / changed files

```
Hypersonic.Core/                             (no changes)

Hypersonic/Contracts/Services/
└── IAppFolderLocator.cs                     (new) GetCacheFolder()

Hypersonic/Services/
├── AppFolderLocator.cs                      (new) production implementation
└── ImageCachePath.cs                        (deleted)

Hypersonic/Services/Cache/
├── PayloadFolderMigrationStep.cs            (new) record (TargetVersion, SourceSubfolder, TargetSubfolder)
├── PayloadFolderMetadata.cs                 (new) DTO for <entity>.json — { FolderVersion: int }
├── SqliteCacheFolderMigrator.cs             (new) internal static driver, mirrors SqliteCacheSchemaDriver
├── SqliteCacheStoreBase.cs                  (changed) takes IAppFolderLocator;
│                                                     PayloadSubfolderName replaces abstract ResolvePayloadFolder;
│                                                     new MetadataFileName virtual;
│                                                     new PayloadFolderMigrationSteps virtual;
│                                                     EnsureInitializedLockedAsync gains migration phase
└── CacheOptionsBase.cs                      (changed) RootPath removed

Hypersonic/Services/
└── SqliteImageCacheStore.cs                 (changed) declares PayloadSubfolderName = "Images";
                                                       declares one migration step ImageCache → Images;
                                                       constructor adds IAppFolderLocator

Hypersonic/App.xaml.cs                       (changed) services.AddSingleton<IAppFolderLocator, AppFolderLocator>()
```

### `IAppFolderLocator`

```csharp
namespace Hypersonic.Contracts.Services;

/// <summary>
/// Resolves per-runtime application folders. MSIX vs unpackaged branching lives here
/// so the rest of the codebase stays platform-agnostic. New folder categories land
/// as additional methods on this interface.
/// </summary>
public interface IAppFolderLocator
{
    /// <summary>
    /// Parent folder for cache stores.
    /// MSIX: <c>ApplicationData.Current.LocalCacheFolder.Path</c>.
    /// Unpackaged: <c>%LOCALAPPDATA%\Hypersonic\Cache</c>.
    /// </summary>
    string GetCacheFolder();
}
```

Production implementation lives in `Hypersonic/Services/AppFolderLocator.cs` and is the only place in the codebase under `Hypersonic.Services.Cache` (or its consumers) that calls `RuntimeHelper.IsMSIX` for cache folders.

### `SqliteCacheStoreBase` surface delta

```csharp
// Added (abstract):
protected abstract string PayloadSubfolderName { get; }

// Added (virtual, default = PayloadSubfolderName + ".json"):
protected virtual string MetadataFileName { get; }

// Added (virtual, default = empty list):
protected virtual IReadOnlyList<PayloadFolderMigrationStep> PayloadFolderMigrationSteps { get; }

// Removed (was abstract):
// protected abstract string ResolvePayloadFolder();

// Changed (constructor takes IAppFolderLocator):
protected SqliteCacheStoreBase(
    CacheOptionsBase options,
    IAppFolderLocator folderLocator,
    ILoggingService logService);
```

`EnsureCacheFolderPath()` becomes:

```csharp
private string EnsureCacheFolderPath()
{
    if (_cacheFolderPath is null)
    {
        // Read the virtual exactly once and reuse the captured value, so the value
        // we validated is the value we compose with (and later hand to the migrator).
        // Lazy timing also avoids the C# footgun of reading an abstract/virtual member
        // from a base constructor before the derived class is fully initialized. The
        // _cacheFolderPath is null guard makes this one-shot per instance.
        var subfolderName = PayloadSubfolderName;
        ValidateLeafName(subfolderName, nameof(PayloadSubfolderName));

        var parent = _folderLocator.GetCacheFolder();
        _cacheFolderPath = Path.Combine(parent, subfolderName);
        _databasePath = Path.Combine(_cacheFolderPath, DatabaseFileName);
    }
    return _cacheFolderPath;
}
```

`ValidateLeafName` is a small static helper (the five-condition check from [Leaf-name validation](#step-shape-subfolder-name-pairs-computed-by-the-driver)) shared between the base and the migrator. The migrator likewise reads `PayloadSubfolderName` (passed in from the base) once into a local before composing source/target paths, so the value validated is the value used end-to-end.

`EnsureInitializedLockedAsync` gains one new step between path resolution and `Directory.CreateDirectory`, and a failure branch:

```csharp
var cacheFolderPath = EnsureCacheFolderPath();

var migrated = await SqliteCacheFolderMigrator.EnsurePayloadFolderAsync(
    _folderLocator,
    PayloadSubfolderName,
    MetadataFileName,
    PayloadFolderMigrationSteps,
    _log,
    cancellationToken).ConfigureAwait(false);

if (!migrated)
{
    // Migrator returned failure (Directory.Move threw, preflight detected
    // an ambiguous chain state, or a pre-final conflict was encountered).
    // Do not create the target or open the DB; leave _initialized false
    // so the next cache call retries init.
    return null;
}

Directory.CreateDirectory(cacheFolderPath);
OnBeforeOpenConnection(cacheFolderPath, _databasePath!);
_connection = await OpenAndPrepareConnectionAsync(cancellationToken).ConfigureAwait(false);
```

The migrator returns `false` in three cases, all of which would otherwise let the store open a folder while leaving cached data stranded elsewhere: (a) a `Directory.Move` threw, (b) per-step preflight detected an existing downstream subfolder that the move would orphan, or (c) a pre-final-target conflict was encountered. The migrator returns `true` for everything else — identity steps, fresh-install no-ops, "already migrated externally" recovery, marker-write failures, and final-target conflict warnings — because none of these strand data: either nothing moved, or the store will open the existing final folder and serve whatever lives there. The migrator is internal and static. It does all its own I/O against the locator's parent path; the base just hands it the inputs.

### `SqliteImageCacheStore` delta

```csharp
protected override string PayloadSubfolderName => "Images";

protected override IReadOnlyList<PayloadFolderMigrationStep> PayloadFolderMigrationSteps =>
[
    new PayloadFolderMigrationStep(
        TargetVersion: 1,
        SourceSubfolder: "ImageCache",
        TargetSubfolder: "Images"),
];

// MetadataFileName left at default → "Images.json"
// Constructor adds IAppFolderLocator forwarded to base
```

Resulting paths after this change:

| | Before | After |
|---|---|---|
| MSIX image cache folder | `<LocalCache>/ImageCache` | `<LocalCache>/Images` |
| Unpackaged image cache folder | `%LOCALAPPDATA%\Hypersonic\Cache\Images` | `%LOCALAPPDATA%\Hypersonic\Cache\Images` (unchanged) |
| MSIX marker file | n/a | `<LocalCache>/Images.json` |
| Unpackaged marker file | n/a | `%LOCALAPPDATA%\Hypersonic\Cache\Images.json` |

---

## Walkthrough: every install scenario

For the image store's single step `(TargetVersion=1, SourceSubfolder="ImageCache", TargetSubfolder="Images")`:

| Scenario | `ImageCache/` | `Images/` | `Images.json` | Driver action | Final state |
|---|---|---|---|---|---|
| Fresh MSIX install | absent | absent | absent (v0) | bump marker to 1, no move | `Images/` created by init's `Directory.CreateDirectory`; `Images.json = 1` |
| Fresh unpackaged install | absent | absent | absent (v0) | bump marker to 1, no move | `Images/` created by init's `Directory.CreateDirectory`; `Images.json = 1` |
| Existing MSIX user (pre-migration) | populated (DB + payloads) | absent | absent (v0) | `Directory.Move(ImageCache → Images)`; bump marker to 1 | All data at `Images/`; `Images.json = 1` |
| Existing unpackaged user (pre-migration) | absent | populated | absent (v0) | "already migrated externally" branch: bump marker to 1, no move | Unchanged; `Images.json = 1` |
| Any post-migration launch | absent | populated | present (v1) | step.TargetVersion ≤ marker → loop skipped entirely | Unchanged |
| MSIX user with both folders (manual interference / partial prior failure) | populated | populated | absent (v0) | conflict on final target: log warning, do not touch either, do not bump marker | Init **continues**; cache opens at `Images/` with whatever lives there; source remains untouched; warning re-appears every launch until operator resolves |
| MSIX user, marker write failed last time | absent | populated | absent (v0) | "already migrated externally" branch: bump marker to 1, no move | Recovered; `Images.json = 1` |
| `Directory.Move` throws (file locked, permission denied, etc.) | populated | absent | absent (v0) | log failure, marker stays at 0, abort remaining steps, return failure | Init **returns null**; `_initialized` stays false; **target folder NOT created**; source remains populated; next cache call retries init |

The image cache's single step is a final-target step (its `TargetSubfolder` equals `PayloadSubfolderName = "Images"`), so the pre-final conflict row of the state machine is never reachable for this store. It activates when a future migration adds an intermediate step.

The matrix is exhaustive against the state machine in **Driver state machine** above. No row produces user data loss or makes the cache permanently unusable.

---

## Test strategy

Existing test files for `SqliteCacheStoreBase` and `SqliteImageCacheStore` stay; their fixtures change to inject a test `IAppFolderLocator` instead of setting `RootPath`. New coverage is concentrated in a new file plus a couple of targeted additions in the image store tests.

### New: `Hypersonic.Tests/Services/Cache/SqliteCacheFolderMigratorTests.cs`

Direct tests of the driver via a tiny `TestCacheStore : SqliteCacheStoreBase<TestEntry>` that declares configurable subfolder/steps. Cases mirror the state-machine matrix:

- Fresh install (source absent, target absent) → marker bumps, no folders touched.
- Existing source, target absent → `Directory.Move` runs, files end up at target, marker bumps.
- Source absent, target present, marker absent → marker bumps to current, nothing moved.
- Source present, target present, marker absent, **final-target step** (`step.TargetSubfolder == PayloadSubfolderName`) → no move, no bump, warning logged, init continues; store opens the existing target.
- Source present, target present, marker absent, **pre-final step** (chain `A → B → Current`, both `A` and `B` populated, `Current` absent) → no move, no bump, warning logged, migrator returns failure, init returns null and leaves `_initialized` false. `Current` is **not** created. Resolving the conflict (removing either `A` or `B`) and re-running init recovers.
- **Preflight: downstream target exists, populated** (chain `A → B → Current`, `A` populated, `B` absent, `Current` populated) → step 1's move preflight detects existing downstream `Current`; migrator returns failure before any move; `A`, `B`, `Current` all left untouched; init returns null. Removing either `A` or `Current` and re-running recovers.
- **Preflight: downstream target exists, empty** (chain `A → B → Current`, `A` populated, `B` absent, `Current` exists as an empty directory) → same outcome as the populated case; preflight uses `Directory.Exists` and does not inspect contents. Important regression guard: a "populated" predicate would have let step 1 move `A` to `B`, then step 2 would have continued on final-target conflict against the empty `Current`, leaving `A`'s data stranded in `B`.
- Identity step (`SourceSubfolder == TargetSubfolder`) → no move, marker bumps.
- Multi-step chain advances marker through each step in order; failure mid-chain leaves marker at the last successful step.
- Marker JSON malformed → treated as v0; driver re-evaluates state table cleanly.
- Marker write fails after a successful move → next driver invocation observes "source missing, target present" and recovers (bumps marker without re-moving).
- `Directory.Move` failure (simulated by file lock or permission error) → marker unchanged, **target subfolder not created**, migrator returns failure, init returns null and leaves `_initialized` false. A subsequent cache call after the simulated failure clears re-runs the migrator and succeeds (in-attempt recovery is the same code path as next-launch recovery).
- Leaf-name validation: passing each of `""`, `"."`, `".."`, `"..\\foo"`, `"foo\\bar"`, `"foo/bar"`, `"C:\\foo"`, `"\\foo"`, and a string containing an invalid filename character throws `ArgumentException`. `PayloadSubfolderName` is checked lazily on the first call to `EnsureCacheFolderPath` (which any cache method or `CacheFolderPath` property access triggers); the check is one-shot per instance because the resolved path is cached on first success. `SourceSubfolder`, `TargetSubfolder`, and `MetadataFileName` are checked at the migrator entry point on every init. A greenfield store (empty step list) with a bad `PayloadSubfolderName` throws on the first cache call (not at construction).
- Step-list validation: `TargetVersion <= 0` throws `ArgumentException`; duplicate `TargetVersion` values across steps throw `ArgumentException`; the highest-`TargetVersion` step having `TargetSubfolder != PayloadSubfolderName` throws `ArgumentException`; any step after the first (ordered by `TargetVersion`) having `SourceSubfolder != previous.TargetSubfolder` throws `ArgumentException`; any non-highest step having `TargetSubfolder == PayloadSubfolderName` throws `ArgumentException` (final-target uniqueness). Single-step and empty step lists pass validation (no chain to break; empty list also has no final target).
- Cache parent does not exist on disk → migrator creates it before reading/writing the marker; state machine still evaluates source/target as absent.

### Additions to `SqliteImageCacheStoreTests`

- Migration declaration is `[(1, "ImageCache", "Images")]` and `PayloadSubfolderName == "Images"`.
- Constructor accepts the new `IAppFolderLocator` argument (verified via DI registration test if one exists; otherwise via direct construction).

### Additions to `SqliteCacheStoreBaseTests`

- `EnsureInitializedLockedAsync` calls the folder migrator **once per successful initialization**. A failed init (e.g. migrator returned failure, or `OpenAndPrepareConnectionAsync` threw) does not mark the store as initialized, so a subsequent call re-runs the migrator. The contract is "we don't migrate after we've successfully opened," not "we run the migrator exactly once over the store's lifetime."
- Migrator returning failure causes `EnsureInitializedLockedAsync` to return `null` *without* creating the target folder or opening the DB.

### Test infrastructure

A small `FixedAppFolderLocator(string cacheFolder) : IAppFolderLocator` test double in `Hypersonic.Tests/Services/Cache/`. Each test creates a per-fixture temp directory and passes it to the locator. Tests that need to simulate a pre-existing pre-migration source pre-populate `<temp>/<SourceSubfolder>/` before constructing the store.

---

## Migration of existing test code

Tests that today construct stores with `new ImageCacheOptions { RootPath = tempDir }` change to:

```csharp
var locator = new FixedAppFolderLocator(tempDir);
var store = new SqliteImageCacheStore(
    Options.Create(new ImageCacheOptions { /* no RootPath */ }),
    locator,
    logService);
```

Cache files now land under `tempDir/Images/cache.db` instead of `tempDir/cache.db`. Tests that hard-code the DB path string need a one-line update; tests that go through the store's public surface need no change.

---

## Boundaries with adjacent designs

- **Generic cache store** ([2026-06-02-generic-cache-store.md](2026-06-02-generic-cache-store.md)): This design extends that base. The base's structure (gate, schema migration, payload lifecycle) is unchanged; only the path-resolution and init-prologue surfaces grow.
- **Lyrics cache (Phase 4.5)** and **audio cache (Phase 5.3)**: Both ship empty `PayloadFolderMigrationSteps`. Their `PayloadSubfolderName` is whatever short name they pick (`"Lyrics"`, `"Audio"`); no metadata file is written until a future migration step adds one.
- **Other application folders** (logs, settings, AppState): `IAppFolderLocator` is shaped to accommodate `GetLogsFolder()` / `GetDataFolder()` follow-ups, but **this PR does not retrofit `FileSink`, `LocalSettingsService`, or `AppStateService`**. Their current branching stays in place and is migrated separately when convenient.

---

## Deferred / out of scope

- **Retrofitting non-cache folders to `IAppFolderLocator`.** Tracked as separate, additive changes after this lands.
- **Cross-store coordination** (e.g. moving the cache parent itself to a new location). Out of scope; the locator's contract is "tell me the parent," not "let me change it at runtime."
- **GUI for resolving conflict cases.** A warning log is the operator-facing signal today. A Settings-page surface for "X.json says you have a pending migration conflict — resolve it" is a future enhancement.

---

## References

- Generic cache store design: [`docs/design/service/2026-06-02-generic-cache-store.md`](2026-06-02-generic-cache-store.md)
- Generic cache store implementation: [`docs/implementation/service/2026-06-02-generic-cache-store.md`](../../implementation/service/2026-06-02-generic-cache-store.md)
- Current image cache path resolution: [`Hypersonic/Services/ImageCachePath.cs`](../../../Hypersonic/Services/ImageCachePath.cs)
- Current cache base: [`Hypersonic/Services/Cache/SqliteCacheStoreBase.cs`](../../../Hypersonic/Services/Cache/SqliteCacheStoreBase.cs)
- Current image cache subclass: [`Hypersonic/Services/SqliteImageCacheStore.cs`](../../../Hypersonic/Services/SqliteImageCacheStore.cs)
- SQL schema migration driver (pattern reference): [`Hypersonic/Services/Cache/SqliteCacheSchemaDriver.cs`](../../../Hypersonic/Services/Cache/SqliteCacheSchemaDriver.cs)
