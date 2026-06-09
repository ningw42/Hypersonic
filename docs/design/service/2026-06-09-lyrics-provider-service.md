# Lyrics Provider + Service (Phase 1) — Design

Companion to `LYRICS.md` §Phase 1. Captures the pivotal decisions and the contracts that Phase 1.5 (external providers + concurrency), Phase 2 (Win2D renderer), and Phase 3 (NowPlayingPage integration) bind to. No source code — see the forthcoming implementation plan for that.

Builds on the shipped Phase 0 data model (`LyricsDocument` / `LyricsLine` / `LyricsWord` / `LyricsSyncKind` / `LyricsValidator`) and mirrors the cover-art cache trio (`CachedImageService` / `SqliteImageCacheStore` / `SqliteCacheStoreBase`).

---

## Scope of this slice

Of the four moving parts `LYRICS.md` packs into Phase 1, this slice builds **the full architecture but only the Subsonic source**:

- ✅ `ILyricsProvider` abstraction and the cross-provider service policy.
- ✅ `LyricsService` (singleton) — orchestrator + cache policy layer.
- ✅ `SqliteLyricsCacheStore` — durable cache mirroring the image cache end to end (raw payload, parse on read).
- ✅ `SubsonicLyricsProvider` — the one source that ships now.
- ✅ Migration of the existing fetch/cache logic out of `NowPlayingViewModel` into the service, producing `LyricsDocument` instead of plain text.

Deferred to **Phase 1.5** (clean fast-follow, no contract churn): `LrcLibLyricsProvider` and other external sources, concurrent quality-tiered fan-out with a time budget, and the `IHttpClientFactory` registration those require. Deferred to **Phase 3**: whether `PlayerViewModel` (not just `NowPlayingViewModel`) consumes the service.

The point of building the whole architecture against a single source now is to prove the durable cache and the provider/parser contracts against the server we already talk to, and to hand Phase 2 a real `LyricsDocument` to render — without taking on any new network, legal, or socket-lifecycle surface.

---

## What we're building, and where it lives

The codebase already separates pure, unit-testable logic (`Hypersonic.Core`) from app plumbing that needs WinUI / SQLite / the Subsonic client (`Hypersonic`). Lyrics follow the same seam.

**`Hypersonic.Core`** — pure, testable without WinUI (like `LyricsValidator`, `PlaybackQueue`):

- **The parser interface + a format registry** (`ILyricsPayloadParser`, keyed by format tag). The contract for turning a raw payload + format tag into a validated `LyricsDocument`, used by *both* the provider at fetch time and the cache at read time so the two can never diverge. Future Client-free parsers (LRC, TTML, YRC) live here too. The two *Subsonic* concrete parsers do **not** — see the placement note below.
- **`ILyricsProvider` contract** and the small value types it trades in (the query, the provider result).

**`Hypersonic`** — needs `ISubsonicClientService`, SQLite, `IAppFolderLocator`:

- **`SubsonicLyricsProvider`** — the one source; a thin fetch + serialize + classify adapter over the existing client.
- **`SubsonicStructuredLyricsParser` / `SubsonicLegacyLyricsParser`** — the two concrete parsers. They reuse the `Hypersonic.Client` `StructuredLyrics` / `LyricLine` DTOs, so they live here (both callers — the provider and the service's read path — are already in `Hypersonic`). This avoids both DTO duplication *and* a new `Hypersonic.Core → Hypersonic.Client` reference, keeping Core dependency-free per the Phase 0 principle.
- **`LyricsService` (singleton)** — orchestrator and cache policy layer; the analog of `CachedImageService`.
- **`SqliteLyricsCacheStore` / `LyricsCacheEntry` / `LyricsCacheOptions`** — the durable cache, mirroring the image trio.
- **The cross-provider fallback policy** — pure, static, sitting next to the providers.

**DI** (`App.xaml.cs`): store, service, and parser registry as singletons; providers registered as `ILyricsProvider` **in priority order** so the service receives an ordered `IEnumerable`; `Configure<LyricsCacheOptions>`; and a lyrics `ReconcileAsync` added to the same startup path as the image reconcile — `App.xaml.cs` `OnLaunched`, beside the existing `ReconcileImageCacheAsync` (the image reconcile runs there today, not in `ActivationService`).

---

## Pivotal decisions

### A provider is one real source, self-contained

The unit of a provider is a **source**, never a **format** or an **endpoint**. A single source may expose several formats (word / line / plain) and may need more than one API call to resolve the best one — and all of that is the provider's *internal* business. The provider returns its single best result.

This is a deliberate reversal of an earlier idea to split Subsonic into a structured "sub-provider" and a legacy "sub-provider". That idea conflates endpoint with source, and it collapses the moment a source like lrclib returns synced *and* plain in one response: you'd either build wasteful per-format sub-providers (duplicated network calls) or end up with an inconsistent model where some providers are per-source and some per-format. One-provider-per-source is the rule for Subsonic and every future source.

Consequences:

- `SubsonicLyricsProvider` owns its structured→legacy fallback internally. The existing two-call decision logic (`LyricsFetchPolicy.Combine` and its classification helpers) **relocates into the provider** rather than being retired — those semantics are Subsonic-specific and belong with the Subsonic source.
- The provider returns its best `LyricsDocument` (already parsed and validated, via the shared parser) together with the raw payload + format tag of whichever endpoint won.

### Cross-provider preference is the service's job; the contract is concurrency-agnostic

The service compares the providers' best results, prefers the higher sync tier (`Word` > `Line` > `Plain`, with registration priority as the tiebreak), picks the winner, and decides caching.

Phase 1 runs this **sequentially** — walk providers in priority order, short-circuit on the first usable Content. Phase 1.5 swaps in **concurrent fan-out + merge** under a time budget. The pivotal property: **the provider contract does not change between the two** — only the service's orchestration strategy does. That is what makes 1.5 a clean fast-follow instead of a rewrite.

The `Word > Line > Plain` tier ranking is therefore **mainly Phase 1.5 behavior**: it only bites when two or more providers return Content and must be compared. In Phase 1 the service short-circuits on the first usable provider, and with only `SubsonicLyricsProvider` present the *provider-local* structured→legacy fallback owns the quality choice. The cross-provider policy is still built now, but this slice exercises it only as a **unit-tested helper against fake providers** — no real second source reaches it until 1.5.

### Caching is gated by a `definitive` bit, preserving today's subtle rule

Today's `Combine` has a subtlety worth keeping: when the structured endpoint fails *transiently* and the legacy endpoint returns content, it **shows the legacy text but does not cache it**, because a better structured answer might exist once the transient clears.

Under the new model this is expressed cleanly: a Content result carries a `definitive` flag. `SubsonicLyricsProvider` sets `definitive: false` in exactly that one case. The service writes a positive cache entry **only when the winning result is definitive**. The semantics survive verbatim, and the rule now generalizes to any future source.

For the no-content case, the negative cache is written **only when every provider was definitive** — any transient outcome blocks the negative write, so a network hiccup never gets persisted as "this track has no lyrics."

### The cache stores the raw payload and parses on read

The durable cache persists the **byte-for-byte provider payload** plus a **format tag**, and rebuilds the `LyricsDocument` by running the format's parser over those bytes on each read. The parsed document is never itself persisted.

This keeps the cached source of truth lossless: the `LyricsDocument` model can gain fields later (translation lane, per-character timing) and old cache entries can be re-parsed to recover them without a re-fetch. It also keeps the cache format-agnostic — when lrclib (LRC text), AMLL (TTML), and NetEase (YRC) arrive, each is just another format tag and parser; the cache layer doesn't change.

What "raw payload" concretely means per source:

| Format tag | Raw payload stored | Payload file | Adapter result |
|---|---|---|---|
| `subsonic-structured` | the `StructuredLyrics` body, re-serialized to canonical JSON | `.json` | synced line-list → `Line`; unsynced → `Plain`; `lang`→language, `offset`→offset. Never `Word` (Subsonic structured carries no word timing). |
| `subsonic-legacy` | the plain-text lyrics blob | `.txt` | `Plain`, one line per text line |

The format tag is a **string**, not an enum, so a new source adds a format without an enum migration. The read-time parser is selected from the stored tag, independent of which provider originally fetched it.

**Malformed timing in `subsonic-structured`.** The `StructuredLyrics` DTO permits `Synced=true` with null per-line `Start` values. The structured adapter treats line-sync as all-or-nothing: when `Synced=true` and *every* line carries a valid non-negative, monotonic `Start`, it emits a `Line` document; when `Synced=false`, or when *any* line's `Start` is null / negative / out of order, it **downgrades the whole document to `Plain`** (all non-blank line values, `StartMs=0`). Downgrading rather than dropping the offending lines preserves all lyric text and avoids a holed timeline that would fail the validator or render with gaps. `Plain` is also the floor the legacy parser produces, so the two Subsonic shapes converge cleanly.

### The parser registry is shared between fetch and read

There is exactly one path from `(raw bytes, format)` to `LyricsDocument`, exposed through a small registry keyed by format tag. The provider calls it at fetch time to produce the document it ranks on; the cache calls it at read time to rebuild from stored bytes. Each parser runs `LyricsValidator` on its output and discards a document that comes back with issues (logging once) — a noisy payload degrades to "no usable lyrics from this source," not a corrupt render.

The interface and registry are Core; the two concrete Subsonic parsers are in `Hypersonic` because they deserialize `Hypersonic.Client` DTOs (see the placement note in §What we're building). The registry is populated by DI, so a parser's assembly is irrelevant to how it is resolved by format tag.

### The durable cache mirrors the image trio exactly

`SqliteLyricsCacheStore : SqliteCacheStoreBase<LyricsCacheEntry>` supplies only the entity-specific bits — schema SQL, column mapping, the locked CRUD/eviction/enumerate queries, `PayloadSubfolderName = "Lyrics"`, and a known-payload-filename predicate (`.json` / `.txt` now; `.lrc` / `.ttml` / `.yrc` reserved). Everything mechanical — the write gate, schema migration, self-heal on corruption, atomic payload write, LRU eviction, reconcile, negative-cache rows, startup temp-file sweep — is inherited from the base unchanged. It is a greenfield store, so it declares no payload-folder migration steps.

The one surface the lyrics store adds beyond the image store is a **payload read**: images hand a file `Uri` to XAML (`TryGetPayloadUriAsync`), but the lyrics service needs the bytes in-process to parse. So `ILyricsCacheStore` exposes `ReadPayloadAsync(keyHash) → (entry, bytes)` that returns the index row and payload bytes together, read **under the gate** so it is atomic with respect to eviction and orphan-row cleanup. The service never resolves a URI and reads the file itself. This can be implemented in the subclass over the base's existing locked get-entry primitive, or promoted into the base for the future audio cache to share.

### Cache keys follow the image cache's Subsonic scope

Subsonic song ids are unique only within one server *and* one user account. The cache key hashes the same triple `SubsonicImageResolver` builds for cover art: **server hash** (normalized base URI) + **active-user identity hash** (`ISubsonicClientService.ActiveUserIdentity`) + **song-id hash**. So a server switch, a credential/account change, or an id collision across servers can never surface the wrong lyrics. As with images, the key is built only when there is an active client *and* a non-empty active user identity; otherwise the lookup/fetch is skipped.

### TTL is asymmetric: positive long, negative short

Positive entries get a long TTL (~30 days, matching the image cache) with stale-fallback on a later transient failure. Negative entries ("definitively no lyrics") get a **shorter TTL (~7 days)** so a track that *gains* lyrics on the server later gets re-checked rather than being pinned to "none" indefinitely. Both are options, not constants.

---

## Contracts (conceptual)

- **`ILyricsProvider`** — an `Id` (also the cached `SourceProvider`) and a single `FetchAsync(query, ct)`. Returns a result that is one of: `Content` (carrying the validated `LyricsDocument`, the raw payload, its format tag, and the `definitive` bit) / `DefinitiveEmpty` / `DefinitiveUnsupported` / `Transient`. The four-way classification reuses the existing `LyricsOutcomeKind` and the Subsonic error-code helpers.
- **`LyricsQuery`** — everything any source might need: song id, artist, title, album, duration. Subsonic-structured uses the id; Subsonic-legacy uses artist + title; lrclib later uses the rest. Carrying them all now means the query shape doesn't change when 1.5 lands.
- **`ILyricsService`** — `GetLyricsAsync(query, ct) → LyricsDocument?`. The service has already done its own provider retries and stale-fallback internally, so the return is terminal for this attempt: **`null` = no usable lyrics this attempt, not cached as definitive** (a later track change or app launch may try again); **`SyncKind = None` = definitively no lyrics, negative-cacheable**; any other document = loaded. The two negative outcomes differ only in caching — both present as `NoLyrics` to the VM, so no `LyricsLoadState` change is needed.
- **`LyricsCacheEntry`** — index row keyed by the scoped hash. Beyond the `ICacheEntry` essentials (`FileName` — empty for negative rows; `Length`; `LastAccessUtc`), it carries `SourceProvider`, `PayloadFormat` (selects the read-time parser), `SyncKind`, `LanguageCode?`, `OffsetMs?`, and the `CreatedAt / ExpiresAt` lifecycle columns. These let the service pick the parser and make TTL decisions without touching the payload file.
- **`ILyricsCacheStore`** — the image store's surface (entry CRUD, `WritePayloadAsync`, `TryGetPayloadUriAsync`, reconcile, eviction, stats/clear) **plus** `ReadPayloadAsync(keyHash) → (entry, bytes)` for gate-consistent parse-on-read.

---

## Data flow

Track change → `NowPlayingViewModel` builds a `LyricsQuery` → `LyricsService.GetLyricsAsync`:

1. **Cache lookup** by key. Positive, unexpired hit → `ReadPayloadAsync` → bytes, parse-on-read via the registry (using the row's stored format), touch `LastAccess`, return the document. Negative, unexpired hit → return `LyricsDocument.Empty`. Expired or missing → continue.
2. **In-flight dedup** by key (a `ConcurrentDictionary<key, Task<LyricsDocument?>>`, exactly like `CachedImageService`) so now-playing and any future preload of the same track share one fetch.
3. **Fallback policy** over the ordered providers → winner. Content → write the raw payload + index row (positive, gated by `definitive`); no content with all-definitive → write the negative row; transient → return `null` and **fall back to a stale entry if one exists**.
4. Result maps to `LyricsLoadState` and `CurrentLyrics` on the VM: a loaded document → `Loaded`; **both `null` and `SyncKind=None` → `NoLyrics`** (the cache-vs-not distinction is the service's concern, invisible to the VM); no track → `NoTrack`.

Startup `ReconcileAsync` (wired into `App.xaml.cs` `OnLaunched`, beside the existing `ReconcileImageCacheAsync`) sweeps rows whose payload file is missing and payload files whose row is missing.

---

## NowPlayingViewModel migration

The VM stops owning lyric logic. Removed: the in-VM structured/legacy fetchers, the `Combine` call, and the in-memory 10-entry LRU. Added: an injected `ILyricsService` and a `CurrentLyrics` (`LyricsDocument?`) property for the Phase 2 renderer. `LyricsText` stays as a flattened-from-document fallback so today's text UI keeps working until Phase 2 replaces it. The generation guard against stale async completions, the dispatcher marshalling, the active/inactive lifecycle, and `TechnicalDetail` are untouched.

---

## Testing strategy

- **Parser/adapter tests** — structured JSON → `Line` / `Plain` (including the `Synced`-with-null-`Start` downgrade to `Plain`), legacy text → `Plain`, validator-discard on malformed payloads, and a raw→parse round-trip. These live in `Hypersonic.Tests` next to the Subsonic parsers (which reuse Client DTOs); the parser interface/registry and any future Client-free parser stay Core-testable.
- **Provider** — `SubsonicLyricsProvider`'s internal structured→legacy resolution. The existing `LyricsFetchPolicyTests` matrix ports here largely intact (it is the same decision logic, relocated).
- **Policy** — cross-provider merge: tier preference, priority tiebreak, the `definitive` gate, and transient-blocks-negative-cache, exercised with fake providers (no real source needed to test the chain).
- **Service** — cache hit / miss / negative / stale-fallback, in-flight dedup, and TTL via an injected `TimeProvider`, against a fake store + fake providers.
- **Store** — a light CRUD round-trip + negative + reconcile; the heavy base behavior is already covered by the image store's tests.
- **VM** — track-change → state mapping against a fake `ILyricsService`.

---

## Boundaries with adjacent phases

- **Phase 0** (shipped) supplies the `LyricsDocument` shape and `LyricsValidator`; this phase consumes both and adds no model changes.
- **Phase 1.5** adds external providers and concurrent tiered fan-out. It plugs into the *same* `ILyricsProvider` contract and the *same* parser registry (new format tags + parsers), and changes only the service's orchestration strategy. It brings the `IHttpClientFactory` registration and the legal-posture settings toggle for NetEase/QQ.
- **Phase 2** (Win2D renderer) consumes `CurrentLyrics` + the playback position; it assumes the validator's invariants hold and does not re-validate or re-fetch.
- **Phase 3** decides whether `PlayerViewModel` consumes the service directly and replaces the text area with the renderer. Phase 1 deliberately keeps consumption in `NowPlayingViewModel` against the singleton service, so that decision is a pure rewire later.

---

## Deferred / out of scope

- **External providers** (lrclib, AMLL TTML, NetEase YRC) and the **concurrent quality-tiered fallback with a time budget** — Phase 1.5. The architecture is built for them; they are not implemented here.
- **`IHttpClientFactory`** — only needed once a source makes its own outbound HTTP. Subsonic-only reuses the existing `SubsonicClient`, so this is a 1.5 concern.
- **Multi-lane lyrics** (translation, romanization) and **per-character timing** — out of scope at the model level (Phase 0) and here. The raw-payload cache means they can be added later without invalidating cached entries.
- **State ownership beyond `NowPlayingViewModel`** — Phase 3.

---

## Risks / open questions

- **Server/user scope derivation reuse** — the cache key reuses the image cache's scope triple (server hash from the normalized base URI + active-user identity hash + song-id hash). `SubsonicImageResolver` keeps that hashing private, so implementation should factor the shared derivation into something both caches call rather than letting the two drift.
- **`StructuredLyrics` round-trip fidelity** — "raw payload" for Subsonic is the *re-serialized* DTO, not the literal HTTP bytes (the client hands the provider a deserialized object). This is lossless with respect to the `StructuredLyrics` structure, which is all the adapter consumes; worth a note so no one expects byte-identical server output.
- **`definitive`-bit blast radius** — keep the one transient/legacy case well covered by tests; it is the one place the cache-write decision is non-obvious.

---

## References

- Feature tracking: `LYRICS.md` (§Phase 1, §risks).
- Phase 0 design: `docs/design/service/2026-05-22-lyrics-data-model.md`; models in `Hypersonic.Core/Models/Lyrics/`.
- Cache pattern mirrored: `Hypersonic/Services/CachedImageService.cs`, `Hypersonic/Services/SqliteImageCacheStore.cs`, `Hypersonic/Services/Cache/SqliteCacheStoreBase.cs`.
- Logic being relocated into the provider: `Hypersonic/Helpers/LyricsFetchPolicy.cs`, `Hypersonic.Tests/Helpers/LyricsFetchPolicyTests.cs`.
- Existing fetch/cache being migrated: `Hypersonic/ViewModels/NowPlayingViewModel.cs`, `Hypersonic/ViewModels/LyricsLoadState.cs`.
- Subsonic surface: `Hypersonic.Client/SubsonicClient.cs` (`GetLyricsBySongIdAsync`, `GetLyricsAsync`), `Hypersonic.Client/Models/Entities/StructuredLyrics.cs`, `LyricLine.cs`.
