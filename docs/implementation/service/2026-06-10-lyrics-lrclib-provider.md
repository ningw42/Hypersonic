# lrclib Provider + Concurrent Tiering (Phase 1.5) — Implementation Notes

Companion to the design at [`../../design/service/2026-06-10-lyrics-lrclib-provider.md`](../../design/service/2026-06-10-lyrics-lrclib-provider.md). Records only what the design (and the plan it spawned) does **not** already say: the concrete shape the code took, where it diverged, and the details settled during implementation and code review. The design's semantics — one-provider-per-source, the concurrent quality-tiered fan-out, eager-pin / negative-suppress caching, the `budget`/`forceRefresh` surface, and raw-payload parse-on-read — were implemented faithfully and are not re-explained here.

Landed on branch `feat/lyrics-lrclib-provider` (will squash to `master`), on top of the design commit `a09b798b5daf15732bc98209a3155a72d3786783`. The Phase 1 provider/parser/cache/policy contracts did not change.

What shipped, in dependency order:

- **Core** (`Hypersonic.Core/Services/Lyrics/`) — `LrcLibLyricsConstants` (provider id + the two format tags, owned in Core because both the Core parsers and the Hypersonic provider reference them); `LrcLineParser` (the reusable LRC tokenizer); `LrcSyncedLyricsParser` (`lrclib-synced` → `Line`); `LrcPlainLyricsParser` (`lrclib-plain` → `Plain`).
- **lrclib source** (`Hypersonic/Services/Lyrics/LrcLibLyricsProvider.cs`) — self-contained `/api/get` → `/api/search` with client-side disambiguation.
- **Orchestration options** (`Hypersonic/Models/LyricsOrchestrationOptions.cs`).
- **Service** (`Hypersonic/Services/LyricsService.cs`, `Hypersonic/Contracts/Services/ILyricsService.cs`) — the sequential walk replaced by `RunFanOutAsync`; the completeness gate added to `FetchAndStoreAsync`; the `budget`/`forceRefresh` surface; two `ExtensionForFormat` cases.
- **Wiring** — `App.xaml.cs` (named `lrclib` `HttpClient`, the two parsers, the provider after Subsonic, `Configure<LyricsOrchestrationOptions>`); `NowPlayingViewModel` call site.

---

## Decisions settled during implementation

### LRC `[offset:N]` maps to `OffsetMs = N` (direct, no sign flip)

The design (§Risks) flagged the LRC offset sign as needing confirmation against a real payload "before trusting the sign mapping," and public LRC-spec sources genuinely disagree (some say `+offset` shows lyrics later, some earlier). Resolved to the **direct** mapping with the product owner: it matches the dominant LRC convention *and* the existing `SubsonicStructuredLyricsParser`, which already maps its `offset` field straight to `OffsetMs`. `LyricsDocument.OffsetMs` render semantics are `effective = timestamp + OffsetMs` (negative = earlier), so the two sources stay consistent. The mapping is pinned by `LrcLineParserTests.HonorsOffsetTag_DirectSign` and is a one-line flip if it ever needs revisiting.

### The LRC tokenizer uses a stable sort

`LrcLineParser` sorts emitted lines by time with `OrderBy` (stable) rather than `List.Sort` (introsort), so lines sharing an identical timestamp (duet/overlap markers) keep their source order. Same-payload re-parse is deterministic either way; the stable sort just removes the only place authoring order could be lost. (Tightened in code review.)

### lrclib routing: only a 404 falls through to search

`/api/get` returns `track_name` + `artist_name` always and `album_name`/`duration` only when present; `/api/search` sends `track_name` + `artist_name` and **never** album/duration (verified against the server's FTS route — search has no duration parameter, and `album_name` only over-narrows). A `/api/get` **404** falls through to search; a 5xx / network / timeout / parse failure returns `Transient` and does **not** search (only a definitive "no such track" should trigger the broader query). A blank `Title` (nothing queryable) returns `Transient` so it never poisons the negative cache. These three boundaries are pinned by tests, including assertions on the actual query string sent (so a regression that leaked album/duration into search, or dropped them from get, fails loudly).

### Disambiguation heuristic

`SelectBest` ranks search candidates by, in order: duration proximity → title+artist closeness → album similarity → prefer-synced. Duration is the primary key; an **unknown** duration scores `double.MaxValue` (sorts last) rather than `0`, so it can't tie a perfect match. Closeness is a deterministic `NameDistance` (0 = equal case/space-insensitive, 1 = one contains the other, 2 = otherwise; a null/blank query field scores 0). The heuristic is the provider's private business and never leaks into the contract.

### The completeness gate lives in the service, not the policy

`LyricsFallbackPolicy` is consumed **unchanged**. The eager-pin (write the positive winner even when a peer was truncated) and negative-suppress (skip the negative write when any provider slot was unfilled at decision time) logic lives in `LyricsService.FetchAndStoreAsync`, keyed off the `anyTruncated` flag `RunFanOutAsync` returns alongside the precedence-ordered `completed` list. The fan-out writes each result into a fixed per-index slot and assembles `completed` in index order, so `Decide`'s strict-`>` equal-tier tiebreak keeps Subsonic (registered first) ahead of lrclib.

### The fan-out budget timer rides `fanoutCts.Token`

`RunFanOutAsync` bounds the wait with `Task.Delay(budget, _timeProvider, fanoutCts.Token)`. An initial implementation rode the long-lived outer token instead, on the theory that a `TimeProvider` delay linked to a `using`-disposed CTS "never completes under `FakeTimeProvider` and hangs." A code-review experiment disproved that (the `Task.Delay(TimeSpan, TimeProvider, CancellationToken)` contract completes the delay when its token is cancelled, and the delay is only ever awaited inside the `WhenAny` that has already returned by dispose time). Riding `fanoutCts.Token` is strictly better: an early-exit win — the common path — tears the timer down at once via `fanoutCts.Cancel()` instead of leaving a ~budget-ms timer ticking per fetch. A `Dispose_DuringInFlightFanOut_ReturnsNull_NoHang` test locks in the cancellation behavior.

### `LyricsOrchestrationOptions` is a separate options class

Per the design's recommendation, budgets live in their own `LyricsOrchestrationOptions` (kept apart from the cache-TTL `LyricsCacheOptions` because budgets are orchestration, not cache). Defaults: `DefaultFanOutBudgetMs = 700`, `ManualRefreshBudgetMs = 5000` (the manual budget is reserved for the Phase 3 refresh button — defined now, not yet consumed).

### User-Agent version derivation

The named `lrclib` client sends `User-Agent: Hypersonic v{version} (https://github.com/ningw42/Hypersonic)`, with `{version}` from `Assembly.GetExecutingAssembly().GetName().Version` to three fields — a single derivation that works in both packaged-MSIX and unpackaged builds (avoids `Package.Current`, which throws when unpackaged). `HttpClient.Timeout` is a generous 30 s backstop; the real per-attempt bound is the fan-out budget via the linked CTS.

### `LrcPlainLyricsParser` intentionally mirrors the Subsonic legacy parser

The plain-text → `Plain` line-splitting duplicates `SubsonicLegacyLyricsParser`'s shape. This is deliberate: the design asked only the **LRC line-parsing** to be factored into a reusable Core helper (`LrcLineParser`); the two Subsonic parsers stay in `Hypersonic`, so the small plain-text split is not shared across the project boundary.

### Review-driven hardening

Three issues surfaced in a review pass after the feature landed and were corrected (each TDD'd):

- **lrclib parse failure → `Transient`, not `DefinitiveEmpty`.** `MapRecord` originally returned `DefinitiveEmpty` whenever neither lyric field yielded a usable document — including when a field was *present but unparseable*, which would negative-cache a track that actually has lyrics. It now distinguishes "no field present" (empty/instrumental → `DefinitiveEmpty`) from "a field was present but nothing parsed" (→ `Transient`), matching the design's outcome table ("parse failure → Transient"). Plain still rescues an unparseable synced field.
- **Cancellation reaches the Subsonic HTTP calls.** The fan-out cancels stragglers via the provider token, but `SubsonicClient`'s request helpers ignored it, so a budget-truncated Subsonic call leaked in the background (lrclib already honored its token). A defaulted `CancellationToken` now threads through the two `getLyrics*` methods and the two generic request helpers to `HttpClient.GetAsync`/`ReadAsStreamAsync`; every non-lyrics endpoint is unchanged (binds to `None`).
- **Negative budgets are clamped.** A caller-supplied negative `budget` bypassed the options-default `> 0` guard and would throw in `Task.Delay` (or, at the `-1ms` sentinel, disable truncation). `GetLyricsAsync` now clamps a negative resolved budget back to the default window. No current caller passes one; this is defensive hygiene for the Phase 3 manual-refresh path.

---

## Testing notes

- **Provider** — driven by a hand-rolled `HttpMessageHandler` + a fake `IHttpClientFactory` (consistent with the repo's no-mock-library posture), exercising real `LyricsParserRegistry` + real LRC parsers end-to-end. Routing branches, outcome mapping, disambiguation ranking, and the exact query strings are all asserted.
- **Fan-out** — fake providers (gated via `TaskCompletionSource`) + `FakeTimeProvider`. Budget-truncation tests drive the fake clock with a guarded background advancer (no wall-clock waits; an iteration cap turns a never-completing regression into a clear assertion failure instead of a hang).
- **Parsers** — timestamp variants, multi-timestamp lines, `[offset:]` sign, ignored metadata tags, validator-discard, and a raw→parse round-trip, all in `Hypersonic.Core.Tests`.

Suite at completion: Core 196, Client 52, App 591 — all green.

---

## Deferred (unchanged from the design)

Background cache-upgrade; NetEase YRC / AMLL TTML (Word-tier) sources + their legal-posture toggle; the refresh-button UI and whether `PlayerViewModel` consumes the service (Phase 3); migrating `SubsonicClient` to `IHttpClientFactory`.
