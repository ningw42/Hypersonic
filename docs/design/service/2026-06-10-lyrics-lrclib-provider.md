# lrclib Provider + Concurrent Tiering (Phase 1.5) — Design

Companion to `LYRICS.md` §Phase 1.5. Builds directly on the shipped Phase 1 architecture
([`docs/design/service/2026-06-09-lyrics-provider-service.md`](2026-06-09-lyrics-provider-service.md)) and
changes as little of it as possible: this slice adds **one new source**, **two new parsers**, and a
**new orchestration strategy** inside the existing service. No source code — see the forthcoming
implementation plan for that.

The headline is the lrclib provider; the reason it earns its keep is the orchestration upgrade. lrclib's
value is **synced (Line-tier) LRC**, and Phase 1's service walks providers *sequentially and short-circuits
on the first content* — so a Subsonic Plain result would forever pre-empt a better lrclib Line one. This
phase replaces the sequential walk with a **concurrent, quality-tiered fan-out** so the higher tier can win.

---

## Scope of this slice

- ✅ `LrcLibLyricsProvider` — the external lrclib source, self-contained (`/api/get` → `/api/search`).
- ✅ Two **Core** parsers for lrclib's payload formats (`lrclib-synced` LRC, `lrclib-plain` text).
- ✅ `LyricsService` orchestration upgraded from sequential walk to **concurrent tiered fan-out with a
  per-call time budget** and early-exit.
- ✅ Caching policy for the concurrent outcome (**eager-pin positive, suppress negative on incomplete
  peers**) plus a **manual force-refresh** capability for on-demand recovery.
- ✅ `IHttpClientFactory` registration — the first outbound HTTP outside the Subsonic client.

Deferred to **Phase 2** (Win2D renderer): the renderer now gets real Line-tier data to exercise.
Deferred to **Phase 3** (integration): the **refresh button UI** that calls the new force-refresh path,
and whether `PlayerViewModel` consumes the service. Deferred further (own future slices): NetEase YRC /
AMLL TTML (Word-tier) sources and their legal-posture settings toggle.

The guiding constraint: **the provider/parser/cache/policy contracts from Phase 1 do not change.** Only
the service's internal orchestration body and the public `GetLyricsAsync` surface evolve, and the new
source plugs into the existing `IEnumerable<ILyricsProvider>` injection. That is what keeps 1.5 a clean
fast-follow rather than a rewrite.

---

## What we're building, and where it lives

Lyrics keep the Phase 0/1 seam between pure Core logic and app plumbing.

**`Hypersonic.Core`** — pure, testable without WinUI / HTTP:

- **Two new `ILyricsPayloadParser` implementations.** An **LRC parser** (format tag `lrclib-synced`) and a
  **plain-text parser** (format tag `lrclib-plain`). Both are Client-free — they deserialize raw text, not
  `Hypersonic.Client` DTOs — so unlike the two *Subsonic* parsers (which stay in `Hypersonic`) these belong
  in Core. The LRC line-parsing is factored as a reusable helper so future LRC-shaped sources can share it.

**`Hypersonic`** — needs `IHttpClientFactory`, the cache, the Subsonic client:

- **`LrcLibLyricsProvider`** — the external source; owns its internal `/api/get` → `/api/search` fallback
  and disambiguation, and returns its single best `LyricsProviderResult`. Stateless; uses a named
  `HttpClient` from the factory.
- **`LyricsService`** — the *same* singleton, with its fetch body rewritten from sequential to concurrent
  (see §Concurrent fan-out) and its public surface extended with a budget + force-refresh (see §Service
  surface). Everything else — cache key derivation, in-flight dedup, parse-on-read, reconcile — is unchanged.

**DI** (`App.xaml.cs`, beside the existing lyrics registrations): `AddHttpClient("lrclib", …)`; register the
two new Core parsers as `ILyricsPayloadParser`; register `LrcLibLyricsProvider` as `ILyricsProvider`
**after** `SubsonicLyricsProvider` (registration order is the cross-provider tiebreak — see §Tiebreak); add
a fan-out budget option.

---

## Pivotal decisions

### The lrclib provider is one self-contained source

Per the Phase 1 rule (*a provider is a source, never a format or an endpoint*), `LrcLibLyricsProvider` owns
its entire resolution internally and exposes only its best result.

**Primary — `GET /api/get`.** lrclib's implementation requires only `track_name` + `artist_name` (both
non-empty); `album_name` and `duration` are **optional refining filters** (verified in the server's
`get_lyrics_by_metadata` route — the docs page that marks all four "required" is misleading). So the provider
attempts `/api/get` whenever `LyricsQuery.Title` + `Artist` are present, and **passes `Album` / `Duration`
when available** to tighten the match (lrclib matches `duration` within ±2s). Only a missing Title or Artist
sends resolution straight to search. Note `album_name`, when sent, is a hard filter with **no server-side
no-album retry** (that path is commented out in the server), so an album *variant* can still 404 — which is
exactly what the search fallback exists to recover.

| lrclib `/api/get` outcome | Provider result |
|---|---|
| 200, non-empty `syncedLyrics` | `Content`, **Line**, format `lrclib-synced`, `definitive: true` |
| 200, empty synced but non-empty `plainLyrics` | `Content`, **Plain**, format `lrclib-plain`, `definitive: true` |
| 200, both lyric fields empty/whitespace (incl. `instrumental: true`) | `DefinitiveEmpty` — the record exists but carries no usable lyrics |
| 404 `TrackNotFound` | fall through to search |
| 5xx / network / timeout / parse failure | `Transient` |

**Fallback — `GET /api/search?track_name=&artist_name=`.** Album and duration are **not sent as search
constraints** (both verified against the server's `get_tracks_by_keyword`): search has **no duration
parameter at all**, so duration disambiguation can only be client-side; and `album_name`, when sent, becomes
an extra **FTS phrase constraint** (`album_name_lower : "…"`) — more forgiving than `/api/get`'s exact
`album_name_lower = ?` equality, but still able to over-narrow on the very variant that 404'd the primary.
With track+artist the server takes its `ORDER BY rank LIMIT 20` branch, so the 20 returned candidates are the
FTS-rank-best for title+artist. The provider then **disambiguates them internally**, ranking by duration
proximity to `DurationSeconds`, then title/artist closeness, then **album similarity** (album informs
*ranking*, not *filtering*), preferring a candidate carrying `syncedLyrics`. The chosen record maps to
Line/Plain exactly as above. An empty array → `DefinitiveEmpty`; a transport failure → `Transient`. The
heuristic is the provider's private business and never leaks into the contract. (The 20-result cap with no
pagination means a title+artist with >20 matches could bury the right album's record beyond the window — an
album-filtered-first-then-fallback search is a possible future refinement if that proves to bite, deliberately
not built now.)

`/api/get-cached` (internal-DB-only) and the publish flow (`/api/request-challenge` + `/api/publish` with the
proof-of-work `X-Publish-Token`) are **not used**: the former adds a third request for marginal latency gain,
the latter is a write path out of scope for a read-only client.

### New cache formats — raw payload, parsed on read

Mirrors the Phase 1 cache discipline exactly: persist the **byte-for-byte lyric text** the format's parser
consumes plus a **format tag**, and rebuild the `LyricsDocument` on each read. The parsed document is never
itself persisted, so the model can gain fields later without invalidating cached entries.

| Format tag | Raw payload stored | Payload file | Parser → document |
|---|---|---|---|
| `lrclib-synced` | the `syncedLyrics` LRC text, UTF-8 | `.lrc` | LRC → **Line** |
| `lrclib-plain` | the `plainLyrics` text, UTF-8 | `.txt` | text → **Plain** |

The `.lrc` extension was already **reserved** by Phase 1's `SqliteLyricsCacheStore` filename predicate and is
the only store-side change beyond two new `ExtensionForFormat` switch cases (`lrclib-synced → .lrc`,
`lrclib-plain → .txt`). The store schema, gate, eviction, reconcile, and negative-cache rows are untouched.

The **LRC parser** handles the real-world shapes: `[mm:ss.xx]`, `[mm:ss.xxx]`, and `[mm:ss]` timestamps;
multiple timestamps on one line (`[t1][t2] text`, emitted as repeated lines); and LRC metadata tags — it
**honors `[offset:±ms]` → `LyricsDocument.OffsetMs`** and ignores informational tags (`[ar:]`, `[ti:]`,
`[al:]`, `[by:]`). As with every parser in this system it runs `LyricsValidator` and discards a document that
fails (logging once), degrading a noisy payload to "no usable lyrics from this source" rather than a corrupt
render. lrclib returns no language code → `LanguageCode = null`.

### HTTP plumbing — `IHttpClientFactory`

This is the first outbound HTTP outside `SubsonicClient`. Register a **named client** `"lrclib"`:
`BaseAddress = https://lrclib.net`, a default **`User-Agent`** of `Hypersonic v{version} ({homepage})` (the
form lrclib requests; version derived from the assembly/package), and a **generous `HttpClient.Timeout`
backstop ≥ the manual budget**. The real per-attempt bound is the **fan-out budget via the linked CTS** —
when the budget elapses the orchestration cancels the in-flight request through the cancellation token, so
`HttpClient.Timeout` only needs to be large enough never to pre-empt the largest legitimate budget (a manual
refresh). The provider injects `IHttpClientFactory` and
creates the named client **per fetch** (the factory pools and rotates handlers — correct for a singleton
provider; a pinned typed-client handler would risk DNS staleness). `SubsonicClient` is **not** migrated to
the factory — it keeps its own `HttpClient`; refactoring it is out of scope and buys nothing here.

### Concurrent tiered fan-out replaces the sequential walk

`LyricsService.FetchAndStoreAsync` is the only behavior that changes. The fan-out is extracted into a
testable seam:

> `RunFanOutAsync(providers, query, budget, ct) → (IReadOnlyList<LyricsProviderResult> completed, bool anyTruncated)`

- Fire **all** providers concurrently under one linked CTS, bounded by an explicit **`budget`**.
- Fold each completion into a best-so-far. **Decide early** the moment **all providers complete**, OR a
  **top-tier (`Word`)** result arrives (nothing can beat it).
- If the **budget elapses** with providers still in flight → **cancel them** and decide on what completed,
  flagging `anyTruncated`.
- **The returned `completed` list is ordered by provider precedence, not completion order.** This is load-
  bearing: `LyricsFallbackPolicy.Decide` breaks an equal-tier tie with strict `>`, keeping the *earliest list
  entry*, so if results were appended as they finished, a faster lrclib would beat Subsonic at the same tier
  and silently invert the §Tiebreak decision. The fan-out therefore writes each result into a fixed slot keyed
  by the provider's position and assembles `completed` in that order (truncated providers leave gaps;
  `anyTruncated` is "any slot unfilled at decision time"). Precedence is **DI registration order** for now; a
  future explicit provider-id → precedence map can replace it without touching the `ILyricsProvider` contract.
- The precedence-ordered `completed` list feeds the **unchanged** `LyricsFallbackPolicy.Decide`, which prefers
  the higher sync tier (`Word > Line > Plain`) with that order as the tiebreak. `anyTruncated` then gates
  caching (see §Caching policy).

The budget timer is built on the injected **`TimeProvider`** (already a `LyricsService` dependency), so the
concurrency is deterministically testable — fake providers with controllable completion + a fake clock, no
wall-clock sleeps. **The budget is a parameter, not a constant read deep inside** — it threads from the
public surface through `FetchAndStoreAsync` into `RunFanOutAsync`, which is exactly the value tests inject.

In Phase 1.5 neither source produces `Word`, so the practical early-exit is "all complete"; the `Word`
short-circuit is correct-by-construction for the Word-tier sources deferred to later phases.

### Caching policy for the concurrent outcome — eager-pin, with manual recovery

The subtle question this phase must answer: after a concurrent fetch where some providers may have been
**truncated** by the budget (or timed out, which surfaces as `Transient`), what gets written to the durable
cache? An incomplete peer is indistinguishable from a slow one that *would have beaten the winner*, and
`Decide`'s positive gate keys only on the winner's `definitive` bit — it does not look at peers — so the
policy must make the peer-completeness call explicitly. It splits in two:

- **Negative cache** (no provider had lyrics): **suppress the write when any peer is incomplete/transient.**
  This is Phase 1's "transient blocks negative" rule extended to truncation — it prevents pinning a
  discouraging, sticky "No lyrics" for the negative TTL when a truncated provider may have had content.
- **Positive cache** (a usable winner exists): **eager-pin the winner at the full positive TTL, regardless
  of incomplete peers.** A truncated higher-tier source simply means the user sees the lower tier (e.g.
  Plain) until they ask for better.

The reason eager-pin is acceptable — rather than the more conservative "don't pin while a peer is in flight"
— is the **manual force-refresh** recovery path. Automatic re-fetch-until-better schemes (re-fetch every
play; provisional short TTLs) exist solely to recover a truncated-but-better answer; a user-initiated refresh
is a simpler, more direct, and more polite-to-lrclib version of that recovery, and it also covers cases no
reasonable *automatic* budget can (e.g., a match that only surfaces via the slower `/api/search`). With that
escape hatch present, the automatic path optimizes purely for **fast, cheap, and polite**, and pins what it
got. A suboptimal-but-present positive is self-evidently fixable by the user; a false negative is not, which
is why only the negative side keeps the completeness guard.

This trades against two alternatives that were the right answer *before* a refresh button existed:
**no-pin-on-truncation** (recovers next play, but re-hits lrclib on every replay of a slow-peer track) and
**provisional short-TTL** (recovers after a bounded TTL, but adds a TTL tier). Both solve a problem the
button solves better. A third, **background-upgrade** (don't cancel slow peers; upgrade the cache entry when
a higher tier lands late), is the best *automatic* cache quality but carries background-lifecycle complexity
disproportionate to a fast-follow — recorded as a future enhancement, not built here.

### Privacy posture — external lookup is always on

Enabling lrclib means each track's title/artist/album/duration leaves the user's network for lrclib.net on
track changes — a real shift from Subsonic-only Phase 1, which only ever talked to the user's own server.
This slice ships lrclib **always on, with no settings toggle**. When the Word-tier sources with a genuine
legal-posture concern (NetEase/QQ) are added later, *that* is where the gating mechanism is built; lrclib
(CC0-licensed content, no auth, openly accessible) does not warrant its own switch now.

### Cross-provider tiebreak — Subsonic wins an equal tier

When two providers return the same tier (e.g., both Line), the existing `LyricsFallbackPolicy` strict-`>`
comparison keeps the **earliest-registered** result. Registering `LrcLibLyricsProvider` **after**
`SubsonicLyricsProvider` therefore makes the user's own server win an equal-tier tie (exact metadata, lower
latency, any lyrics they curated), and lets lrclib win **only when it offers a strictly higher tier** — its
Line over Subsonic's Plain, which is the whole point of adding it.

---

## Service surface (contract delta)

The Phase 1 `ILyricsService.GetLyricsAsync(query, ct)` gains two **orthogonal, optional** knobs — one method,
not two:

```
Task<LyricsDocument?> GetLyricsAsync(
    LyricsQuery query,
    TimeSpan? budget = null,          // null → default budget from options; otherwise the wait window
    bool forceRefresh = false,        // true → bypass the cache read and overwrite the entry
    CancellationToken cancellationToken = default)
```

- **`budget`** is the flexible wait-time knob — the same value threaded into `RunFanOutAsync`, so there is no
  internal-vs-public split. `null` resolves to the default fan-out budget from options.
- **`forceRefresh`** is the orthogonal **cache-semantics** knob, and it is *not* expressible as a budget: the
  auto path is cache-first and short-circuits on a pinned entry *before* the budget is ever consulted, so a
  large budget alone does nothing on a refresh. `forceRefresh` is what says "ignore the cached entry, fetch,
  and overwrite."

The two compose freely: the **track-change path** calls `GetLyricsAsync(query, cancellationToken: token)`
(cache-first, default budget); the **refresh button** calls `GetLyricsAsync(query, budget: <large>,
forceRefresh: true, cancellationToken: token)` (bypass + overwrite + generous wait). The budgets (default
~700ms, manual ~few seconds) are options, not constants. The single existing caller (`NowPlayingViewModel`,
today `GetLyricsAsync(query, CancellationToken.None)`) updates only to pass its CT by the `cancellationToken:`
name, since `budget` and `forceRefresh` now sit before the token.

---

## Data flow (delta from Phase 1)

Track change → `NowPlayingViewModel` builds a `LyricsQuery` → `LyricsService.GetLyricsAsync`:

1. **Cache lookup** by scoped key — **unless `forceRefresh`**, which skips straight to the fetch. (Positive/
   negative/expired handling is unchanged.)
2. **In-flight dedup** by key — unchanged; one fetch task per key, now internally concurrent.
3. **`RunFanOutAsync`** over the ordered providers under the resolved budget → `(completed, anyTruncated)`.
4. **`LyricsFallbackPolicy.Decide(completed)`** → winner + base action, then the **completeness gate**:
   - `WritePositive` → write positive at full TTL (eager-pin), even if `anyTruncated`.
   - `WriteNegative` → write only if **not** `anyTruncated`; otherwise show nothing-cached.
   - non-definitive content / transient with no winner → return a stale entry if one exists (unchanged).
   - On `forceRefresh`, a positive/negative write **overwrites** any existing entry.
5. Result maps to `LyricsLoadState` / `CurrentLyrics` exactly as in Phase 1 (loaded / `NoLyrics` / `NoTrack`).

Startup `ReconcileAsync` is unchanged.

---

## DI / registration

In `App.xaml.cs`, beside the existing lyrics registrations:

- `services.AddHttpClient("lrclib", …)` — base address, default `User-Agent`, per-request timeout.
- `services.AddSingleton<ILyricsPayloadParser, LrcSyncedLyricsParser>();`
- `services.AddSingleton<ILyricsPayloadParser, LrcPlainLyricsParser>();`
- `services.AddSingleton<ILyricsProvider, LrcLibLyricsProvider>();` — **after** the Subsonic provider.
- Fan-out budget options (default + manual) — a small `LyricsOrchestrationOptions` (kept separate from the
  cache-TTL `LyricsCacheOptions`, since budgets are orchestration not cache), `Configure`d like the cache
  options. Extending `LyricsCacheOptions` is the lower-ceremony alternative if separation isn't wanted.

The service already receives `IEnumerable<ILyricsProvider>` and `IEnumerable<ILyricsPayloadParser>`, so the
new source and parsers are picked up with no change to its constructor.

---

## Testing strategy

- **Provider** (`Hypersonic.Tests`) — driven by a **hand-rolled `HttpMessageHandler`** returning canned
  responses (consistent with the repo's no-mock-library posture): `/api/get` 200-synced → Line, 200-plain →
  Plain, 200-both-empty (instrumental **and** non-instrumental empty/whitespace) → DefinitiveEmpty, 404 →
  search fallback → candidate ranking (duration proximity, **title/artist closeness**, **album similarity**,
  synced-preferred), search-empty → DefinitiveEmpty, 5xx/timeout → Transient, missing-signature → straight to
  search.
- **LRC parser** (`Hypersonic.Core.Tests`) — timestamp variants, multi-timestamp lines, `[offset:±ms]` →
  `OffsetMs`, ignored metadata tags, monotonicity, validator-discard on garbage; a raw→parse round-trip.
- **Plain parser** (`Hypersonic.Core.Tests`) — text → Plain, one line per text line.
- **Orchestration** (`Hypersonic.Tests`) — fake providers with controllable completion + fake `TimeProvider`:
  higher tier wins across providers; early-exit on all-complete; **budget truncation sets `anyTruncated`**;
  eager-pin positive even when truncated; **negative suppressed on truncation**; Subsonic-first equal-tier
  tiebreak; `forceRefresh` bypasses the cache read and overwrites.
- **Service surface** — `GetLyricsAsync` budget/`forceRefresh` plumbing reaches `RunFanOutAsync` with the
  expected values.

---

## Boundaries with adjacent phases

- **Phase 0/1** (shipped) supply the model, validator, provider/parser/cache contracts, and the pure policy —
  all consumed unchanged. The only Phase 1 *code* that changes is `LyricsService`'s fetch body and public
  surface, plus two `ExtensionForFormat` cases.
- **Phase 2** (Win2D renderer) now has a real Line-tier source to exercise during development.
- **Phase 3** (integration) wires the **refresh button** to `GetLyricsAsync(forceRefresh: true)` and decides
  `PlayerViewModel` ownership. This phase builds the *service capability* so that wire-up is trivial. Phase 3
  must honor one precondition: **disable the refresh control while a lyrics request for the current track is in
  flight** (see §Risks — it is what lets the service skip a separate force-refresh in-flight identity).

---

## Deferred / out of scope

- **Background-upgrade caching** (don't cancel slow peers; upgrade the entry on a late higher-tier result) —
  the best automatic cache quality, but disproportionate lifecycle complexity for a fast-follow. Noted as a
  future enhancement.
- **NetEase YRC / AMLL TTML** (Word-tier sources) and their **legal-posture settings toggle** — own future
  slices. The `Word` early-exit and the format-tag/parser registry are already shaped for them.
- **The refresh button UI** — Phase 3.
- **`/api/get-cached` and the publish flow** — not used (latency-marginal; write path).
- **Migrating `SubsonicClient` to `IHttpClientFactory`** — out of scope; it keeps its own `HttpClient`.

---

## Risks / open questions

- **`/api/search` disambiguation quality** — the duration-proximity + title/artist heuristic, and its
  tolerance threshold (lrclib's own `/api/get` uses ±2s; search has none). A wrong-match pins for the full
  TTL and a plain refresh re-picks the same candidate; a manual source/candidate picker is a possible future
  affordance, out of scope here.
- **LRC `[offset:]` sign convention** — confirm against a real lrclib payload during implementation before
  trusting the sign mapping into `OffsetMs`.
- **`User-Agent` version derivation** — packaged (MSIX) vs unpackaged builds expose the app version
  differently; pick a single derivation that works in both.
- **Chronically-slow lrclib** — eager-pin means a truncated slow source is simply not reflected until a manual
  refresh; acceptable and logged, never silently re-hammered.
- **`forceRefresh` vs in-flight dedup** — relies on a **Phase 3 caller precondition: the refresh control is
  disabled while a lyrics request for the current track is in flight** (the only two callers are the
  track-change load and the refresh button, so under that precondition a force-refresh never overlaps a
  cache-first fetch for the same key). Given the precondition, the service needs **no** separate force-refresh
  in-flight identity. If a future caller violates it, a force-refresh would coalesce onto the in-flight
  cache-first task and receive its default-budget, non-overwriting result — at which point a distinct in-flight
  identity (or dedup bypass) for the force path is the fix. Not built now.

---

## References

- Feature tracking: `LYRICS.md` (§Phase 1.5).
- Phase 1 design (contracts this builds on): [`2026-06-09-lyrics-provider-service.md`](2026-06-09-lyrics-provider-service.md).
- Official lrclib API: https://lrclib.net/docs — `/api/get`, `/api/get-cached`, `/api/get/{id}`, `/api/search`,
  `/api/request-challenge`, `/api/publish`. No API key, no rate limiting; `User-Agent` encouraged.
- Contracts reused (verified): `Hypersonic.Core/Contracts/Lyrics/ILyricsProvider.cs`,
  `Hypersonic.Core/Models/Lyrics/LyricsProviderResult.cs` / `LyricsQuery.cs` / `LyricsOutcomeKind.cs`,
  `Hypersonic.Core/Services/Lyrics/LyricsParserRegistry.cs`.
- Code that changes: `Hypersonic/Services/LyricsService.cs` (fetch body + surface),
  `Hypersonic/Services/Lyrics/LyricsFallbackPolicy.cs` (consumer of the gate; the policy itself is unchanged),
  `Hypersonic/App.xaml.cs` (DI), `Hypersonic/Contracts/Services/ILyricsService.cs` (surface).
- Cache mirrored: `Hypersonic/Services/SqliteLyricsCacheStore.cs` (reserves `.lrc`),
  `Hypersonic/Models/LyricsCacheEntry.cs`.
