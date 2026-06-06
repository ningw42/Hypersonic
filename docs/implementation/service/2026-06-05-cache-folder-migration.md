# Cache Folder Migration — Implementation Details

Companion to [docs/design/service/2026-06-05-cache-folder-migration.md](../../design/service/2026-06-05-cache-folder-migration.md). This document records what was actually built, the decisions made during implementation, and the deviations from the original design with their justifications. Landed in commit `ff5d012f654b235a5b9aa4d1f8c7d5d41b947ec5`.

The design covers the shape of the mechanism (the `IAppFolderLocator` seam, the symmetric folder-migration driver, the state-machine matrix, and the per-install walkthrough). This doc does **not** repeat any of that. It focuses on the parts the design left unspecified or where the code took a different path, so the two read as a pair.

---

## Files Changed

The set of files matches the design's architecture diagram almost exactly. New files:

| File | Purpose |
|---|---|
| [Hypersonic/Contracts/Services/IAppFolderLocator.cs](../../../Hypersonic/Contracts/Services/IAppFolderLocator.cs) | One-method interface (`GetCacheFolder()`) — the authoritative cache-parent source. |
| [Hypersonic/Services/AppFolderLocator.cs](../../../Hypersonic/Services/AppFolderLocator.cs) | Production impl. The only place under the cache subsystem that branches on `RuntimeHelper.IsMSIX`; the MSIX branch the deleted `ImageCachePath` owned now lives here. |
| [Hypersonic/Services/Cache/PayloadFolderMigrationStep.cs](../../../Hypersonic/Services/Cache/PayloadFolderMigrationStep.cs) | `record (int TargetVersion, string SourceSubfolder, string TargetSubfolder)` — one declared rename. |
| [Hypersonic/Services/Cache/PayloadFolderMetadata.cs](../../../Hypersonic/Services/Cache/PayloadFolderMetadata.cs) | `record (int FolderVersion)` — the `<entity>.json` marker DTO. |
| [Hypersonic/Services/Cache/SqliteCacheFolderMigrator.cs](../../../Hypersonic/Services/Cache/SqliteCacheFolderMigrator.cs) | `internal static` driver. Whole state machine + leaf-name/step-list validation + marker I/O. |
| [Hypersonic.Tests/Services/Cache/FixedAppFolderLocator.cs](../../../Hypersonic.Tests/Services/Cache/FixedAppFolderLocator.cs) | Test `IAppFolderLocator` returning a per-fixture temp dir — replaces the old `RootPath` seam. |
| [Hypersonic.Tests/Services/Cache/RecordingLoggingService.cs](../../../Hypersonic.Tests/Services/Cache/RecordingLoggingService.cs) | Test `ILoggingService` that records warning messages, so conflict/preflight/move-failure paths can be asserted without coupling to message text. |
| [Hypersonic.Tests/Services/Cache/SqliteCacheFolderMigratorTests.cs](../../../Hypersonic.Tests/Services/Cache/SqliteCacheFolderMigratorTests.cs) | State-machine matrix + ahead-marker recovery + cancellation (19 test methods, one case each). |
| [Hypersonic.Tests/Services/Cache/SqliteCacheFolderMigratorValidationTests.cs](../../../Hypersonic.Tests/Services/Cache/SqliteCacheFolderMigratorValidationTests.cs) | Leaf-name and step-list validation rules (11 test methods; the `Bad*` and `NonPositiveTargetVersion` ones are data-driven, so the runner expands them to more cases). |

Modified files:

| File | Changes |
|---|---|
| [Hypersonic/Services/Cache/SqliteCacheStoreBase.cs](../../../Hypersonic/Services/Cache/SqliteCacheStoreBase.cs) | Constructor takes `IAppFolderLocator`; abstract `ResolvePayloadFolder()` → abstract `PayloadSubfolderName`; new `MetadataFileName` and `PayloadFolderMigrationSteps` virtuals; new `_payloadSubfolderName` captured field; migration phase in `EnsureInitializedLockedAsync`. |
| [Hypersonic/Services/Cache/CacheOptionsBase.cs](../../../Hypersonic/Services/Cache/CacheOptionsBase.cs) | `RootPath` removed; only `MaximumBytes` remains. |
| [Hypersonic/Services/SqliteImageCacheStore.cs](../../../Hypersonic/Services/SqliteImageCacheStore.cs) | Declares `PayloadSubfolderName = "Images"` and the one step `ImageCache → Images`; constructor adds `IAppFolderLocator`. |
| [Hypersonic/App.xaml.cs](../../../Hypersonic/App.xaml.cs) | `services.AddSingleton<IAppFolderLocator, AppFolderLocator>()`. |
| [Hypersonic/Services/ImageCachePath.cs](../../../Hypersonic/Services/ImageCachePath.cs) | **Deleted.** Its MSIX branch moved into `AppFolderLocator`. |
| [Hypersonic.Tests/Services/Cache/TestCacheStore.cs](../../../Hypersonic.Tests/Services/Cache/TestCacheStore.cs) | Configurable `SubfolderName` / `MigrationStepsValue` / `MetadataFileNameValue`; the capture-contract seam `SubfolderNameAfterFirstRead`. |
| [Hypersonic.Tests/Services/Cache/SqliteCacheStoreBaseTests.cs](../../../Hypersonic.Tests/Services/Cache/SqliteCacheStoreBaseTests.cs) | Fixtures inject a `FixedAppFolderLocator`; 3 new migrator-integration tests. |
| [Hypersonic.Tests/Services/SqliteImageCacheStoreTests.cs](../../../Hypersonic.Tests/Services/SqliteImageCacheStoreTests.cs) | Fixtures inject the locator; `CacheFolderPath_ComposesCacheParentWithImagesSubfolder`, `LegacyImageCacheFolder_IsMigratedToImagesOnFirstLaunch`. |
| [Hypersonic.Tests/Services/ImageCacheServiceTests.cs](../../../Hypersonic.Tests/Services/ImageCacheServiceTests.cs) | Fixtures inject the locator instead of setting `RootPath`. |

`Hypersonic.Core` is untouched, as the design predicted — the locator is a WinUI-layer concern (it reaches `ApplicationData.Current` and `RuntimeHelper.IsMSIX`).

---

## Design Deviations

### A marker *ahead* of the highest declared step is treated as untrustworthy and re-evaluated

The design's state-machine matrix and per-install walkthrough were described as exhaustive, but neither covers the case where the on-disk marker's `FolderVersion` is **greater** than the highest declared `TargetVersion`. The implementation adds an explicit branch before the step loop ([SqliteCacheFolderMigrator.cs:76-90](../../../Hypersonic/Services/Cache/SqliteCacheFolderMigrator.cs#L76-L90)): if `currentVersion > highestVersion`, it logs a warning and resets `currentVersion = 0` **in memory**, re-running the whole state table from scratch. Because the table is idempotent, the "fresh install", "already migrated externally", and "move" branches reconcile against real on-disk state. The on-disk marker is only rewritten down to the highest declared version on a branch that actually reaches `BumpMarkerAsync` — i.e. the no-op and successful-move paths ([SqliteCacheFolderMigrator.cs:108](../../../Hypersonic/Services/Cache/SqliteCacheFolderMigrator.cs#L108), [:119](../../../Hypersonic/Services/Cache/SqliteCacheFolderMigrator.cs#L119), [:155](../../../Hypersonic/Services/Cache/SqliteCacheFolderMigrator.cs#L155)). A re-evaluation that instead ends on a conflict or failure branch returns *before* any marker write ([:134](../../../Hypersonic/Services/Cache/SqliteCacheFolderMigrator.cs#L134), [:152](../../../Hypersonic/Services/Cache/SqliteCacheFolderMigrator.cs#L152), [:168](../../../Hypersonic/Services/Cache/SqliteCacheFolderMigrator.cs#L168), [:177](../../../Hypersonic/Services/Cache/SqliteCacheFolderMigrator.cs#L177)), so the ahead value persists on disk until a later run resolves the conflict.

**Why:** Two realistic ways a marker can run ahead — a *downgrade* (the user installs an older build after a newer one migrated the folder further) and a *corrupt/tampered* marker. Trusting an ahead marker means `step.TargetVersion <= currentVersion` skips every step, so a legacy install whose data still sits in an old source folder would never get moved, and the store would open an empty current folder while the data is stranded next to it. Degrading to 0 is the same safe posture already used for an *unparseable* marker, and the idempotent branches guarantee no double-move. Pinned by `MarkerAheadOfDeclaredSteps_WithLegacySource_ReEvaluatesAndMigrates` (ahead marker + un-migrated source → migrates and clamps) and `MarkerAheadOfDeclaredSteps_AlreadyMigrated_PreservesDataAndClampsMarker` (ahead marker + already-correct on-disk state → no move, marker clamped down).

### Cache-parent creation failure is a fourth fail-init cause

The design enumerated exactly three migrator-returns-`false` causes (`Directory.Move` threw, preflight detected an ambiguous chain, pre-final conflict) and described the entry-point `Directory.CreateDirectory(parent)` only as a bootstrap step — silent on what happens if it throws. The implementation wraps that call in `try`/`catch (IOException or UnauthorizedAccessException)` and returns `false` on failure ([SqliteCacheFolderMigrator.cs:59-70](../../../Hypersonic/Services/Cache/SqliteCacheFolderMigrator.cs#L59-L70)), making it a fourth fail-init cause.

**Why:** The unpackaged parent (`%LOCALAPPDATA%\Hypersonic\Cache`) may not exist on first launch and is created here, before the per-store folder. If a file occupies the parent path or ACLs deny access, the right outcome is the cache's existing "unavailable, safe defaults" state — not a raw `IOException` escaping init. Folding it into the same failure contract as a `Directory.Move` failure keeps a single, uniform failure model rather than letting one I/O site throw while the others degrade. Pinned by `CacheParentCannotBeCreated_FailsInitGracefullyWithoutThrowing`. (The `EnsureInitializedLockedAsync` comment still lists the original three causes; the parent-creation case is the implicit fourth.)

### The validated subfolder name is captured into a field, not re-read from the virtual

The design's `EnsureInitializedLockedAsync` snippet passed `PayloadSubfolderName` (a fresh virtual read) straight to the migrator, and described `MetadataFileName`'s default as `PayloadSubfolderName + ".json"` (another fresh read). The implementation promotes the validated name to a new `_payloadSubfolderName` field, set exactly once inside `EnsureCacheFolderPath()` ([SqliteCacheStoreBase.cs:1284-1303](../../../Hypersonic/Services/Cache/SqliteCacheStoreBase.cs#L1284-L1303)). The migrator call passes the captured field (`_payloadSubfolderName!`), and `MetadataFileName`'s default getter calls `EnsureCacheFolderPath()` then returns `_payloadSubfolderName + ".json"` ([SqliteCacheStoreBase.cs:86-93](../../../Hypersonic/Services/Cache/SqliteCacheStoreBase.cs#L86-L93)).

**Why:** `PayloadSubfolderName` is a `protected virtual`/`abstract` member — nothing forces a subclass getter to be a constant. If the value were read fresh at each use site, a getter that returned different values over time could make three things diverge: the folder the store actually opens, the final-target the migrator validates against, and the marker file name. Reading the virtual once and threading the captured value end-to-end makes "validate once, use everywhere" structural rather than conventional. The contract is verified by a dedicated test seam — `TestCacheStore.SubfolderNameAfterFirstRead` returns a *different* name on every read after the first — and the test `FolderMigrator_UsesSubfolderNameCapturedDuringPathResolution` asserts the store still resolves to the originally-captured folder. The commit message frames this capture as the intended design; the design doc's code sketch hadn't yet encoded it.

### Greenfield stores skip declaration validation entirely

The design said the migrator validates `SourceSubfolder`, `TargetSubfolder`, and `MetadataFileName` "at its entry point on every init call." The implementation short-circuits `steps.Count == 0` and returns `true` **before** any validation runs ([SqliteCacheFolderMigrator.cs:38-42](../../../Hypersonic/Services/Cache/SqliteCacheFolderMigrator.cs#L38-L42)). So a greenfield store (empty step list) with a malformed `MetadataFileName` override is never validated by the migrator.

**Why:** A greenfield store writes no marker file, so `MetadataFileName` has no on-disk consequence — validating it would reject a value that is never used. `PayloadSubfolderName` is still validated independently in `EnsureCacheFolderPath()` regardless of step count, so the only folder path a greenfield store actually touches is still guarded against separators / rooted paths / traversal. The net effect: the single field left unvalidated for greenfield stores is the one field that has no effect for greenfield stores.

---

## Implementation Notes

### `BumpMarkerAsync` advances the in-memory version even when the on-disk write fails

The design said only "failed marker writes are logged; next launch re-evaluates." The mechanism that makes the *current* run keep going is that `BumpMarkerAsync` returns the target `version` unconditionally ([SqliteCacheFolderMigrator.cs:319-346](../../../Hypersonic/Services/Cache/SqliteCacheFolderMigrator.cs#L319-L346)) — even down the `catch (IOException or UnauthorizedAccessException)` path where the temp write or `File.Move` failed. The on-disk move has already happened by this point, so in-memory the loop must proceed to the next step against the moved data; persistence of the marker is best-effort. Correctness on the next launch relies entirely on the idempotent state table: with the marker still low, the next run sees "source missing, target present" and bumps without re-moving. `MarkerWriteFailure_DoesNotStrandData_AndRecoversNextRun` exercises exactly this.

### Cancellation checkpoints and re-throw semantics

The driver honors the token at four points for a non-empty migration: right after declaration validation (the entry checkpoint), at the top of each step iteration, immediately before each `Directory.Move`, and inside both marker-I/O helpers. (A greenfield store with an empty step list returns at the short-circuit *before* the entry checkpoint, so it never observes cancellation — there is no I/O for it to abort.) The two marker helpers `catch (OperationCanceledException) { throw; }` *ahead* of their broad `catch` ([SqliteCacheFolderMigrator.cs:307-310](../../../Hypersonic/Services/Cache/SqliteCacheFolderMigrator.cs#L307-L310), [:328-332](../../../Hypersonic/Services/Cache/SqliteCacheFolderMigrator.cs#L328-L332)) so a cancel during marker read is **not** swallowed into the "unreadable → version 0" degrade, and a cancel during marker write deletes the temp file before propagating. `Directory.Move` itself can't be interrupted mid-flight, so the pre-move check is the last safe abort point. `Migrator_HonorsCancellationBeforeMoving` pins the pre-move checkpoint.

### Preflight collects *all* existing downstream folders for the warning

`FindExistingDownstreamTargets` ([SqliteCacheFolderMigrator.cs:274-292](../../../Hypersonic/Services/Cache/SqliteCacheFolderMigrator.cs#L274-L292)) returns a `List<string>` of every strictly-later step's `TargetSubfolder` that exists on disk, not just the first one found. The list is included verbatim in the "ambiguous chain state" warning so an operator sees every folder implicated in the stall at once. It relies on `orderedSteps` being sorted ascending by `TargetVersion` (already validated unique), so "index > currentIndex" is exactly "strictly later in the chain." Single-step chains (the image cache) have no later indices, so this is a no-op there.

### `ValidateSteps` re-checks step leaf names on every init, in addition to structural rules

Beyond the positive/unique `TargetVersion`, final-target-match, chain-continuity, and final-target-uniqueness rules from the design, `ValidateSteps` also runs `ValidateLeafName` over each step's `SourceSubfolder` and `TargetSubfolder` ([SqliteCacheFolderMigrator.cs:235-236](../../../Hypersonic/Services/Cache/SqliteCacheFolderMigrator.cs#L235-L236)). This is the same leaf-name guard the base applies to `PayloadSubfolderName`, but applied to the *declared* source/target so a typo'd `"..\\Images"` in a step throws an `ArgumentException` at the migrator entry rather than escaping the cache parent during a `Directory.Move`. `ValidateLeafName` itself lives on the migrator as `internal static` and is the single shared implementation the base reuses.

### Subfolder-name comparisons are case-insensitive

Every subfolder-name comparison in the migrator uses `StringComparison.OrdinalIgnoreCase`: identity-step detection ([SqliteCacheFolderMigrator.cs:106](../../../Hypersonic/Services/Cache/SqliteCacheFolderMigrator.cs#L106)), final-target conflict detection ([:160](../../../Hypersonic/Services/Cache/SqliteCacheFolderMigrator.cs#L160)), and the three structural validations — final-target match ([:242](../../../Hypersonic/Services/Cache/SqliteCacheFolderMigrator.cs#L242)), chain continuity ([:252](../../../Hypersonic/Services/Cache/SqliteCacheFolderMigrator.cs#L252)), and final-target uniqueness ([:264](../../../Hypersonic/Services/Cache/SqliteCacheFolderMigrator.cs#L264)). This matches the default casing behavior of the Windows/NTFS filesystem the app targets. The consequence worth flagging for future migrations: a step whose `SourceSubfolder` and `TargetSubfolder` differ **only** in casing (e.g. `imagecache` → `ImageCache`) is treated as an identity step — no `Directory.Move` runs, the marker just bumps — so a casing-only rename cannot be carried out through this mechanism. It also means two declared subfolders that differ only in case are considered the *same* folder for chain-continuity and final-target checks.

### `RecordingLoggingService` is new test infrastructure beyond the design

The design's test strategy only named `FixedAppFolderLocator`. The conflict / preflight / move-failure branches are observable only through warning logs (they intentionally touch no folders), so the implementation adds a `RecordingLoggingService` that captures warning messages into a concurrent queue. Tests assert a warning *was* emitted on those paths without asserting exact wording, keeping the message text free to change.

### The image store's two migration concepts stay independent

`SqliteImageCacheStore` declares `MigrationSteps => Array.Empty<MigrationStep>()` (SQL schema deltas — none, the v0→v1 bootstrap is the schema driver's special case) while `PayloadFolderMigrationSteps` declares the single `ImageCache → Images` folder step. The two migration axes share no state: schema version lives in `PRAGMA user_version` inside the DB, folder version lives in `Images.json` at the parent. The legacy-manifest discard (`OnBeforeOpenConnection`) is unchanged from the generic-cache-store work and now simply runs at the post-migration `Images/` location, because the folder move completes earlier in `EnsureInitializedLockedAsync`.

### Init order, as built

`EnsureInitializedLockedAsync` runs: (1) `EnsureCacheFolderPath()` resolves and validates the subfolder name and captures `_payloadSubfolderName`; (2) `SqliteCacheFolderMigrator.EnsurePayloadFolderAsync(...)` runs and, on `false`, returns `null` *before* any folder is created or DB opened; (3) `Directory.CreateDirectory(cacheFolderPath)`; (4) `OnBeforeOpenConnection(...)`; (5) `OpenAndPrepareConnectionAsync(...)` (PRAGMA setup, `SqliteCacheSchemaDriver`, startup temp-file sweep). This matches the design's placement constraints — folder migration before `CreateDirectory` (so the empty target doesn't mask the "source exists, target absent" signal) and before the connection opens (the DB lives inside the folder being moved).

---

## Verification Results

### Build

`dotnet build Hypersonic.Tests -p:Platform=x64` (which transitively builds `Hypersonic`, `Hypersonic.Core`, and `Hypersonic.Client`) compiles clean. As with the generic-cache-store work, the wider build may surface pre-existing analyzer warnings; warning-clean remains a separate follow-up.

### Automated tests

Observed run: `dotnet test Hypersonic.Tests -p:Platform=x64 --filter "FullyQualifiedName~Cache|FullyQualifiedName~ImageCache"` → **147 passed, 0 failed, 0 skipped** (≈3 s). This covers `SqliteCacheFolderMigratorTests` (state-machine matrix, ahead-marker recovery, cancellation), `SqliteCacheFolderMigratorValidationTests` (leaf-name + step-list rules), the 3 new `SqliteCacheStoreBaseTests` integration cases, the 2 new `SqliteImageCacheStoreTests`, and the pre-existing base/image/service tests now driven through `FixedAppFolderLocator` instead of `RootPath`.

| Test project | Result |
|---|---|
| `Hypersonic.Core.Tests` | Not affected by this change (no Core edits). |
| `Hypersonic.Client.UnitTest` | Not affected. |
| `Hypersonic.Tests` | Cache/image filter: 147 passed, 0 failed (the full project was not run in this pass — only the `Cache`/`ImageCache` filter). |

### Migrator state-machine coverage (`SqliteCacheFolderMigratorTests`)

| Test | Scenario |
|---|---|
| `FreshInstall_SourceAndTargetAbsent_BumpsMarkerWithoutMoving` | Fresh install → marker bumps, no move. |
| `ExistingSource_TargetAbsent_MovesDirectoryAndBumpsMarker` | Pre-migration source → `Directory.Move` + bump. |
| `SourceAbsent_TargetPresent_MarkerAbsent_BumpsMarkerWithoutMoving` | Already-migrated-externally → bump, no move. |
| `FinalTargetConflict_WarnsAndContinuesWithoutBumping` | Both folders present, final step → warn, no bump, init continues on existing target. |
| `PreFinalTargetConflict_FailsInitWithoutCreatingFinalTarget_ThenRecovers` | Pre-final conflict → fail init, final target uncreated, recovers after resolution. |
| `Preflight_DownstreamTargetExistsPopulated_FailsInit_ThenRecovers` | Populated downstream target → preflight fails init before any move. |
| `Preflight_DownstreamTargetExistsEmpty_FailsInit` | Empty downstream target → same outcome (`Directory.Exists`, not content). |
| `IdentityStep_NoMove_BumpsMarker` | `Source == Target` → no move, bump. |
| `MultiStepChain_AdvancesMarkerThroughEachStep` | Chain advances marker step by step. |
| `MoveFailureMidChain_LeavesMarkerAtLastSuccessfulStep` | Mid-chain move failure → marker at last good step. |
| `DirectoryMoveFailure_FailsInitWithoutCreatingTarget_ThenRecovers` | Move throws → fail init, target uncreated, recovers next call. |
| `MalformedMarker_TreatedAsVersionZero_AndReEvaluatesCleanly` | Garbage marker → v0, clean re-evaluation. |
| `MarkerWriteFailure_DoesNotStrandData_AndRecoversNextRun` | Marker write fails after move → recovers next run. |
| `MarkerAheadOfDeclaredSteps_WithLegacySource_ReEvaluatesAndMigrates` | Ahead marker + legacy source → re-evaluate, migrate, clamp. |
| `MarkerAheadOfDeclaredSteps_AlreadyMigrated_PreservesDataAndClampsMarker` | Ahead marker + already-migrated → no move, marker clamped down. |
| `CacheParentDoesNotExist_MigratorCreatesIt` | Parent absent → migrator creates it, state evaluated as absent. |
| `CacheParentCannotBeCreated_FailsInitGracefullyWithoutThrowing` | Parent creation throws → fail init, no exception escapes. |
| `Migrator_HonorsCancellationBeforeMoving` | Cancel before move → `OperationCanceledException`, no move. |
| `GreenfieldStore_EmptySteps_WritesNoMarker` | Empty steps → no marker file, no on-disk cost. |

### Validation coverage (`SqliteCacheFolderMigratorValidationTests`)

| Test | Scenario |
|---|---|
| `BadPayloadSubfolderName_ThrowsOnFirstCacheCall` | `""`, `"."`, `".."`, `"..\\foo"`, `"foo\\bar"`, `"foo/bar"`, `"C:\\foo"`, `"\\foo"`, invalid chars → throw on first cache call. |
| `BadPayloadSubfolderName_ThrowsOnCacheFolderPathAccess` | `CacheFolderPath` property triggers the same lazy validation. |
| `BadPayloadSubfolderName_DoesNotThrowAtConstruction` | Construction is side-effect-free; validation is lazy. |
| `BadSourceSubfolder_ThrowsOnFirstCacheCall` | Malformed step source → throw at migrator entry. |
| `BadMetadataFileName_ThrowsOnFirstCacheCall` | Malformed marker name → throw (steps present). |
| `NonPositiveTargetVersion_Throws` | `TargetVersion <= 0` → throw. |
| `DuplicateTargetVersion_Throws` | Duplicate versions → throw. |
| `HighestStepTargetNotEqualToSubfolder_Throws` | Final-target mismatch → throw. |
| `ChainDiscontinuity_Throws` | Broken chain → throw. |
| `NonFinalStepTargetingSubfolder_Throws` | Final-target uniqueness violation → throw. |
| `SingleStepList_PassesValidation` | One step (the image-cache shape) passes. |

### Base-integration coverage (`SqliteCacheStoreBaseTests`)

| Test | Scenario |
|---|---|
| `FolderMigratorFailure_ReturnsNullWithoutCreatingTargetOrOpeningDb_ThenRetriesNextCall` | Migrator `false` → init returns null, no folder/DB, retries next call. |
| `FolderMigrator_DoesNotRunAfterSuccessfulInit` | Migrator runs once per *successful* init, not per cache call. |
| `FolderMigrator_UsesSubfolderNameCapturedDuringPathResolution` | Capture-once contract: a mutating `PayloadSubfolderName` getter never surfaces. |

### Image-store coverage (`SqliteImageCacheStoreTests`)

| Test | Scenario |
|---|---|
| `CacheFolderPath_ComposesCacheParentWithImagesSubfolder` | `CacheFolderPath == <parent>/Images`. |
| `LegacyImageCacheFolder_IsMigratedToImagesOnFirstLaunch` | Pre-populated `ImageCache/` → moved to `Images/` on first launch, no data lost. |

---

## References

- Cache folder migration design: [`docs/design/service/2026-06-05-cache-folder-migration.md`](../../design/service/2026-06-05-cache-folder-migration.md)
- Generic cache store design: [`docs/design/service/2026-06-02-generic-cache-store.md`](../../design/service/2026-06-02-generic-cache-store.md)
- Generic cache store implementation: [`docs/implementation/service/2026-06-02-generic-cache-store.md`](2026-06-02-generic-cache-store.md)
- Folder-migration driver: [`Hypersonic/Services/Cache/SqliteCacheFolderMigrator.cs`](../../../Hypersonic/Services/Cache/SqliteCacheFolderMigrator.cs)
- Cache base: [`Hypersonic/Services/Cache/SqliteCacheStoreBase.cs`](../../../Hypersonic/Services/Cache/SqliteCacheStoreBase.cs)
- Image cache subclass: [`Hypersonic/Services/SqliteImageCacheStore.cs`](../../../Hypersonic/Services/SqliteImageCacheStore.cs)
- Folder locator: [`Hypersonic/Services/AppFolderLocator.cs`](../../../Hypersonic/Services/AppFolderLocator.cs)
- SQL schema migration driver (pattern reference): [`Hypersonic/Services/Cache/SqliteCacheSchemaDriver.cs`](../../../Hypersonic/Services/Cache/SqliteCacheSchemaDriver.cs)
