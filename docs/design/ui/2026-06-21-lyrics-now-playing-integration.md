# Lyrics on Now Playing — Integration Design (Phase 3)

> Tracking context: [`LYRICS.md`](../../../LYRICS.md) §2 "Phase 3 — Integration on `NowPlayingPage`".
> Prior phase: Win2D lyrics control ([`2026-06-13-lyrics-canvas-control.md`](2026-06-13-lyrics-canvas-control.md)).
> Service layer: provider/service ([`../service/2026-06-09-lyrics-provider-service.md`](../service/2026-06-09-lyrics-provider-service.md)),
> lrclib + concurrency ([`../service/2026-06-10-lyrics-lrclib-provider.md`](../service/2026-06-10-lyrics-lrclib-provider.md)).

The goal: retire the text-only lyrics area on `NowPlayingPage` and let the Phase 2
`LyricsCanvasControl` render **all** lyric kinds, wired to live playback, with a manual refresh
button for on-demand recovery of the best available lyrics.

This is the terminal phase of the Time-Synced Lyrics feature. The control, the orchestrating
service, and the consuming ViewModel all already exist; this phase is **wiring, not new
subsystems** — plus one small, additive UI affordance (the refresh button) and the removal of the
Phase 2 dev harness.

---

## 1. Scope

**In scope:**

- Replace the `ScrollViewer`/`TextBlock` lyrics surface on `NowPlayingPage` with `LyricsCanvasControl`,
  bound to the existing `NowPlayingViewModel.CurrentLyrics` plus live position / play-state / duration
  from the singleton `PlayerViewModel`.
- Drive the control's effect knobs to the product's implicit defaults (center alignment, neighbor
  blur on, bloom off, karaoke wipe only for word-synced).
- Feed the control theme-aware colors (bright foreground for sung text, dimmed for un-sung),
  refreshed on Light/Dark switches.
- Add a **manual refresh** button to the lyrics surface — a generous-budget, cache-bypassing
  re-fetch whose progress feedback lives entirely in the button.
- Retire the Phase 2 throwaway dev harness (`LyricsLabPage` / `LyricsLabViewModel`, its nav entry,
  its DI + page-service registrations).

**Out of scope (deferred):**

- Per-effect **settings UI**. The effect defaults below are hardcoded; user customization is a later
  pass that swaps the constants for settings-backed values. (Requirement #6.)
- Canvas **user-scroll**. The control has no built-in scroll yet, so long Plain lyrics are cut off at
  the viewport edge for now (see §4). (Requirement #7's deferred half.)
- Album-art **palette extraction** — colors stay theme-derived inputs (a palette service can later
  feed the same control unchanged).
- Lyric **interaction** (click-line-to-seek). The Phase 2 scroll model already accommodates it; the
  wiring is a future pass.

---

## 2. The lyrics surface swap

Today the lyrics surface is a single grid with three children layered by `LyricsLoadState`
visibility: a `ProgressRing` (Loading), a `ScrollViewer` + `TextBlock` (Loaded), and a "No lyrics
available" message (NoLyrics). The swap **preserves this state structure** and replaces only the
middle child:

| State | Before | After |
|---|---|---|
| **Loading** | `ProgressRing` | unchanged (native) |
| **Loaded** | `ScrollViewer` + `TextBlock` | **`LyricsCanvasControl`** |
| **NoLyrics** | "No lyrics available" text | unchanged (native), now joined by the refresh button |

Keeping `ProgressRing` and the empty-state text **native** is a deliberate boundary call. The control
is pure lyrics presentation: its only content input is a `LyricsDocument?`, and it has no input that
distinguishes "still fetching" from "definitively none". Delegating the empty state to the canvas is
cheap but delegating the spinner would mean reimplementing `ProgressRing` on the render thread —
losing native accessibility, theming, and reduced-motion handling. Neither is worth the coupling for
this phase, so the canvas owns **only the lyrics-present (Loaded) surface**.

Because every sync kind — Word, Line, and Plain — now routes through the canvas, there is no per-kind
branching at the page level: `Loaded` simply means "the canvas has a document to show". The
flattened-string fallback (`NowPlayingViewModel.LyricsText` and its `Flatten` helper) loses its only
consumer when the `TextBlock` is removed and is **deleted** as part of the swap.

The page root gains the `Hypersonic.Controls.Lyrics` XAML namespace. The control stretches to fill
the surface; it does its own centering, scrolling, and edge-fade internally.

---

## 3. Driving position, play-state, duration

The control's timing inputs are in **milliseconds** (`PositionMs`, `TrackDurationMs`) plus a
play-state bool; `PlayerViewModel` exposes `Position` and `Duration` in **seconds** plus `IsPlaying`.
The bridge is a unit conversion, and it lives in **testable C# on `NowPlayingViewModel`**, not in a
XAML converter:

- The VM gains proxy properties — position (ms), play-state, duration (ms, null when non-positive) —
  kept current by subscribing to `PlayerViewModel.PropertyChanged` for the relevant properties and
  re-projecting. This mirrors the proven pattern from the Phase 2 harness ViewModel.
- The canvas binds all four content/timing inputs to the VM, so its bindings read off one object that
  also owns `CurrentLyrics`.

**Why C#-side conversion over a XAML converter:** the decisive factor is unit-testability of the
sec→ms projection and the null-duration guard, not binding-source count (binding straight to
`PlayerViewModel` with a converter would be equally "one source"). The control's internal
`PlaybackClock` smooths the coarse (500 ms) position cadence regardless, so the choice does not affect
render smoothness.

`PlayerViewModel.Position` is driven by the singleton playback path — `PlaybackService`'s 500 ms
position timer raises `PositionChanged`, which the singleton `PlayerViewModel` forwards — independent
of whether the NowPlaying page is open. The VM's proxy simply re-projects whatever the singleton
currently holds; it owns no timer of its own.

---

## 4. Effect defaults (requirements #2–#6)

The control exposes each effect as a dependency property. The page sets them to the product defaults,
overriding the control's own defaults. **Only the karaoke wipe is dynamic**; the rest are constants.

| Effect | Value | How driven |
|---|---|---|
| **Alignment** (#2) | Center | constant |
| **Neighbor blur** (#4) | on | constant |
| **Bloom** (#5) | off — even for word-synced | constant |
| **Karaoke wipe** (#3) | on **iff** `SyncKind == Word` | derived from `CurrentLyrics` |

The wipe gate is a computed `bool` on the VM (`CurrentLyrics?.SyncKind == Word`), recomputed whenever
`CurrentLyrics` changes — keeping the §3 rule that derived values live in testable C#. Line-synced and
Plain therefore get the renderer's discrete solid-highlight path instead of the sweep.

**Behavioral note:** disabling the wipe does **not** disable line advancement. On a line-synced track
the active line still centers and scrolls (emphasis/scroll are independent of the wipe) — it just
highlights as a solid block rather than sweeping. That is the intended reading of "disable the karaoke
wipe for line-synced".

**Plain (untimed) lyrics** render through the canvas as a static, dimmed block pinned near the top.
Untimed text has no active line, so there is no magnification to apply and the per-line emphasis falls
out to the neutral state. With no built-in canvas scroll yet, a long Plain lyric is **cut off at the
viewport edge** — an accepted limitation this phase; canvas-scroll is the deferred follow-up. (This is
the resolution of requirement #7: route Plain through the canvas now, accept no-scroll until the
control gains it, rather than keeping a parallel scrollable text path.)

None of these effects exposes a UI toggle (#6); they are hardcoded bindings/constants. The later
customization pass swaps the constants for settings-backed values without touching the control.

---

## 5. Colors (theme-aware)

The control takes `Color` values (not brushes), so a `ThemeResource` brush cannot bind directly. The
page **resolves theme colors in code-behind and pushes them onto the control**, reusing the page's
existing theme-refresh channel — the same mechanism already used for the metadata hyperlink
foregrounds (hidden brush-provider elements read back and re-applied on `ActualThemeChanged`).

- **Played (sung)** = primary foreground (`TextFillColorPrimary`) — bright, near-white in dark and
  near-black in light. This is the monochrome Apple-Music look named as the north-star UX in LYRICS.md.
- **Unplayed (un-sung)** = tertiary foreground (`TextFillColorTertiary`) — clearly dimmed in both
  themes.

The colors are re-resolved and re-applied whenever the page's theme changes, so the lyrics track the
rest of the page in lockstep on a Light/Dark switch. Album-art palette extraction remains out of scope;
because colors are inputs, a palette service can later feed the same control unchanged.

---

## 6. Manual refresh button (requirement #1)

A small icon button (refresh glyph, tooltip "Refresh lyrics") sits at the **top-right of the lyrics
surface**, overlaying the canvas. Placement is a reasonable starting point and may be tuned during
implementation. It is enabled whenever there is a track, so it works in **both** the Loaded and
NoLyrics states — refresh is the user's path to retry a no-result or upgrade to better lyrics.

**Force-refresh semantics.** The button invokes the service with the generous manual budget
(`ManualRefreshBudgetMs`, already defined for this purpose) and `forceRefresh: true` — bypassing the
cache read and overwriting the entry, the on-demand recovery path the service was built to support.
The budget value reaches the VM via the same injected `LyricsOrchestrationOptions` the service uses
(the exact injection wiring is an implementation detail; the design commitment is that refresh uses
the manual budget, not the default).

**Feedback lives entirely in the button.** While the fetch runs:

- The canvas keeps rendering the current lyrics and following the song — `CurrentLyrics` is **not**
  cleared and `LyricsLoadState` is **not** flipped to Loading. No blank gap, no full-surface spinner.
- The button shows a busy/disabled state (driven by the async command's running flag). No separate
  indicator, no descriptive text — the button *is* the indicator.
- When the fetch returns, the result swaps in through the **same** decision path as a track change
  (null / `SyncKind == None` → NoLyrics; otherwise → Loaded).

**Correctness:**

- The refresh shares the VM's existing **generation guard**: it increments the generation, and a
  track change mid-refresh drops the stale result — the same mechanism the track-change fetch uses.
- The async command's built-in re-entrancy guard prevents a second concurrent refresh.
- A sideloaded TTML **override still wins** even on force-refresh: the service consults the override
  source *before* the cache and the `forceRefresh` branch, so an override is intentionally not
  bypassed. This is correct — an explicit local override should outrank a network re-fetch.

**Shared apply path.** The "apply a returned document → state" tail currently inlined in the
track-change fetch is extracted into a small shared helper so the track-change path and the refresh
command resolve the null / `None` / document decision through one code path.

---

## 7. Retiring the dev harness

The Phase 2 harness was the visual exercise vehicle and is now superseded by the real surface:

- Delete `LyricsLabPage` (view + code-behind) and `LyricsLabViewModel`.
- Remove the "Lyrics Lab" navigation item from the shell.
- Remove its DI registrations and its page-service mapping.

**Kept:** everything the harness was *exercising* — the control, `LocalFileLyricsSource`,
`TtmlLyricsParser`, and the pre-cache override hook. The sideloaded-`<songId>.ttml` override remains a
real (if undocumented-in-UI) feature and a genuine local-override capability; only the page that
visualized it is removed.

---

## 8. Testing

Following the repo's integration-oriented, no-mocking style and the existing synchronous-VM test
pattern:

- **`NowPlayingViewModel`** — the sec→ms proxies (conversion and the null-on-non-positive-duration
  guard); the `EnableKaraokeWipe` derivation (`Word` → true; `Line` / `Plain` / `None` → false); and
  the refresh command: that it force-refreshes with the manual budget, applies the result through the
  shared apply path, honors the generation guard, and does **not** clear `CurrentLyrics` mid-refresh.
- **Control** — already covered by Phase 2's tests.
- **Page wiring** — the surface swap is largely XAML, but it is **not** fully exercised by VM tests:
  the code-behind color pushes, the four timing/content bindings, the effect-default values, and the
  refresh-button command binding can each be wrong while every VM test still passes. These are
  verified by the manual smoke check, not unit tests — so the smoke check must explicitly confirm each
  binding renders (position advances, wipe gates on sync kind, colors track theme, refresh fires).
- **Page layout** — the existing layout tests plus a manual 60 fps smoke check on representative
  hardware (the standing LYRICS.md perf risk), now against the live NowPlaying surface rather than the
  harness.

---

## 9. Implementation notes (deferred from design)

Mechanics that are the implementation's responsibility, recorded here so they are not forgotten but
deliberately kept out of the design narrative:

- **Subscription lifecycle.** The VM's new `PlayerViewModel.PropertyChanged` subscription (§3) must
  attach and detach on the same `OnNavigatedTo` / `OnNavigatedFrom` boundary the VM already uses for
  its `IPlaybackService.TrackChanged` subscription. `NowPlayingViewModel` is transient and
  `PlayerViewModel` is singleton, so a missing detach would leak old VM instances and double-apply
  position ticks. No new lifecycle hook is needed — it rides the existing one.
- **Budget injection.** `LyricsOrchestrationOptions` is injected into the VM (via `IOptions<>`, as
  elsewhere) so the refresh command reads `ManualRefreshBudgetMs` rather than hardcoding it.
- **In-button busy visual.** The exact busy affordance (swap the glyph for a small ring vs. rely on
  the disabled state) and the button's precise placement are settled during implementation; the design
  fixes only the behavior (feedback in-button, canvas untouched) and the starting placement.

---

## 10. Phase boundary

Phase 3 ends with `NowPlayingPage` rendering live lyrics of every kind through `LyricsCanvasControl`,
a working manual refresh, and the harness removed. LYRICS.md §2 Phase 3 is marked complete, noting the
two refinements that emerged here: **all** kinds (including Plain) route through the canvas, and the
refresh button's feedback is entirely in-button. The remaining deferred work — canvas user-scroll
(unblocking long Plain lyrics), click-line-to-seek, per-effect settings UI, and album-art palette —
layers onto this surface without reworking it.
