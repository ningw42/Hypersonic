# Lyrics Provider + Service (Phase 1) — Implementation Notes

Companion to the design at [`../../design/service/2026-06-09-lyrics-provider-service.md`](../../design/service/2026-06-09-lyrics-provider-service.md). Records only what the design (and the plan it spawned) does **not** already say: the concrete shape the code took, where it diverged from the plan, and the details discovered during implementation and code review. The design's semantics — one-provider-per-source, the `definitive`-bit cache gate, raw-payload parse-on-read, asymmetric positive/negative TTL, and the Subsonic server+user+id scope — were implemented faithfully and are not re-explained here.

Landed in commit `bf561f10a6ced7d6bdb157f2d1cbcb9200f8ae42` (squash of the feature plus three code-review rounds), on top of the design commit `8d81ac5a226ff5e6b1758bae803f782c36baa929`.

What shipped, in dependency order:

- **Core contracts + value types** — `ILyricsProvider`, `ILyricsPayloadParser`, `ILyricsParserRegistry`, `LyricsQuery`, `LyricsProviderResult`, `LyricsOutcomeKind` (moved here), and `LyricsParserRegistry`.
- **Subsonic source** (`Hypersonic/Services/Lyrics/`) — `SubsonicLyricsProvider`, the structured + legacy parsers, `SubsonicLyricsClassifier`, `SubsonicLyricsConstants`, and the shared `LyricsSyncKindRank`.
- **Cross-provider policy** — `LyricsFallbackPolicy` (+ `LyricsCacheAction`, `LyricsFallbackDecision`).
- **Durable cache** — `LyricsCacheEntry`, `LyricsCacheOptions`, `ILyricsCacheStore`, `SqliteLyricsCacheStore`, the generic `CachePayload<TEntry>`, and the base's new `ReadPayloadAsync` + orphan-cleanup on `WriteNegativeAsync`.
- **Scope helper** — `SubsonicCacheScope`, extracted from `SubsonicImageResolver`.
- **Service** — `ILyricsService` / `LyricsService`.
- **Client DTO timing fix** — XML wire shims on `LyricLine` / `StructuredLyrics`.
- **Migration + wiring** — `NowPlayingViewModel` rewired to the service; DI registration + startup reconcile in `App.xaml.cs`; `LyricsFetchPolicy` and its tests deleted.

---

## Deviations from the plan

### `LyricsFallbackPolicy` lives in `Services/Lyrics/`, not `Helpers/`

The plan filed the cross-provider policy under `Hypersonic/Helpers/`. It shipped in `Hypersonic/Services/Lyrics/` ([`LyricsFallbackPolicy.cs`](../../../Hypersonic/Services/Lyrics/LyricsFallbackPolicy.cs)) instead, for two reasons: `Hypersonic.Helpers` already held the legacy `LyricsOutcomeKind` being relocated to Core, and a `using` alias did not cleanly resolve the resulting collision; and the design itself says the policy sits *"next to the providers,"* which `Services/Lyrics/` satisfies literally. The implementation matches the design's placement, not the plan's sketch.

### A generic `CachePayload<TEntry>` replaces the plan's tuple / lyrics-specific record

The plan had the base `ReadPayloadAsync` return `Task<(TEntry Entry, byte[] Bytes)?>` and `ILyricsCacheStore` declare a lyrics-specific `LyricsCachePayload(LyricsCacheEntry, ReadOnlyMemory<byte>)`. Implementation: one generic [`CachePayload<TEntry>(TEntry Entry, ReadOnlyMemory<byte> Bytes)`](../../../Hypersonic/Services/Cache/CachePayload.cs) in `Services/Cache/`, returned by the base and surfaced through `ILyricsCacheStore.ReadPayloadAsync` as `CachePayload<LyricsCacheEntry>?`.

**Why:** the same record will serve the future audio cache, mirroring how `CacheStoreStats` is one shared shape across entities. Bytes are `ReadOnlyMemory<byte>` (not `byte[]`) so the parser can be handed a span without a defensive copy.

### `ILyricsCacheStore` omits `TryGetPayloadUriAsync`

The design's contract list (design §`ILyricsCacheStore`) mirrors the image store's surface *including* `TryGetPayloadUriAsync`, **plus** `ReadPayloadAsync`. The shipped [`ILyricsCacheStore`](../../../Hypersonic/Contracts/Services/ILyricsCacheStore.cs) drops `TryGetPayloadUriAsync` entirely: lyrics are parsed in-process from bytes and never hand a file `Uri` to XAML, so the URI accessor has no consumer on the lyrics path. The base still implements it (the image store uses it); the lyrics interface simply does not surface it. A deliberate, reasonable narrowing of the mirrored surface.

### `ReadPayloadAsync` is public on the generic base, and `WriteNegativeAsync` gained orphan cleanup there

The plan/design framed `ReadPayloadAsync` as a lyrics-store addition (it offered subclass-vs-base). It went on the **base** ([`SqliteCacheStoreBase.cs:496`](../../../Hypersonic/Services/Cache/SqliteCacheStoreBase.cs#L496)), consistent with the generic-cache-store doc's "public DB methods live on the base" decision — `SqliteLyricsCacheStore` writes only the `*LockedAsync` halves. `WriteNegativeAsync` already existed on the base from the generic-cache work; the lyrics slice **extended** it (see *Negative write cleans up the prior positive payload* below) rather than adding a new method. `ILyricsCacheStore` still declares the consumer-facing `ReadPayloadAsync`/`WriteNegativeAsync` so callers are unaware the bodies are inherited.

### The provider prefers the highest sync tier across entries, not "the first usable entry"

The **plan** said the Subsonic provider *"picks the first usable `StructuredLyrics` entry."* (The design does not specify multi-entry selection — it only says the service short-circuits on the first usable *provider* and that the provider-local structured→legacy fallback "owns the quality choice," leaving the entry-pick unspecified.) A server may return several `structuredLyrics` blocks (e.g. a plain copy and a synced copy), and picking the first would let a plain entry listed ahead of a synced one win. The shipped `SubsonicLyricsProvider.SelectBestStructuredContent` instead serializes + parses **each** text-usable entry and keeps the one whose parsed document has the highest [`LyricsSyncKindRank`](../../../Hypersonic/Services/Lyrics/LyricsSyncKindRank.cs), with list order as the tiebreak. This is a correction to the plan's entry-pick rule, not a departure from the design. (Tightened during code review.)

### `LyricsSyncKindRank` is a new shared helper

Not in the plan. Both the provider's best-entry pick and the policy's best-result pick need the same `Word > Line > Plain > None` ordering; factoring it into one `internal static` type ([`LyricsSyncKindRank.cs`](../../../Hypersonic/Services/Lyrics/LyricsSyncKindRank.cs)) keeps "better" from being defined twice and drifting.

### The read path overlays index-row metadata onto the parsed document

The plan's positive-read path was just *"`ReadPayloadAsync` + registry parse + touch."* Implementation adds `LyricsService.OverlayRowMetadata` ([`LyricsService.cs:352`](../../../Hypersonic/Services/LyricsService.cs#L352)): after parsing, source/language/offset from the index row fill any field the parser left null, then a no-op fast path returns the document untouched when nothing needs overlaying (the common Subsonic case, where the payload already carries them). The point is forward-compat: a future metadata-poor format (LRC/TTML/YRC) can recover those fields from the row without a contract change. The parser always owns `SyncKind` and `Lines`.

### `ExtensionForFormat` lives on the service, not the store

The plan put `internal static ExtensionForFormat(formatTag)` on `SqliteLyricsCacheStore`. It shipped as a private method on `LyricsService` ([`LyricsService.cs:374`](../../../Hypersonic/Services/LyricsService.cs#L374)) — the service is the only caller, because it composes the entry's `FileName` (`keyHash + extension`) before handing the entry to `WritePayloadAsync`. The store never needs the map.

### `SubsonicCacheScope` returns one composed key hash, not three out-params

The plan sketched `TryBuildSongScope(service, songId, out serverHash, out userScopeHash, out idHash)`. The shipped helper is [`TryBuildSongKeyHash(service, songId, out keyHash)`](../../../Hypersonic/Services/Cache/SubsonicCacheScope.cs#L52): it returns the single fully-composed key `Hash("lyrics|{serverHash}|{userScopeHash}|{idHash}")`, since the only caller (`LyricsService`) needs the final key, not the parts. The `lyrics|` prefix namespaces the key so a future per-entity cache built from the same triple cannot collide. The helper also exposes `Hash` and `GetServerHash`, which `SubsonicImageResolver` now calls (behavior-preserving — identical hashes to its former private logic).

---

## Implementation details beyond the design

### XML wire shims rescue nullable structured timing in the Client DTOs

The structured timing fields (`LyricLine.start`, `StructuredLyrics.offset`) are `long?` so consumers see one universally-typed property. But `XmlSerializer` cannot encode a `Nullable<long>` value type as an attribute, so an XML-format server response silently dropped both (the JSON path via `System.Text.Json` was unaffected). The fix ([`LyricLine.cs`](../../../Hypersonic.Client/Models/Entities/LyricLine.cs), [`StructuredLyrics.cs`](../../../Hypersonic.Client/Models/Entities/StructuredLyrics.cs)): keep `Start`/`Offset` as the single consumer-facing `long?` (`[XmlIgnore]`), and add a string-backed `StartXml`/`OffsetXml` (`[XmlAttribute]`, `[JsonIgnore]`, `[EditorBrowsable(EditorBrowsableState.Never)]`) whose setter parses the attribute into the nullable. The shim is hidden from IntelliSense; a missing or non-numeric attribute leaves the value null. This is the only change outside `Hypersonic` / `Hypersonic.Core` — it touches `Hypersonic.Client`.

### The provider observes cancellation at three points and tells caller-cancel from HTTP timeout

`SubsonicLyricsProvider.FetchAsync` ([`SubsonicLyricsProvider.cs`](../../../Hypersonic/Services/Lyrics/SubsonicLyricsProvider.cs)) calls `ThrowIfCancellationRequested` before the structured call and again between the structured short-circuit and the legacy call. Each endpoint helper rethrows `OperationCanceledException` **only when our token is cancelled** (genuine caller cancellation); otherwise — including a `TaskCanceledException` from an HTTP timeout, where our token is *not* cancelled — it is classified as a transient failure, so the legacy fallback still runs. Without the distinction, a server timeout would have masqueraded as caller cancellation and skipped the fallback.

### In-flight dedup guards against synchronous completion

`LyricsService.FetchAndStoreAsync` opens with `await Task.Yield()` ([`LyricsService.cs:173`](../../../Hypersonic/Services/LyricsService.cs#L173)) so the task cannot complete *inside* the `ConcurrentDictionary.GetOrAdd` factory. A fully-synchronous fetch (a fake store in tests, or a same-tick failure) would otherwise let the removal continuation run before the entry was inserted, stranding a completed task in the dedup map and suppressing every later refetch of that key. Real fetches yield on I/O, but the code must not rely on it.

### Negative write cleans up the prior positive payload

When a key flips positive → negative, the base `WriteNegativeAsync` ([`SqliteCacheStoreBase.cs:1105`](../../../Hypersonic/Services/Cache/SqliteCacheStoreBase.cs#L1105)) now captures the old entry, upserts the negative row, then best-effort deletes the previous payload file. A track that loses its lyrics no longer leaves an orphaned `.json`/`.txt` behind until the next reconcile. The prior-entry lookup is wrapped so a failure there just skips the cleanup (it is best-effort, not part of the write's success contract). Added in code review.

### Offset survives a Plain structured document

The structured parser maps `offset` → `OffsetMs` on **both** branches — the synced `Line` document and the `Plain` document (whether unsynced, or synced-but-downgraded by bad/missing per-line timing) — matching the design's `offset` → offset mapping for the whole `subsonic-structured` format rather than only the synced path ([`SubsonicStructuredLyricsParser.cs`](../../../Hypersonic/Services/Lyrics/SubsonicStructuredLyricsParser.cs)). `LyricsValidator` does not inspect `OffsetMs`, so a Plain document carrying it stays valid. Added in code review.

### The structured request opts into no word-timing tier

`GetLyricsBySongIdAsync(string id)` and its [`GetLyricsBySongIdRequest`](../../../Hypersonic.Client/Models/Requests/GetLyricsBySongIdRequest.cs) send only `id` — there is no `enhanced` / word-timing opt-in anywhere in `Hypersonic.Client`. The OpenSubsonic `structuredLyrics` this endpoint returns carries line-level `start` at most, so the structured parser is inherently Line-or-Plain and **never** emits `Word`; the design's "never Word" note has this request shape as its concrete cause. A word-timing tier — and any server or extension that would expose one — is a Phase 1.5+ concern.

### `LyricsService` mirrors `CachedImageService`'s lifecycle and disposes providers

The service reuses the image service's disposal/cancellation shape: a `_disposeCts`, a linked-token source per call, a volatile `IsDisposed` guard, and OCE/`ObjectDisposedException` swallowed on the dispose path. Beyond that it disposes any `IDisposable` providers on its own `Dispose` ([`LyricsService.cs`](../../../Hypersonic/Services/LyricsService.cs)) — Phase 1.5 external sources will own `HttpClient`/socket handles. The positive write preserves `CreatedAtUtc` from any prior entry (a refresh bumps `LastAccess`/`Expires` but does not reset creation time).

### Store specifics

[`SqliteLyricsCacheStore`](../../../Hypersonic/Services/SqliteLyricsCacheStore.cs) supplies only the entity-specific bits over the base:

- A `WITHOUT ROWID` `Entries` table keyed by `KeyHash`, with `SyncKind` stored as **TEXT** (the enum name, parsed back with a `LyricsSyncKind.None` fallback) and nullable `LanguageCode` / `OffsetMs` columns.
- An `IX_Entries_LastAccessUtc` index backing LRU eviction.
- Eviction candidates filtered by `FileName != ''`, so negative rows are never evicted.
- `IsKnownLyricsFileName`: a 64-hex basename plus `.json` / `.txt` (with `.lrc` / `.ttml` / `.yrc` reserved for Phase 1.5 formats); excludes `cache.db` and unrelated files.
- Greenfield: `MigrationSteps` is empty and the schema bootstraps via `CreateCurrentSchemaSql` (the base special-cases v0 → v1).

### `LyricsOutcomeKind` moved to Core; the Subsonic specifics stayed

Plan decision #1, carried out: the four-way `LyricsOutcomeKind` enum now lives in `Hypersonic.Core/Models/Lyrics/` so the Core `LyricsProviderResult` can carry it. The Subsonic error-code classification (`ClassifyFailedStatus` and friends) stayed in `Hypersonic` as `SubsonicLyricsClassifier`, since those codes are Subsonic-specific.

### `NowPlayingViewModel` migration

As designed, the VM drops the in-VM structured/legacy fetchers, the `Combine` call, the 10-entry LRU, and the `ISubsonicClientService` dependency; it injects `ILyricsService` and adds `CurrentLyrics` (`LyricsDocument?`) for the Phase 2 renderer, keeping `LyricsText` as a flattened-from-document fallback ([`NowPlayingViewModel.cs`](../../../Hypersonic/ViewModels/NowPlayingViewModel.cs)). Both `null` (no usable lyrics this attempt) and `SyncKind == None` (definitively none) map to `NoLyrics`; a fetch exception also maps to `NoLyrics` (logged). The generation guard, dispatcher marshalling, active/inactive lifecycle, `TechnicalDetail`, `SelectedTab`, and the `PendingLyricsFetch` test handle are unchanged. The VM passes `CancellationToken.None` — the service owns its own dispose-linked cancellation, so the VM does not thread one through.

---

## Files

| Area | Files |
|---|---|
| Core contracts | `Hypersonic.Core/Contracts/Lyrics/{ILyricsProvider,ILyricsPayloadParser,ILyricsParserRegistry}.cs` |
| Core models | `Hypersonic.Core/Models/Lyrics/{LyricsOutcomeKind,LyricsQuery,LyricsProviderResult}.cs` |
| Core registry | `Hypersonic.Core/Services/Lyrics/LyricsParserRegistry.cs` |
| Subsonic source | `Hypersonic/Services/Lyrics/{SubsonicLyricsProvider,SubsonicStructuredLyricsParser,SubsonicLegacyLyricsParser,SubsonicLyricsClassifier,SubsonicLyricsConstants,LyricsSyncKindRank}.cs` |
| Cross-provider policy | `Hypersonic/Services/Lyrics/LyricsFallbackPolicy.cs` |
| Durable cache | `Hypersonic/Models/{LyricsCacheEntry,LyricsCacheOptions}.cs`, `Hypersonic/Contracts/Services/ILyricsCacheStore.cs`, `Hypersonic/Services/SqliteLyricsCacheStore.cs`, `Hypersonic/Services/Cache/CachePayload.cs`, `Hypersonic/Services/Cache/SqliteCacheStoreBase.cs` (added `ReadPayloadAsync`; extended `WriteNegativeAsync`) |
| Scope helper | `Hypersonic/Services/Cache/SubsonicCacheScope.cs`, `Hypersonic/Services/ImageResolvers.cs` (delegates to it) |
| Service | `Hypersonic/Contracts/Services/ILyricsService.cs`, `Hypersonic/Services/LyricsService.cs` |
| Client DTOs | `Hypersonic.Client/Models/Entities/{LyricLine,StructuredLyrics}.cs` (XML timing shims) |
| Migration + wiring | `Hypersonic/ViewModels/NowPlayingViewModel.cs`, `Hypersonic/App.xaml.cs` |
| Removed | `Hypersonic/Helpers/LyricsFetchPolicy.cs`, `Hypersonic.Tests/Helpers/LyricsFetchPolicyTests.cs` |
| Tests | `Hypersonic.Core.Tests/Services/Lyrics/LyricsParserRegistryTests.cs`; `Hypersonic.Tests/Services/Lyrics/{SubsonicLyricsParsersTests,SubsonicLyricsProviderTests,SubsonicLyricsClassifierTests,LyricsFallbackPolicyTests,LyricsServiceTests,SqliteLyricsCacheStoreTests}.cs`; `Hypersonic.Tests/Services/Cache/SubsonicCacheScopeTests.cs`; `Hypersonic.Tests/Fakes/FakeLyricsSubsonicClient.cs`; `GetLyricsBySongIdResponseTest` (XML timing); `NowPlayingViewModelTests` (rewritten against a fake `ILyricsService`) |

---

## Verification

`dotnet build Hypersonic.sln -p:Platform=x64` — succeeds with no errors. A fresh build emits a few pre-existing warnings, none introduced by this change: `WMC1506` XAML markup-compiler warnings (this change touches no XAML) and a `CS8604` in `SqliteCacheStoreBase.cs`'s streamed-write commit path (untouched here — this change edited other methods in that file; already noted in the generic-cache-store implementation notes).

| Suite | Result |
|---|---|
| `Hypersonic.Core.Tests` | 176 passing |
| `Hypersonic.Client.UnitTest` | 52 passing (incl. the XML synced-timing round-trip) |
| `Hypersonic.Tests` | 571 passing (incl. the existing image-cache suites, proving the base `ReadPayloadAsync` addition and the `SubsonicCacheScope` extraction are behavior-preserving) |

Each logic-bearing change — the provider's structured/legacy matrix, the sync-tier selection, the fallback policy, the cache store CRUD/negative/reconcile, the metadata overlay, the cancellation contract, and the offset preservation — was written test-first (red → green) and followed by a full-suite regression.
