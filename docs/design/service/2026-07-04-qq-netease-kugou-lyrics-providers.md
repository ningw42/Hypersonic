# QQ / Netease / Kugou Lyrics Providers (Phase 1.5) — Design

Companion to the Phase 1 lyrics design (`docs/design/service/2026-06-09-lyrics-provider-service.md`) and the AMLL provider (`docs/design/service/2026-07-03-amll-ttml-db-provider.md`). Adds three word‑tier external lyric sources — **QQ Music**, **Netease Cloud Music**, and **Kugou** — by extending the existing `ILyricsProvider` fan‑out. This is the follow‑up the Phase 1 doc named: *"Phase 1.5 … brings the `IHttpClientFactory` registration and the legal‑posture settings toggle for NetEase/QQ."* No source code here — see the forthcoming implementation plan.

Prior art: BetterLyrics (`../BetterLyrics`, `BetterLyrics.WinUI3/Services/LyricsSearchService.cs`) fetches these exact platforms. The pivotal finding is that BetterLyrics does **not** implement the reverse‑engineered APIs itself — it delegates search + fetch + decrypt to the third‑party **`Lyricify.Lyrics.Helper`** NuGet (Apache‑2.0). We adopt the same mechanism.

---

## Scope of this slice

Three new **search‑based, word‑tier** lyric sources, added purely additively. The service, the cross‑provider policy, the durable cache, the data model, and the Now Playing UI are all **unchanged**. New surface: three providers, a shared library facade, word‑format parsers + a syllable mapper, a shared match scorer, per‑provider options/constants, and DI registration.

Locked decisions (from brainstorming):

| Decision | Choice |
|---|---|
| Mechanism | Reuse the `Lyricify.Lyrics.Helper` NuGet (Apache‑2.0) — the same library BetterLyrics uses. Not hand‑rolled. |
| Platforms | QQ Music, Netease, Kugou (all three). |
| Granularity | Highest available per source: QQ → QRC (word), Kugou → KRC (word), Netease → YRC (word) when present, else LRC (line). |
| Extra lanes | Main lyric lane only. Translation/romanization deferred (recoverable later from the raw‑payload cache with no re‑fetch). |
| Default state | Enabled by default; each provider individually toggleable via its own options. |
| Latency handling | Manual‑refresh‑only. These providers join the fan‑out but usually lose the automatic 700 ms budget; the **existing** Now Playing refresh button (5 s budget, force‑refresh) is the reliable path. No new service or UI work. |
| Wrapping | Three thin providers over one shared `LyricifyLyricsClient` facade (one provider per source). |
| Match confidence | A shared, weighted `LyricsMatchScorer` (Core) gates search results; lrclib's `SelectBest` is unified onto it (see §The match scorer). |

---

## What we're building, and where it lives

The lyrics stack already separates pure logic (`Hypersonic.Core`) from app plumbing (`Hypersonic`, which owns the Subsonic client, SQLite, `IHttpClientFactory`, and now the NuGet). The new pieces follow that seam.

**`Hypersonic.Core`** — pure, dependency‑free, unit‑testable:

- **`LyricsMatchScorer`** + a small `LyricsCandidate` value type (`Title`, `Artist`, `Album`, `DurationMs`). The shared similarity math (see §The match scorer). Includes a tiny hand‑rolled normalized‑Levenshtein (Core takes no dependencies).

**`Hypersonic`** — depends on the NuGet, `IHttpClientFactory`, `ISubsonicClientService`:

- **`LyricifyLyricsClient`** — the single facade over `Lyricify.Lyrics.Helper`. Given a platform + `LyricsQuery`, it runs the library's search, picks the library's best candidate (no library‑side pre‑filter, mirroring BetterLyrics' `MatchType.NoMatch`), fetches + decrypts, and returns the decrypted raw lyric text, its format tag, and the candidate's metadata — or signals "no hit" vs "transient failure" distinctly. This is the **only** type that touches the library's network/decrypt surface.
- **`QQMusicLyricsProvider` / `NeteaseLyricsProvider` / `KugouLyricsProvider`** — thin `ILyricsProvider`s (note the double‑capital `QQ`). Each: honor `Enabled`, guard blank title, call the facade, apply the scorer gate, parse via the registry, return `Content` / `DefinitiveEmpty` / `Transient`.
- **`QrcLyricsParser` / `KrcLyricsParser` / `YrcLyricsParser`** — `ILyricsPayloadParser`s (format tags `"qrc"` / `"krc"` / `"yrc"`) that delegate to the library's format parsers and map through one shared **`LyricifySyllableMapper`** → a word‑tier `LyricsDocument`. They live in `Hypersonic` because they depend on the NuGet — the same placement exception the Subsonic parsers use for depending on Client DTOs (a parser's assembly is irrelevant to registry resolution). Netease's line‑only LRC fallback reuses the existing pure `LrcLineParser` behind a netease‑tagged parser.
- **`QQLyricsOptions` / `NeteaseLyricsOptions` / `KugouLyricsOptions`** (each `Enabled = true` + `MatchThreshold`) and per‑platform `*LyricsConstants` (provider id, format tags), mirroring `AmllLyricsOptions` / `AmllLyricsConstants`. The `QQ` brand stays double‑capital in every type and display name.

**DI** (`App.xaml.cs`, beside the existing registrations): the three parsers as `ILyricsPayloadParser`; the facade + scorer as singletons; the three providers as `ILyricsProvider` in priority order (see §DI & ordering); a `Configure<…Options>` per provider. No `IHttpClientFactory` named client is required — the library owns its own HTTP; the existing lrclib/AMLL named clients are untouched.

---

## Pivotal decisions

### One provider per source, over one library facade

Each platform is its own `ILyricsProvider` with its own stable id (`"qq"` / `"netease"` / `"kugou"` — persisted cache tokens follow the all‑lowercase convention of `"lrclib"` / `"amll-ttml-db"`; the `QQ` brand stays double‑capital in every type and display name), its own `Enabled` toggle, and its own cache lineage. All three route through the single `LyricifyLyricsClient` facade, which is the one seam that knows the NuGet exists. This keeps the "provider is one real source" rule, gives per‑platform toggles and distinct `SourceProvider` cache ids, and — as a bonus — confines the library's blast radius to the facade + the three format parsers, so a future swap to a hand‑rolled client (should the library rot) touches only that adapter layer, never the providers or the service.

Rejected alternatives: one parameterized provider registered thrice (awkward keyed/factory DI, muddier ids/toggles); one combined "CN" provider racing all three internally (violates one‑provider‑per‑source, collapses the toggles, hides three sources under one cache id).

### Word tier via the library's parsers + a shared syllable mapper

QQ/Kugou return word‑by‑word formats (QRC/KRC); Netease returns YRC (word) when available, else LRC (line). The facade returns the **decrypted** raw text; the registered parser re‑derives the document from those bytes at both fetch time and cache‑read time (the raw‑payload‑cache invariant). The three word parsers delegate to the library's battle‑tested QRC/KRC/YRC parsing and funnel through one `LyricifySyllableMapper` that emits a `Word` `LyricsDocument`.

The mapper must satisfy the `LyricsValidator` word‑tier invariants: every line carries ≥1 word; words are monotonic and each within its line's `[StartMs, EndMs]` bounds; lines are monotonic; `StartMs ≥ 0`. So it sets `line.StartMs` = first word start and `line.EndMs` = last word end so words are always in bounds. A payload whose timing violates an invariant fails validation → the parser returns `null` → the provider returns `Transient` (a present‑but‑unparseable payload is never asserted as "no lyrics"). This mirrors the AMLL provider's parse‑failure handling exactly.

**Parser placement (noted alternative).** We use the library's parsers (in `Hypersonic`). The alternative — hand‑writing pure QRC/KRC/YRC parsers in Core to confine the library strictly to the facade — was rejected as wasteful duplication of formats the library already parses well, given we've already taken the dependency. The facade + parsers together form the library adapter layer; that is a clean enough boundary.

### The match scorer — unified selection + gating, transparent to the policy

Search‑based sources can return a confident‑looking result for the **wrong song**. Because `LyricsFallbackPolicy` ranks purely by sync tier (`Word > Line > Plain`, registration order as tiebreak) with **no notion of match confidence**, an unguarded word‑tier wrong‑song result would strictly out‑rank a correct Subsonic/lrclib line result. So every search‑based provider computes a match score against its candidate's metadata and **rejects below a threshold**, returning `DefinitiveEmpty` ("no confident match from this source") rather than handing wrong‑song lyrics to the policy.

`LyricsMatchScorer` (Core, pure) computes one **weighted absolute score in `[0, 100]`** from up to four factors, each a similarity in `[0, 1]`.

**Shared normalization** (applied to every string before comparison): lowercase (invariant), trim, collapse internal whitespace to a single space, fold full‑width → half‑width. Title and album additionally strip a trailing parenthetical/bracketed qualifier (`(Live)`, `(Remastered 2011)`, `[Deluxe]`, …).

**`sim(a, b)`** is normalized Levenshtein: `1 − editDistance(a, b) / max(len a, len b)` (defined as `1` when both are empty). Levenshtein itself is standard and omitted here.

- **Title** — `sim(queryTitle, candidateTitle)`. Always present (a blank query title is rejected upstream, before scoring).
- **Album** — `sim(queryAlbum, candidateAlbum)`, **low weight** (albums legitimately differ: single vs album vs compilation).
- **Duration** — `1 − min(|candSec − querySec| / Tolerance, 1)`, `Tolerance` a constant (~12 s). Exact → 1; a full tolerance off or more → 0.
- **Artist** — multi‑artist aware. Compares the query's artist string against **one candidate's** artist string, splitting *both* into artist tokens. (Choosing *which* candidate song is a level up — selection, below; this factor scores a single candidate.)
  1. Normalize both artist strings.
  2. **Split** each into artist tokens on any of `/  ,  ;  &  、  ，` and the collaboration words `feat` / `feat.` / `ft` / `ft.` / `featuring` / `vs` / `vs.` (case‑insensitive delimiters). Trim; drop empties. (`x` / `×` are deliberately **not** split on — too many false positives, e.g. "Charli XCX".) Say this yields `X` query tokens and `M` candidate‑artist tokens.
  3. **Match, query‑driven and asymmetric:** for **each of the `X` query tokens**, take its best similarity against **any of the `M` candidate‑artist tokens** — `best(q) = maxₖ sim(q, candArtistTokenₖ)`.
  4. **Artist factor** = mean of the `X` `best(q)` values → one similarity in `[0, 1]`, which then enters the weighted blend below.

  This rewards every *queried* artist appearing in the candidate and **ignores extra** artists the candidate lists — because tags routinely under‑ or over‑list collaborators, and we only care that the artists we searched for are present. Examples: query `A/B` (X=2) vs candidate `A` (M=1) → `best(A)=1.0`, `best(B)≈0` (absent) → mean ≈ **0.5** (a missing collaborator is penalized proportionally, not fatally); query `宇多田ヒカル` (X=1) vs candidate `宇多田ヒカル/Skrillex` (M=2) → exact match, extra `Skrillex` ignored → **1.0**.

**Missing data = neutral.** A factor whose data is absent on **either** side (no album tag, no duration, no artist tokens) is **dropped** and the remaining weights are renormalized, so it neither helps nor hurts: `score = 100 × ( Σ_present wᵢ·factorᵢ ⁄ Σ_present wᵢ )`.

**Default weights** — illustrative, tunable constants, renormalized when a factor drops: title `0.35`, artist `0.30`, duration `0.30`, album `0.05`.

The score serves two uses that are the same operation read two ways:

- **Selection** — `candidates.MaxBy(score)` (best of N).
- **Gating** — `score ≥ MatchThreshold ? accept : reject`.

This **unifies** lrclib's existing `SelectBest` onto the shared scorer. `SelectBest` today is a *ranker only* (a lexicographic `OrderBy…ThenBy` comparator that never rejects); it shares only the underlying similarity primitives with what the CN providers need, and it lacks a gate. Under this design:

- **lrclib `/api/search`** adopts the scorer for selection (lexicographic → weighted) **and** gains the gate. This is a behavior change to a shipped provider — selection can pick a different candidate in conflict cases, and a below‑threshold best match now returns `DefinitiveEmpty` instead of being shown. To bound regression, lrclib's default `MatchThreshold` is set **conservatively (loose)**; CN providers default **moderate**. The `LrcLibLyricsProviderTests` selection assertions are re‑baselined (the existing duration‑proximity case still holds under the blend).
- **lrclib `/api/get`** is left untouched — it is an *exact* server‑side match (all fields), so gating a returned hit with our fuzzy scorer would wrongly reject legitimate matches.
- **Subsonic** (by id) and **AMLL** (index owns matching) are not search‑based and do not use the scorer.

Crucially, the scorer's verdict is **baked into the outcome kind inside each provider** — a rejected match becomes `DefinitiveEmpty` before the service ever sees it. The numeric score does **not** cross the provider boundary. So `LyricsFallbackPolicy` stays tier‑only and completely **agnostic** to the scorer; the two never call each other, coupled only through `LyricsProviderResult`. Consequence to accept: among providers that *pass* their gate, tier + registration order decide the winner — never who matched "better." Cross‑provider score comparison would require a confidence field on `LyricsProviderResult` and a policy change, and is explicitly **out of scope**.

### Latency: these providers are slower than the automatic budget — and that's fine

The service fans out under `DefaultFanOutBudgetMs` (700 ms); when the budget elapses, `RunFanOutAsync` cancels stragglers and **discards** their work (there is no background write‑through). Each CN provider does **two sequential round‑trips** (search → fetch+decrypt) to Chinese servers, which from many regions take seconds or time out. So on automatic track change they will usually be truncated — showing **no lyrics on first play, poisoning nothing** (truncation suppresses the negative‑cache write).

The reliable path is the **existing** Now Playing refresh affordance: `NowPlayingViewModel.RefreshLyricsCommand` already calls `GetLyricsAsync(query, ManualRefreshBudgetMs = 5 s, forceRefresh: true)`, wired to `RefreshLyricsButton` in `NowPlayingPage.xaml`. Under the 5 s budget the CN providers complete; the best word‑tier winner is validated, parsed, and positive‑cached, so every later play of that track is instant. **No service or UI change is required** — the providers simply join the fan‑out and rely on the manual path for their first surfacing.

### Caching is unchanged; below‑threshold is `DefinitiveEmpty`

Each provider gets a distinct `SourceProvider` id and distinct format tag; the scoped cache key (server + user + song‑id), the positive/negative TTLs, and the parse‑on‑read path are all unchanged. The raw payload stored is the **decrypted** QRC/KRC/YRC (or LRC) text, so re‑parse is lossless and a future translation/roma lane can be recovered from cache without a re‑fetch.

A below‑threshold match returns `DefinitiveEmpty` (negative‑cacheable), consistent with "this source has no confident answer." **Deferred, noted for later:** once `MatchThreshold` becomes a *live user setting* (rather than a startup‑bound constant), a negative entry computed under an old threshold would mask a threshold reduction until its TTL expires. The clean fix is to stamp negative entries with a threshold‑config epoch and invalidate them lazily when the epoch bumps — deliberately **not built now** (threshold is a constant; the 7‑day negative TTL plus the manual refresh already cover it). Reusing `Transient` for below‑threshold was rejected: it is semantically a *deterministic policy rejection*, not a transient failure, and it would permanently disable negative caching for near‑miss tracks (re‑fan‑out on every play).

### Opt‑in posture

Each provider ships **enabled by default** with its own `Enabled` option (the seam `AmllLyricsOptions` established), individually toggleable. `Enabled == false` → the provider is inert, returning `DefinitiveEmpty` (never a permanent `Transient`, which would block the negative cache).

---

## Data flow (unchanged service, new participants)

```
   Track change (auto)                      Refresh button (manual)
   budget = 700 ms                          budget = 5000 ms, forceRefresh = true
          │                                            │
          └──────────────────────┬─────────────────────┘
                                 ▼
         NowPlayingViewModel  →  LyricsQuery { songId, title, artist, album, durationSec }
                                 ▼
         LyricsService.GetLyricsAsync
                                 │
              cache hit (fresh, & not forceRefresh) ──────────────► return LyricsDocument
                                 │  miss / expired / forceRefresh
                                 ▼
     ┌──────────────────────────────────────────────────────────────────────────────┐
     │  RunFanOutAsync — ALL providers in parallel, bounded by the budget             │
     │  (stragglers past the budget are cancelled & discarded → anyTruncated = true)  │
     └──────────────────────────────────────────────────────────────────────────────┘
        │            │            │              │
        ▼            ▼            ▼              ▼
 ┌───────────┐ ┌──────────┐ ┌──────────┐ ┌───────────────────────────────────────────┐
 │ Subsonic  │ │  lrclib  │ │   AMLL   │ │   QQMusic / Netease / Kugou                 │
 │ (by id)   │ │          │ │ (index   │ │   (search, via LyricifyLyricsClient facade) │
 │           │ │ /api/get │ │  resolve)│ │        │ candidate metadata                 │
 │ no scorer │ │  exact,  │ │ no scorer│ │        ▼                                    │
 │           │ │  ungated │ │          │ │  ★ LyricsMatchScorer (0–100)                │
 │           │ │ /api/    │ │          │ │    score ≥ MatchThreshold ?                 │
 │           │ │  search: │ │          │ │      yes → parse (qrc/krc/yrc/lrc)          │
 │           │ │  scorer  │ │          │ │             → Content(Word/Line)            │
 │           │ │  select  │ │          │ │      no  → DefinitiveEmpty                  │
 │           │ │  + gate  │ │          │ │                                             │
 └─────┬─────┘ └────┬─────┘ └────┬─────┘ └────────────────────┬────────────────────────┘
       │            │            │                            │
       ▼            ▼            ▼                            ▼
     each provider returns ONE  LyricsProviderResult:
        Content(doc, syncKind, definitive) | DefinitiveEmpty | DefinitiveUnsupported | Transient
       │            │            │                            │
       └────────────┴──────┬─────┴────────────────────────────┘
                           ▼  (only results completed within budget)
        ┌───────────────────────────────────────────────────────────────┐
        │  LyricsFallbackPolicy.Decide(results)   ← tier-only, no scores  │
        │    keep Content · winner = highest tier · tie = reg. order      │
        │    cache action gated by winner.Definitive / all-definitive     │
        └───────────────────────────────┬───────────────────────────────┘
                                         ▼
        LyricsService applies the action (suppress WriteNegative when anyTruncated)
                                         ▼
                    write cache  →  return LyricsDocument?  →  Now Playing renderer
```

The **★ scorer lives strictly inside the search‑based provider box** and decides whether that box emits `Content` or `DefinitiveEmpty`; the **policy is downstream**, consuming only result kinds and ranking by tier. The arrow into the policy carries a *result kind*, never a *score* — the visual form of "the scorer is transparent to the policy."

---

## Error handling

- Library/network failure or timeout → `Transient` (never negative‑cached); genuine caller cancellation is re‑thrown, never masked. Verbatim the AMLL/lrclib pattern.
- Library returns no search candidate → `DefinitiveEmpty` (this source honestly has nothing).
- Candidate found but below `MatchThreshold` → `DefinitiveEmpty`.
- Candidate found, fetched, but the payload fails to parse/validate → `Transient` (present‑but‑unparseable is never "no lyrics").
- `Enabled == false` → `DefinitiveEmpty` (inert, non‑blocking).

---

## Testing strategy

- **`LyricsMatchScorer`** (Core) — pure unit tests: each factor (exact / near / mismatch), normalized Levenshtein edges (CJK + latin), multi‑artist token pairing, missing‑duration neutrality, weight blending, and threshold boundaries. The unification's key safety net.
- **Parsers + `LyricifySyllableMapper`** — QRC/KRC/YRC/LRC samples → the expected word/line `LyricsDocument`; validator‑discard on malformed timing; raw→parse round‑trip. Live in `Hypersonic.Tests` beside the NuGet dependency.
- **Providers** — with a **fake facade** (no real network): match‑gate reject → `DefinitiveEmpty`; success → `Content`; library throw → `Transient`; no candidate → `DefinitiveEmpty`; disabled → `DefinitiveEmpty`.
- **lrclib** — re‑baseline `LrcLibLyricsProviderTests` for the weighted selection and the new gate; confirm `/api/get` remains ungated.
- **Service / policy** — unchanged; existing tests continue to cover the fan‑out and tier ranking.

---

## DI & ordering

Registration order is the policy's equal‑tier tiebreak. Proposed order (all after the existing sources): **Subsonic → lrclib → AMLL → QQ → Netease → Kugou**. Since every real CN match is word‑tier and gated for confidence, this only decides which *confident word‑tier* source wins when several match — AMLL (community‑curated TTML) first among word sources, then the CN trio. Adjustable if a different preference is wanted.

---

## Deferred / out of scope

- **Translation & romanization lanes** — the sources return them; the current `LyricsDocument` is single‑lane. Deferred; the raw‑payload cache means they can be added later by re‑parsing cached bytes, with no re‑fetch.
- **Adjustable‑threshold negative‑cache invalidation** (threshold‑config epoch on negative entries) — only needed once `MatchThreshold` is a live user setting; noted, not built.
- **Background write‑through for slow providers** (caching results that arrive after the budget) — explicitly declined in favor of the manual‑refresh path; would be a separate service change if ever wanted.
- **Extending the gate to a distinct `LowConfidence` outcome** — considered and set aside; `DefinitiveEmpty` is the chosen representation.

---

## Risks / open questions

- **Library staleness & geo‑blocking.** `Lyricify.Lyrics.Helper` last released v0.1.4 (Aug 2024), and these are unofficial reverse‑engineered endpoints of commercial Chinese services that break periodically and are often unreachable outside China. The facade seam contains the blast radius; failures degrade to `Transient` (no user‑visible error, just "no lyrics from this source"). Accepted per the "reuse the library" decision.
- **lrclib behavior change.** Unifying selection and adding a gate alters a shipped provider; mitigated by a conservative default threshold and re‑baselined tests. Confirm the appetite for this at review — the alternative is to unify only lrclib's *selection* and leave it un‑gated.
- **Threshold tuning.** Too strict rejects real matches; too loose surfaces wrong‑song lyrics that then out‑tier correct results. Defaults are a starting point; the per‑provider `MatchThreshold` is the knob.

---

## References

- Prior art: `../BetterLyrics/src/BetterLyrics.WinUI3/Services/LyricsSearchService.cs` (`SearchQQNeteaseKugouAsync`), `Lyricify.Lyrics.Helper` (NuGet, Apache‑2.0).
- Phase 1 architecture: `docs/design/service/2026-06-09-lyrics-provider-service.md`.
- AMLL provider (most recent external source, pattern to mirror): `docs/design/service/2026-07-03-amll-ttml-db-provider.md`.
- Contracts/models: `Hypersonic.Core/Contracts/Lyrics/`, `Hypersonic.Core/Models/Lyrics/` (`LyricsDocument`, `LyricsLine`, `LyricsWord`, `LyricsValidator`, `LyricsSyncKind`).
- Existing providers/parsers to mirror: `Hypersonic/Services/Lyrics/{AmllTtmlLyricsProvider,LrcLibLyricsProvider}.cs`, `Hypersonic.Core/Services/Lyrics/{LrcSyncedLyricsParser,LrcLineParser,TtmlLyricsParser}.cs`.
- Orchestration & policy: `Hypersonic/Services/LyricsService.cs`, `Hypersonic/Services/Lyrics/LyricsFallbackPolicy.cs`, `Hypersonic/Models/LyricsOrchestrationOptions.cs`.
- Consumer: `Hypersonic/ViewModels/NowPlayingViewModel.cs` (`RefreshLyricsCommand`), `Hypersonic/Views/NowPlayingPage.xaml`.
