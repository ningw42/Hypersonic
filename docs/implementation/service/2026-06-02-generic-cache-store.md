# Generic SQLite Cache Store — Implementation Details

Companion to [docs/design/service/2026-06-02-generic-cache-store.md](../../design/service/2026-06-02-generic-cache-store.md). This document records what was actually built, decisions made during implementation, and deviations from the original design. Landed in commit `8389314ef271c4b1ac0a032c688bc420c28ec2dc`.

---

## Files Changed

### New files

| File | Purpose |
|---|---|
| [Hypersonic/Services/Cache/ICacheEntry.cs](../../../Hypersonic/Services/Cache/ICacheEntry.cs) | Three-field contract (`FileName`, `Length`, `LastAccessUtc`) every `TEntry` must expose. Also documents the matching column-name convention. |
| [Hypersonic/Services/Cache/CacheOptionsBase.cs](../../../Hypersonic/Services/Cache/CacheOptionsBase.cs) | Shared options base (`RootPath`, `MaximumBytes`). Per-entity options classes inherit and add their own policy knobs. |
| [Hypersonic/Services/Cache/CacheStoreStats.cs](../../../Hypersonic/Services/Cache/CacheStoreStats.cs) | `record (long TotalBytes, int EntryCount)` — single shared stats shape across entities. |
| [Hypersonic/Services/Cache/MigrationStep.cs](../../../Hypersonic/Services/Cache/MigrationStep.cs) | `record (int TargetVersion, string Sql)` — one declared delta step. |
| [Hypersonic/Services/Cache/PayloadWriteContext.cs](../../../Hypersonic/Services/Cache/PayloadWriteContext.cs) | `record (long Length, DateTimeOffset WrittenAtUtc)` — handed to the streamed-write `entryFactory` after a successful temp-write. |
| [Hypersonic/Services/Cache/SqliteCacheSchemaDriver.cs](../../../Hypersonic/Services/Cache/SqliteCacheSchemaDriver.cs) | Static driver: reads `PRAGMA user_version`, applies the v0→current shortcut, runs incremental deltas, or rebuilds when ahead of current. All inside a single outer transaction. |
| [Hypersonic/Services/Cache/SqliteCacheStoreBase.cs](../../../Hypersonic/Services/Cache/SqliteCacheStoreBase.cs) | Generic `SqliteCacheStoreBase<TEntry>` — connection lifecycle, gate, self-heal, dispose drain, stats/clear/flush, buffered+streamed payload write, eviction, reconcile, startup temp-file sweep. ~1450 lines. |
| [Hypersonic.Tests/Services/Cache/TestCacheEntry.cs](../../../Hypersonic.Tests/Services/Cache/TestCacheEntry.cs) | Minimal `ICacheEntry` impl with only the three mandated fields, for base tests. |
| [Hypersonic.Tests/Services/Cache/TestCacheOptions.cs](../../../Hypersonic.Tests/Services/Cache/TestCacheOptions.cs) | Empty `CacheOptionsBase` subclass for base tests. |
| [Hypersonic.Tests/Services/Cache/TestCacheStore.cs](../../../Hypersonic.Tests/Services/Cache/TestCacheStore.cs) | Minimal `SqliteCacheStoreBase<TestCacheEntry>` subclass — four-column schema (`KeyHash`, `FileName`, `Length`, `LastAccessUtc`), `.bin` extension predicate. Adds two test-only failure-injection switches (`ThrowOnEnumerateAll`, `ThrowOnGetEvictionCandidates`). |
| [Hypersonic.Tests/Services/Cache/SqliteCacheStoreBaseTests.cs](../../../Hypersonic.Tests/Services/Cache/SqliteCacheStoreBaseTests.cs) | 37 tests covering schema lifecycle, self-heal, gate semantics, buffered + streamed writes, eviction (incl. negative-row protection), reconcile (incl. abort-on-enumeration-failure), startup temp sweep, post-write-eviction failure isolation. |

### Modified files

| File | Changes |
|---|---|
| [Hypersonic/Services/SqliteImageCacheStore.cs](../../../Hypersonic/Services/SqliteImageCacheStore.cs) | Rewritten as a subclass of `SqliteCacheStoreBase<ImageCacheEntry>`. Now ~330 lines (was ~970). Provides only image-specific bits: entries-table schema SQL, column mapping, image extension predicate, legacy `manifest.json` discard. No longer owns connection lifecycle, gate, schema driver, dispose, or payload-write mechanics. |
| [Hypersonic/Services/CachedImageService.cs](../../../Hypersonic/Services/CachedImageService.cs) | Slimmed to ~460 lines (was ~720). Drops `_writeGate`, in-line atomic file-write dance, `EvictIfNeededAsync`, `ReconcileAsync` (the body — public surface delegates to store), `TryDeleteFile`. Keeps resolver chain, in-flight dedup, TTL check, stale fallback, dispose cancellation. |
| [Hypersonic/Contracts/Services/IImageCacheStore.cs](../../../Hypersonic/Contracts/Services/IImageCacheStore.cs) | Added `CacheFolderPath` property, `WritePayloadAsync`, `TryGetPayloadUriAsync`, `ReconcileAsync`, `EvictIfNeededAsync`. Removed `GetEntriesAsync`, `GetEvictionCandidatesAsync`, `ImageCacheStoreStats`, `ImageCacheStoreEntry`. Stats now use the generic `CacheStoreStats`. |
| [Hypersonic/Contracts/Services/IImageService.cs](../../../Hypersonic/Contracts/Services/IImageService.cs) | `ImageCacheStats` lost its `CacheFolderPath` field. The path lives on `IImageCacheStore.CacheFolderPath` now. |
| [Hypersonic/Models/ImageCacheOptions.cs](../../../Hypersonic/Models/ImageCacheOptions.cs) | Now inherits `CacheOptionsBase`. `RootPath` and `MaximumBytes` move up to the base; `TimeToLiveDays` stays. Default `MaximumBytes` (256 MiB) set in the constructor since the base property has no default. |
| [Hypersonic/Services/ImageCachePath.cs](../../../Hypersonic/Services/ImageCachePath.cs) | Three new comment lines spelling out the resolved paths. No behavior change. |
| [Hypersonic/Services/SqliteImageCacheSchema.cs](../../../Hypersonic/Services/SqliteImageCacheSchema.cs) | **Deleted.** Schema SQL inlined into `SqliteImageCacheStore.CreateCurrentSchemaSql`; migration loop absorbed by `SqliteCacheSchemaDriver`. |
| [Hypersonic.Tests/Services/SqliteImageCacheStoreTests.cs](../../../Hypersonic.Tests/Services/SqliteImageCacheStoreTests.cs) | Added `CacheFolderPath_ReturnsConfiguredRootPath`. Removed `GetEntriesAsync_ThrowsWhenStoreCannotEnumerate` (the throw-on-failure contract moved to `EnumerateAllEntriesLockedAsync`, exercised in the base tests). Replaced `SqliteImageCacheSchema.CurrentSchemaVersion` references with the literal `1`. |
| [Hypersonic.Tests/Services/ImageCacheServiceTests.cs](../../../Hypersonic.Tests/Services/ImageCacheServiceTests.cs) | Added `ImageCacheStats_DoesNotExposePhysicalCacheFolder` (reflection-based privacy assertion). Removed `ReconcileAsync_AbortsWithoutDeletingFiles_WhenStoreEnumerationFails` — equivalent coverage is now in `SqliteCacheStoreBaseTests.ReconcileAsync_AbortsWithoutDeletingFilesWhenEnumerationFails`. Removed `Dispose_CancelsRunningReconcileBeforeDisposingGate`; there is no direct base-test equivalent — the gate now lives on the store and the dispose-drain-while-gated-work-is-pending path is covered structurally (gate `Wait` with `DisposeDrainTimeout`) but not by an analogous "dispose during a long reconcile" test. Removed the `BlockingGetEntriesStore` and `ThrowingGetEntriesStore` hand-rolled fakes — `IImageCacheStore` no longer exposes the methods they were impersonating. |
| [Hypersonic.Tests/ViewModels/SettingsViewModelGeneralTests.cs](../../../Hypersonic.Tests/ViewModels/SettingsViewModelGeneralTests.cs) | Updated `ImageCacheStats` constructor calls to the two-arg form. |
| [Hypersonic.Tests/ViewModels/SettingsViewModelTestDoubles.cs](../../../Hypersonic.Tests/ViewModels/SettingsViewModelTestDoubles.cs) | Same — `ImageCacheStats` is now `(TotalBytes, EntryCount)`. |

---

## Design Deviations

### `PayloadWriteContext` does not carry `FileName`

The design spec defined the context as `(string FileName, long Length, DateTimeOffset WrittenAtUtc)`. The implementation is `(long Length, DateTimeOffset WrittenAtUtc)` — `FileName` is dropped.

**Why:** The base cannot know the final filename without asking the subclass, and the subclass knows it as soon as it has `Length` (since the extension comes from the bytes or from prior subclass state, not from the base). Threading a "pretend `FileName`" through the context only to have the factory overwrite it would be ceremony. The factory now sets `FileName` on the returned `TEntry` directly; the base reads `entry.FileName` after the factory returns and uses it as the rename target. Reflected in [SqliteCacheStoreBase.cs:665-676](../../../Hypersonic/Services/Cache/SqliteCacheStoreBase.cs#L665-L676) — the base validates that `entry.FileName` is non-empty after calling the factory.

### Public DB methods live on the base, not on the subclass

The design described every subclass DB method existing in two forms: a public gate-acquiring variant (preserving today's signatures on `SqliteImageCacheStore`) and a `*LockedAsync` sibling for the base's internal use.

The implementation makes the public variants live on the **base**, not the subclass. `SqliteImageCacheStore` overrides only the six `*LockedAsync` abstracts. `GetEntryAsync`, `TouchEntryAsync`, `UpsertEntryAsync`, `RemoveEntryAsync`, `GetEvictionCandidatesAsync`, and the newly-added `EnumerateAllEntriesAsync` are all in [SqliteCacheStoreBase.cs](../../../Hypersonic/Services/Cache/SqliteCacheStoreBase.cs).

**Why:** Every public wrapper is identical: take the gate, ensure init, delegate to the `*LockedAsync` variant, catch `SqliteException`, release the gate. Duplicating that body into each subclass guarantees three near-identical copies for image/lyrics/audio. Pushing it to the base costs nothing (the base already calls all the abstracts internally) and means new entities only write the `*LockedAsync` half. `IImageCacheStore` still declares the consumer-facing signatures so the contract from consumer code is unchanged; the implementation just happens to be inherited.

### `EnumerateAllEntriesAsync` gets a public wrapper

The design only specced the `*LockedAsync` form, used internally by `ReconcileAsync`. Implementation also exposes the public gate-acquiring wrapper (with the standard warn-and-return-empty contract).

**Why:** Tests need a way to introspect the row set without reaching into protected members. `SerializeStoreEntriesAsync` in `ImageCacheServiceTests` switched from the now-removed `GetEntriesAsync` to `EnumerateAllEntriesAsync`. The wrapper is the same five-line pattern as the other public DB methods.

### `EvictIfNeededLockedAsync` is virtual, swallows `SqliteException` internally

The design table said audio would override `GetEvictionCandidatesAsync` to filter `Pinned=0`, and `EvictIfNeededAsync` would call it. Implementation makes the **entire** `EvictIfNeededLockedAsync` `protected virtual` so future entities can replace the whole eviction strategy (not just the candidate list). More importantly, the method explicitly catches every `SqliteException` from its internal stats query, candidate enumeration, row deletion, and WAL checkpoint — none propagate.

**Why:** `EvictIfNeededLockedAsync` is the last step of every buffered/streamed write. If a DB hiccup during eviction propagated, a successful payload write would turn into a user-visible exception even though the write itself committed. The contract is "post-write eviction is best-effort; on failure log and move on, the cache stays over-budget for one cycle." The `WritePayloadAsync_DoesNotThrowWhenPostWriteEvictionFails` and `WriteStreamedPayloadAsync_DoesNotThrowWhenPostWriteEvictionFails` tests pin this behavior using `TestCacheStore.ThrowOnGetEvictionCandidates`.

### `OnBeforeOpenConnection` is a general subclass hook, not image-only

The design treated the legacy `manifest.json` discard as an image-only "stays as one-off" item in the responsibility table — implying it would live as a private helper inside `SqliteImageCacheStore`.

Implementation generalizes it: [SqliteCacheStoreBase.cs:1239-1241](../../../Hypersonic/Services/Cache/SqliteCacheStoreBase.cs#L1239-L1241) defines a `protected virtual void OnBeforeOpenConnection(string, string)` hook that runs after the folder exists and before the connection opens. The image subclass overrides it for the manifest sweep.

**Why:** Future entities may have their own one-off init quirks (e.g. legacy file formats, version-1 file-naming changes). Having a documented hook beats each subclass reaching into the init sequence by accident. Cost is one virtual call and four lines of base code.

### `IImageCacheStore` exposes `CacheFolderPath`; `ImageCacheStats` drops it

The design didn't explicitly cover where the cache folder path is exposed. Today's `ImageCacheStats` record carried a `CacheFolderPath` string field that no UI consumed — `SettingsViewModel` reads `TotalBytes` / `EntryCount` only. The implementation removes the field from the stats record and adds a `CacheFolderPath` property to `IImageCacheStore` instead.

**Why:** The path was redundant payload on the settings refresh path (which re-pulls stats on construction and after clear) for a value no caller used. Worse, having it on the stats record made it easy for new consumers — or accidental logging / telemetry — to leak the absolute filesystem path. The test `ImageCacheStats_DoesNotExposePhysicalCacheFolder` uses reflection to keep that surface gone. Legitimate callers that need the folder go through `store.CacheFolderPath`; `service.ClearAsync` logs it explicitly when it clears.

### `MigrationSteps` for the image cache is empty; v0→v1 is special-cased

The design said the image cache would declare its v0→v1 step in `MigrationSteps`. Implementation: `MigrationSteps` is `Array.Empty<MigrationStep>()`, and the schema driver special-cases v0 by running `CreateCurrentSchemaSql` directly ([SqliteCacheSchemaDriver.cs:40-47](../../../Hypersonic/Services/Cache/SqliteCacheSchemaDriver.cs#L40-L47)).

**Why:** Greenfield entities (lyrics, audio) start at v1 with no deltas. If v0→v1 were required in `MigrationSteps`, every new entity would duplicate its full schema across `CreateCurrentSchemaSql` *and* a one-step `MigrationSteps` entry. The special case keeps the contract "deltas only describe bumps from `v >= 1`; bootstrapping is always `CreateCurrentSchemaSql`."

### Streamed-write temp-file pattern is matched strictly, not loosely

The design said `IsKnownPayloadFileName` must exclude `.tmp` files so the startup sweep and reconcile don't race with in-flight writers. Implementation goes further: the sweep predicate `IsStreamedWriteTempFileName` ([SqliteCacheStoreBase.cs:1321-1369](../../../Hypersonic/Services/Cache/SqliteCacheStoreBase.cs#L1321-L1369)) requires the exact shape `<64-hex>.<32-hex>.tmp` (101 chars total). Any other `.tmp` file is left alone by the sweep.

**Why:** The cache folder is user-visible (especially in unpackaged dev). Stomping on `scratch.tmp` or any other tooling artifact a user happened to drop in the folder would be a hostile surprise. The streamed writer always produces the exact `<keyHash>.<guid:N>.tmp` shape, so the strict predicate is both safe and sufficient. Asserted by `StartupTempFileSweep_DeletesStreamedWriterTempsAndLeavesEverythingElseAlone`.

### Streamed write takes the gate briefly for warm-up init

The design described the streamed write as running its writer "outside the gate" with the gate re-acquired only for the rename + UPSERT. The implementation adds a third gate acquisition at the **start**: a brief warm-up that calls `EnsureInitializedLockedAsync`, then releases the gate before opening the temp file.

**Why:** The startup temp-file sweep runs inside init. If a streamed writer opened its temp file before init completed, the sweep would delete that temp out from under the live writer. Acquiring the gate for warm-up init guarantees the sweep has finished before any temp file is created. After warm-up the gate is released so the writer body runs uncontended (the original design intent).

### `RemoveEntryAsync` keeps its `flushImmediately` parameter

The design didn't address this. Today's `IImageCacheStore.RemoveEntryAsync(string, bool flushImmediately, …)` parameter is preserved on the base, with the parameter explicitly discarded via `_ = flushImmediately;` and a comment noting "preserved for signature compatibility — SQLite commits each mutation, so there's nothing deferred to flush."

**Why:** The parameter is a contract relic from when removal could be batched. Removing it would be an interface break with no caller-visible upside. Keeping the parameter (as a no-op) lets the base ship without touching call sites.

### Connection pooling is disabled

The design didn't address pooling. The implementation sets `Pooling = false` in the connection string ([SqliteCacheStoreBase.cs:1249](../../../Hypersonic/Services/Cache/SqliteCacheStoreBase.cs#L1249)).

**Why:** Pooling adds nothing for a single-connection cache (one cache → one persistent connection), and disabling it makes `Dispose` deterministic: the WAL/SHM files are released the moment the connection is disposed. `SqliteConnection.ClearAllPools()` is still called as a belt-and-suspenders during dispose and self-heal. This mirrors the pre-refactor image-store behavior.

### No `CachePath.cs`, no `CacheAdminService.cs`

The design's architecture diagram listed `Hypersonic.Services.Cache/CachePath.cs` (helper for greenfield entities) and an optional `Hypersonic/Services/CacheAdminService.cs`. Neither file was created in this commit.

**Why:** `CachePath.cs` has no consumers until lyrics or audio land — `SqliteImageCacheStore` calls `ImageCachePath.Resolve(_options)` directly inside `ResolvePayloadFolder()`. Adding `CachePath.cs` now would create a one-call helper for nothing. `CacheAdminService.cs` was explicitly marked optional in the design ("if/when needed") and no consumer yet aggregates cross-entity stats. Both are deferred to the phases that actually need them.

---

## Implementation Notes

### Dispose drain has three phases

[SqliteCacheStoreBase.cs:1072-1167](../../../Hypersonic/Services/Cache/SqliteCacheStoreBase.cs#L1072-L1167). Each addresses a different in-flight class of work:

1. **Cancel `_disposeCts`** — signals streamed writers (which run outside the gate) to abort. Buffered writes / reads / upserts are gated and don't need a signal; they will finish on their own.
2. **Poll `_inFlightStreams` to zero** — waits up to `DisposeDrainTimeout` (5s) for streamed writers to hit their `finally` blocks and decrement the counter (and delete their temp files). If a writer ignores its token, the timeout elapses and dispose proceeds; the leaked temp is cleaned by the next launch's startup sweep.
3. **Drain the gate** — separate 5s wait for any buffered op already past `_gate.WaitAsync`. The semaphore is **not** disposed; queued waiters may still wake up and observe `_disposed`, and disposing the semaphore under them would surface `ObjectDisposedException` on the benign release path.

The connection is disposed and `SqliteConnection.ClearAllPools()` is called from inside the gate-acquired region so no other thread can be mid-query.

### Buffered writes use a small async buffer; streamed writes use a big one

`WritePayloadAsync` (buffered) opens the temp `FileStream` with `bufferSize: 4096` — the payload is already in memory, so we don't want a second large buffer in front of the disk write. `WriteStreamedPayloadAsync` uses `bufferSize: 81920` to absorb the larger, lumpier flushes typical of network-streaming writers. Both use `useAsync: true`.

### `entryFactory` validation is post-call, not via constraint

The streamed-write `entryFactory` returns a `TEntry`. The base validates the returned entry's `FileName` is non-empty *after* calling the factory ([SqliteCacheStoreBase.cs:668-674](../../../Hypersonic/Services/Cache/SqliteCacheStoreBase.cs#L668-L674)) and throws `InvalidOperationException` if not — the temp file is deleted before throwing. There's no way to express the constraint statically; the throw is loud-fail so a buggy factory shows up in tests rather than silently leaking temps.

### Reconcile's two-pass design avoids enumerating files twice

Pass 1 walks the DB rows, removes any whose payload file is missing, and collects every alive `FileName` into a `HashSet<string>(StringComparer.OrdinalIgnoreCase)`. Pass 2 walks `Directory.EnumerateFiles`, skips anything not matching `IsKnownPayloadFileName`, and deletes any file not in the hash set. The hash set carries the entire "what we expect on disk" intent across the boundary, so each filesystem entry is examined exactly once.

### Schema driver uses one transaction for the whole bump

`SqliteCacheSchemaDriver.EnsureSchemaAsync` opens one `SqliteTransaction`, runs every applicable migration step inside it, then commits `PRAGMA user_version = currentSchemaVersion` last. Any failure before commit rolls back the entire bump — `user_version` stays at its previous value and the next launch retries cleanly. The rebuild path (when `user_version > currentSchemaVersion`) takes the same approach: drop + create + stamp version, all in one transaction.

**Single-table rebuild limitation.** `RebuildAsync` issues `DROP TABLE IF EXISTS {TableName}` — exactly one table, named by the subclass's `TableName` property. SQLite drops the table's indexes and triggers automatically, but **auxiliary tables are not dropped**. Today every subclass has a single backing table, so this is fine. If a future entity adds auxiliary tables (e.g. audio pinning metadata in a separate table), the rebuild path needs an extension hook — either a `RebuildSql` virtual that subclasses can extend, or accept that the auxiliary table is rebuilt only via its own migration deltas. There is no such hook today; consider it a known limitation for the v2-and-beyond audio work.

### Self-heal targets only the cache database

[SqliteCacheStoreBase.cs:1191-1220](../../../Hypersonic/Services/Cache/SqliteCacheStoreBase.cs#L1191-L1220). On any `SqliteException` (or `IOException` / `UnauthorizedAccessException`) during init, the base disposes the connection, deletes `cache.db` + `cache.db-wal` + `cache.db-shm`, clears the connection pool, and retries `OpenAndPrepareConnectionAsync` once. A second failure logs and returns `null` — the cache becomes unavailable until the next process launch (every read returns null/default, every write is a no-op). Cancellation during the retry propagates rather than getting swallowed.

### Payload-folder access uses `_cacheFolderPath` directly, with `Directory.CreateDirectory` defensively re-called

The cache folder is created during init, but `WritePayloadAsync` / `WriteStreamedPayloadAsync` / `ReconcileAsync` all call `Directory.CreateDirectory(_cacheFolderPath!)` again before touching files. This is a deliberate no-op-on-success / heal-on-missing pattern: a user who deletes the cache folder between init and the first write doesn't get an `IOException` for "directory not found" — they get a re-created folder and a successful write.

### Negative-cache enforcement is write-surface validation

`WriteNegativeAsync` throws `ArgumentException` if the caller passes an entry with non-empty `FileName` or non-zero `Length` ([SqliteCacheStoreBase.cs:1003-1015](../../../Hypersonic/Services/Cache/SqliteCacheStoreBase.cs#L1003-L1015)). The reverse check exists on `WritePayloadAsync`: it throws `ArgumentException` for empty `FileName` and points callers at `WriteNegativeAsync`. The two write surfaces are non-overlapping by contract — there's no "neutral" write that accepts either.

---

## Verification Results

### Build

`dotnet build Hypersonic.sln -p:Platform=x64` — passes with no errors. The build may surface pre-existing or new analyzer warnings (a fresh review pass observed 44 warnings including a `CS8604` in `SqliteCacheStoreBase.cs`); treat warning-clean as a follow-up.

### Automated tests

| Test project | Result |
|---|---|
| `Hypersonic.Core.Tests` | All passing (not affected by this change). |
| `Hypersonic.Client.UnitTest` | All passing (not affected). |
| `Hypersonic.Tests` | All passing; includes 37 new `SqliteCacheStoreBaseTests`, the trimmed `SqliteImageCacheStoreTests` (image-specific schema/UPSERT/legacy-manifest coverage retained), and the trimmed `ImageCacheServiceTests` (policy coverage retained; mechanical coverage moved to base tests). |

### New base-test coverage

| Test | Scenario |
|---|---|
| `GetStatsAsync_FreshStore_ReturnsZeroBytesAndZeroCount` | Empty cache returns `(0, 0)`. |
| `SchemaVersionZero_AppliesV0ToCurrentShortcutAndStampsUserVersion` | Pre-create empty DB with `user_version=0`; init runs `CreateCurrentSchemaSql` and stamps `user_version=1`. |
| `SchemaVersionGreaterThanCurrent_RebuildsTableAndStampsCurrentVersion` | Pre-create DB at `user_version=999` with a divergent table; init drops and rebuilds. |
| `CorruptedDatabase_SelfHealsOnFirstAccess` | Pre-write garbage bytes; init rebuilds via self-heal path. |
| `UpsertAndGet_RoundTripsAllRequiredFields` | All three `ICacheEntry` fields persist + reload at millisecond precision. |
| `GetEntryAsync_ReturnsNullWhenAbsent` | Miss returns `null`. |
| `TouchEntryAsync_UpdatesOnlyLastAccessUtc` | Touch leaves `FileName` / `Length` unchanged. |
| `RemoveEntryAsync_RemovesRowAndIsNoopWhenAbsent` | Remove tolerates missing rows. |
| `GetEvictionCandidatesAsync_OrdersByLastAccessSkipsProtectedSkipsNegatives` | LRU order, protected key excluded, negative rows excluded. |
| `EnumerateAllEntriesAsync_SeesNegativeAndOrdinaryRows` | Public wrapper sees both. |
| `ClearAsync_DeletesRowsAndKnownPayloadFilesButLeavesUnrelatedFilesAlone` | `Clear` is selective on filename predicate. |
| `FlushAsync_DoesNotThrow` | WAL checkpoint round-trips. |
| `TryGetPayloadUriAsync_ReturnsNullOnMissingRow` | Row miss → null. |
| `TryGetPayloadUriAsync_ReturnsUriWhenRowAndFileExist` | Happy path returns `file://` URI. |
| `TryGetPayloadUriAsync_RemovesOrphanRowAndReturnsNullWhenFileMissing` | Auto-orphan-sweep on file-missing. |
| `TryGetPayloadUriAsync_ReturnsNullForNegativeEntryWithoutRemovingRow` | Negative row preserved; URI null. |
| `TryGetPayloadUriAsync_DoesNotTouchLastAccess` | Confirms no implicit touch in the read path. |
| `WritePayloadAsync_WritesFileAndRowAndReturnsUri` | Buffered write commits both file and row. |
| `WritePayloadAsync_OverwriteSameName_UpdatesFileAndRow` | Same-name overwrite leaves no stray siblings. |
| `WritePayloadAsync_OverwriteDifferentName_DeletesOldFile` | Different-name overwrite deletes the prior file. |
| `WritePayloadAsync_LeavesNoTempFiles` | Success path cleans temps. |
| `WriteNegativeAsync_WritesRowWithEmptyFileNameAndNoPayloadFile` | Negative cache writes row + skips file. |
| `WriteNegativeAsync_RejectsEntriesWithFileNameOrLength` | Argument validation throws. |
| `EvictIfNeededAsync_OverBudget_EvictsLruRespectingProtectedKey` | LRU eviction down to cap, protected key survives. |
| `EvictIfNeededAsync_MaximumBytesZero_DisablesEviction` | `MaximumBytes=0` short-circuits. |
| `EvictIfNeededAsync_SkipsNegativeCacheRows` | Negative rows survive over-budget eviction. |
| `ReconcileAsync_RemovesRowsWhoseFileIsMissing` | Pass-1 sweep. |
| `ReconcileAsync_DeletesOrphanFilesAndLeavesUnrelatedFilesAlone` | Pass-2 sweep is filename-predicate-scoped. |
| `ReconcileAsync_NegativeCacheRowsAreNotMistakenForOrphans` | Negative rows survive reconcile. |
| `ReconcileAsync_AbortsWithoutDeletingFilesWhenEnumerationFails` | Enumeration failure aborts cleanly; no files touched. |
| `WriteStreamedPayloadAsync_WritesFileAndRowAndReturnsUri` | Streamed happy path. |
| `WriteStreamedPayloadAsync_EntryFactoryReceivesActualWrittenLength` | `PayloadWriteContext.Length` reflects bytes written. |
| `WriteStreamedPayloadAsync_ReadsAgainstOtherEntriesProgressDuringTheStreamedWrite` | Concurrent reads complete while the writer runs (gate released for writer body). |
| `WriteStreamedPayloadAsync_DisposeCancelsWriterAndCleansUpTempFile` | Disposal mid-write completes within drain timeout; temp file is cleaned. |
| `StartupTempFileSweep_DeletesStreamedWriterTempsAndLeavesEverythingElseAlone` | Strict `<64-hex>.<32-hex>.tmp` pattern matching. |
| `WritePayloadAsync_DoesNotThrowWhenPostWriteEvictionFails` | Post-write eviction failures are swallowed; write still succeeds. |
| `WriteStreamedPayloadAsync_DoesNotThrowWhenPostWriteEvictionFails` | Same guarantee on the streamed path. |
