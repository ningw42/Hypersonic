# AMLL TTML DB Provider (Word-tier) — Design

Companion to `LYRICS.md` and the shipped Phase 1.5 fan-out
([`2026-06-10-lyrics-lrclib-provider.md`](2026-06-10-lyrics-lrclib-provider.md)). This slice adds the
**AMLL TTML DB** as a third `ILyricsProvider` — the first **Word-tier** external source — plus the one
piece of infrastructure it needs that the other providers do not: a **locally-cached, background-refreshed
index** with strict client-side matching. No source code — see the forthcoming implementation plan.

The headline is a karaoke-grade (word-synced) source. The reason it needs its own design rather than
copying `LrcLibLyricsProvider` is that [amll-ttml-db](https://github.com/amll-dev/amll-ttml-db) is **not a
queryable service** — it is a static GitHub repository keyed only by streaming-platform IDs
(Netease / QQ / Apple Music / Spotify), with **no title-search API, no duration, and no MusicBrainz ID or
ISRC** — i.e. nothing a Navidrome track normally carries. So the provider cannot do a per-track keyed GET
the way Subsonic (song id) and lrclib (title/artist search endpoint) do. The only universal path is to
**download the repo's index once, build a local title/artist lookup, and match against it.** That index —
and its lifecycle — is what this design is really about.

The parser is already done: `TtmlLyricsParser` (Core, format tag `ttml`) was built during Phase 2 and its
docstring already reserves it for "the deferred AMLL-TTML-DB provider." It emits `LyricsSyncKind.Word`, is
namespace-agnostic, and normalizes real-world AMLL timing wobbles. This slice reuses it unchanged.

---

## Scope of this slice

- ✅ `AmllTtmlLyricsProvider` — the new `ILyricsProvider`, thin: resolve → GET → parse → map.
- ✅ `AmllLyricsIndex` (WinUI) + pure `AmllLyricsIndexData` (Core) — the downloaded index, its disk cache,
  ETag-conditional background refresh, and the strict local matcher.
- ✅ A **change to the shipped fan-out**: **remove the Word early-exit** so cross-provider priority
  (tier, then registration order) is authoritative for every tie, including Word-vs-Word.
- ✅ `IHttpClientFactory` registration for a named `"amll"` client pointed at a CDN mirror.
- ✅ `AmllLyricsOptions` (refresh interval + an `Enabled` seam for the future toggle).

**Deferred (explicit):**

- The **legal-posture settings toggle** and its Settings UI — the provider ships **always-on** to validate
  matching quality first; the `Enabled` options flag is the seam the toggle will later drive.
- **Platform-ID direct lookup / hybrid** — if a library's tags ever carry Netease/Apple/Spotify IDs, a
  direct `<platform>-lyrics/<id>.ttml` GET is a cheap exact path; not built here (near-zero coverage for a
  typical Western Navidrome library).
- **Fuzzy matching + a manual source/candidate picker** — strict matching only for now (see §Matching).
- **Translation / romanization lanes** — the parser already drops `ttm:role` aux spans; single-lane stands.

The guiding constraint mirrors 1.5: **the provider / parser / cache / policy contracts do not change.** The
only shipped code that changes is the fan-out's early-exit path plus **one new `ExtensionForFormat` case**
(`ttml → .ttml`); everything else is additive and plugs into the existing `IEnumerable<ILyricsProvider>`
injection.

---

## What we're building, and where it lives

Lyrics keep the Core/WinUI seam: pure logic in Core (dependency-free, fast to unit-test), app plumbing in
`Hypersonic`.

**`Hypersonic.Core`** — pure, no WinUI / HTTP / disk:

- **`AmllLyricsIndexData`** — an immutable, parsed index plus the matcher. Owns the `.jsonl` → lookup parse,
  the normalization rules, and `TryResolve(LyricsQuery) → string? rawLyricFile`. This is the unit that gets
  the bulk of the tests, against fixture index lines — no network, no disk.

**`Hypersonic`** — needs `IHttpClientFactory`, `IAppFolderLocator`, the parser registry:

- **`AmllLyricsIndex : IAmllLyricsIndex`** (singleton) — the stateful shell around `AmllLyricsIndexData`.
  Owns the disk cache, the ETag-conditional background download + refresh, a readiness flag, and
  `WarmAsync()`. Delegates `TryResolve` to the held `AmllLyricsIndexData`.
- **`AmllTtmlLyricsProvider : ILyricsProvider`** — deliberately thin. `FetchAsync` asks the index to
  resolve a file, GETs it via the named client, parses via the shared `ILyricsParserRegistry` (`ttml`
  tag), and maps to a `LyricsProviderResult`. No parsing or normalization of its own.
- **`AmllLyricsConstants`** — provider id (`amll-ttml-db`, matching the `SourceProvider` already used in
  the data-model tests), CDN base + relative paths, cache subfolder name.

**DI** (`App.xaml.cs`, beside the existing lyrics registrations): register `AmllTtmlLyricsProvider` as
`ILyricsProvider` **after** `SubsonicLyricsProvider` and `LrcLibLyricsProvider` (registration order is the
tiebreak — see §Priority); register `IAmllLyricsIndex`; `AddHttpClient("amll", …)`; and add the options.

Rationale for the split: the matcher is the part most likely to be wrong (normalization, artist sets, CJK),
so it lives where it can be tested purely and exhaustively; the HTTP/disk/refresh machinery is thin and
mockable behind `IAmllLyricsIndex`, and the provider itself is trivial to test against a fake index.

---

## The matching problem (the crux)

amll-ttml-db's index is a single **~1.5 MB** `metadata/raw-lyrics-index.jsonl`, one JSON object per line.
`metadata` is an **array of `[key, [values]]` pairs**, not a plain object. Two real lines:

```jsonc
{"metadata":[["album",["Idol"]],["artists",["YOASOBI"]],["musicName",["Idol"]],
  ["ncmMusicId",["2048982668"]],…],"rawLyricFile":"1689089845000-39523898-31c2fa0c.ttml"}
{"metadata":[["album",["Wishes Vol. 1 - The Remixes"]],
  ["artists",["Famba","Eric Lumiere","Raven & Kreyn"]],
  ["musicName",["In My Head (Raven & Kreyn Remix)"]],…],"rawLyricFile":"1689272876000-…-4f4aa2d4.ttml"}
```

Per line we keep `musicName[0]` (title), the whole `artists[]` array, `album[0]`, and `rawLyricFile`;
everything else (platform IDs, author) is discarded.

**In-memory shape** — a `Dictionary<string, List<Entry>>` keyed by the **normalized title**, each `Entry`
carrying its normalized artist **set**, normalized album, and the raw file name:

```
"idol"  →  [ { File="…31c2fa0c.ttml", Artists={"yoasobi"}, Album="idol" },
             { File="…85d330ae.ttml", Artists={"yoasobi"}, Album="idol" } ]   // real duplicate submissions
"in my head (raven & kreyn remix)"
        →  [ { File="…4f4aa2d4.ttml", Artists={"famba","eric lumiere","raven & kreyn"},
                                        Album="wishes vol. 1 - the remixes" } ]
```

A `List` per key is load-bearing: the DB genuinely contains duplicate submissions for the same song, and
distinct songs can share a title — the artist filter disambiguates both without data loss.

**Normalization** (applied identically when building the index and to the query): Unicode **NFKC** →
lowercase-invariant → trim → collapse internal whitespace. NFKC is the pivotal choice — it folds
full-width ↔ half-width forms, which matters across the heavily CJK content.

**Artist matching is deliberately asymmetric:**

- **Index side** — each `artists[]` element is normalized *as-is*; the DB has already separated distinct
  performers into array elements.
- **Query side** — the single `LyricsQuery.Artist` string is *split* on `, / & feat. ft. ; 、 ×` into a
  normalized token set. Splitting the query is what bridges Subsonic cramming `"A feat. B"` into one string
  against the index's separated array.

A match requires an **exact normalized title bucket hit AND a non-empty artist-set intersection.** Album is
only a tiebreaker among multiple qualifying candidates; otherwise first-in-file wins (all candidates are
Word-tier and, once the artist filter has run, the correct song — so any is equivalent).

**Lookup walkthrough** — query `{Title:"Idol", Artist:"YOASOBI"}`: normalize title → `"idol"`, O(1) bucket
hit → two candidates; query artist set `{"yoasobi"}` intersects both; take the first → GET
`raw-lyrics/…31c2fa0c.ttml`. A query `{Title:"Idol", Artist:"Some Other Band"}` hits the same bucket but
finds **no artist intersection** → no match. A query for the Japanese `"アイドル"` lands in a different
bucket entirely, so the English and Japanese versions never cross-match.

**Why strict** — there is no duration in the index to confirm a candidate, the content is CJK-dense, and a
*wrong* AMLL match is doubly costly: being Word-tier it would outrank correct Subsonic/lrclib lyrics, and a
positive result pins for the full positive TTL (a plain refresh re-picks the same wrong song). Precision
over recall: an unmatched track simply yields nothing from AMLL and the exact-keyed sources win.

*Known minor gap (deferred):* an intra-element collab name like `"Raven & Kreyn"` is matched whole on the
index side, so a query artist of exactly `"Raven & Kreyn"` splits to `{"raven","kreyn"}` and would miss it.
Rare, and the exact-title gate keeps it safe.

---

## Index lifecycle

**Source — a CDN mirror.** Base `https://cdn.jsdelivr.net/gh/amll-dev/amll-ttml-db@main/`, with relative
paths `metadata/raw-lyrics-index.jsonl` (index) and `raw-lyrics/<file>.ttml` (payloads), via a named
`"amll"` `HttpClient`. jsDelivr is chosen over `raw.githubusercontent.com` for real CDN caching + ETag and
to sidestep GitHub raw's abuse rate-limits; 1.5 MB is far under its file cap. The only cost is a few-hours
`@main` purge lag, irrelevant against a weekly refresh.

**Disk cache** — the raw `.jsonl` plus a tiny sidecar (`ETag`, `fetchedUtc`) under
`IAppFolderLocator.GetCacheFolder()` in an `AmllIndex\` subfolder — the same cache root as the SQLite lyrics
and image caches:

| Mode | Path |
|---|---|
| **MSIX** | `%LOCALAPPDATA%\Packages\Hypersonic_257ke566bd2wr\LocalCache\AmllIndex\` |
| **Unpackaged** | `%LOCALAPPDATA%\Hypersonic\Cache\AmllIndex\` |

This is the correct partition: MSIX resolves to `LocalCacheFolder` — OS/user-purgeable, excluded from
backup/roaming — which is right for derived, re-downloadable data. (Contrast the lyrics *override* folder,
which uses `LocalFolder`/LocalState precisely because it is user-authored and must not be purged.) If
Windows clears it, we simply re-fetch.

**Refresh** — on warm, load the disk copy into `AmllLyricsIndexData` immediately if present; if absent or
older than `RefreshIntervalDays` (default **7**), issue a conditional GET (`If-None-Match: <etag>`).
`304 Not Modified` → keep the disk copy, restamp `fetchedUtc`. `200` → atomically replace the file and
rebuild the lookup. All in the background; a failed refresh leaves the existing index serving.

**Warm trigger** — the existing fire-and-forget lyrics seam. `App.xaml.cs` already dispatches
`ReconcileLyricsCacheAsync()` with `_ =` (not awaited) after activation, off the UI-critical path; the index
warm rides the same seam, swallowing/logging its own exceptions like its siblings (a fire-and-forget task
must never surface an unobserved exception). This never blocks first paint or the UI thread — the parse is
threadpool CPU work and the fetch is background IO; on a slow machine there is mild resource contention
during startup, but no jank and no delay to the window appearing.

**Readiness** — until `AmllLyricsIndexData` exists in memory, the provider reports the index "not ready" and
returns `Transient` (never `DefinitiveEmpty`), so a cold start cannot poison the negative cache.

---

## Provider `FetchAsync` and outcome mapping

Thin, and mirroring the outcome discipline of `LrcLibLyricsProvider`:

| Situation | Result | Why |
|---|---|---|
| Provider disabled (`Enabled = false`) | `DefinitiveEmpty` | Inert but non-blocking — a permanent `Transient` would suppress every negative-cache write; no index/network work either |
| Title blank | `Transient` | Nothing queryable; do not negative-cache |
| Index not ready (warming) | `Transient` | Retried on the next track; cannot poison negative cache |
| No index match | `DefinitiveEmpty` | This source honestly has no entry — contributes to the negative cache only if *every* provider is also empty |
| Match → GET → parse OK | `Content(doc, payload, "ttml", definitive: true)` | Word-tier, positive-cacheable |
| HTTP non-success · parse-fail · non-caller cancellation | `Transient` | Transient failure; never assert "empty" for content we could not fetch/parse |

`definitive: true` always — AMLL is a single source with no internal higher-tier endpoint that a retry could
improve, so its content is safe to positive-cache. Caller cancellation (`OperationCanceledException` on the
passed token) is rethrown, never masked as a provider transient — the shared contract.

The fetched TTML flows through the existing cache and parser paths unchanged: `raw-lyrics/<file>.ttml` bytes
are the `RawPayload`, parsed on both fetch and cache-read by the shared `TtmlLyricsParser` (`ttml` tag,
already registered). The SQLite store already lists `.ttml` among its known payload extensions, but the
write-path `ExtensionForFormat` map does **not** yet have a `ttml` case (it would fall through to `.txt`) —
so this slice adds one `ttml → .ttml` case, the single-line change every provider slice makes (the lrclib
slice added two).

---

## Change to existing behavior: remove the Word early-exit

Introducing a *second* Word-tier source makes the shipped fan-out's Word early-exit
(`LyricsService.RunFanOutAsync`) an inconsistency: it settles the instant *any* Word result arrives, so in a
Subsonic-Word vs AMLL-Word race whichever returns first would win — bypassing the cross-provider priority
that decides every other tie. We remove it so priority is the single source of truth.

- **Remove** the Word-content early-exit branch (the `TrySetResult()` on a `Word` `Content` result).
- **Keep** the all-providers-complete early-exit and the budget timer.

The fan-out now settles when **all providers finish _or_ the budget elapses**, and the winner is decided
solely by `LyricsFallbackPolicy` — already "highest tier, then registration order." The policy itself does
not change; we only remove the path that let arrival-time preempt it. The method's doc comment loses its
"OR a top-tier (Word) result arrives" clause.

**Tradeoff (accepted):** on a cache *miss*, the lyrics panel may take up to the budget (~700 ms) to populate
instead of flipping the instant the fastest Word result lands. This is never a playback or UI cost — the
track transition and audio precede the `TrackChanged` event, the lyrics load is fire-and-forget off the UI
thread (`NowPlayingViewModel.HandleTrackChange` sets `Loading` immediately and awaits the fetch without
blocking), and cache *hits* skip the fan-out and budget entirely. The trade buys fully deterministic,
reproducible provider priority for a one-time, per-uncached-track, background populate delay.

---

## Priority / registration order

AMLL is registered **last** among the three providers. Because registration order is now authoritative for
every tie, the deterministic order becomes:

> **Subsonic > lrclib > AMLL** on an equal tier; **AMLL wins only when it is strictly higher tier** than what
> the others produced.

So AMLL-Word beats lrclib-Line and Subsonic-Plain/Line, but an AMLL-Word vs Subsonic-Word tie goes to
Subsonic — the user's own server, keyed exactly by song id with no fuzzy-match risk. That is the safe
default: prefer the exact-keyed sources on a tie, let the community source win only when it genuinely offers
more sync detail.

---

## Configuration

`AmllLyricsOptions` via `IOptions<>` (the `LyricsCacheOptions` / `LyricsOrchestrationOptions` pattern):

- `RefreshIntervalDays` (default `7`) — the disk-index staleness threshold that triggers a conditional GET.
- `Enabled` (default `true`) — the **seam for the deferred toggle**. `WarmAsync` and `FetchAsync`
  early-return when false; the future Settings toggle simply drives this flag (and, when added, gates the
  index download so a disabled provider does no network work).

The named `"amll"` client carries the same Hypersonic User-Agent as `"lrclib"` and a generous backstop
`Timeout` (30 s). The per-track payload GET is really bounded by the fan-out's linked CTS (the budget); the
background index fetch is not budget-bound.

---

## Testing

- **Core (`Hypersonic.Core.Tests`, plain `dotnet test`)** — `AmllLyricsIndexData` against fixture `.jsonl`
  lines: exact title hit, artist-set intersection, multi-artist + `feat.` query splitting, NFKC
  width/case folding, duplicate-title bucket selection, distinct-song-same-title disambiguation, and
  no-match → null. This is where matching correctness is pinned.
- **WinUI (`Hypersonic.Tests`)** — `AmllTtmlLyricsProvider` against a fake `IAmllLyricsIndex` and fixture
  TTML run through the **real** `TtmlLyricsParser` / registry: `Content(Word)` on a hit, and
  `Transient` / `DefinitiveEmpty` per the outcome table (blank title, index-not-ready, no-match,
  HTTP failure, parse failure). Mirrors `LrcLibLyricsProviderTests`.
- **Fan-out change** — a `LyricsService` test asserting that with two Word-tier providers, the
  registration-order winner is chosen deterministically (Subsonic over AMLL) rather than the first to
  return, guarding against regression of the removed early-exit.

---

## Risks / open questions

- **Coverage skew.** amll-ttml-db is overwhelmingly CJK / anime / Vocaloid. For a Western library, hit rates
  may be low — acceptable, since a miss just yields to the exact-keyed sources, and the always-on ship is
  precisely to measure real-world coverage before investing in the toggle/UI.
- **Match precision without duration.** Strict title+artist is the mitigation; the residual risk is a
  same-title/same-artist but *different-recording* match (e.g. a remix vs the original) pinning for the
  positive TTL. Album tiebreak helps only when multiple candidates qualify. A manual candidate picker is the
  deferred escape hatch (already floated in the lrclib design).
- **Index growth.** 1.5 MB today; if it grows substantially, the per-launch parse cost and heap footprint
  grow with it. The disk cache + conditional GET bound the *network* cost; the parse stays background. A
  future compacted on-disk form is possible if needed.
- **jsDelivr availability / `@main` freshness.** A CDN outage degrades to "no AMLL this session" (the last
  disk index keeps serving); purge lag delays newly-added lyrics by hours, immaterial against a weekly
  refresh.
