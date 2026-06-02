# Generic SQLite Cache Store — Design

Refactor today's image-only cache infrastructure into a generic per-entity SQLite cache, so lyrics (Phase 4.5) and audio (Phase 5.3) can plug in without reinventing connection lifecycle, schema migration, eviction, or self-heal.

---

## What we're building

A small generic SQLite cache framework in `Hypersonic.Services.Cache/` that:

- Provides a `SqliteCacheStoreBase<TEntry>` abstract class owning **all** of today's image-cache plumbing **plus** the file-payload lifecycle that today lives in `CachedImageService`: connection open + pragmas, `PRAGMA user_version` migration driver, self-heal on corruption, semaphore gate, dispose drain, generic stats/clear/flush, **atomic file write + row upsert, negative-cache writes, LRU eviction with file cleanup, and orphan-file reconciliation.**
- Hosts each entity type in its own `cache.db` under its own folder (`ImageCache/`, `LyricsCache/`, `AudioCache/`), with its own connection, gate, and migration timeline.
- Reduces today's `SqliteImageCacheStore` to a thin subclass that supplies image-specific schema SQL, column mapping, and any entity-specific queries.
- Leaves `CachedImageService` (and future `LyricsService`, `AudioCacheService`) as **per-entity policy layers** — resolvers, in-flight dedup, stale-fallback policy, cancellation orchestration, consumer-facing API. No cross-entity monolith; cross-entity ops (total stats, clear-all) go in a thin `CacheAdminService` facade if/when needed.

No data migration required for existing users — the image cache keeps its current schema and `user_version=1`, and its on-disk paths are preserved verbatim (MSIX: `ImageCache/cache.db`; unpackaged: `Cache\Images\cache.db`). Lyrics and audio caches start fresh at v1 in their own folders.

---

## Pivotal decisions

### One database per entity, not one shared DB

Considered three layouts (shared-table-with-discriminator, shared-DB-with-per-entity-tables, one-DB-per-entity). Chose per-entity DBs:

- **Schema migration** uses SQLite's native `PRAGMA user_version`, which is one integer per file. With per-entity DBs, image, lyrics, and audio versions evolve independently. With a shared DB, every entity's `EnsureSchemaAsync` runs on every version bump for any entity — even no-op steps still execute, and a bug in one entity's migration step can affect a user whose other caches are healthy.
- **Concurrency** improves. Audio is the dominant writer (MB–tens of MB per track, streamed); WAL serializes writers at the file level. Per-entity DBs let audio writes and lyrics reads progress in parallel; shared-DB does not.
- **Corruption blast radius** is one entity instead of all three. Self-heal (delete `.db*`, rebuild) targets the broken cache instead of nuking everything.
- **Cross-entity atomicity** — the one feature shared-DB offers and per-entity-DB cannot — has no semantic meaning for caches. Each cache is an independent reproducible black box keyed by content; nothing references across them.

Cost is ~50 lines of plumbing (3 DI registrations, 3 subclasses) and ~100 KB of disk header overhead. Trivial against the gigabytes the audio cache will hold.

### Image cache schema does not change

The existing `Entries` table (image-specific columns: `Provider`, `Kind`, `ServerHash`, `ServerScopeHash`, `Size`) stays exactly as today, including its `user_version=1`. The refactor is purely structural — the image subclass owns the same schema SQL that lives in `SqliteImageCacheSchema.cs` today.

Considered renaming columns to generic names (`Size` → `Variant`, drop `ServerScopeHash` for lyrics/audio that don't need user-scoping) but rejected: lyrics has no concept of `Size` and audio's variant axis is bitrate, not pixels — these aren't actually shared semantics. Forcing them into shared columns sacrifices clarity for a non-existent reuse opportunity.

### Each entity defines its own `TEntry`, constrained to a minimal `ICacheEntry` contract

The base needs **three** entry fields to do its job (stats, eviction ordering, payload-file resolution): `FileName`, `Length`, `LastAccessUtc`. These are required by an `ICacheEntry` interface that every `TEntry` implements:

```csharp
public interface ICacheEntry
{
    string FileName { get; }              // "" for negative-cache rows
    long Length { get; }                  // 0 for negative-cache rows
    DateTimeOffset LastAccessUtc { get; }
}

public abstract class SqliteCacheStoreBase<TEntry> where TEntry : ICacheEntry { ... }
```

Column names are a **base-class convention**: tables must name these three columns exactly `FileName`, `Length`, `LastAccessUtc` so the base can write generic `SUM(Length)`, `ORDER BY LastAccessUtc ASC`, and `WHERE FileName != ''` SQL without per-subclass parameterization. Today's image schema already complies.

Beyond that, each subclass owns:

- `string CreateCurrentSchemaSql` — the **complete** current schema: `CREATE TABLE` + every index + every trigger. Used by the v0→current step *and* by the future-version rebuild path. Image subclass concatenates today's `CreateEntriesTableSql` + `CreateLastAccessIndexSql`.
- `int CurrentSchemaVersion` and an ordered migration step list.
- `TEntry ReadEntry(SqliteDataReader)` — column-by-column mapping.
- The entity-specific UPSERT SQL and parameter binding (the rest of the column list is per-entity).

The base owns: schema driver loop, connection lifecycle, gate, self-heal, `GetStats`, `Clear` (rows + payload files), `Flush` (WAL checkpoint), dispose drain, `WritePayloadAsync` (buffered), `WriteStreamedPayloadAsync` (streamed), `WriteNegativeAsync`, `TryGetPayloadUriAsync` (lookup + file-exists + orphan-row cleanup, **no touch**), `TouchEntryAsync`, `EvictIfNeededAsync`, `ReconcileAsync`. The full list is in [Responsibility migration](#responsibility-migration-service--store).

This keeps the base small and entity-agnostic on policy axes (TTL, stale-fallback, resolvers) while honest about the storage axes it can't avoid knowing (file pointer + size + access timestamp).

### Locking convention: `*LockedAsync` for gate-held callers

The base holds the gate for the duration of `WritePayloadAsync` / `EvictIfNeededAsync` / `ReconcileAsync` / `ClearAsync` so the file/row mutations stay coordinated. From inside those methods it needs to call subclass `Get/Upsert/Touch/Remove/GetEvictionCandidates` while the gate is already held. `SemaphoreSlim(1,1)` is non-reentrant, so we cannot reuse today's gate-acquiring subclass methods directly — they would deadlock.

Convention: every subclass DB method comes in two forms.

- **Public** (e.g. `UpsertEntryAsync`) — acquires the gate, ensures init, calls the `*LockedAsync` variant, releases the gate. Used by service-layer callers.
- **Locked** (e.g. `UpsertEntryLockedAsync`) — assumes the gate is held and the connection is open. Used by the base's payload-lifecycle methods. The base passes the `SqliteConnection` it already validated.

The image subclass keeps its existing public methods (signatures unchanged), and adds the `*LockedAsync` siblings that take a `SqliteConnection` parameter.

### Error contract: public methods warn + return; locked methods throw

The public (gate-acquiring) and locked (gate-held, called by the base) variants differ on error propagation. This is forced by the fact that the base's payload-lifecycle methods need to know whether an inner DB call succeeded so they can decide whether to delete a temp file, return `null`, etc. — and they can only know that if failures propagate as exceptions.

**Public methods** (consumed by services) — preserve today's behavior in `SqliteImageCacheStore`:

- `SqliteException` from any DB call → log warning, return safe default (null entry, zero stats, empty list, completed task). The cache treats DB hiccups as "miss" so consumers never crash on cache flakiness.
- `OperationCanceledException` and `ObjectDisposedException` (when disposed) propagate.

**`*LockedAsync` methods** (consumed by the base) — narrower contract:

- `SqliteException` propagates. The base catches and decides:
  - In `WritePayloadAsync` / `WriteStreamedPayloadAsync`: clean up any temp file, return `null` (mirroring today's "DB failure → silent miss").
  - In `WriteNegativeAsync`: log warning, no-op (the method returns `Task`, not `Task<Uri?>`).
  - In `TryGetPayloadUriAsync` / `EvictIfNeededAsync`: log warning, return safe default (null / no-op).
  - In `ReconcileAsync`: see [`EnumerateAllEntriesLockedAsync`](#reconciliation-needs-a-different-enumeration-contract) below — failure surfaces and aborts the reconcile.
- `OperationCanceledException` / `ObjectDisposedException` propagate.

Public methods that today already have non-throwing semantics (e.g. `UpsertEntryAsync` returns `Task` and swallows on failure) keep that signature; the new locked variant `UpsertEntryLockedAsync` adopts the throw-on-DB-failure contract. Service-layer callers that today call the public method see no behavior change.

**File I/O** (`IOException` / `UnauthorizedAccessException`) inside `WritePayloadAsync` propagates to the service in all cases. Service's resolver-failure path (`FetchAndStoreAsync` today) already catches `IOException` and falls back to stale.

### Reconciliation needs a different enumeration contract

`GetEvictionCandidatesLockedAsync` is **not** the right read for reconcile: it filters out negative-cache rows (empty `FileName`), and audio will override it to filter out pinned rows. Reconcile must see **every** row so it doesn't delete legitimately-pinned files or legitimately-negative rows as orphans.

The subclass exposes a separate locked enumeration:

- `Task<IReadOnlyList<(string KeyHash, TEntry)>> EnumerateAllEntriesLockedAsync(SqliteConnection conn, CancellationToken)` — every row, no filter. Used **only** by `ReconcileAsync`.

`EnumerateAllEntriesLockedAsync` is in the throw-on-failure family. The base's `ReconcileAsync` wraps it in `try/catch (SqliteException)` and, on failure, logs a warning and aborts the reconcile entirely. It does **not** treat the exception as "empty list" — that would interpret a SQLite hiccup as "all cached files are orphans" and delete every payload on disk. Today's `CachedImageService.ReconcileAsync` already has this safeguard; the new contract makes it explicit at the base level.

### File-payload model stays; lyrics negative cache uses an empty-payload row

Image cache stores payload as sidecar files (`<keyhash>.<ext>`) referenced by `FileName` in the row. We keep that for all entities for consistency. Lyrics' "definitively no lyrics" negative result is a row with `Length=0` and `FileName` empty — written via `WriteNegativeAsync` and read by the service looking at the raw entry (`GetEntryAsync`), not via `TryGetPayloadUriAsync` (which is specifically for file-backed hits). Reconcile and eviction skip rows with empty `FileName`.

Alternative considered: BLOB-in-DB for lyrics (small JSON, fits naturally). Rejected because it forks the code path — file-backed reads and BLOB reads have different lifecycles, different concurrency profiles, and double the test surface for marginal benefit. Lyrics files are small but few; the disk overhead is negligible.

### Two write surfaces: buffered and streamed

Audio payloads can be tens of MB. Forcing them through `WritePayloadAsync(ReadOnlyMemory<byte>)` would (a) buffer the entire response in memory and (b) hold the cache gate for the whole disk write, blocking concurrent reads and small lyrics/image writes.

The base provides two write surfaces:

- `WritePayloadAsync(string keyHash, ReadOnlyMemory<byte> bytes, TEntry entry, …)` — for small payloads (images, lyrics JSON). Buffered, gated end-to-end. The caller has already computed `Length` (`bytes.Length`), so it constructs the entry up front.
- `WriteStreamedPayloadAsync(string keyHash, Func<Stream, CancellationToken, Task> writer, Func<PayloadWriteContext, TEntry> entryFactory, …)` — for large payloads (audio). The base creates a temp file at `<keyhash>.<guid>.tmp`, hands the open stream to `writer`, and **does not hold the gate during the write**. After the writer completes successfully, the base computes `(FileName, Length, WrittenAtUtc)`, calls `entryFactory(context)` to get the final `TEntry` (with the correct `Length` baked in), then re-acquires the gate to do the atomic move + UPSERT + post-write eviction.

```csharp
public sealed record PayloadWriteContext(
    string FileName,       // <keyhash><ext>, final name after rename
    long Length,           // bytes actually written
    DateTimeOffset WrittenAtUtc);
```

The reconcile filename predicate (`IsKnownPayloadFileName`) excludes `.tmp` files (today's `IsCachedImageFileName` already rejects non-hex basenames), so an in-flight stream write is invisible to a concurrent reconcile. Stale `.tmp` files left behind by crashed or abandoned writers are cleaned up by a startup sweep — see [Startup temp-file sweep](#startup-temp-file-sweep) below.

Image cache uses the buffered overload (no behavior change from today). Audio cache (Phase 5.3) uses the streamed overload.

### Streamed writes need an in-flight lease for safe disposal

Moving streamed writes outside the gate means `Dispose` can no longer drain all store work by simply taking the gate. A writer can be filling a temp file while `Dispose` runs, and a naïve teardown would leave temp files behind and (worse) try to re-acquire the gate after `_connection` is gone for the post-write rename + UPSERT.

The base adds two coordination primitives:

- `_disposeCts` (`CancellationTokenSource`) — linked into the writer's `CancellationToken` so writers observe disposal and abort.
- `_inFlightStreams` (`int`, manipulated via `Interlocked`) — incremented on entry to `WriteStreamedPayloadAsync` before the temp file is opened, decremented in the finally block.

Disposal sequence:

1. Set `_disposed = true` (further `WriteStreamedPayloadAsync` calls bail with `ObjectDisposedException` immediately).
2. `_disposeCts.Cancel()` — in-flight writers observe cancellation, abort their stream write, hit their finally block, delete the temp file, decrement the in-flight counter.
3. Wait up to `DisposeDrainTimeout` (5s, same as today's gate drain) for `_inFlightStreams == 0`.
4. Acquire the gate (now uncontended for streamed writes; buffered writers were already serialized through it).
5. Tear down the connection, clear pools.

If a streamed writer is mid-rename when disposal fires, the gate it already holds completes the rename + UPSERT before disposal's gate acquisition. If a writer ignores its cancellation token and is still writing bytes when disposal times out, its temp file is logged as a leak and left on disk — to be cleaned up by the next launch's startup sweep (below).

Concrete behavior:
- Writers must respect the cancellation token. Implementations that ignore it will block disposal up to the drain timeout, then be abandoned.
- Post-write rename + UPSERT is gated by `_disposed` — if disposal has fired, the writer's finally branch deletes the temp file instead of trying to commit.
- The pre-existing `_writeGate` semantics for buffered writes are unchanged; only streamed writes need the new lease.

### Startup temp-file sweep

`.tmp` files are deliberately excluded from `IsKnownPayloadFileName` so a concurrent `ReconcileAsync` doesn't race with an in-flight streamed write and delete the temp out from under it. But that exclusion means crashed processes, killed writers, or writers that ignored their cancellation token can leave stale `.tmp` files behind, and `ReconcileAsync` can't clean them up safely while the store is live.

The base runs a one-shot sweep during `EnsureInitializedLockedAsync`, right after schema is ready and before any service traffic can land:

- Enumerate files in `ResolvePayloadFolder()`.
- Delete any file whose name matches the temp pattern (`<hex>.<guid>.tmp`).
- Log the count.

The sweep is safe because no streamed writer can be in flight before the store has finished initializing — `WriteStreamedPayloadAsync` calls `EnsureInitializedLockedAsync` themselves and queue behind the init gate. After init completes, the sweep window has already closed and any new `.tmp` belongs to a live writer.

Trade-off: this is a per-launch sweep, not a continuous one. A `.tmp` file from a writer that crashed *between* launches will linger until the next start. Acceptable — the file is `O(payload size)` and a process crash is the only path that gets here.

### Write ordering and compensation (atomic-on-success, self-healing-on-failure)

"Atomic file write + row upsert" is shorthand — file move and SQLite UPSERT cannot be one transactional unit. The base sequences them so that any partial failure is self-healing on the next read or next reconcile:

1. Write bytes to temp file `<keyhash>.<guid>.tmp`.
2. `File.Move(temp, final, overwrite: true)` — final name is `<keyhash><ext>`.
3. If the old entry's `FileName` differs from the new one, delete the old file.
4. UPSERT the row.
5. Run `EvictIfNeededAsync` if total bytes exceed `MaximumBytes`.

Failure modes:

| Fails at | State after | Healed by |
|---|---|---|
| Step 1 (temp write) | Temp file may or may not exist; no DB or final-file change | `TryDeleteFile(temp)` in catch; service decides retry |
| Step 2 (move), new `FileName` differs from old | Temp file may linger; old final + old row still authoritative | Temp file: deleted in catch. Old entry: unchanged. |
| Step 2 (move), **new `FileName` equals old** (same extension) | Old final file has been **overwritten** with new bytes; old row still authoritative for metadata (`Length`, `LastAccessUtc`, `ExpiresAtUtc`) | See "same-name overwrite" note below — accepted as benign cache staleness. |
| Step 3 (delete old) | New final exists, old file leaked, new row not yet written | Reconcile sweeps the leaked old file as an orphan on next pass |
| Step 4 (UPSERT), new `FileName` differs from old | New final exists on disk, row still points at old `FileName` (which step 3 already deleted) | Next `TryGetPayloadUriAsync` sees row → checks file → file missing → removes orphan row. New file is then orphaned until reconcile sweeps it. |
| Step 4 (UPSERT), **new `FileName` equals old** (same extension) | Final file has new bytes; row keeps old metadata pointing at it | See "same-name overwrite" note below. |
| Step 5 (eviction) | New entry committed; over-budget for one cycle | Next write or next reconcile triggers eviction again |

**Same-name overwrite is benign cache staleness, not corruption.** When the new payload's extension matches the old one (most common case for images — same source URL, same content type), `<keyhash><ext>` is the same path on both writes, so step 2 overwrites the old file in place and step 3 is a no-op. If step 4's UPSERT then fails, the row carries stale `Length` / `LastAccessUtc` / `ExpiresAtUtc` for the new bytes that now live on disk. Concretely:

- `Length` may be off — affects `GetStatsAsync` totals (small drift) and `EvictIfNeededAsync` decisions (could over- or under-evict by the difference, self-corrects on the next successful write).
- `LastAccessUtc` may be older than the actual access — affects LRU ordering (the entry looks colder than it is, slightly more eviction-prone).
- `ExpiresAtUtc` may be older than intended — the entry expires earlier than the new write would have set, triggering a refetch sooner. **This is a self-healing direction**: an earlier refetch attempt re-runs the write, and a success commits correct metadata.

Reconcile cannot detect this — file and row both exist, and the row's `Length` doesn't have to match the file's on-disk size. We don't add a length-check pass because (a) `os.stat` per row on every reconcile is expensive and (b) the cache is reproducible — stale metadata can never serve wrong bytes, only nudge eviction/expiry timing. Accepted as a documented trade-off.

The "old entry / old file remain authoritative" guarantee from earlier holds for the cross-extension case (step 2 writes to a new path). For same-name overwrites, the file content advances even when the metadata doesn't.

Today's `CachedImageService` does both file-payload mechanics (temp-file + atomic move, post-write eviction with file cleanup, reconcile against the payload folder) and policy (resolver chain, in-flight dedup, stale fallback on fetch failure, cancellation). With three entities arriving, every per-entity service would otherwise reimplement the same temp-file dance and the same reconcile loop.

Moving the file-payload lifecycle into `SqliteCacheStoreBase<TEntry>` collapses that duplication. The store becomes the sole owner of "what's in the cache folder and what's in the DB are consistent." The write gate moves into the store too — it has to, since reconcile and the atomic-move+upsert pair both need the same gate.

What stays in the service is what's genuinely per-entity: resolver selection, key construction, in-flight fetch deduplication, stale-fallback policy, cancellation lifecycle tied to consumer scope. Each entity's service shrinks to a thin policy layer over its store. See the [Responsibility migration](#responsibility-migration-service--store) table below for the line-by-line split.

Considered an alternative monolithic `CacheService` that handles all three entities. Rejected: the consumer APIs are genuinely different shapes — image returns `Uri`, lyrics returns `LyricsDocument`, audio returns a stream source — and the cancellation profiles diverge (many short XAML-lifetime fetches vs. one long-lived page-lifetime parse vs. prefetch + range-aware streaming). A monolith merges three independent APIs into one file with no shared interface; per-entity services keep consumer dependencies honest.

### Per-entity options classes

`ImageCacheOptions` already exists; `LyricsCacheOptions` and `AudioCacheOptions` are added when their respective phases land. The base reads only what *it* needs (`RootPath` for path resolution, `MaximumBytes` for eviction) from a shared `CacheOptionsBase` that each entity's options class inherits. **TTL is service-layer policy, not a base concern** — `TimeToLiveDays` lives on the per-entity options class (today's `ImageCacheOptions`), is read by the service, and never reaches the base.

Entity-specific options (audio's pinning policy and max parallel pre-fetches, image's TTL, lyrics' negative-cache TTL) all live on the derived options class.

This avoids both "all entities forced to share one options class" and "base class has to know about every entity's options."

### Reconcile and write-coordination move into the store base

Reconciliation (sweep DB rows whose file is missing; sweep payload files whose row is missing) doesn't consume any image-domain knowledge except a filename predicate. Today it lives in `CachedImageService` only because that's where the write gate lives — without that gate, reconcile would race against in-flight `FetchAndStoreAsync` calls that have written a temp file but not yet upserted the row (or vice versa).

The cleaner factoring, with three entities arriving, is to move the write gate, the atomic write+upsert, and reconcile **all** into the store base. Then reconcile is naturally adjacent to the writes it must coordinate with, and per-entity services don't reimplement the same temp-file dance three times. The filename predicate (`IsCachedImageFileName` today) becomes a per-subclass virtual.

See the [Responsibility migration](#responsibility-migration-service--store) table for the full list of what moves.

---

## Responsibility migration: service → store

What today lives in `CachedImageService` and `SqliteImageCacheStore`, and where it lands after this refactor. "Base" = `SqliteCacheStoreBase<TEntry>`; "Image store" = `SqliteImageCacheStore` subclass; "Image service" = `CachedImageService` (and same pattern for future lyrics/audio).

| Concern | Today (where) | After (where) | Notes |
|---|---|---|---|
| `SqliteConnection` open + WAL/synchronous pragmas | Image store | **Base** | Unchanged behavior, moved up. |
| `PRAGMA user_version` read + migration driver loop | `SqliteImageCacheSchema` (static) | **Base** | Subclass supplies steps. |
| Schema rebuild on version-greater-than-current | `SqliteImageCacheSchema` | **Base** | Uses subclass `CreateCurrentSchemaSql` (must include indexes). |
| Self-heal: delete `.db*` + retry once on init failure | Image store | **Base** | Targets the per-entity DB only. |
| `SemaphoreSlim` gate | Image store + image service (separate gates) | **Base** (one gate covers DB + payload writes) | Eliminates today's split gate; reconcile/write/upsert all share. Non-reentrant — subclass exposes `*LockedAsync` variants for gate-held callers. |
| `Dispose` drain (timeout + connection teardown + pool clear) | Image store + image service | **Base** | Service dispose still cancels in-flight fetch tasks. |
| `GetStatsAsync` (SUM Length, COUNT) | Image store | **Base** | Generic, uses subclass `TableName` + the `Length` column convention. |
| `ClearAsync` — DELETE rows | Image store | **Base** | Generic. |
| `ClearAsync` — delete payload files | Image service | **Base** | Was duplicated between service and store; consolidates. |
| `FlushAsync` (WAL checkpoint) | Image store | **Base** | Generic. |
| `GetEntryAsync` / `TouchEntryAsync` / `UpsertEntryAsync` / `RemoveEntryAsync` (public, gate-acquiring, warn-and-return) | Image store | **Image store** (unchanged signatures + behavior) | Wraps the locked variant: catches `SqliteException` to preserve today's contract. |
| Same methods as `*LockedAsync` (gate-held, connection-passed, throw-on-DB-failure) | n/a | **Image store** (new) | Called by the base's payload-lifecycle methods. Throws so the base can decide per-call-site recovery. |
| `GetEvictionCandidatesAsync` (ordered by `LastAccessUtc`, excludes empty `FileName`) | Image store | **Image store** (unchanged) | Audio subclass will override (filter `Pinned=0`). |
| `EnumerateAllEntriesLockedAsync` (no filters, throws on DB failure) | n/a | **Image store** (new) | Used by `ReconcileAsync` only — must see pinned and negative rows. |
| **Buffered atomic file write** (temp → move → delete-prior → upsert) | Image service (`FetchAndStoreAsync` tail) | **Base**: `WritePayloadAsync(keyHash, ReadOnlyMemory<byte>, TEntry)` | Service hands bytes + metadata; store owns the dance. |
| **Streamed file write** (temp open outside gate; gated rename+upsert; in-flight lease for disposal) | n/a (no audio yet) | **Base**: `WriteStreamedPayloadAsync(keyHash, Func<Stream, CT, Task>, Func<PayloadWriteContext, TEntry>, …)` | For audio. Write happens with gate released; rename + upsert under the gate. Tracked by `_inFlightStreams`; disposal cancels + drains. |
| **Reconcile** (sweep rows whose files are missing; sweep files with no row) | Image service (`ReconcileAsync`) | **Base** | Uses subclass `IsKnownPayloadFileName` predicate + `EnumerateAllEntriesLockedAsync` for the full row set. Skips rows with empty `FileName` on both passes. Aborts (does not delete files) if enumeration throws. |
| **Negative-cache write** | n/a (no lyrics yet) | **Base**: `WriteNegativeAsync(keyHash, TEntry)` | `Length=0` + empty `FileName`. |
| **Post-write LRU eviction** (call `GetEvictionCandidatesAsync`, delete files, remove rows, flush) | Image service (`EvictIfNeededAsync`) | **Base** | Pluggable `MaximumBytes` from `CacheOptionsBase`. Audio store overrides candidate query to skip pinned. Skips rows with empty `FileName`. |
| **Startup temp-file sweep** | n/a (today's image cache has no streamed writes) | **Base** (during `EnsureInitializedLockedAsync`) | Deletes `<hex>.<guid>.tmp` files left by crashed/abandoned streamed writers. Safe because it runs before any new writer can be in flight. |
| `IsCachedImageFileName` predicate | Image store (static) | **Image store** (override of base virtual) | Lyrics/audio subclasses override with their own extension lists. |
| Cache-hit URI construction (`new Uri(Path.Combine(folder, fileName))`) | Image service | **Base**: `TryGetPayloadUriAsync(keyHash)` | Returns null on row-miss, missing-file (auto-removes orphan row), or negative entry. **Does not touch and does not check TTL** — service composes `GetEntryAsync → check TTL/stale → TryGetPayloadUriAsync → optional TouchEntryAsync`. The `Uri?` return only proves "file exists for this row right now," not "policy-allowed hit." |
| TTL expiry check (`ExpiresAtUtc <= now`) | Image service | **Image service** (stays) | Policy: image returns null on expired; lyrics may still serve `SyncKind=None`; audio may serve stale during offline. Per-entity decision. |
| `TouchEntryAsync` invocation (when to bump `LastAccessUtc`) | Image service (via store) | **Image service** (stays) | Service decides; base no longer auto-touches in the read path. |
| Stale-fallback on fetch failure (`allowStale=true` path) | Image service | **Image service** (stays) | Policy. |
| Resolver chain (`IImageResolver` selection) | Image service | **Image service** (stays) | Per-entity. |
| In-flight fetch dedup (`_inFlightFetches` dictionary) | Image service | **Image service** (stays) | Per-entity; dedup key shape differs. |
| Cancellation lifecycle (`_disposeCts`, linked tokens) | Image service | **Image service** (stays) | Per-entity; tied to consumer scope. |
| Legacy `manifest.json` discard on init | Image store | **Image store** (stays as one-off) | Historical, image-only. Other entities have no legacy. |
| Image cache path resolution (MSIX vs unpackaged branches) | `ImageCachePath.Resolve` | **Image store** (preserved verbatim) | Today's MSIX→`ImageCache`, unpackaged→`Cache\Images` mapping stays. `CachePath.Resolve` for greenfield entities only. See [Per-entity path layout](#path-layout). |

Net effect on `CachedImageService`: drops `_writeGate`, `FetchAndStoreAsync` post-fetch block, `EvictIfNeededAsync`, `ReconcileAsync`, `TryDeleteFile`. `TryGetCachedUriAsync` becomes `GetEntry → check TTL → TryGetPayloadUriAsync → optional TouchEntryAsync` (a few lines). Keeps resolver chain, in-flight dedup, stale-fallback, cancellation. Estimated shrink: ~680 lines → ~280 lines.

Net effect on the base: ~650 lines of new generic code (most of it lifted from existing image service/store with the entity-specific bits parameterized). Subclasses are ~200–250 lines each (schema SQL + column mapping + public + `*LockedAsync` UPSERT/select methods).

---

## Architecture

```
Hypersonic.Services.Cache/                 (new namespace)
├── ICacheEntry.cs                          FileName + Length + LastAccessUtc contract
├── CacheOptionsBase.cs                     RootPath, MaximumBytes (TTL stays on per-entity derived class)
├── CachePath.cs                            Resolve(options, subfolderName) — for greenfield entities only
├── SqliteCacheStoreBase.cs                 Generic<TEntry> base — open, pragmas, gate, migrations,
│                                           self-heal, dispose, stats, clear+files, flush,
│                                           buffered + streamed write, eviction, reconcile
└── SqliteCacheSchemaDriver.cs              user_version read + version-incremental migration loop

Hypersonic/Services/
├── SqliteImageCacheStore.cs                Subclass: image schema SQL, ImageCacheEntry mapping,
│                                           image-specific UPSERT/eviction queries + *LockedAsync siblings,
│                                           IsKnownPayloadFileName override (hex+image-ext allowlist),
│                                           ResolvePayloadFolder preserving today's MSIX/unpackaged paths
├── ImageCachePath.cs                       Stays (or inlines into image store) — same MSIX/unpackaged branches
├── CachedImageService.cs                   Slimmed: resolver chain, in-flight dedup, TTL/touch/stale policy
├── SqliteLyricsCacheStore.cs               (Phase 4.5) subclass for lyrics
├── LyricsService.cs                        (Phase 4.5) provider chain + negative-cache policy
├── SqliteAudioCacheStore.cs                (Phase 5.3) subclass for audio (+ Pinned column)
├── AudioCacheService.cs                    (Phase 5.3) prefetch + stream + pin policy
└── CacheAdminService.cs                    (optional, when needed) thin facade:
                                             GetTotalStatsAsync, ClearAllAsync
```

DI:

```csharp
services.Configure<ImageCacheOptions>(...);
services.AddSingleton<IImageCacheStore, SqliteImageCacheStore>();
services.AddSingleton<IImageService, CachedImageService>();

// Later phases add their own store + service pair.
// CacheAdminService is added only when Settings UI needs cross-entity ops.
```

`IImageCacheStore` gains the new base-provided methods (`WritePayloadAsync`, `TryGetPayloadUriAsync`, `ReconcileAsync`, `EvictIfNeededAsync`) and loses nothing. `IImageService` keeps its current consumer-facing shape (`GetImageAsync`, `GetStatsAsync`, `ReconcileAsync`, `ClearAsync`) — the implementation delegates the mechanical parts to the store and keeps the per-entity policy (TTL check, stale fallback, touch decision).

---

## Base class contract

What the base provides:

**Lifecycle**
- `Task<SqliteConnection?> EnsureInitializedLockedAsync(CancellationToken)` — open, pragmas (`journal_mode=WAL`, `synchronous=NORMAL`), schema, self-heal once on failure.
- `void Dispose()` — drain gate (timeout 5s), dispose connection, clear pools.

**Generic DB ops**
- `Task<CacheStoreStats> GetStatsAsync(CancellationToken)` — `SELECT COALESCE(SUM(Length), 0), COUNT(*) FROM <TableName>`. SQLite failures → log + return `(0, 0)`.
- `Task ClearAsync(CancellationToken)` — `DELETE FROM <TableName>` **plus** delete all payload files matching `IsKnownPayloadFileName` in the payload folder.
- `Task FlushAsync(CancellationToken)` — `PRAGMA wal_checkpoint(PASSIVE)`.

**Payload lifecycle (new)**
- `Task<Uri?> TryGetPayloadUriAsync(string keyHash, CancellationToken)` — calls `GetEntryLockedAsync` + checks file existence + returns `new Uri(fullPath)`. Returns `null` on row-miss, missing file (auto-removes the orphaned row), or negative entry (`FileName` empty). **Does not touch and does not check TTL.** The service composes the full read path: `GetEntryAsync → service-side TTL/stale check → TryGetPayloadUriAsync (if policy allows hit) → optional TouchEntryAsync`. The `Uri?` return signals "a payload file exists for this row right now," not "policy-allowed hit." Callers that need the negative-cache signal use `GetEntryAsync` directly. SQLite failure inside the locked call → base catches, logs, returns `null`.
- `Task<Uri?> WritePayloadAsync(string keyHash, ReadOnlyMemory<byte> bytes, TEntry entry, CancellationToken)` — buffered: writes temp file → atomic move → cleanup of prior file if `FileName` changed → UPSERT → post-write eviction. Returns the final payload URI on success; returns `null` if the inner `UpsertEntryLockedAsync` throws `SqliteException` (file is left on disk and healed by reconcile per [Write ordering and compensation](#write-ordering-and-compensation-atomic-on-success-self-healing-on-failure)). Throws on `IOException` / cancellation / dispose.
- `Task<Uri?> WriteStreamedPayloadAsync(string keyHash, Func<Stream, CancellationToken, Task> writer, Func<PayloadWriteContext, TEntry> entryFactory, CancellationToken)` — streamed: opens a temp file outside the gate, runs `writer` against it, then re-acquires the gate, calls `entryFactory(new PayloadWriteContext(fileName, length, writtenAtUtc))` to materialize the final `TEntry` with the correct `Length`, then atomic-move + cleanup + UPSERT + evict. Same null-on-SQLite-failure / throw-on-everything-else contract as `WritePayloadAsync`. Tracked by the in-flight lease (see [Streamed writes need an in-flight lease for safe disposal](#streamed-writes-need-an-in-flight-lease-for-safe-disposal)).
- `Task WriteNegativeAsync(string keyHash, TEntry entry, CancellationToken)` — UPSERT with `Length=0` and `FileName` empty. No file written, no URI returned. Inner SQLite failure → base catches, logs, returns (no-op).
- `Task EvictIfNeededAsync(string? protectedKeyHash, CancellationToken)` — invoked automatically by `WritePayloadAsync` / `WriteStreamedPayloadAsync`, exposed for explicit "free space now" calls. Iterates `GetEvictionCandidatesLockedAsync`, deletes file + row pairs, flushes WAL. Audio subclass overrides the candidate query to filter `Pinned=0`; the base also filters out empty-`FileName` rows in its iteration.
- `Task ReconcileAsync(CancellationToken)` — uses `EnumerateAllEntriesLockedAsync` (not `GetEvictionCandidatesLockedAsync`) so pinned and negative rows are visible and not mistaken for orphans. Sweeps DB rows with non-empty `FileName` whose file is missing; sweeps payload files matching `IsKnownPayloadFileName` whose row is missing. Skips negative-cache rows (empty `FileName`) on both passes. If `EnumerateAllEntriesLockedAsync` throws `SqliteException`, `ReconcileAsync` logs the failure and aborts — it never treats an enumeration failure as "empty list," which would delete every cached payload file.

What the subclass must provide:

**Properties**
- `string TableName { get; }` — used by generic Stats / Clear / default eviction query.
- `string PayloadFolderName { get; }` — per-entity subfolder name; passed to the subclass's path-resolution method.
- `string ResolvePayloadFolder()` — subclass owns this so the image subclass can keep today's MSIX-vs-unpackaged branches.
- `CacheOptionsBase Options { get; }` — typed accessor; subclass exposes its own derived options class.
- `int CurrentSchemaVersion { get; }`
- `IReadOnlyList<MigrationStep> MigrationSteps { get; }` — each step is `(targetVersion, sql)`. All steps for one bump execute inside a single outer transaction; any failure rolls back the entire bump and leaves `user_version` at its previous value, so the next launch retries cleanly.
- `string CreateCurrentSchemaSql { get; }` — the **complete** current schema (table + every index + every trigger). Used by the v0→current step *and* by the future-version rebuild path. Must always represent the latest schema; do not split table and indexes across separate properties.
- `bool IsKnownPayloadFileName(string fileName)` — for `ClearAsync` + `ReconcileAsync`. Must exclude `.tmp` files used by streamed writes (today's hex-only basename check already does).

**Locked methods (gate-held; take `SqliteConnection`)** — called by the base's payload-lifecycle methods. **All locked methods throw `SqliteException` on DB failure**; the base catches and decides the right user-visible behavior per call site (see [Error contract](#error-contract-public-methods-warn--return-locked-methods-throw)).
- `Task<TEntry?> GetEntryLockedAsync(SqliteConnection conn, string keyHash, CancellationToken)`
- `Task TouchEntryLockedAsync(SqliteConnection conn, string keyHash, DateTimeOffset, CancellationToken)`
- `Task UpsertEntryLockedAsync(SqliteConnection conn, string keyHash, TEntry, CancellationToken)`
- `Task RemoveEntryLockedAsync(SqliteConnection conn, string keyHash, CancellationToken)`
- `Task<IReadOnlyList<(string KeyHash, TEntry)>> GetEvictionCandidatesLockedAsync(SqliteConnection conn, string? protectedKeyHash, CancellationToken)` — for eviction; audio overrides to filter `Pinned=0`. Excludes empty-`FileName` rows.
- `Task<IReadOnlyList<(string KeyHash, TEntry)>> EnumerateAllEntriesLockedAsync(SqliteConnection conn, CancellationToken)` — for reconcile **only**. No filters: returns pinned, negative, and ordinary rows. Throws on DB failure; the base's `ReconcileAsync` catches and aborts so a SQLite hiccup never gets interpreted as "every file is an orphan."

**Public methods (gate-acquiring)** — kept for service-layer callers; trivially delegate to the `*LockedAsync` variants after acquiring the gate, then catch `SqliteException` to preserve today's warn-and-return-safe-default contract. Signatures match today's `IImageCacheStore`.

The image subclass keeps its existing public methods (signatures unchanged) plus the legacy-manifest discard one-off. The `*LockedAsync` variants are new, as is `EnumerateAllEntriesLockedAsync`.

---

## Migration framework details

Per-entity `EnsureSchemaAsync` driver lives in the base, parameterized over the subclass's `CurrentSchemaVersion` and `MigrationSteps`. Pattern matches today's image cache:

```
read user_version
if version == CurrentSchemaVersion: done
if version > CurrentSchemaVersion: rebuild (drop table + indexes, recreate via CreateCurrentSchemaSql)
else: for each step where step.target > version: run step in transaction
set user_version = CurrentSchemaVersion, commit
```

All steps for one bump share a single outer transaction; any failure rolls back the entire bump, leaving `user_version` at its previous value, so the next launch retries cleanly.

For the image cache: `CurrentSchemaVersion=1`. `CreateCurrentSchemaSql` concatenates today's `CreateEntriesTableSql` + `CreateLastAccessIndexSql`. Existing DBs already at v1 short-circuit on the first check. Existing DBs at v0 (empty, fresh install before this refactor) run the single v0→v1 step.

For lyrics/audio: each starts at `CurrentSchemaVersion=1` with their own `CreateCurrentSchemaSql`. Independent timelines.

---

## Path layout

```
<MSIX LocalCache>                            (packaged)
├── ImageCache/                              ← preserved verbatim from today
│   ├── cache.db (+ -wal, -shm)
│   └── <keyhash>.<ext>
├── LyricsCache/                             (Phase 4.5)
└── AudioCache/                              (Phase 5.3)

%LOCALAPPDATA%\Hypersonic\Cache\             (unpackaged / dev)
├── Images/                                  ← preserved verbatim from today (note: not "ImageCache")
│   ├── cache.db
│   └── <keyhash>.<ext>
├── LyricsCache/                             (Phase 4.5)
└── AudioCache/                              (Phase 5.3)
```

**Path resolution is per-subclass**, not centralized. Today's `ImageCachePath.Resolve` already branches on `RuntimeHelper.IsMSIX` and resolves to two different folder names (`ImageCache` packaged, `Images` unpackaged). Centralizing on a single `CachePath.Resolve(options, "ImageCache")` would silently relocate the unpackaged cache and abandon every dev machine's existing entries.

`SqliteImageCacheStore.ResolvePayloadFolder()` preserves today's `ImageCachePath.Resolve` logic verbatim — same MSIX vs unpackaged branches, same folder names. `ImageCachePath` either stays as-is and is called from the subclass, or its body inlines into the subclass; either way, the resolved paths don't change.

Greenfield entities (lyrics, audio) use **a single folder name across both layouts**: `LyricsCache` (Phase 4.5) and `AudioCache` (Phase 5.3). No legacy compat to preserve, so the packaged and unpackaged names match — keeps debugging and docs simpler. Their `ResolvePayloadFolder()` calls `CachePath.Resolve(options, "LyricsCache" | "AudioCache")`, which branches on `RuntimeHelper.IsMSIX` the same way image's does but uses the same subfolder name on both branches.

---

## Test strategy

Existing tests stay and shift owners:

- **`SqliteImageCacheStoreTests`** — keeps DB-level tests (UPSERT round-trip, schema migration, self-heal, contended write fail-fast, legacy manifest discard, privacy assertions). Tests for behaviors that **move into the base** (reconcile orphan sweep, eviction with file cleanup, atomic write+upsert, payload directory lifecycle) **also move** to `SqliteCacheStoreBaseTests`, exercised through a test-only `TestCacheStore : SqliteCacheStoreBase<TestEntry>` subclass.
- **`ImageCacheServiceTests`** — keeps tests for service-layer policy (resolver chain selection, in-flight dedup, TTL expiry, stale fallback on fetch failure, cancellation on dispose). Tests that exercised "service writes file then upserts" semantics are subsumed by the base tests.

Add `Hypersonic.Tests/Services/SqliteCacheStoreBaseTests.cs`:

- Open + migration v0→v1 (via `TestCacheStore.CurrentSchemaVersion=1`).
- Self-heal: pre-corrupt the file, observe rebuild.
- Concurrent gate: blocked write doesn't deadlock; bounded fail-fast under contention.
- Dispose drain timeout.
- Migration v(current+1)→rebuild path.
- **New**: `WritePayloadAsync` (buffered) atomic move + upsert + prior-file cleanup when `FileName` changes.
- **New**: `WriteStreamedPayloadAsync` opens the temp file before acquiring the gate; concurrent reads against existing entries complete while the streamed writer is still running; the gated rename + upsert happens only after the writer finishes; `entryFactory` receives the actual written `Length`.
- **New**: `WriteStreamedPayloadAsync` disposal — disposing the store mid-stream cancels the writer's token, deletes the temp file in the writer's finally block, decrements the in-flight counter, and disposal completes within the drain timeout.
- **New**: `WriteNegativeAsync` writes `Length=0` + empty `FileName` row, leaves no payload file, and a subsequent `TryGetPayloadUriAsync` returns null while `GetEntryAsync` surfaces the negative entry.
- **New**: `EvictIfNeededAsync` deletes both files and rows, protects the named key, respects `MaximumBytes`, and skips negative-cache rows (no file to delete; row is tiny).
- **New**: `ReconcileAsync` uses `EnumerateAllEntriesLockedAsync` so pinned and negative rows are visible (not mistakenly orphan-swept); removes rows for missing files, deletes orphan files matching the predicate, leaves unrelated files alone.
- **New**: `ReconcileAsync` aborts on enumeration failure — induce a `SqliteException` from `EnumerateAllEntriesLockedAsync` and assert no files are deleted.
- **New**: `ClearAsync` deletes both rows and payload files (and leaves non-matching files alone).
- **New**: Write-compensation paths — induced UPSERT failure with **different** new `FileName` leaves the orphaned new file findable by reconcile; induced `File.Move` failure leaves the prior entry/file authoritative.
- **New**: Same-name overwrite staleness — induce UPSERT failure with **same** new `FileName` (same extension), assert the file has new bytes, the row carries old `Length`/`LastAccessUtc`/`ExpiresAtUtc`, and reconcile leaves both alone (documented as benign staleness).
- **New**: Startup temp-file sweep — pre-seed `<keyhash>.<guid>.tmp` files in the payload folder, init the store, assert they're gone and real payload files are untouched.

Per-entity subclasses get focused tests for their schema/UPSERT/queries (the image one already has them, trimmed of the moved behaviors).

---

## Boundaries with adjacent phases

- **Phase 4.5 (Time-Synced Lyrics)** introduces `ILyricsCacheStore`, `LyricsCacheEntry`, `SqliteLyricsCacheStore`, and `LyricsService`. Negative-cache semantics ("definitively no lyrics") use `WriteNegativeAsync` (`Length=0` row, no file). The service layer owns provider chain (structured → legacy fallback) and validator invocation; the store just persists.
- **Phase 5.3 (Audio Cache)** introduces `IAudioCacheStore`, `AudioCacheEntry` (with `Pinned INTEGER NOT NULL DEFAULT 0`), `SqliteAudioCacheStore`, and `AudioCacheService`. The store overrides `GetEvictionCandidatesAsync` to filter `WHERE Pinned = 0`; the base's `EvictIfNeededAsync` accepts a single protected key today, with a planned widening to a key set for pre-fetch protection (the service passes "current + next 1–2 tracks").
- **Settings page total cache stats** aggregates across all three stores via the optional `CacheAdminService` facade, or directly in `SettingsViewModel` (`Task.WhenAll` over per-entity `GetStatsAsync`).

---

## Deferred / out of scope

- **Per-entity Settings UI** ("Clear lyrics cache only"). Trivial follow-up once the stores exist.
- **Cross-entity transactions**. No use case; not supported by per-entity-DB architecture.
- **Encryption-at-rest** for cached content. The current `PrivacyAssertions` test verifies no plaintext secrets land in the DB; that pattern extends to lyrics/audio. Full encryption is a separate decision orthogonal to this refactor.
- **Cache size budget arbitration across entities**. Each entity has its own `MaximumBytes`. A future "total cache cap" feature would need a coordinator service; not in scope here.
- **Migration of the existing image cache to a new schema**. Schema is unchanged; existing users see no migration.

---

## References

- Current implementation: `Hypersonic/Services/SqliteImageCacheStore.cs`, `Hypersonic/Services/SqliteImageCacheSchema.cs`, `Hypersonic/Services/CachedImageService.cs`.
- Image cache options: `Hypersonic/Models/ImageCacheOptions.cs`.
- Image cache path: `Hypersonic/Services/ImageCachePath.cs`.
- Roadmap: `ROADMAP.md` §5.2 (Image Cache, done), §5.3 (Audio Cache, planned), §4.5 (Time-Synced Lyrics, planned).
- Lyrics data model: `docs/design/service/2026-05-22-lyrics-data-model.md`.
