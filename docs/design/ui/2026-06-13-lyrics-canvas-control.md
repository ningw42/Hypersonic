# Win2D Lyrics Control — Design (Phase 2)

> Tracking context: [`LYRICS.md`](../../../LYRICS.md) §2 "Phase 2 — Win2D lyrics control".
> Prior phases: data model ([`2026-05-22-lyrics-data-model.md`](../service/2026-05-22-lyrics-data-model.md)),
> provider/service ([`2026-06-09-lyrics-provider-service.md`](../service/2026-06-09-lyrics-provider-service.md)),
> lrclib + concurrency ([`2026-06-10-lyrics-lrclib-provider.md`](../service/2026-06-10-lyrics-lrclib-provider.md)).

The goal: **word-synced lyrics rendering in WinUI 3 that mimics Apple Music's word-sync style** —
the gradient "wipe" that sweeps across each line as it is sung, the per-word bloom (pop + glow + lift),
soft spring scrolling, and dimmed/blurred neighbor lines. The reference implementation is
[BetterLyrics](https://github.com/BetterLyrics/BetterLyrics) (`../BetterLyrics`, WinUI 3, same platform);
its `NowPlayingCanvas` is exactly this pattern.

---

## 1. Scope

This phase ships a **standalone, pure-presentation control** (`LyricsCanvasControl`) plus the two pieces
needed to feed it real word-timed data for verification. It does **not** touch `NowPlayingPage` — that
swap is Phase 3.

**In scope (the "Apple-faithful" effect set):**

- Vertical line layout, active line centered at a ~40 % top-offset, smooth spring scroll between lines.
- Active-line **gradient wipe** advancing by played fraction (word-level when word timing is present;
  line-level fill otherwise).
- Per-word **bloom** on activation: scale pop + upward float + glow (when word timing is present and enabled).
- Inactive-line **emphasis falloff**: opacity down, gaussian blur up, slight scale-down with distance.
- A self-interpolating **playback clock** so the wipe stays fluid between coarse (500 ms) position updates.
- A **TTML parser** (Core) and a **local-file override source** that `LyricsService` consults *before* the
  cache, so a sideloaded TTML for a given song takes hard precedence over network providers, bypasses the
  cache and fan-out, writes nothing (deleting the file reverts cleanly), and can be played back live (§3.8).
- A throwaway **dev harness page** that hosts the control for visual verification.

**Explicitly out of scope (v1):**

- Translation / romanization lanes (multi-lane). The control is single-lane.
- Fan / 3D / parallax tilt, breathing / audio-reactive effects, spectrum / fluid backgrounds.
- Album-art **palette extraction**. Colors are *inputs* to the control; the driver supplies theme accent.
- Lyric **interaction** (click-line-to-seek, manual scroll). Display-only — but the scroll/layout model
  is built to accommodate a user-scroll offset and pointer hit-testing so Phase 3 adds wiring, not a rewrite.
- Settings UI for any of the above.
- Sub-word (per-syllable / per-character) effects. The bloom unit is the **word**.

---

## 2. Provenance & licensing

BetterLyrics is **GPL-3.0**; Hypersonic carries no license file. Copying or close-porting BetterLyrics
source would impose GPL on Hypersonic. Therefore this control is a **clean-room reimplementation from the
techniques** (gradient-stop wipe, render-model shape, frame-loop structure), not lifted code. This also
matches the project's design-doc convention: high-level ideas and pivotal spots, not transcribed source.

The techniques we reuse conceptually: a 4-stop linear-gradient sweep per line; a precomputed per-line
text layout with per-word pixel extents; a cached linear-scan synchronizer; an Update→Draw
`CanvasAnimatedControl` loop. The machinery we **deliberately do not reproduce**: the full
`ValueTransition<T>` keyframe subsystem (see §4) and every effect listed as out-of-scope above.

---

## 3. Component architecture

Win2D (`Microsoft.Graphics.Win2D`) is **already referenced** in `Hypersonic.csproj`, so no new dependency.
The control and all rendering live in **`Hypersonic`** (they need `CanvasTextLayout` → Win2D, which cannot
live in the platform-independent Core). Only the TTML parser + its format registration land in **Core**,
keeping Core dependency-free.

```
Hypersonic/Controls/Lyrics/
  LyricsCanvasControl.xaml(.cs)   UserControl wrapping CanvasAnimatedControl. Public Phase 2 surface.
  PlaybackClock.cs                Anchored, self-interpolating position estimator.
  LyricsVisualModel.cs            LyricsVisualLine -> LyricsVisualRow -> LyricsVisualWord. Per-track model.
  LyricsLayout.cs                 Measure/stack lines (multi-row aware); per-row segments, per-word fragments.
  LyricsSynchronizer.cs           Cached linear scan -> active line index.
  LyricsChoreographer.cs          Per-frame: spring scroll, emphasis targets, wipe/bloom math.
  LyricsSceneRenderer.cs          Per-frame Draw: per-row gradient wipe, bloom, neighbor falloff, edge fade.
  Tween.cs / Spring.cs            ~60-LOC animation primitive (see §4).

Hypersonic/Services/Lyrics/
  LocalFileLyricsSource.cs        Override source (NOT a fan-out provider); reads sideloaded TTML by song id.
  (LyricsService.cs — edit)       Consult the override source before the cache read (§3.8).

Hypersonic.Core/.../Lyrics/Parsing/
  TtmlLyricsParser.cs             TTML word/syllable spans -> LyricsDocument{ SyncKind = Word }.
                                  Registered as format "ttml" in the existing parser registry.

Hypersonic/Views/  (throwaway)
  LyricsLabPage / LyricsLabViewModel   Dev harness hosting the control.
```

### 3.1 `LyricsCanvasControl` — the public surface

A `UserControl` wrapping a single `CanvasAnimatedControl`. It is **pure presentation**: no service, no
ViewModel, no `MediaPlayer` reach-in. Inputs (dependency properties / setters):

| Input | Type | Meaning |
|---|---|---|
| `Lyrics` | `LyricsDocument?` | The lines to render plus `SyncKind` and `OffsetMs`. `null`/empty → blank. |
| `PositionMs` | `double` | Authoritative playback position — treated as a clock **anchor**, not a per-frame truth. |
| `IsPlaying` | `bool` | Drives the clock (advance vs freeze). |
| `TrackDurationMs` | `double?` | Track length — bounds the final line's end and clamps the clock. Driver supplies it (`PlayerViewModel`; `LyricsQuery.DurationSeconds` confirms it is known upstream). |
| `PlayedColor` / `UnplayedColor` | `Color` | Wipe colors. Driver supplies theme accent + dimmed foreground. |
| font/size inputs | — | Family, base size, optional stroke width. Sensible defaults. |

> **Input-type note.** LYRICS.md §2 sketched the input as `IReadOnlyList<LyricsLine> + currentPositionMs`.
> We deliberately take the whole `LyricsDocument` instead: the renderer needs `SyncKind` (to pick the
> word / line / plain rendering path) and `OffsetMs` (global timing offset), and the clock needs `IsPlaying`.
> This is a refinement of the sketch, not a contradiction.

It owns the frame loop and instantiates the clock, layout, synchronizer, choreographer, renderer, and the
cached visual model. A track change (new `Lyrics`) rebuilds the visual model; a size change re-measures only.

### 3.2 `PlaybackClock` — the 500 ms fix

`IPlaybackService.Position` only refreshes every 500 ms (a `DispatcherQueueTimer`), so forwarding it
directly makes the wipe stutter in 500 ms steps. The clock decouples render smoothness from update cadence:

- Holds an **anchor** `(authoritativeMs, wallClockAtAnchor)`.
- `Estimate(now)` = `authoritativeMs + (now − wallClockAtAnchor)` while `IsPlaying`; frozen while paused.
- On a new `PositionMs`: if `|authoritative − estimate| > ~250 ms` → a **seek** → snap the anchor; otherwise
  **converge smoothly** (absorb the small delta over the next frames) so there is no visible jump.

The control reads `Estimate()` once per frame and feeds it to synchronizer + choreographer.

### 3.3 `LyricsVisualModel` — per-track render model

Built once per track (lazily, on first draw, since it needs the canvas device + width for text layout):

- **`LyricsVisualLine`**: a wrapping `CanvasTextLayout` for the line, the line's timing (see *line-end
  resolution* below), per-line emphasis state (opacity/scale/blur tweens), its ordered words, and its derived
  **visual rows** (a long line wraps to ≥1 rows — see §3.4 / §5.1).
- **`LyricsVisualRow`**: one visual row of a wrapped line — its Y, its character range, and its pixel bounds,
  derived from the text layout's character regions. The wipe is rendered per row (§5.1).
- **`LyricsVisualWord`**: timing (`StartMs`/`EndMs` from `LyricsWord`), bloom state (scale/float/glow tweens),
  and the **row fragments** it occupies — a word maps to 1+ (row, x-range) pieces since it can straddle a wrap
  boundary. Pixel extents come from the layout's character regions.
- **Line-end resolution**: each line's end = `EndMs ?? nextLine.StartMs`; the **final line** = `EndMs ??
  TrackDurationMs ?? (StartMs + DefaultTailMs)` (a ~4 s tail) so the last line still wipes even with no end
  and no known duration. (Word-synced TTML carries real word/line ends; this guard mainly serves the
  Line/Plain fallback, whose last line often has no `EndMs`.)
- **Line-only fallback**: when `LyricsLine.Words` is empty, synthesize **one** word spanning `[StartMs, end]`.
  The renderer then produces a line-level fill (still flowing per-row across wraps) with no per-word bloom.
- **Plain fallback** (`SyncKind == Plain`, no timing): render static, all `UnplayedColor`, no wipe/scroll-sync.
- **Offset**: `LyricsDocument.OffsetMs` (when present) is folded into every line/word `StartMs`/`EndMs` here,
  at model-build time, so the synchronizer, clock comparison, and wipe all operate on already-offset timing
  and nothing downstream needs to know about the offset.

Single lane only — `LyricsLine` has no translation/romanization fields, and they are out of scope.

### 3.4 `LyricsLayout`

Measures and stacks lines at the current control width with **wrapping enabled**: a line's `CanvasTextLayout`
may produce multiple visual rows, so per-line height is taken from the layout (never assumed single-row) and
lines stack by their true heights. From the layout's character regions it derives each line's **visual rows**
(Y + character range) and each word's **row fragments**. Produces per-line Y, total content height, and the
visible line index range for the current scroll offset + viewport (renderer culls off-screen lines).
Recomputed only on track / size / font change — never per frame. A single token wider than the viewport
(rare, after wrapping) falls back to a per-line horizontal scale-to-fit with a min-scale clamp.

### 3.5 `LyricsSynchronizer`

A cached linear scan (port of BetterLyrics' ~40-LOC `LyricsSynchronizer`, single-lane simplification): given
the estimated position and the line list, return the active line index, remembering the last index to make
the common forward-playback case O(1).

### 3.6 `LyricsChoreographer`

Per frame, given active line index + estimated position:

- **Scroll**: target Y = center the active line at the top-offset factor; a critically-damped **spring**
  glides the current scroll Y toward it (no overshoot, ~350 ms settle). Track change / first layout → jump.
- **Emphasis**: for each visible line, set opacity / scale / blur targets by distance from the active line;
  ease via the tween primitive so active-line changes are soft.
- **Wipe**: compute the active line's played fraction (§5.1).
- **Bloom**: when a word newly becomes active, fire its onset tweens (§5.2).

### 3.7 `LyricsSceneRenderer`

Per frame Draw, visible lines only:

- Active line → **gradient wipe** (§5.1) plus per-word **bloom** (§5.2) when enabled.
- Inactive lines → solid `UnplayedColor` at the emphasis opacity, optional gaussian blur + scale-down.
- Top/bottom **edge-fade** mask so lines dissolve at the viewport edges.

### 3.8 Feeding real data — `LocalFileLyricsSource` + `TtmlLyricsParser`

Verification uses a real song plus a **sideloaded TTML** placed at:

```
%LOCALAPPDATA%\Hypersonic\LyricsOverride\<songId>.ttml
```

`<songId>` is the Subsonic song id — matching how the lyrics cache already scopes entries.

- **`LocalFileLyricsSource`** is an **override source, not a fan-out provider.** It is *not* registered in
  `LyricsService`'s `_providers` array — the fan-out + quality-tier policy is for fallback peers, and an
  override is not a peer. Instead `LyricsService` consults it **before the cache read** at the top of
  `GetLyricsAsync`: given `LyricsQuery.SongId` it checks for `<songId>.ttml`; if present it parses via the
  `ttml` registry parser and **returns that document directly — bypassing the cache read, the fan-out, and
  any cache write.** If absent, control falls through to the existing cache → fan-out path completely
  unchanged.
  - **Why pre-cache, not a provider.** The cache is read first (`LyricsService.cs:80`) and a prior network
    result is positive-cached for ~30 days, so a provider-shaped override dropped in *after* a play would
    never be seen without `forceRefresh`. And on a fresh fetch a Word-tier override would win only by *tier +
    early-exit*, not by being "registered first" — registration order is merely the equal-tier tiebreak
    (`LyricsFallbackPolicy.cs:42`). A pre-cache check gives **hard precedence** unconditionally.
  - **Reversible.** Because nothing is written to the cache, deleting the override file restores normal
    behavior on the next fetch — no eviction needed. That also makes this a genuine, safe "local lyrics
    override" feature; a harmless no-op when the folder is empty.
  - **Scope cost.** One small, isolated branch in `LyricsService` ahead of the cache read, plus the new source
    + its DI registration. It does not touch the fan-out, policy, or cache-store code.
- **`TtmlLyricsParser`** (Core) parses TTML timed spans into `LyricsDocument{ SyncKind = Word }`, words =
  spans, registered as format `ttml` in the existing format→parser registry. **The exact TTML dialect will be
  pinned to a sample file from the user** (AMLL TTML and Apple syllable TTML differ in attributes/structure).
  It is the same parser the deferred AMLL TTML DB provider will reuse, so it is not throwaway. (When that
  provider lands, the override simply takes precedence over it too.)

### 3.9 Dev harness

A throwaway `LyricsLabPage` hosts `LyricsCanvasControl`, binds `Lyrics` to `LyricsService`'s result for the
current track and `PositionMs`/`IsPlaying` to `PlayerViewModel`. This is the visual exercise for Phase 2;
Phase 3 deletes it and swaps the same control into `NowPlayingPage`'s lyrics area.

---

## 4. Animation primitive (Approach C)

BetterLyrics drives every animated property through a full stateful keyframe engine
(`ValueTransition<T>` — queues, delays, easing, per-line and per-char instances). Most of that flexibility
exists to serve effects we cut. We instead use **analytic core + a minimal primitive**:

- **Analytic (no stored state)**: the wipe progress and active-line resolution are pure functions of the
  estimated position + cached geometry. This is the bulk of the per-frame work and is trivially unit-testable.
- **Minimal primitive (the only state)**: a ~60-LOC helper providing
  - a **critically-damped spring** for scroll Y (gives the glide-and-settle), and
  - a short **eased tween** for per-word bloom onsets and per-line emphasis targets.

This is one small helper instead of the `ValueTransition` subsystem, matched to the trimmed effect set, and
it is the clean-room boundary for the GPL concern. If a future phase wants richer choreography, the primitive
can grow or be replaced without touching the renderer's analytic core.

---

## 5. Pivotal rendering math

### 5.1 The gradient wipe (per visual row)

The wipe is driven by a single **played head** in the line's reading order, then rendered **per visual row**
so wrapped lines flow correctly (BetterLyrics' sub-line *region* approach). The head in pixels, walking words
in order:

```
playedHeadPx = Σ(width of fully-played words) + currentWord.width × clamp((t − wordStart)/wordDur, 0, 1)
```

Measured along the line's flattened reading order; because each word knows its row fragments, the head maps
to a `(row, x-within-row)` position. For each visual **row**, with the head expressed as that row's local
fraction `pRow ∈ [0,1]` (0 if the head is before the row, 1 if past it), draw a horizontal **4-stop linear
gradient** across the row's bounds:

```
stop0: PlayedColor   @ 0
stop1: PlayedColor   @ pRow
stop2: UnplayedColor @ pRow + softness
stop3: UnplayedColor @ 1
```

So rows entirely before the head are solid `PlayedColor` (`pRow = 1`), the head's row carries the advancing
edge, and rows after are solid `UnplayedColor` (`pRow = 0`). `softness` is a small constant (~half a glyph as
a fraction of the row) giving the glowing leading edge. Word timing → a smooth intra-word sweep; the
line-only synthetic word (§3.3) → a clean fill that still flows row-by-row across a wrapped line.

### 5.2 Per-word bloom

Fired only when the line has real word timing **and** bloom is enabled. On a word becoming active, start
onset tweens with a **fixed short duration (~180 ms), independent of word length** (so a long held word does
not scale in slowly over seconds — only the wipe spans the full word duration):

- **scale** 1.0 → ~1.10 ease-out, settle to 1.0; pivot at the word's baseline center — the "pop".
- **float** 0 → ~0.1·lineHeight upward, ease back to 0 — the "lift".
- **glow** a gaussian-blurred crop (~0.2·lineHeight) of the word's *played* portion; opacity rises then fades.

Granularity is the **word**; no sub-word synthesis.

### 5.3 Emphasis & scroll constants (starting points, to be tuned in the smoke test)

- Active line: played words at full `PlayedColor`; unplayed words ~0.4 alpha of bright; scale 1.0.
- Neighbor lines: opacity ~0.5 (adjacent) falling to ~0.25 (far); blur rising with distance; scale ~0.95.
- Scroll spring: critically damped, ~350 ms settle; top-offset factor ~0.40.
- Clock: seek threshold ~250 ms.

These are tuning seeds; final values are dialed in during the §7 smoke test against the reference look.

---

## 6. Data flow

```
Track change ──► LyricsService.GetLyricsAsync(query)
                       │  (override source checked PRE-CACHE: <songId>.ttml wins outright if present,
                       │   bypassing cache + fan-out + write; else cache → fan-out exactly as before)
                       │  LyricsDocument
                       ▼
            LyricsCanvasControl.Lyrics = doc  ──► rebuild LyricsVisualModel (on next draw)

Per frame (CanvasAnimatedControl Update→Draw):
   PlayerViewModel.Position/IsPlaying ──► PlaybackClock.Estimate(now)
        │ estimatedMs
        ▼
   LyricsSynchronizer ──► activeLineIndex
        │
        ▼
   LyricsChoreographer ──► spring scroll Y, emphasis targets, wipe p, bloom onsets
        │
        ▼
   LyricsSceneRenderer.Draw ──► gradient wipe + bloom + neighbor falloff + edge fade
```

Size change → `LyricsLayout` re-measure only (visual model text layouts rebuilt at new width).

---

## 7. Risks & verification

- **Text-layout perf at 60 fps** — the standing LYRICS.md risk. Mitigation: cache layouts + word extents;
  build bloom effects for the active line only; draw visible lines only. **Smoke-test 60 fps early**, before
  layering bloom/glow, on representative hardware.
- **TTML dialect drift** — pin `TtmlLyricsParser` to the user's actual sample before finalizing; do not
  guess attributes.
- **`IsPlaying` source** — confirm `IPlaybackService` / `PlayerViewModel` exposes a play/pause state for the
  clock to freeze on (the existing `PlaybackStateChanged` path). If only position is available, derive
  playing-state from successive position deltas as a fallback.
- **Colors** — v1 uses theme accent (played) + dimmed foreground (unplayed), passed as inputs. Album-art
  palette extraction is a separate future enhancement and must not block this phase; keeping colors as inputs
  means a palette service can later feed the same control unchanged.
- **`LyricsService` pre-cache hook** — the override (§3.8) is the one change to a Phase 1 component: a single
  isolated branch ahead of the cache read. It must not alter the cache / fan-out / policy paths when no
  override file exists — guard it with a fast existence check, run only on track change, never per frame.
- **Wrapping & scripts** — exercise the multi-row wipe/bloom on a long line and on a CJK line in the smoke
  test; a token wider than the viewport falls back to per-line scale-to-fit (§3.4).

---

## 8. Phase boundary

Phase 2 ends with `LyricsCanvasControl` rendering a sideloaded word-timed TTML for a real song in the dev
harness, with graceful line-level fallback for the live (Line/Plain) providers. **Phase 3** wires
`CurrentLyrics` + position onto the owning ViewModel, replaces `NowPlayingPage`'s text-only lyrics area with
this control, deletes the harness, and adds interaction (click-line-to-seek, manual scroll) onto the
already-accommodating scroll model.
