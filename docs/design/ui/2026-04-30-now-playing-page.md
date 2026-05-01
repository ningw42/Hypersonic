# Now Playing / Full-Window Player — Design Spec

## Context

Roadmap item 2.2 calls for a dedicated full-content page for the currently playing track — a richer surface than the player bar can provide, showing large album art, track metadata, lyrics, and the upcoming queue inline. The page lives inside the existing shell `Frame` (not a separate window) so the player bar stays pinned at the bottom and continues to own all transport state.

### Constraints from the existing architecture

- **Singleton `PlayerViewModel` is the source of truth** for playback state, queue, and now-playing track. The new page must not duplicate ownership of any of these — it consumes them via DI and reacts to events.
- **The player bar lives in `ShellPage`**, above the `Frame`. The now-playing page renders inside the `Frame`, so it can never paint over or modify the bar. This is fine — the bar acts as the persistent transport surface across all pages.
- **Queue display already has a working pattern** in the player-bar flyout backed by `Player.QueueItems` (`IReadOnlyList<QueueItem>`). The now-playing page binds to the same collection so the two views stay in lock-step by construction.

### Out of scope (v1)

- **Synced lyrics** — even when the OpenSubsonic API returns timestamped `LyricLine[]`, v1 renders them as plain text without time-syncing the active line. Tracked as roadmap item **4.5 Time-Synced Lyrics** (see [`ROADMAP.md`](../../../ROADMAP.md)).
- **Click-to-seek lyric lines** — depends on synced lyrics; covered by roadmap 4.5.
- **Album-art-based blurry background** — would require a Composition API effect graph; defer to a polish phase. v1 uses the flat theme background.
- **Queue interactions** — no drag-to-reorder, no context menu, no per-row remove. Read-only (with **double-tap-to-jump** as the only interaction, matching the existing flyout's `QueueList_DoubleTapped` handler).
- **Color extraction / accent tinting** — defer.
- **Full-screen / borderless mode** — the page lives in the standard shell layout.

---

## Feature Overview

A new page (`NowPlayingPage`) accessible by clicking the **album art** or **track title** in the player bar. Two-column layout:

- **Left column** — large square album art on top, track metadata below, both centered horizontally within the column.
- **Right column** — `SelectorBar` with two tabs: **Lyrics** and **Queue**.

The page is empty/placeholder when no track is playing. The entry-point click handlers must be no-ops in that state.

### Entry points

- **Player bar album art** — the **container `Grid`** holding both the placeholder (`Border` + `FontIcon`) and the `PlayerCoverArt` `Image` ([ShellPage.xaml:419-427](../../../Hypersonic/Views/ShellPage.xaml)) gains the tap handler and the hand cursor. The handler must **not** be attached only to the `Image`: when the current track has no cover art, the `Image.Source` is null and only the placeholder `Border`/`FontIcon` underneath is visible — attaching only to the `Image` would make the visible placeholder un-clickable. Since the existing element is unnamed, give the `Grid` an `x:Name` (e.g. `PlayerCoverArtContainer`) and wire the handler to it.
- **Player bar track title** (`TextBlock` bound to `Player.TrackTitle`) — gain a tap handler and hand cursor on the `TextBlock` itself.

Both navigate to `NowPlayingViewModel`. Both must indicate clickability on hover (cursor change to hand). Both no-op when `IPlaybackService.NowPlaying` is null. Equivalently the tap handler can check `Player.HasTrack` (a derived `PlayerViewModel` property) since `ShellPage` already has `Player` in scope — both are equivalent and either is fine.

To enforce a **single-instance back-stack rule** for the now-playing page (at most one `NowPlayingPage` in `NavigationFrame.BackStack` at any time, plus optionally the current page), the tap handler runs this sequence:

1. If the current page is already `NowPlayingPage`, no-op (cheap fast path; also avoids self-pruning footguns).
2. Otherwise, before navigating, scan `ViewModel.NavigationService.Frame.BackStack` and remove any `PageStackEntry` whose `SourcePageType == typeof(NowPlayingPage)`. This is sufficient because back-stack entries store the page type, not a `Page` instance. (`ShellPage` reaches the navigation service via its `ViewModel` — `ViewModel.NavigationService` — or via `App.GetService<INavigationService>()`. There is no `_navigationService` field on `ShellPage` today, and adding one is unnecessary; both existing access patterns are already used elsewhere in `ShellPage.xaml.cs`.)
3. Navigate via the standard `INavigationService.NavigateTo(typeof(NowPlayingViewModel).FullName!)` — do **not** pass `clearNavigation: true` (that would wipe Albums/Artists/etc. from history, not what we want).

Why this is needed: the existing duplicate-only-while-current rule does not cover the sidebar-routed case (e.g. `MainPage → NowPlayingPage → AlbumsPage → NowPlayingPage` would otherwise leave the first `NowPlayingPage` in history and push a second). `INavigationService.NavigateTo` does not provide single-instance semantics, and `clearNavigation: true` is too coarse. Scanning `BackStack` in the tap handler is the minimum-surface fix; a generalized "single-instance pages" navigation option is a larger refactor and out of scope.

This is also the **only** duplicate-prevention mechanism — `TopLevelPages` does not provide it, see Navigation Registration.

#### Testability

The decision logic above (whether to navigate, which back-stack entries to prune) is non-trivial enough to deserve unit coverage, but `ShellPage` and a real `Frame.BackStack` cross the WinUI test-host boundary that crashes `[UITestMethod]`. Same approach as `QueueAutoScrollCoordinator`: extract the rules into a small pure helper:

```csharp
public sealed record NowPlayingTapDecision(
    bool ShouldNavigate,
    IReadOnlyList<int> BackStackIndicesToRemove);

public static class NowPlayingTapPolicy
{
    public static NowPlayingTapDecision Decide(
        bool hasTrack,
        Type? currentPageType,
        IReadOnlyList<Type> backStackPageTypes)
    {
        if (!hasTrack) return new(false, []);
        if (currentPageType == typeof(NowPlayingPage)) return new(false, []);

        var prune = new List<int>();
        for (var i = 0; i < backStackPageTypes.Count; i++)
        {
            if (backStackPageTypes[i] == typeof(NowPlayingPage))
            {
                prune.Add(i);
            }
        }
        return new(true, prune);
    }
}
```

The `ShellPage` tap handler is thin glue:

1. Project the real `Frame.BackStack` into `IReadOnlyList<Type>` (one `Select(e => e.SourcePageType)`).
2. Call `NowPlayingTapPolicy.Decide(...)`.
3. If `ShouldNavigate == false`, return.
4. Apply `BackStackIndicesToRemove` to the real `Frame.BackStack` (descending, so indices stay valid after each `RemoveAt`).
5. Call `NavigationService.NavigateTo(typeof(NowPlayingViewModel).FullName!)`.

Automated tests target `NowPlayingTapPolicy.Decide` only — no `Frame`, no `ShellPage`, no UI thread. The thin glue (steps 1, 4, 5) is verified by manual smoke tests #1–3 and #16–17.

Returning indices (rather than a pre-pruned list of types) is intentional: the helper stays purely functional, and the caller mutates the real `Frame.BackStack` in place, which `Frame` requires anyway.

The artist-name area in the player bar is already a `RichTextBlock` rendering segmented artist hyperlinks — those continue to navigate to the artist detail page and are not part of the now-playing entry surface.

### Player bar behavior on the now-playing page

The bar stays visible and functional. There is *intentional* visual redundancy (small art in the bar, large art on the page) — this preserves a single, consistent transport surface across all pages and avoids re-implementing playback controls inside the new page.

---

## Layout

### Top-level structure

`NowPlayingPage` must set `behaviors:NavigationViewHeaderBehavior.HeaderMode="Never"` on its root `Page` element, matching every other page in the app (`MainPage`, `AlbumsPage`, `ArtistsPage`, `TracksPage`, `PlaylistsPage`, and the detail pages). This is especially important for `NowPlayingPage` because it is **not** a sidebar item — without `HeaderMode="Never"` the shell's `NavigationView` header keeps showing the previously selected sidebar item's label while the now-playing layout is visible, which is confusing.

Page layout itself is a two-column `Grid`:

- Left column: width `*` (or `Auto` capped to a reasonable max — see Sizing below)
- Right column: width `*`

A back navigation chevron in the top-left corner returns to the previous page via `INavigationService.GoBack()`. Standard nav back behavior already wired via `ShellPage`'s back button covers this — no separate handler needed if the shell back button is visible. Confirm during implementation; if the shell back button is hidden on this page (it shouldn't be), add an in-page chevron.

### Left column — art + metadata

Vertically stacked, both elements horizontally centered within the column:

1. **Album art** — square, uses `SquarePanel` (existing custom control) or a square-constrained `Image`. Cover art comes from `IPlaybackService.NowPlaying.CoverArtUri` (or equivalently from `Player.CoverArtUrl` since the singleton `PlayerViewModel` already mirrors it). Falls back to the same placeholder glyph (`&#xE8D6;` / MusicNote) used elsewhere when the URI is null.
2. **Track metadata** — three to four lines below the art:
   - **Track title** — large, prominent (e.g., 24–28pt, SemiBold).
   - **Artist** — segmented `RichTextBlock` with clickable artist hyperlinks (reuse the pattern from `ShellPage`'s player bar artist rendering and `PlaylistDetailPage`/`ArtistDetailPage`). Falls back to `FlatArtistText` when `HasStructuredArtists` is false.
   - **Album** — clickable hyperlink to album detail when `AlbumId` is present, otherwise plain text.
   - **Technical line** — codec/quality info (see Technical line format).

All metadata lines center-aligned within the column. Long titles/artist/album names trim with ellipsis.

#### Always-on metadata render

The artist `RichTextBlock` and the album hyperlink can't be kept current by simple `{x:Bind}` — `RichTextBlock` rebuilds its `Inlines`/`Hyperlink` graph imperatively, and several of the values it needs (`HasStructuredArtists` in particular) are **not** `[ObservableProperty]` on `PlayerViewModel` ([line 30](../../../Hypersonic/ViewModels/PlayerViewModel.cs)). This is the exact problem `ShellPage` already solved for the player-bar artist row: subscribe to `Player.NowPlayingMetadataChanged` and call a `RenderNowPlaying()` code-behind that rebuilds artist links + album hyperlink imperatively ([ShellPage.xaml.cs:74, 92-103, 154](../../../Hypersonic/Views/ShellPage.xaml.cs)).

`NowPlayingPage` (the **page code-behind**) owns **one always-on metadata subscription** (plus a separate always-on `ActualThemeChanged` lifecycle wired the same way — see "Theme-change refresh" below):

- **`Player.NowPlayingMetadataChanged`** → marshal through `_pageDispatcher` and call `RenderNowPlaying()` which rebuilds the artist `RichTextBlock`, the album hyperlink, the title text, and the cover-art binding.

Wire it in the page **constructor** (after `InitializeComponent()`) and unsubscribe in the **`Unloaded`** event handler — the page-side hook pair (see "Use constructor + `Unloaded`…" below). Call `RenderNowPlaying()` once **immediately after the always-on subscriptions are wired** (i.e., after the `+= OnPlayerMetadataChanged` and `+= OnActualThemeChanged` lines, before the queue-tab plumbing construction and the `Unloaded` lambda) for initial population. The exact placement is shown in both constructor snippets below — initial render happens while the page-active flag is true and the always-on handlers are live, but it does not need to wait for queue-tab plumbing (which only matters once the user opens the Queue tab). Without this initial call, a track change while the page is open updates lyrics and the technical line but leaves the artist/album hyperlinks pointing at the previous track.

**The handler must marshal through the captured page dispatcher** (see "Captured dispatcher" subsection in Auto-scroll). `Player.NowPlayingMetadataChanged` is raised at the end of `PlayerViewModel.OnTrackChanged`, which itself runs from the playback service's thread context (potentially non-UI). `RenderNowPlaying()` touches XAML, so calling it directly from the event would either race the UI thread or throw on access.

**Use constructor + `Unloaded` for page-side lifecycle, not `INavigationAware`.** The current `INavigationService` only invokes `OnNavigatedTo` / `OnNavigatedFrom` on `frame.GetPageViewModel()` ([NavigationService.cs:74-79, 95-103](../../../Hypersonic/Services/NavigationService.cs)) — i.e., on the page's `ViewModel` property, **not on the `Page` itself** ([FrameExtensions.cs:7](../../../Hypersonic/Helpers/FrameExtensions.cs)). If a page code-behind defines methods named `OnNavigatedTo` / `OnNavigatedFrom` they are simply never called by the navigation service. The existing pages (`MainPage`, `AlbumsPage`, `AlbumDetailPage`, `ArtistDetailPage`, `PlaylistDetailPage`, `TracksPage`) consistently use the **constructor + `Unloaded` event** pattern for page-side subscriptions; mirror that. (No `Loaded` hook is wired in this work — the constructor runs once when the page is activated, which is sufficient for one-time setup; `Unloaded` is the matching teardown.)

`NowPlayingViewModel` keeps its own `INavigationAware` lifecycle (lyrics fetching, technical-detail recompute) — that subscription chain is invoked correctly because the navigation service finds it via the `ViewModel` property. Don't touch it. The page-side subscriptions described below are independent.

**Dispatcher-enqueued callbacks need a navigation guard** because `_pageDispatcher.TryEnqueue` is fire-and-forget — work that was queued before `Unloaded` will still run after the handlers are detached, on a page that's no longer in the visual tree. Touching `RichTextBlock` / `QueueListView` after detach can throw or update an orphaned page. Guard every enqueued callback against a `_pageActive` flag set in the constructor (after `InitializeComponent()`) and cleared in `Unloaded`. Concrete shape:

```csharp
public NowPlayingViewModel ViewModel { get; }
public PlayerViewModel Player { get; }

private readonly IPlaybackService _playbackService;
private DispatcherQueue _pageDispatcher = null!;
private bool _pageActive;

// Queue-tab plumbing — declared here for the `Unloaded` cleanup; constructed
// in the same constructor body before `Unloaded` is wired (full construction
// shown in Implementation Sequence step 8).
private readonly QueueTabSubscriptionGate _queueTabGate;
private readonly QueueAutoScrollCoordinator _scrollCoordinator;

public NowPlayingPage()
{
    // DI resolution BEFORE InitializeComponent — XAML bindings on the
    // page (and the navigation service's reflection-based VM lookup)
    // require ViewModel and Player to be set when component init runs.
    ViewModel = App.GetService<NowPlayingViewModel>();
    Player = App.GetService<PlayerViewModel>();
    _playbackService = App.GetService<IPlaybackService>();   // for queue-tab QueueChanged subscription (step 8)

    InitializeComponent();
    _pageDispatcher = DispatcherQueue.GetForCurrentThread();
    _pageActive = true;

    // Page-side subscriptions — match the existing page pattern (MainPage, AlbumsPage, …).
    Player.NowPlayingMetadataChanged += OnPlayerMetadataChanged;
    ActualThemeChanged += OnActualThemeChanged;
    RenderNowPlaying();    // initial population — already on UI thread, no marshal needed

    // Queue-tab plumbing — full construction (attachAll/detachAll lambdas, MarshalAndScroll
    // wiring) lives in Implementation Sequence step 8. Both fields MUST be assigned before
    // the `Unloaded` lambda is wired below — it references both.
    _scrollCoordinator = new QueueAutoScrollCoordinator(MarshalAndScroll);
    _queueTabGate = new QueueTabSubscriptionGate(
        attachAll: () => { /* see step 8 */ },
        detachAll: () => { /* see step 8 */ });

    Unloaded += (_, _) =>
    {
        // Order matters — see Auto-scroll → "Mandatory `Unloaded` cleanup" and step 8.
        _pageActive = false;                                  // (1) BEFORE detach so any in-flight enqueued callback sees false
        _queueTabGate.Detach();                               // (2) idempotent — no-op if Lyrics tab is currently active
        _scrollCoordinator.OnTabSelected(NowPlayingTab.Lyrics, -1);  // (3) reset coordinator's active flag
        Player.NowPlayingMetadataChanged -= OnPlayerMetadataChanged; // (4a) detach always-on metadata-render handler
        ActualThemeChanged -= OnActualThemeChanged;           // (4b) detach always-on theme-refresh handler
    };
}

private void OnPlayerMetadataChanged(object? sender, EventArgs e)
{
    _pageDispatcher.TryEnqueue(() =>
    {
        if (!_pageActive) return;   // guard against post-Unloaded execution
        RenderNowPlaying();
    });
}

private void OnActualThemeChanged(FrameworkElement sender, object args)
{
    // ActualThemeChanged fires on the UI thread synchronously, so no enqueue,
    // and -= in Unloaded prevents it from running post-navigation.
    // No _pageActive guard needed here.
    RenderNowPlaying();
}
```

The same `_pageActive` flag guards the queue-tab scroll adapter (see "Captured dispatcher" subsection). Set the flag to `false` **before** detaching event handlers in `Unloaded`: there's a small window where an event fired by the singleton service could have already invoked the handler, which has already enqueued a callback, between the flag clear and the `-=`. Setting the flag first makes that pending callback no-op when it runs.

Note the asymmetry: the initial `RenderNowPlaying()` call inside the constructor and the `ActualThemeChanged` handler are both UI-thread-local and call `RenderNowPlaying` directly; only the `Player.NowPlayingMetadataChanged` handler routes through `_pageDispatcher.TryEnqueue` because its event source may originate off the UI thread, and only the enqueued callback needs the `_pageActive` guard. Don't naively mirror `ShellPage`'s direct call here — `ShellPage` is constructed on the UI thread *and* its handler can land on whatever thread the event uses (no captured dispatcher today); the new page captures the dispatcher precisely so its handler is correct under all event-source threads.

#### Theme-change refresh

`RenderNowPlaying()` builds the `Run`/`Hyperlink` graph imperatively and reads brushes from theme-resource providers (`SecondaryBrushProvider.Foreground`, `TertiaryBrushProvider.Foreground` — same providers `ShellPage` uses). Those reads return **snapshot brushes**, not live theme-resource references; the `Run`/`Hyperlink` foregrounds don't update when the system theme switches. `ShellPage` already handles this by also calling `RenderNowPlaying()` from `ActualThemeChanged` ([ShellPage.xaml.cs:219-222](../../../Hypersonic/Views/ShellPage.xaml.cs)).

`NowPlayingPage` mirrors that — wire `ActualThemeChanged` to call `RenderNowPlaying()`. **Use the same named-handler + `+= … -=` lifecycle as the metadata subscription** (the `ActualThemeChanged += OnActualThemeChanged;` / `-= OnActualThemeChanged;` lines in the snippet above). A lambda subscription (`ActualThemeChanged += (_, _) => RenderNowPlaying();`) cannot be detached and would leak across navigation cycles — the named-handler form is the correct match for the rest of the page's explicit lifecycle. Without the theme refresh, switching themes while the page is open leaves the artist/album hyperlinks and any imperatively-styled `Run`s with stale brushes until the next track change.

(If a future cleanup converts `RenderNowPlaying` to use theme-resource-bound styling that updates without rebuilding the inline graph, the `ActualThemeChanged` trigger becomes unnecessary. That's not in scope here — match the existing `ShellPage` pattern.)

`IPlaybackService.TrackChanged` is **not** a page subscription. It is owned by `NowPlayingViewModel`, which subscribes in `OnNavigatedTo` and unsubscribes in `OnNavigatedFrom` (i.e., active for the whole time the VM is navigated-to, not literally always on — see "Navigation lifecycle") to drive lyrics fetching and technical-detail recompute (see "Track-change handling" → Algorithm). The page does not need its own `TrackChanged` handler — `Player.NowPlayingMetadataChanged` is `PlayerViewModel`'s post-mapping signal raised at the **end** of its own `OnTrackChanged`, after the derived metadata properties (`TrackTitle`, `Artists`, `FlatArtistText`, `AlbumName`, `AlbumId`, `CoverArtUrl`, `HasStructuredArtists`) have all been updated. Implementing a redundant page-side `TrackChanged` handler would either fight the VM's subscription order or read stale derived properties.

To summarize the always-on subscriptions across both objects:

| Owner | Event | Purpose |
|-------|-------|---------|
| `NowPlayingPage` (code-behind) | `Player.NowPlayingMetadataChanged` | Rebuild artist/album hyperlinks + title + cover-art binding (`RenderNowPlaying()`). |
| `NowPlayingPage` (code-behind) | `ActualThemeChanged` (FrameworkElement) | Re-run `RenderNowPlaying()` so the imperatively-set foreground brushes track theme switches (mirrors the `ShellPage` pattern; see "Theme-change refresh"). |
| `NowPlayingViewModel` | `IPlaybackService.TrackChanged` | Recompute `TechnicalDetail`; run lyrics fetch / cache-hit / null-track algorithm. **Subscribed in `OnNavigatedTo`, unsubscribed in `OnNavigatedFrom`** — "always on while the VM is navigated-to/active," not literally always on. The page's metadata + theme handlers are the only literally-always-on subscriptions (lifetime = page object lifetime). |

The Queue tab's auto-scroll handlers are tab-gated and listed in their own section (Auto-scroll); they're separate from all three of the above.

The Queue tab's auto-scroll **still gates on tab activation** — it's a Queue-tab-only concern. Metadata rendering is page-wide, both tabs share the left column, so it must run regardless of which tab is selected.

### Right column — tabs

A `SelectorBar` at the top of the column with two `SelectorBarItem`s:

- **Lyrics**
- **Queue**

Selection drives a content area below — a `Grid` with two child surfaces, swapped via visibility based on the selected tab. (Reuse the same pattern from `ArtistDetailPage`'s top-songs SelectorBar — code-behind `SelectionChanged` handler flips a ViewModel property.)

Lyrics is the default tab on first navigation. Tab selection is **not persisted** — each navigation to the page starts on Lyrics.

### Sizing

- **Album art** — capped to a sensible max (e.g., 400×400 or 50% of column width, whichever is smaller) so it doesn't dominate on ultrawide windows. Use `MaxWidth`/`MaxHeight` on the `Image` and let the column expand around it.
- **Lyrics scroll viewport** — fills the right column below the SelectorBar, scrollable vertically.
- **Queue list** — same: fills the right column below the SelectorBar, virtualized scroll.

Exact pixel values are an implementation detail to refine during XAML iteration.

### Page caching

`NowPlayingPage` does **not** need `NavigationCacheMode="Required"`. Its state (lyrics text, tab selection) is either re-derivable from the current track or trivially defaulted. A fresh ViewModel per navigation is fine and avoids stale lyrics for an old track.

---

## Technical Line Format

Format: **`Codec · BitDepth/SampleRate · Bitrate`**

The center separator is the interpunct (`·`, U+00B7) to match existing metadata-line conventions in the app.

### Components and missing-data rules

`Suffix` is `string?`. The three numeric fields (`BitRate`, `BitDepth`, `SamplingRate`) are non-null `int`s on `Child` and `SongInfo`; when the server omits them they arrive as `0`, **not** `null`. The formatter must treat any `value <= 0` as "missing" for each numeric component independently — otherwise it would emit visible nonsense like `MP3 · 0 kbps` or `FLAC · 24-bit/0kHz · 0 kbps`.

| Component | Source | Treated as missing when | Display when present |
|-----------|--------|-------------------------|----------------------|
| Codec | `Suffix` | `null` or whitespace | Uppercase (`flac` → `FLAC`) |
| BitDepth | `BitDepth` | `<= 0` | `{n}-bit` (e.g., `24-bit`) |
| SampleRate | `SamplingRate` | `<= 0` | `{kHz}kHz`, divide by 1000, drop trailing zeros (e.g., `96000` → `96kHz`, `44100` → `44.1kHz`) |
| Bitrate | `BitRate` | `<= 0` | `{n} kbps` |

### Composition rules

Three components are emitted in order, with `·` separators between non-empty components:

1. **Codec** — emitted when `Suffix` is non-blank.
2. **BitDepth/SampleRate composite** — emitted only when **both** `BitDepth > 0` and `SamplingRate > 0`. If either is missing (typical for lossy codecs like MP3, AAC, OGG), the entire composite component is omitted. Half-data (e.g., bit-depth without sample rate) is **not** rendered as a partial composite — it's omitted entirely; otherwise we'd produce `FLAC · 24-bit/` or `FLAC · /96kHz`.
3. **Bitrate** — emitted when `BitRate > 0`.

After applying the per-component rules, if **only the codec remains** (no bitrate, no composite), the entire line is hidden — a bare `FLAC` with no quality info is noise. If the codec is missing entirely, the entire line is hidden regardless of what numeric components have values (a bitrate without context isn't meaningful).

| Track | Display |
|-------|---------|
| FLAC, 24-bit, 96 kHz, 4608 kbps | `FLAC · 24-bit/96kHz · 4608 kbps` |
| FLAC, 16-bit, 44.1 kHz, 940 kbps | `FLAC · 16-bit/44.1kHz · 940 kbps` |
| MP3, 320 kbps (no bit depth) | `MP3 · 320 kbps` |
| OGG, 192 kbps | `OGG · 192 kbps` |
| FLAC, 24-bit, 96 kHz, **bitrate=0** | `FLAC · 24-bit/96kHz` (bitrate omitted) |
| FLAC, **24-bit, sampleRate=0**, 4608 kbps | `FLAC · 4608 kbps` (composite omitted because half-data) |
| FLAC, **all numerics 0** | (line hidden — codec alone is noise) |
| **Missing suffix**, 320 kbps | (line hidden — bitrate without codec context isn't meaningful) |
| Missing all fields | (line hidden) |

### Rendering

A small helper (e.g., `TechnicalDetailFormatter.Format(suffix, bitDepth, samplingRate, bitRate)` returning a string or null) keeps the conditional logic out of XAML. The XAML binds to a single computed `TechnicalDetail` string property on the ViewModel and hides the `TextBlock` when null/empty.

---

## Data Plumbing — PlaybackTrack Extension

The technical fields needed for the metadata line are not currently on `PlaybackTrack`. They live on `SongInfo` and are populated from the Subsonic `Child` entity, but the mapping layer drops them when constructing `PlaybackTrack`.

### Required additions to `PlaybackTrack`

| Field | Type | Source on `Child`/`SongInfo` |
|-------|------|------------------------------|
| `Suffix` | `string?` | `Child.Suffix` / `SongInfo.Suffix` |
| `BitRate` | `int` | `Child.BitRate` / `SongInfo.BitRate` |
| `BitDepth` | `int` | `Child.BitDepth` / `SongInfo.BitDepth` |
| `SamplingRate` | `int` | `Child.SamplingRate` / `SongInfo.SamplingRate` |

### Mapper update

Every place that constructs a `PlaybackTrack` must propagate the four new fields. Sites verified at design time:

1. **From `SongInfo`** — the per-VM playback-build helpers. Each VM has its own helper name; `ArtistDetailViewModel`'s helper does not follow the `BuildPlaybackTracks` convention:

   | ViewModel | Helper(s) | Notes |
   |-----------|-----------|-------|
   | `AlbumDetailViewModel` | `BuildPlaybackTracks` | Album track list |
   | `PlaylistDetailViewModel` | `BuildPlaybackTracks` | Playlist track list |
   | `TracksViewModel` | `BuildPlaybackTracks` | All-tracks page |
   | `ArtistDetailViewModel` | `BuildTopSongsPlaybackTracks` ([line 584](../../../Hypersonic/ViewModels/ArtistDetailViewModel.cs)) | **Top-songs** play path on the artist page. The albums-on-artist play path goes through `AlbumDetailViewModel` after navigation, so it's covered by row #1. The top-songs path is the one that's easy to miss because the helper name doesn't follow the same convention. |

   `SongInfo` already carries the four fields (via `SongMapper.MapFromChild`, which already propagates them). Each helper just needs to copy them into the constructed `PlaybackTrack`.

2. **From `SearchSuggestionItem`** (`ShellViewModel.HandleSuggestionChosen`'s search-result play path). This path **does not** go through `SongInfo` — the search performed in `PerformSearchAsync` populates a `SearchSuggestionItem` directly from the `Child` returned by `Search3`, then `HandleSuggestionChosen` builds a `PlaybackTrack` from that item. `SearchSuggestionItem` does **not** currently expose the four technical fields, so they get dropped on this path. Fix:
   - Add `Suffix`, `BitRate`, `BitDepth`, `SamplingRate` to `SearchSuggestionItem` (in `Hypersonic.Core/Models/SearchSuggestionItem.cs`, in the existing "Song-only fields for playback" group).
   - Populate them from each `song` in `PerformSearchAsync`'s song-mapping loop (the `Child` already has them).
   - Copy them into the `PlaybackTrack` in `HandleSuggestionChosen`.

There's no compile-time enforcement of completeness here — the mapper sites are a grep-and-fix task. **Re-grep `new PlaybackTrack` across the solution before declaring done** — additional construction sites may have been added since this design was written. A short helper (e.g., `PlaybackTrackBuilder.FromSongInfo(SongInfo)`) would centralize the `SongInfo` path and reduce future drift, but is not strictly required for v1.

The existing `SongMapper` already propagates these four fields from `Child` to `SongInfo` (see [Hypersonic/Helpers/SongMapper.cs:32-35](../../../Hypersonic/Helpers/SongMapper.cs)) — no change needed there. Verify this during implementation.

### Backward compatibility

`PlaybackTrack` is also used to hydrate the SMTC display, `MediaPlaybackItem` properties, and (potentially) persisted queue state. Adding optional fields is safe — existing consumers ignore them. Confirm there's no JSON deserialization of `PlaybackTrack` for persistence in v1 (queue persistence is deferred per roadmap 1.2); if it's added later, the new fields must default cleanly.

---

## Data Plumbing — Playback Service Extension

### Why a new API

The existing `IPlaybackService.JumpToTrack(string trackId)` looks up the target by ID via `PlaybackQueue.JumpToTrack`'s `FindIndex(t => t.Id == trackId)`. This is **incorrect for queues containing the same track twice** — clicking the second occurrence jumps to the first. It is also misaligned with the **shuffled** view: `Player.QueueItems` is built from `GetEffectiveQueue()` (the shuffled order), but `JumpToTrack` searches the underlying queue, so even unique tracks may behave inconsistently with what the user clicked.

The now-playing page's queue tab makes this bug user-visible because it explicitly claims positional identity for highlighting (a duplicate track shows only the playing instance highlighted) — clicking the non-highlighted instance and watching the highlighted one start playing would be jarring.

### New API

Add a position-aware jump method that operates on the effective (shuffled-or-not) queue. The public service surface is `void`; the internal queue-layer method returns a result so the service can distinguish a successful mutation from a no-op:

```csharp
// IPlaybackService (public surface)
void JumpToQueueIndex(int effectiveIndex);

// PlaybackQueue (internal layer)
public PlaybackTrack? JumpToQueueIndex(int effectiveIndex);   // returns the new current track, or null on no-op
```

Semantics: jump to the track at `effectiveIndex` within `GetEffectiveQueue()`. No-op if the index is out of range (`< 0` or `>= GetEffectiveQueue().Count`) or already current.

`PlaybackQueue.JumpToQueueIndex` translates the effective index to the underlying queue index via the shuffle map (the existing internal mapping used by `GetEffectiveQueue`), pushes the outgoing track to history, updates `_currentIndex`, **and — when shuffle is enabled — also sets `_shufflePosition = effectiveIndex`** so the queue's two-part shuffled cursor stays consistent. It then **returns the new `CurrentTrack`** on success. **Returns `null`** for both no-op cases (out-of-range, already-current). `PlaybackQueue` itself remains event-free.

The shuffle-cursor update is mandatory, not optional. Shuffled playback state in `PlaybackQueue` is two-part: `EffectiveIndex` returns `_shufflePosition` when shuffle is on (`PlaybackQueue.cs:38-39`), and `AdvanceInternal` advances by incrementing `_shufflePosition` and reading `_shuffleOrder[_shufflePosition]` (`PlaybackQueue.cs:523-533`). The existing `JumpToTrack` already maintains this invariant by recomputing `_shufflePosition = _shuffleOrder.IndexOf(_currentIndex)` after the jump (`PlaybackQueue.cs:448-454`). If `JumpToQueueIndex` only updates `_currentIndex`, three things break in shuffled mode: (a) the correct track starts playing, but `EffectiveIndex` still returns the **old** `_shufflePosition`, so `Player.EffectiveQueueIndex` (and hence the queue-tab highlight, the auto-scroll target, and any UI consumer of the position) points at the wrong row; (b) the next `NextTrack`/`AdvanceInternal` increments from the stale `_shufflePosition` and lands on the track *after the previous position*, not after the jumped-to one — playback order becomes incoherent; (c) `JumpToTrack`'s by-ID path and the new by-index path produce divergent post-state for the same target track, defeating the whole point of mirroring `JumpToTrack`'s pattern.

The implementation does **not** need to recompute via `_shuffleOrder.IndexOf(_currentIndex)` the way `JumpToTrack` does, because `JumpToQueueIndex` already received the effective index as input — `_shufflePosition = effectiveIndex` is the direct assignment. (The translation back to `_currentIndex` is `_shuffleOrder[effectiveIndex]`.) Order of operations: validate `effectiveIndex` against `GetEffectiveQueue().Count` first, then push history, then assign `_currentIndex` (via the shuffle map when shuffle is on) and `_shufflePosition` together, then return `CurrentTrack`.

`PlaybackService.JumpToQueueIndex` calls into the queue and **only drives playback when the queue actually moved** — i.e., when the queue layer returned non-null. This mirrors the existing `JumpToTrack` pattern verbatim ([PlaybackService.cs:330-344](../../../Hypersonic/Services/PlaybackService.cs)):

```csharp
public void JumpToQueueIndex(int effectiveIndex)
{
    var prevHasNext = HasNext;
    var prevHasPrevious = HasPrevious;
    var result = _playbackQueue.JumpToQueueIndex(effectiveIndex);
    if (result == null)
    {
        _log.Debug("JumpToQueueIndex ignored", new { EffectiveIndex = effectiveIndex });
        return;
    }

    _log.Info("Jumped to queue index", new { EffectiveIndex = effectiveIndex });
    PlayCurrent();
    FirePlaybackStateChangedIfHasChanged(prevHasNext, prevHasPrevious);
}
```

Why the explicit return-value gate matters: if `PlaybackQueue.JumpToQueueIndex` were a `void` mutation helper, `PlaybackService` would have no way to distinguish a successful jump from a no-op. Calling `PlayCurrent()` after a no-op restarts the same track, re-emits `TrackChanged`, and re-enters the `MediaPlayer.Source` reload path — visible to the user as a glitchy mid-playback restart, and visible to subscribers (lyrics fetch, technical-detail recompute, queue auto-scroll) as a spurious track-change cascade. The return-value gate keeps no-op semantics honest end-to-end.

The public `IPlaybackService.JumpToQueueIndex(int)` stays `void` because callers (the player-bar flyout glue, `PlayerViewModel.JumpToQueueIndex(QueueItem)`) don't need the result — only the service's internal forwarding cares. This matches `IPlaybackService.JumpToTrack(string)`'s public `void` signature even though `PlaybackQueue.JumpToTrack` returns `PlaybackTrack?`.

### `PlayerViewModel` wrapper

Replace `JumpToQueueTrack(QueueItem item)` with `JumpToQueueIndex(QueueItem item)` (or add the new one and migrate callers). Implementation:

```csharp
public void JumpToQueueIndex(QueueItem item)
{
    var index = IndexOfQueueItem(item);
    if (index >= 0)
    {
        _playbackService.JumpToQueueIndex(index);
    }
}

private int IndexOfQueueItem(QueueItem item)
{
    var queue = QueueItems;
    for (var i = 0; i < queue.Count; i++)
    {
        if (ReferenceEquals(queue[i], item))
        {
            return i;
        }
    }
    return -1;
}
```

`QueueItems` is exposed as `IReadOnlyList<QueueItem>` (which has no `IndexOf`), so the lookup is a manual loop using `ReferenceEquals`. Reference-equality is the correct comparison: rows in the bound `ListView` carry the exact `QueueItem` instances from the collection as their `DataContext`, so the same instance comes back from a tap-handler `args.OriginalSource → DataContext` cast. This also preserves the duplicate-row guarantee — two `QueueItem` instances with the same `Track.Id` are not reference-equal and resolve to their own positions.

The public `QueueItems` type is intentionally **not** widened — keeping it `IReadOnlyList<QueueItem>` preserves the read-only public surface of `PlayerViewModel`.

### Side effect: pre-existing flyout bug fixed

The player-bar flyout's `QueueList_DoubleTapped` currently calls the old ID-based `JumpToQueueTrack`. Migrating both call sites (flyout + new now-playing queue) to `JumpToQueueIndex` fixes the duplicate-track jump bug for the flyout as a side effect. This is in scope for this work.

### Backward compatibility: keeping `JumpToTrack` while migrating queue callers

`JumpToTrack(string)` may still have legitimate non-queue-display callers (e.g., search-result play, deep links by track ID). Keep `JumpToTrack` available for those; only the queue-display call sites switch to `JumpToQueueIndex`. Audit during implementation — if no callers remain, deprecate or remove `JumpToTrack` separately (out of scope for this work).

### Known limitation: Previous after a duplicate-track jump

The forward jump is now position-aware, but the **Previous** button is not. `PlaybackQueue._navigationHistory` is a `Stack<string>` of track IDs, and `GoBack()` resolves via `_tracks.FindIndex(t => t.Id == previousTrackId)`. After jumping to position 0 of a duplicate-track queue (current was position 3), Previous pops the previous track ID and finds its **first** occurrence by ID — which may not be the position the user actually came from.

**Decision for v1: document and defer.** The forward case (the user-visible click on a queue row) is fixed, which addresses the visible jarring behavior the spec called out. The Previous case is a strictly less common path (the user has to first reach a duplicate via a position-aware jump *and then* press Previous) and fixing it touches the navigation-history representation across `PlaybackQueue` plus a queue-mutation fallback strategy (e.g., (occurrenceIndex, trackId) tuples with FindIndex fallback when the queue has been mutated). That's a meaningful refactor and is out of scope for this work.

Tracked as **roadmap item 4.4 Position-Aware Navigation History** (see [`ROADMAP.md`](../../../ROADMAP.md)). The roadmap entry covers the representation change, the queue-mutation fallback, and the duplicate-Previous tests.

The now-playing-page work does **not** add tests for the duplicate-Previous case, since the v1 behavior is intentionally the existing ID-based fallback.

---

## Lyrics

### Fetching

On track change (and on initial page load with a track playing), the VM first checks the per-VM lyrics cache (see "Caching" below) and **only on cache miss** calls `SubsonicClient.GetLyricsBySongIdAsync(trackId)`. The response is a `LyricsList` with an array of `StructuredLyrics` entries. Cache hits — including a definitive no-lyrics hit (cached `null`/blank) — short-circuit before the structured call and so do not touch the network. The full per-call decision (cache lookup → structured → fallback → cache-write) is laid out in "Track-change handling" → Algorithm; this subsection covers only the structured-call shape.

### Rendering

For v1 (no sync):

1. From `LyricsList.StructuredLyrics`, pick the **first usable** entry (see "Usable entry" definition below) — not necessarily the first array element.
2. Flatten its `Line[]` to plain text by joining each `LyricLine.Value` with newlines, **skipping null/blank values**. The `Synced` flag and `Start` offsets are ignored in v1.
3. Render the plain text in a `TextBlock` (or `ScrollViewer` containing a `TextBlock`) with center horizontal alignment, generous line spacing, and a comfortable max-width for readability (e.g., 600px max).

### "Usable entry" definition

`StructuredLyrics.Line` is nullable, and each `LyricLine.Value` is nullable. A non-empty `StructuredLyrics` array can therefore contain entries whose `Line` is null, whose `Line` is empty, or whose `Line` items all have null/whitespace `Value`s — none of which produce visible lyrics. To avoid rendering a blank surface and suppressing the legacy fallback, define:

> A `StructuredLyrics` entry is **usable** when its `Line` array is non-null **and** at least one `LyricLine` has a non-null `Value` that is not whitespace-only (`!string.IsNullOrWhiteSpace(line.Value)`).

The structured path returns "success with content" only when at least one usable entry exists in `LyricsList.StructuredLyrics`. The chosen entry is the first usable one in array order. Any other shape — null `LyricsList`, empty `StructuredLyrics`, or all-non-usable entries — is treated identically to "structured success but empty" and triggers the legacy fallback.

### Fallback algorithm

The structured endpoint (`getLyricsBySongId`) is OpenSubsonic-only — non-OpenSubsonic servers can either reject it with a Subsonic `Status="failed"` + `Error` response, or (less commonly) return a transport/HTTP error. The legacy `getLyrics` endpoint is the universal fallback. The implementation must distinguish three structured-call outcomes so it doesn't skip the legacy fallback in the unsupported-endpoint path:

1. **Structured success with at least one usable entry** (per the "Usable entry" definition above) → use the first usable entry. Done.
2. **Structured success with no usable content** (null `LyricsList`, empty `StructuredLyrics`, all entries non-usable) → call legacy `GetLyricsAsync(artist, title)`. If `Lyrics.Value` is non-blank, use it; otherwise `NoLyrics`.
3. **Structured response indicates failure** — either `SubsonicResponse.Status == "failed"` (with `Error` populated; e.g. codes 30 "incompatible client" / 20 "protocol mismatch" indicating the endpoint isn't available, or code 70 "data not found" indicating the server has no lyrics for this track) **or** the call throws (network, deserialization, etc.) → call legacy `GetLyricsAsync(artist, title)`. Same content/empty/failure handling as #2 — **except** the cache write is gated on whether the failure is definitive, see "Cache-write rule" below. Per the classification table there, codes 20/30 classify as **definitive (unsupported)** and code 70 classifies as **definitive (empty)**; both still trigger the legacy fallback, but they differ in what they tell the cache layer.

### Cache-write rule (definitive vs. transient)

A subtle but important distinction: when **both** structured and legacy paths end without content, the page lands on `NoLyrics` for that track. Whether to **cache** that `null`/blank result depends on **why** both paths failed.

#### Per-call classification

Each call's outcome is classified as **definitive**, **transient**, or **content**:

| Outcome | Classification |
|---------|----------------|
| HTTP success + `Status="ok"` + non-blank lyrics | **content** |
| HTTP success + `Status="ok"` + empty `LyricsList` / all-non-usable entries / blank `Lyrics.Value` | **definitive (empty)** — server says "no lyrics for this track" |
| HTTP success + `Status="failed"` with code 30 "incompatible client" or 20 "protocol mismatch" | **definitive (unsupported)** — endpoint exists but this server can't service it |
| HTTP success + `Status="failed"` with code 70 "data not found" | **definitive (empty)** — server says "I don't have this" |
| HTTP success + `Status="failed"` with any other code (e.g. 0 generic, 10 required parameter missing) | **transient** — could be server-side glitch or recoverable error |
| HTTP failure / network throw / deserialization throw / timeout | **transient** — might succeed next time |

#### Cache-write rule

**Cache `null` for a track ID only when *both* the structured path AND the legacy path ended definitively without content** — i.e., structured was definitive (empty or unsupported) **and** legacy was also definitive (empty or unsupported). The structured path is the authoritative source — legacy is a compatibility fallback. Structured being definitive is necessary but **not** sufficient: a transient legacy outcome (network failure, deserialization throw, `Status="failed"` with a non-definitive code) means the legacy path might recover and return content on a future fetch, so caching `null` would suppress that recovery for the lifetime of the page VM. If structured was transient, we don't have a trustworthy answer about whether the track has structured lyrics, regardless of what legacy returned, so we likewise refuse to cache.

In short: cache `null` requires *both sides definitive*; **any** transient outcome on **either** side blocks the cache write.

**Legacy is called whenever structured is not content.** The only short-circuit is `structured == content` — every other structured outcome (definitive empty, definitive unsupported, transient) attempts the legacy fallback before resolving the display state. This matches the fallback algorithm in the section above (outcomes #2 and #3 both "call legacy `GetLyricsAsync`"); skipping legacy on a structured-empty result would lose lyrics on servers where `getLyricsBySongId` returns empty/no-data while legacy `getLyrics(artist, title)` has plain text.

| Structured outcome | Legacy outcome | Display | Cache write? |
|--------------------|----------------|---------|--------------|
| content | (not called — short-circuit) | Loaded | cache the structured lyrics |
| definitive (empty) | content | Loaded | cache the legacy lyrics (legacy supplied content the structured side missed) |
| definitive (empty) | definitive (empty) | NoLyrics | cache `null` (both sides agree) |
| definitive (empty) | definitive (unsupported) | NoLyrics | cache `null` (structured was definitively empty; legacy can't service this server, so neither path will return content) |
| definitive (empty) | transient | NoLyrics | **no cache** (legacy might recover and return content) |
| definitive (unsupported) | content | Loaded | cache the legacy lyrics |
| definitive (unsupported) | definitive (empty) | NoLyrics | cache `null` (both sides agree) |
| definitive (unsupported) | definitive (unsupported) | NoLyrics | cache `null` (no path will ever return content for this server config) |
| definitive (unsupported) | transient | NoLyrics | **no cache** (legacy might recover) |
| transient | content | Loaded | **no cache** (we don't know what structured would have returned) |
| transient | definitive (empty) | NoLyrics | **no cache** (the preferred endpoint is the source of truth and we don't have its answer) |
| transient | definitive (unsupported) | NoLyrics | **no cache** (structured was transient; legacy unsupported tells us nothing about structured) |
| transient | transient | NoLyrics | **no cache** (both paths might recover) |

The "transient + definitive(empty)" row is the case the prior wording got wrong: a successful empty legacy response only tells us legacy had no content, not that structured wouldn't have returned content if it hadn't thrown. Caching `null` in that case would make a temporary `getLyricsBySongId` outage sticky until the page VM is recreated. The rule of thumb: **never cache `null` when the structured call's result is unknown.**

#### Cache-hit read path

The read path remains unchanged (cached `null`/blank still maps to `LyricsLoadState = NoLyrics`). The cache stores only the *lyrics text* (or `null` for definitive no-lyrics); transient failures leave no entry in the cache, so the next track-change to the same ID re-issues both calls.

In short: any structured-call outcome that is not "structured success with at least one usable entry" attempts the legacy fallback before giving up. Only if **both** paths fail or return empty/non-usable do we land in `NoLyrics`. All structured-call exceptions are caught locally so the legacy attempt isn't skipped; both paths' failures are logged at Warning. The legacy path's status check is identical to the structured path's, and its `Lyrics.Value` is treated as "no content" when null/whitespace.

### States

| State | Rendering |
|-------|-----------|
| NoTrack (placeholder; current track is null) | Empty surface (no `ProgressRing`, no "No lyrics available" message). Used when the page is visible but `IPlaybackService.NowPlaying == null` — see the "No current track" / "Queue cleared" rows in Edge Cases. |
| Loading | Centered `ProgressRing` |
| Loaded (has lyrics, structured or plain) | Plain text, center-aligned, vertically scrollable |
| NoLyrics (both endpoints empty/failed/threw for an actual track) | Centered subtle text: "No lyrics available" |

`NoTrack` and `NoLyrics` are distinct states so the placeholder rendering for "no current track" doesn't display the misleading "No lyrics available" message — that message is reserved for the case where the user is looking at a real track whose lyrics are genuinely unavailable. The track-change algorithm picks `NoTrack` for the null-track branch and `NoLyrics` only for the non-null-track branch.

### Caching

Cache the last-fetched lyrics keyed by track ID in the ViewModel. Cache lookup happens on **every** non-null track change (see Track-change handling) — switching away from and back to the same track must not re-fetch, and tab switches (Lyrics ↔ Queue) likewise must not re-fetch. The cached value is `string?`: a non-blank string is the lyrics text; `null`/blank means "definitive no-lyrics for this track ID, also worth caching to avoid re-fetching".

Track change does **not** invalidate the cache. What track change *does* invalidate is the in-flight fetch generation (`_lyricsGeneration`, see Stale-fetch guard) — any pending fetch from the previous track is discarded on return. Cache entries themselves only leave the dictionary via capacity-based eviction.

A trivial in-memory dictionary scoped to the ViewModel lifetime is sufficient — no cross-page or cross-session caching for v1. Bound the cache to e.g. last 10 tracks to avoid unbounded growth during long listening sessions; LRU eviction is fine but a simple FIFO ring is also acceptable at this scale.

### Stale-fetch guard

Like the all-tracks page, lyric fetches need a stale-result guard. If the user skips through several tracks rapidly, the response for an earlier track must not overwrite the displayed lyrics for the current track. A `_lyricsGeneration` counter incremented on each track change, captured before the fetch, and compared after the await accomplishes this. On mismatch, discard the response.

---

## Queue Tab

### Data binding

Bind a `ListView`'s `ItemsSource` directly to `Player.QueueItems` (the same collection used by the player-bar flyout). The two views stay in sync by construction — there is no second projection of the queue.

### Item template

A new, richer `DataTemplate` (vs. the flyout's compact 40-px row), targeting `QueueItem`. Columns:

| Column | Width | Content |
|--------|-------|---------|
| Cover art | ~48px | Small thumbnail from `Track.CoverArtUri`, with the same placeholder fallback |
| Title + artist | `*` | Two stacked `TextBlock`s: title (bold when `IsCurrent`) and `Track.FlatArtistText` (secondary color) |
| Duration | ~60px | `Track.Duration` formatted via `SecondsToTimeConverter` |

The current row is highlighted via `IsCurrent` (existing converter `BoolToHighlightBrushConverter` already used by the flyout). Title font weight switches via `BoolToFontWeightConverter` (also existing).

### Highlight semantics

The queue uses **positional identity** — `QueueItem.IsCurrent` reflects which queue *position* is currently playing, not which track ID. This is intentionally distinct from `NowPlayingTracker`'s track-ID-based highlight used in browse pages: a queue can contain the same track twice and only the actually-playing row should highlight. `PlayerViewModel`'s queue refresh logic must maintain this correctly across queue mutations (rebuild, shuffle toggle, reorder, replacement); see "Refresh contract" below for the precise rule the rebuild must obey.

### Refresh contract

`PlayerViewModel.RefreshQueue` exists to keep `QueueItems` in lockstep with `IPlaybackService.GetEffectiveQueue()` after every queue or track change. It also has an in-place fast path that preserves `QueueItem` instances when the queue hasn't really changed, so the bound `ListView` (and its scroll position, virtualization, and animations) doesn't get reset on every track advance. The fast path matters for the always-on player-bar flyout binding, the new now-playing queue tab, and the auto-scroll coordinator that depends on rows being stable across non-mutating refreshes.

The contract: **the visible row at index `i` must always represent the same effective-queue occurrence as `GetEffectiveQueue()[i]`.** "Same occurrence" is stronger than "same `Track.Id`" — `QueueItem.Track` is `init`, so once the wrapper exists, its underlying `PlaybackTrack` reference is frozen for the life of that wrapper. If `RefreshQueue` reuses an existing `QueueItem` whose frozen `PlaybackTrack` is not the same object as the new effective-queue entry at that position, the row's title/artist/cover-art/duration/technical fields can silently diverge from what the playback service believes is queued there.

`Track.Id`-equality at each position is **insufficient** to prove sameness. Two scenarios break the ID-only check:

1. **Same-ID new-instance replacement.** A queue mutation (resort, dedupe, refresh-from-server, future "reload metadata" action) that rebuilds `_queue` with fresh `PlaybackTrack` instances produced from updated `SongInfo` data. Old instances and new instances share `Track.Id`s and positions; ID-equality says "no change," so the fast path runs and the displayed metadata stays stuck on the old instance values until the next genuine length change. Tracks whose metadata changed server-side (re-tagged, normalized, bitrate updated) never refresh their visible row.
2. **Duplicate same-ID reorder/swap.** Two queue entries with the same `Track.Id` but distinct `PlaybackTrack` instances (e.g. an album track present twice with different `QueueSource` annotations, or a future per-occurrence playback flag) get swapped or reordered while length is preserved. ID-equality at each position still passes, the fast path runs, and the rows' frozen `Track` references no longer match the occurrences at those positions. The current-row highlight will still be correct (it's set by position), but any per-instance `Track` data the row displays comes from the wrong occurrence.

`RefreshQueue` must therefore short-circuit to in-place `IsCurrent` updates **only** when, for every position `i`, `ReferenceEquals(existing[i].Track, effective[i])`. Reference equality on the `PlaybackTrack` instance proves the row already wraps the exact occurrence the playback service has at that position — there is nothing the wrapper could be displaying differently. Any failure of reference equality (instance replacement at any position, swap of two same-ID instances, length change, ID change) forces a full rebuild: a new `List<QueueItem>` populated from `effective`, assigned to `QueueItems`, with `IsCurrent` set positionally. The cost of a rebuild is proportional to queue length (allocation of N small wrappers and one `INotifyPropertyChanged` cycle on `QueueItems`); the cost of a stale row is silent UI corruption.

(An "occurrence token" — a per-`QueueItem` opaque integer assigned at construction time and stored alongside `Track` — would also satisfy the contract, but would require `PlaybackQueue` to surface its own per-occurrence identity through `IPlaybackService.GetEffectiveQueue()`. Reference equality on the existing `PlaybackTrack` is strictly weaker but sufficient for every queue mutation `PlaybackService` performs today: shuffle toggle, `AddNext`/`AddToEnd`, `RemoveAt`, `MoveTrack`, `ClearUpcoming`, `ClearAll` — none of these synthesize new `PlaybackTrack` instances for unchanged entries, so reference equality holds whenever the effective sequence is genuinely unchanged at that position. Adding the occurrence token is a future option if `PlaybackQueue` ever starts cloning instances.)

### Interaction

**Double-tap a row** to jump to that track. The handler calls `Player.JumpToQueueIndex(item)`, which translates the row's position in `QueueItems` to an effective queue index and calls the new `IPlaybackService.JumpToQueueIndex(int)` API (see Data Plumbing — Playback Service Extension). This guarantees the double-tapped row plays even when the queue contains duplicate track IDs.

Single click does **not** trigger jump — the gesture is double-tap throughout v1, matching the existing player-bar flyout's `QueueList_DoubleTapped` handler. The flyout itself is migrated to the new `JumpToQueueIndex` API as part of this work, but its gesture stays the same; both queue surfaces use double-tap consistently.

No drag-and-drop, no context menu, no per-row remove for v1.

### Empty queue

When `Player.IsQueueEmpty` is true (which on this page implies no current track), show the same "Queue is empty" centered text as the flyout. This state is **not reachable on initial navigation** — the player-bar entry points (art/title tap) are no-ops when `IPlaybackService.NowPlaying` is null, so the page only opens with a non-empty queue. It **is** reachable defensively if playback is cleared **while the page is already open** — e.g. the queue is cleared from elsewhere via a future "clear queue" UI, or playback is otherwise torn down (see the "Queue cleared / playback torn down while page is open" row in Edge Cases, plus manual smoke test #22 which exercises this path with the Queue tab open). The binding must handle it without throwing — the lyrics surface goes to `LyricsLoadState = NoTrack` (silent placeholder, no "No lyrics available" message) and the queue surface shows the "Queue is empty" centered text. Auto-scroll is gated on `effectiveIndex >= 0` so the empty-queue case silently no-ops there too (covered by `QueueAutoScrollCoordinator` and `QueueScrollResolver bounds` test rows).

### Auto-scroll

When the queue tab is selected, **immediately** scroll the current row into view (required, not best-effort). On any subsequent change to the queue contents OR to the current track while the queue tab is active, bring the current row into view. Implementation: code-behind handler on `SelectionChanged` for the SelectorBar tab, plus subscriptions to **both** `Player.NowPlayingMetadataChanged` (track changes) **and** `IPlaybackService.QueueChanged` (queue mutations) while the queue tab is active. Use `ListView.ScrollIntoView`. The handler must marshal to the UI thread.

#### Tab-gated subscription lifecycle (idempotent attach/detach)

The two Queue-tab subscriptions are attached on entering the Queue tab and detached on leaving it (or on `Unloaded`). To avoid duplicate registrations across rapid Lyrics → Queue → Lyrics → Queue toggles, route the attach/detach through a small helper — `Hypersonic/Helpers/QueueTabSubscriptionGate.cs` — that owns a single `_subscribed` flag and gates **both** subscriptions as one atomic unit. A per-event API (e.g. `Attach(eventSource, handler)`) is **not** acceptable here: the production code needs both subscriptions attached together or detached together. A single gate flag with two independently-callable attach methods would let the first event attach and the second skip on a partial-attach race, or push implementers back toward duplicating gate state in the page.

The helper takes both event-source bindings at construction time and exposes a single `Attach()` / `Detach()` pair:

```csharp
// Hypersonic/Helpers/QueueTabSubscriptionGate.cs
public sealed class QueueTabSubscriptionGate
{
    private readonly Action _attachAll;
    private readonly Action _detachAll;
    private bool _subscribed;

    public QueueTabSubscriptionGate(Action attachAll, Action detachAll)
    {
        _attachAll = attachAll;
        _detachAll = detachAll;
    }

    internal bool IsSubscribed => _subscribed;   // for direct test assertion; behavioral checks are sufficient

    public void Attach()
    {
        if (_subscribed) return;   // idempotent — multiple Queue activations don't stack handlers
        _subscribed = true;
        _attachAll();              // attaches BOTH subscriptions as one unit
    }

    public void Detach()
    {
        if (!_subscribed) return;  // idempotent — Unloaded after a Lyrics-tab exit is a no-op
        _subscribed = false;
        _detachAll();              // detaches BOTH subscriptions as one unit
    }
}
```

The page constructs one gate instance in its own constructor, wiring both `+=` calls into `attachAll` and both `-=` calls into `detachAll`:

```csharp
// In NowPlayingPage.xaml.cs constructor (after _playbackService is resolved):
_queueTabGate = new QueueTabSubscriptionGate(
    attachAll: () =>
    {
        Player.NowPlayingMetadataChanged += OnQueueTabMetadataChanged;
        _playbackService.QueueChanged += OnQueueTabQueueChanged;
    },
    detachAll: () =>
    {
        Player.NowPlayingMetadataChanged -= OnQueueTabMetadataChanged;
        _playbackService.QueueChanged -= OnQueueTabQueueChanged;
    });
```

Page-side wiring becomes thin forwarders — no `_queueTabSubscribed` field on the page, no `AttachQueueTabHandlers()` / `DetachQueueTabHandlers()` page methods. The two handler method names (`OnQueueTabMetadataChanged`, `OnQueueTabQueueChanged`) live on the page (they need access to `_pageDispatcher`, `_pageActive`, `_scrollCoordinator`, `Player`); only the gate flag and the attach/detach pairing live in the helper.

Wire the gate from three call sites:

1. **`SelectionChanged` handler on the SelectorBar** — call `_queueTabGate.Attach()` when the new selection is the Queue tab and `_queueTabGate.Detach()` when leaving it.
2. **`Unloaded`** — call `_queueTabGate.Detach()` unconditionally, **regardless of which tab is currently selected**. The idempotent guard makes this safe in all three navigate-away scenarios: (a) the user is on the **Lyrics tab after a prior Queue visit** (no-op — the gate was already detached by the `SelectionChanged` that ran when the user switched back to Lyrics); (b) the user is on the **Lyrics tab without ever opening Queue** (no-op — the gate was never attached); (c) the user is on the **Queue tab** (the live attach gets detached). Only case (c) does real work; (a) and (b) are protected by the gate flag.
3. (No constructor `Attach()` — Lyrics is the default tab and the subscriptions don't exist until the user opens Queue.)

The two handler method names (`OnQueueTabMetadataChanged`, `OnQueueTabQueueChanged`) are intentionally distinct from the always-on `OnPlayerMetadataChanged` (step 6) so `+= … -= …` mismatch-by-name bugs are obvious. Each handler invokes `_scrollCoordinator.OnQueueRefreshed(Player.EffectiveQueueIndex)` (with the `_pageDispatcher.TryEnqueue` + `_pageActive` guard wrapper, per "Captured dispatcher" / "Page-active guard for enqueued callbacks").

`Unloaded` MUST also detach **both** always-on subscriptions from step 6 — the `Player.NowPlayingMetadataChanged` metadata-render handler and the `ActualThemeChanged` theme-refresh handler — and reset the coordinator's active flag. Set `_pageActive = false` first, then call `_queueTabGate.Detach()`, then **reset `_scrollCoordinator` (e.g. `_scrollCoordinator.OnTabSelected(NowPlayingTab.Lyrics, -1)` or `_scrollCoordinator.Reset()`)**, then `-=` both always-on handlers (`Player.NowPlayingMetadataChanged` and `ActualThemeChanged`). `PlayerViewModel` and `IPlaybackService` are singletons, so missed teardowns leak the page instance, enqueue scrolls against an invisible `ListView`, and stack duplicate handlers across navigations. The exact teardown sequence lives in Implementation Sequence step 8. Page-side lifecycle uses the `Unloaded` event (not `INavigationAware.OnNavigatedFrom`) — see "Page-active guard for enqueued callbacks" for the rationale.

The "immediate scroll on tab activation" rule matters because the page lives across track changes — opening the Queue tab on a 50-track queue mid-listen must land on the playing row, not at index 0. Without the immediate scroll, the row only becomes visible at the next track change.

#### Why two subscriptions

Track changes and queue mutations are distinct signals in this architecture:

- **`Player.NowPlayingMetadataChanged`** is raised at the end of `PlayerViewModel.OnTrackChanged → RefreshQueue()` ([PlayerViewModel.cs:371-372](../../../Hypersonic/ViewModels/PlayerViewModel.cs)) — i.e., after `QueueItems` and `IsCurrent` have been refreshed for the new track.
- **`IPlaybackService.QueueChanged`** is raised by `PlaybackService` on every queue mutation: shuffle toggle ([PlaybackService.cs:56](../../../Hypersonic/Services/PlaybackService.cs)), `AddNext`, `AddToEnd`, `RemoveAt`, `MoveTrack`, `ClearUpcoming`, `ClearAll`, and `PlayQueue`. `PlayerViewModel.OnQueueChanged` ([PlayerViewModel.cs:375-378](../../../Hypersonic/ViewModels/PlayerViewModel.cs)) calls `RefreshQueue()` but **does not** raise `NowPlayingMetadataChanged`. So a `QueueChanged`-driven refresh is invisible to `NowPlayingMetadataChanged` subscribers.

The user-visible failure case: with the Queue tab open and a long queue playing somewhere mid-list, the user toggles shuffle from the player bar. `ShuffleEnabled.set` raises `QueueChanged`; `RefreshQueue()` rebuilds `QueueItems` in the new effective order; the current row's position changes (in the unshuffled→shuffled direction it moves to effective index 0; in the shuffled→unshuffled direction it moves back to its underlying index). Without a `QueueChanged` subscription, the list rebuilds and `ListView` resets scroll to top, leaving the current row offscreen until the next track change.

The `IPlaybackService.QueueChanged` handler invokes `coordinator.OnQueueRefreshed(Player.EffectiveQueueIndex)` after the refresh has propagated. The coordinator's existing gating (queue-tab-active AND `effectiveIndex >= 0`) covers the no-op cases. The same `OnQueueRefreshed` method serves both event sources — no new method needed on the coordinator.

> **Note on `PlayerViewModel.RefreshQueue` ordering**: the `QueueChanged` event from `IPlaybackService` reaches `PlayerViewModel`'s subscriber and the page's subscriber in subscription order. `PlayerViewModel` subscribes during singleton construction (app startup), and the page's `_playbackService.QueueChanged` subscription is added later — on **Queue-tab activation** in the SelectorBar `SelectionChanged` handler, **not** in the page constructor (it's tab-gated; see Implementation Sequence step 8). Because the page's subscription is added strictly after `PlayerViewModel`'s, `PlayerViewModel.OnQueueChanged → RefreshQueue()` runs first. By the time the page handler reads `Player.EffectiveQueueIndex`, it reflects the post-refresh state. This mirrors the `NowPlayingMetadataChanged` ordering rationale.

`NowPlayingViewModel`'s **own** subscriptions (lyrics fetching, technical-detail recompute) continue to use `IPlaybackService.TrackChanged` directly — those care about the raw playback event, not the player-bar refresh ordering, and they don't depend on `Player.QueueItems` state.

#### Testability

The non-trivial logic in auto-scroll is *which* index to scroll to and *whether* to scroll at all (only when queue tab is active, only when `EffectiveQueueIndex >= 0`). The `ListView.ScrollIntoView` call itself is a thin glue line. To make the decision logic testable without instantiating `NowPlayingPage` or a real `ListView` (which would hit the `[UITestMethod]` host crash), extract a small coordinator:

```csharp
public sealed class QueueAutoScrollCoordinator
{
    private readonly Action<int> _scrollIntoView;
    private bool _isQueueTabActive;

    public QueueAutoScrollCoordinator(Action<int> scrollIntoView) { _scrollIntoView = scrollIntoView; }

    // Called by the page when the SelectorBar selection changes.
    // currentEffectiveIndex must be the live Player.EffectiveQueueIndex.
    public void OnTabSelected(NowPlayingTab tab, int currentEffectiveIndex)
    {
        _isQueueTabActive = tab == NowPlayingTab.Queue;
        if (_isQueueTabActive && currentEffectiveIndex >= 0)
        {
            _scrollIntoView(currentEffectiveIndex);
        }
    }

    // Called by the page on each Player.NowPlayingMetadataChanged
    // AND each IPlaybackService.QueueChanged. Both event sources funnel
    // through this method so queue mutations like shuffle toggles
    // also re-scroll the current row into view (see "Why two
    // subscriptions" above).
    public void OnQueueRefreshed(int effectiveIndex)
    {
        if (_isQueueTabActive && effectiveIndex >= 0)
        {
            _scrollIntoView(effectiveIndex);
        }
    }
}
```

The initial scroll is **required**, not optional. `OnTabSelected` takes the live `Player.EffectiveQueueIndex` and invokes `_scrollIntoView` immediately when transitioning into the Queue tab with a valid current index. The page passes `Player.EffectiveQueueIndex` at the call site; no other coordination needed.

#### Layout-timing wrinkle for the initial scroll

`SelectorBar.SelectionChanged` may fire **before** the Queue surface (which was previously hidden/collapsed) has been measured and its `ListView` items realized. Calling `ScrollIntoView` on an unmeasured `ListView` is at best a no-op and at worst races subsequent layout.

The page-side glue should defer the initial scroll past the next layout pass when the Queue tab transitions from inactive → active. Two acceptable patterns; pick the simpler one that works:

1. **`DispatcherQueue.TryEnqueue`** — wrap the `ScrollQueueToIndex(currentEffectiveIndex)` call in `_pageDispatcher.TryEnqueue(...)` (see "Captured dispatcher" below). Default priority is sufficient: a one-tick dispatcher deferral is enough to let the SelectorBar's layout pass complete before the scroll runs. (`DispatcherQueuePriority.Low` would also work and is slightly more conservative, but in practice the default-priority overload that the snippets use suffices and is what `TryEnqueue(Action)` resolves to.)
2. **One-shot `Loaded`/`SizeChanged` handler on the queue surface** — if `TryEnqueue` proves insufficient (e.g. virtualization needs more than one tick), latch on the next `SizeChanged` of the `ListView` after `Visibility` flips, scroll once, then unsubscribe.

Subsequent scrolls (track change, queue mutation while the tab is already active) don't need the deferral — the surface is already laid out — **but** they still need to marshal through the captured dispatcher because they're triggered by `IPlaybackService.QueueChanged` / `Player.NowPlayingMetadataChanged`, which may originate off the UI thread.

#### Captured dispatcher

`NowPlayingPage` captures its UI-thread `DispatcherQueue` once at construction, after `InitializeComponent()` but before any subscription is wired (full constructor shape with DI resolution and subscriptions in "Always-on metadata render"):

```csharp
private DispatcherQueue _pageDispatcher = null!;

public NowPlayingPage()
{
    ViewModel = App.GetService<NowPlayingViewModel>();
    Player = App.GetService<PlayerViewModel>();
    _playbackService = App.GetService<IPlaybackService>();
    InitializeComponent();
    _pageDispatcher = DispatcherQueue.GetForCurrentThread();
    // … subscriptions, _pageActive = true …
}
```

All dispatcher marshaling done by the page — the deferred initial scroll, the auto-scroll handler invoked from `IPlaybackService.QueueChanged`, the always-on metadata-render handler invoked from `Player.NowPlayingMetadataChanged` if the originating event arrives off the UI thread — must use `_pageDispatcher.TryEnqueue(...)`, **not** `DispatcherQueue.GetForCurrentThread().TryEnqueue(...)`.

The reason: `GetForCurrentThread()` returns the dispatcher of *whatever thread is currently executing*. On the UI thread that's correct; on a `MediaPlayer` callback thread or a `Task.Run` continuation that's `null` (or a wrong dispatcher), and the marshal call silently no-ops or throws. Capturing once during page construction guarantees the correct dispatcher regardless of which thread the event source uses.

#### Page-active guard for enqueued callbacks

`_pageDispatcher.TryEnqueue` is fire-and-forget — the callback runs whenever the UI thread services it. A callback enqueued shortly before `Unloaded` will still run **after** the page is detached, on a `ListView` / `RichTextBlock` that's no longer in the visual tree. Detaching the source event handler doesn't help because the enqueue has already happened.

> **Why `Unloaded`, not `OnNavigatedFrom`** — `INavigationService` only invokes `OnNavigatedFrom` on `frame.GetPageViewModel()` ([NavigationService.cs:74-79, 95-103](../../../Hypersonic/Services/NavigationService.cs)), which returns the page's `ViewModel` property — **not the `Page` itself**. Page code-behind named `OnNavigatedFrom` is dead code. Existing pages use the constructor + `Unloaded` event instead; mirror that.

Mitigation: a single `_pageActive` `bool` field on the page, set to `true` in the page constructor (after `InitializeComponent()`) and to `false` (**before** detaching event handlers) in the `Unloaded` handler. Every body enqueued via `_pageDispatcher.TryEnqueue` checks `_pageActive` first and returns if false:

```csharp
// Queue-tab scroll adapter (revised from "Wiring sketch" in step 8):
private void MarshalAndScroll(int effectiveIndex)
{
    _pageDispatcher.TryEnqueue(() =>
    {
        if (!_pageActive) return;
        ScrollQueueToIndex(effectiveIndex);
    });
}

// Always-on metadata handler (defined in Always-on metadata render):
private void OnPlayerMetadataChanged(object? sender, EventArgs e)
{
    _pageDispatcher.TryEnqueue(() =>
    {
        if (!_pageActive) return;
        RenderNowPlaying();
    });
}
```

Why set the flag **before** the `-=` calls: there's a small window between the flag clear and the detach where an event fired by the singleton service could already have entered the handler and called `TryEnqueue`. Setting `_pageActive = false` first guarantees that pending callback is a no-op when it runs.

This pattern only protects the `TryEnqueue`-marshaled paths. Direct UI-thread call sites (the initial `RenderNowPlaying()` in the constructor, the `ActualThemeChanged` handler) don't need the guard — `Unloaded`'s `-=` is sufficient because they execute synchronously from a UI-thread event source.

`NowPlayingViewModel` follows the same dispatcher-capture pattern (it already captures its dispatcher in the public constructor — see "Dispatcher / test seam" — and uses the captured reference in track-change handling). The VM's lifecycle uses `INavigationAware.OnNavigatedTo` / `OnNavigatedFrom` because the navigation service **does** invoke those on the VM (it's the page's `ViewModel` property). Page-side and VM-side lifecycles are deliberately split: page → constructor + `Unloaded`, VM → `INavigationAware`.

This is a `NowPlayingPage.xaml.cs` glue concern only; the coordinator's contract (immediate invocation of the `Action<int>` on tab activation) is unchanged. The deferral lives in the `Action<int>` callback the page passes to the coordinator.

Smoke test #20 already exercises the path; if it ends up timing-flaky in practice, escalate to pattern #2.

The page code-behind owns the coordinator, wires `SelectionChanged → OnTabSelected`, subscribes to **both** `Player.NowPlayingMetadataChanged` and `IPlaybackService.QueueChanged` while the queue tab is active and calls `OnQueueRefreshed(Player.EffectiveQueueIndex)` from each. The bound-checked scroll work is split into a pure `internal static` resolver (testable from `Hypersonic.Tests` without `ShellPage` or `ListView`) and a thin instance method that calls `ScrollIntoView` with the resolved item:

```csharp
// Hypersonic/Helpers/QueueScrollResolver.cs — pure helper, no UI types.
internal static class QueueScrollResolver
{
    /// <summary>
    /// Returns the item to scroll to, or null when the index is out of range.
    /// Mirrors the existing flyout guard (ShellPage.xaml.cs:272-275): the
    /// coordinator gates on effectiveIndex >= 0, but the index can race a
    /// queue rebuild (shuffle toggle, ClearAll, etc.) and become stale by
    /// the time this runs on the UI thread. Out-of-range → null → no-op.
    /// </summary>
    public static QueueItem? ResolveScrollTarget(IReadOnlyList<QueueItem> items, int effectiveIndex)
    {
        if (effectiveIndex < 0 || effectiveIndex >= items.Count)
        {
            return null;
        }
        return items[effectiveIndex];
    }
}

// NowPlayingPage.xaml.cs — thin glue. The Queue tab's ListView and the
// auto-scroll lifecycle live on NowPlayingPage; ShellPage only owns the
// existing player-bar flyout. (The flyout could later reuse
// QueueScrollResolver if its inline guard is consolidated, but that's
// not part of this work.)
private void ScrollQueueToIndex(int effectiveIndex)
{
    var target = QueueScrollResolver.ResolveScrollTarget(Player.QueueItems, effectiveIndex);
    if (target is null)
    {
        return;
    }
    QueueListView.ScrollIntoView(target, ScrollIntoViewAlignment.Leading);
}
```

The bounds-check testability requirement is satisfied by `QueueScrollResolver.ResolveScrollTarget` itself: tests target the resolver directly with a stub `IReadOnlyList<QueueItem>` (see the `QueueScrollResolver bounds` test row). The page-side `ScrollQueueToIndex` is a four-line glue that maps null → no-op and non-null → `ListView.ScrollIntoView`; it does **not** need to be unit-test-visible (no `internal` exposure required) and is covered by manual smoke #20–22.

Two reasons the resolver returns the item from `Player.QueueItems` rather than letting the page index `QueueListView.Items[i]` directly:

1. **Bind to the same collection the coordinator gated on.** `Player.QueueItems` is the source of truth; `ListView.Items` is a reflected view that can momentarily lag (or briefly throw `ArgumentOutOfRangeException`) during rebuilds. Resolving the item from `Player.QueueItems` and passing it to `ScrollIntoView` keeps the call self-consistent.
2. **Match the existing flyout pattern.** `QueueList_DoubleTapped`'s sibling auto-scroll already uses `Player.EffectiveQueueIndex >= 0 && Player.EffectiveQueueIndex < Player.QueueItems.Count` ([ShellPage.xaml.cs:272-275](../../../Hypersonic/Views/ShellPage.xaml.cs)) and indexes `Player.QueueItems`. Same pattern, same defensive guard, same scroll-alignment value (`Leading`).

The `Action<int>` (the page-side `ScrollQueueToIndex` glue) is the only piece that touches the UI; everything else — the coordinator's gating logic and the resolver's bounds check — is plain logic and is unit-testable in `Hypersonic.Tests` without `[UITestMethod]`.

This is intentionally minimal — no abstraction over the `ListView` itself, no DI registration, no service injection. The resolver is a single static method; the coordinator is one class with two methods; the page glue is four lines. The split exists purely so the parts that can be tested are tested.

---

## ViewModel

`NowPlayingViewModel` extends `ObservableRecipient`, implements `INavigationAware`.

### Dependencies

- `IPlaybackService` — current track via `NowPlaying`, track-change subscription via `TrackChanged`, source for the technical line
- `ISubsonicClientService` — lyrics fetching
- `ILoggingService` — logging fetch failures

`NowPlayingViewModel` does **not** depend on `PlayerViewModel`. The queue surface (`QueueItems`, `EffectiveQueueIndex`, `JumpToQueueIndex`, `NowPlayingMetadataChanged`) is consumed by the **page**, which holds its own `Player` property resolved separately in the page constructor (see "Page activation contract" below). Derived metadata bindings (`TrackTitle`, `Artists`, `FlatArtistText`, `AlbumName`, `CoverArtUrl`, `HasStructuredArtists`) are read off `Player` through the page's XAML bindings — the VM never touches them. Keeping the VM independent of `PlayerViewModel` matches the boundary stated in "Always-on metadata render" (the page owns metadata rendering and queue binding; the VM owns lyrics + technical-detail) and avoids the circular ownership shape the boundary exists to prevent.

**Page activation contract**: `NowPlayingPage` is reached through `INavigationService.NavigateTo(typeof(NowPlayingViewModel).FullName!)`, which maps the VM key to the page type and calls `Frame.Navigate(pageType, parameter)` ([NavigationService.cs:95](../../../Hypersonic/Services/NavigationService.cs)). That activation path requires a **public parameterless constructor** on the page; it does **not** use the page's DI registration or constructor injection. Constructor injection is reserved for `ShellPage`-style pages resolved directly from `App.GetService<ShellPage>()`. The navigation lifecycle then reflects the page's `ViewModel` property to find the VM ([FrameExtensions.cs:7](../../../Hypersonic/Helpers/FrameExtensions.cs), [NavigationService.cs:123](../../../Hypersonic/Services/NavigationService.cs)) and invokes `INavigationAware` on it.

**Why the page is still registered in `App.xaml.cs` despite the activation path not using DI**: the repo registers every page type as transient in the DI container ([App.xaml.cs:111-130](../../../Hypersonic/App.xaml.cs)) as a project-wide convention — see the existing entries for `MainPage`, `AlbumsPage`, `AlbumDetailPage`, etc. The registration is **not** consumed by `Frame.Navigate(pageType, parameter)` (which uses `Activator.CreateInstance` on the parameterless constructor), but it keeps the registration list complete and uniform across pages, and is available for any future code that resolves a page directly via `App.GetService<TPage>()` (the `ShellPage` pattern). Skipping the registration would make `NowPlayingPage` the only page missing from `App.xaml.cs`'s page-registration block — a confusing inconsistency with no upside. So Navigation Registration step 1 and the Files-to-Modify entry for `App.xaml.cs` keep the page registration; just don't expect the activation path to call into it.

Required `NowPlayingPage` constructor shape (mirrors `MainPage.xaml.cs:20-29`):

```csharp
public NowPlayingViewModel ViewModel { get; }
public PlayerViewModel Player { get; }

private readonly IPlaybackService _playbackService;
private DispatcherQueue _pageDispatcher = null!;
private bool _pageActive;

// Queue-tab plumbing — declared here so the `Unloaded` lambda can reference both;
// constructed in the constructor body before `Unloaded` is wired (full construction
// — `attachAll`/`detachAll` lambdas, `MarshalAndScroll` glue — lives in step 8).
private readonly QueueTabSubscriptionGate _queueTabGate;
private readonly QueueAutoScrollCoordinator _scrollCoordinator;

public NowPlayingPage()
{
    // Resolve dependencies BEFORE InitializeComponent — XAML bindings will
    // read ViewModel/Player during component initialization.
    ViewModel = App.GetService<NowPlayingViewModel>();
    Player = App.GetService<PlayerViewModel>();
    _playbackService = App.GetService<IPlaybackService>();

    InitializeComponent();

    _pageDispatcher = DispatcherQueue.GetForCurrentThread();
    _pageActive = true;

    // Subscriptions detailed in "Always-on metadata render" and Implementation Sequence step 8.
    Player.NowPlayingMetadataChanged += OnPlayerMetadataChanged;
    ActualThemeChanged += OnActualThemeChanged;
    RenderNowPlaying();

    // Queue-tab plumbing — full lambda bodies in step 8. Both fields MUST be assigned
    // before the `Unloaded` lambda below, which references both.
    _scrollCoordinator = new QueueAutoScrollCoordinator(MarshalAndScroll);
    _queueTabGate = new QueueTabSubscriptionGate(
        attachAll: () => { /* see step 8 */ },
        detachAll: () => { /* see step 8 */ });

    // _playbackService.QueueChanged and a second Player.NowPlayingMetadataChanged
    // are wired by step 8's queue-tab logic via the QueueTabSubscriptionGate
    // helper — Attach() on Queue tab activation, Detach() on leaving the Queue
    // tab AND on Unloaded.

    Unloaded += (_, _) =>
    {
        // Order matters — see Auto-scroll → "Mandatory `Unloaded` cleanup" and step 8.
        _pageActive = false;                                  // (1) BEFORE detach so any in-flight enqueued callback sees false
        _queueTabGate.Detach();                               // (2) idempotent — handles Queue-tab-active, Lyrics-after-Queue-visit, Lyrics-never-Queue
        _scrollCoordinator.OnTabSelected(NowPlayingTab.Lyrics, -1);  // (3) reset coordinator's active flag (or call _scrollCoordinator.Reset() if added)
        Player.NowPlayingMetadataChanged -= OnPlayerMetadataChanged; // (4a) detach always-on metadata-render handler
        ActualThemeChanged -= OnActualThemeChanged;           // (4b) detach always-on theme-refresh handler
    };
}
```

Why these three specifically:

- **`ViewModel`** is required because the navigation service's reflection finds it by name and runs `OnNavigatedTo` on the instance — without the property assignment, `NowPlayingViewModel.OnNavigatedTo` never fires, so lyrics fetching / technical-detail / VM-side subscriptions are dead.
- **`Player`** is required because the page's metadata-render subscription, queue binding, and queue-tab auto-scroll all read `Player.QueueItems` / `Player.EffectiveQueueIndex` / `Player.NowPlayingMetadataChanged`.
- **`_playbackService`** (page-private field, not a public property) is required because the queue-tab auto-scroll subscribes to `IPlaybackService.QueueChanged` — that event covers the shuffle-toggle / `AddNext` / `RemoveAt` / `MoveTrack` / `ClearAll` / `PlayQueue` cases, none of which raise `Player.NowPlayingMetadataChanged` (`PlayerViewModel.OnQueueChanged` calls `RefreshQueue()` without raising the metadata event — see "Why two subscriptions" under Auto-scroll). Subscribing on `Player.NowPlayingMetadataChanged` alone would lose all queue-mutation auto-scroll, which is what shuffle toggling on a long Queue tab depends on.

The page must **not** subscribe to `IPlaybackService.TrackChanged` — that subscription is owned by `NowPlayingViewModel` for lyrics + technical-detail recompute, and the page's metadata render is driven by `Player.NowPlayingMetadataChanged` instead (see "Always-on metadata render" → responsibility table for the boundary). The page-private `_playbackService` field exists **solely** to wire the queue-tab `QueueChanged` subscription.

All three resolve via `App.GetService<T>()`. The DI container has `NowPlayingViewModel` registered as transient (each navigation builds a fresh VM, matching the design's "no `NavigationCacheMode="Required"` / fresh ViewModel per navigation" decision), `PlayerViewModel` as the existing singleton, and `IPlaybackService` (concretely `PlaybackService`) as the existing singleton. The page binds the queue list directly to that singleton `Player`. The new ViewModel does **not** wrap or re-expose queue state. The new ViewModel **does** subscribe to `IPlaybackService.TrackChanged` for its own concerns (lyrics fetching, technical-detail recompute) — `NowPlayingMetadataChanged` on `PlayerViewModel` would also work, but subscribing to the service event directly avoids ordering coupling with `PlayerViewModel`'s update sequence.

### Owned state

- `SelectedTab` — `Lyrics` or `Queue` enum, defaults to `Lyrics`
- `LyricsText` — current lyrics string, null/empty when none. Always updated **together with** `LyricsLoadState`.
- `LyricsLoadState` — `NoTrack` / `Loading` / `Loaded` / `NoLyrics`. Authoritative for what the UI renders; `LyricsText` alone is not sufficient (see Track-change handling). `NoTrack` is the placeholder state used when `IPlaybackService.NowPlaying == null` (silent empty surface); `NoLyrics` is reserved for an actual track whose lyrics are genuinely unavailable (renders the visible "No lyrics available" message).
- `TechnicalDetail` — formatted codec/quality string, computed from `IPlaybackService.NowPlaying`. Empty/null when no track.
- `_lyricsCache` — `Dictionary<string, string?>` keyed by track ID. **The cached value is `string?`**: a non-null/non-blank string means "lyrics found, this is the text"; a `null` or blank cached value means "this track has no lyrics" (a definitive negative result, also worth caching to avoid re-fetching).
- `_lyricsGeneration` — long counter for stale-fetch guard.

### Track-change handling

Subscribe to `IPlaybackService.TrackChanged` in `OnNavigatedTo`, unsubscribe in `OnNavigatedFrom`. On track change, marshal to the UI thread (the playback service may fire events from a non-UI thread) and run.

#### Active-VM guard for dispatcher-enqueued callbacks

`NowPlayingViewModel` has the same fire-and-forget race the page does (see "Page-active guard for enqueued callbacks" in Auto-scroll → Captured dispatcher). A `TrackChanged` event marshaled via `_vmDispatcher.TryEnqueue(...)` shortly before `OnNavigatedFrom` will still run on the UI thread **after** the VM has been navigated away from. The existing `_lyricsGeneration` guard does **not** protect this case: `OnNavigatedFrom` increments the generation, but the late-arriving callback then increments it *again* (per algorithm step 2), starting a fresh fetch with a captured generation that is no longer stale relative to the now-bumped current generation. The fetch completes against a detached VM and stomps on `LyricsText` / `LyricsLoadState`.

Mitigation mirrors the page-side pattern (and the `NowPlayingTracker._isActive` pattern at [NowPlayingTracker.cs:38-76, 108-117](../../../Hypersonic/Helpers/NowPlayingTracker.cs)): a single `_vmActive` `bool` field, set `true` in `OnNavigatedTo` and set `false` (**before** any other teardown work) in `OnNavigatedFrom`. Every track-change body — both the synchronous test path and the dispatcher-enqueued production path — checks `_vmActive` first and returns immediately if false:

```csharp
private bool _vmActive;
private readonly Action<Action> _enqueue;   // production: adapter wrapping _vmDispatcher.TryEnqueue; tests: capturing fake

// Production constructor (excerpt):
//   _vmDispatcher = DispatcherQueue.GetForCurrentThread();
//   _enqueue = action => _vmDispatcher.TryEnqueue(() => action());
//
// The lambda is required because DispatcherQueue.TryEnqueue takes a
// DispatcherQueueHandler delegate (not Action) and returns bool. The
// production adapter intentionally discards the bool — TryEnqueue only
// returns false when the dispatcher has been shut down, which doesn't
// occur during a NowPlayingPage's lifetime; if it ever does, the
// enqueued body would never run and the page would already be in an
// unrecoverable state. If the failure signal becomes interesting later,
// change the seam to Func<Action, bool> and have callers act on the result.

public void OnNavigatedTo(object parameter)
{
    _vmActive = true;
    _playbackService.TrackChanged += OnTrackChanged;
    HandleTrackChange();   // initial population — already on the navigation thread, runs now
}

public void OnNavigatedFrom()
{
    _vmActive = false;                                  // FIRST, so any in-flight enqueued callback short-circuits
    _playbackService.TrackChanged -= OnTrackChanged;
    Interlocked.Increment(ref _lyricsGeneration);       // invalidate any future stale return
}

private void OnTrackChanged(object? sender, EventArgs e)
{
    if (_synchronous)
    {
        if (!_vmActive) return;
        HandleTrackChange();
    }
    else
    {
        _enqueue(() =>   // production: _vmDispatcher.TryEnqueue; test: capture
        {
            if (!_vmActive) return;
            HandleTrackChange();
        });
    }
}
```

The `_vmActive` check inside the enqueued lambda is the critical line — without it, a callback already in flight when `OnNavigatedFrom` runs would proceed past the now-`false` flag and execute `HandleTrackChange()` (which increments `_lyricsGeneration` and kicks off a fresh fetch). The synchronous test path needs the same guard so tests that drive `TrackChanged` after calling `OnNavigatedFrom` see the same behavior.

Routing the enqueue through an injectable `Action<Action>` (defaulting in production to a `_vmDispatcher.TryEnqueue`-wrapping adapter — see the inline note in the sketch above; `Action<Action>` is not directly assignable from `DispatcherQueue.TryEnqueue` because the latter is `bool TryEnqueue(DispatcherQueueHandler)`) is what makes the dispatcher-enqueued race testable: a synchronous-mode test cannot exercise it because `OnNavigatedFrom` already `-=`'d the handler before the test would raise the next `TrackChanged`. With the `enqueueOverride` seam, a test can capture the lambda body in step 1, call `OnNavigatedFrom` in step 2, then invoke the captured lambda in step 3 — simulating the "callback queued before navigation, runs after" production race deterministically. See "VM-active guard for late `TrackChanged`" in the test rows.

The fetch path additionally double-checks `_vmActive` after the await returns — the network round-trip can outlive a navigation that was already processing while the request was in flight. On `_vmActive == false`, discard the response without touching state, regardless of generation match.

`_lyricsGeneration` is **still required** alongside `_vmActive`: it handles same-session staleness (rapid track skipping while the page stays open), which `_vmActive` alone doesn't cover. The two guards are orthogonal — `_vmActive` is a navigation-lifetime guard, `_lyricsGeneration` is an in-session staleness guard.

#### Dispatcher / test seam

`NowPlayingViewModel` mirrors the testability pattern already established by `NowPlayingTracker` ([NowPlayingTracker.cs:13, 28-35, 110-117](../../../Hypersonic/Helpers/NowPlayingTracker.cs)):

- **Public constructor**: takes the standard service dependencies (`IPlaybackService`, `ISubsonicClientService`, `ILoggingService` — no `PlayerViewModel`, per "Dependencies" above). Captures `DispatcherQueue.GetForCurrentThread()` as `_vmDispatcher` and uses `TryEnqueue` to marshal `TrackChanged` callbacks to the UI thread.
- **`internal` constructor**: takes the same dependencies plus three test hooks:
  - `bool synchronous` — when `true`, callbacks run inline (no dispatcher capture, no `TryEnqueue`) so tests can drive `TrackChanged` directly and assert post-call state without a real UI thread.
  - `Action<Action>? enqueueOverride` — when non-null, replaces the production `_enqueue` adapter (`action => _vmDispatcher.TryEnqueue(() => action())`) with the test-supplied delegate. A capturing test fake stores the body in a list/queue and runs it on demand later. This is the only way to exercise the **dispatcher-enqueued race** (event arrives, body is enqueued, navigation-away happens, body runs after) — see the "VM-active guard for late `TrackChanged`" test row.
  - `Func<long, Task<…>>? fetchOverride` (or equivalent test seam on the lyrics fetch) — lets tests inject a `TaskCompletionSource`-controlled fetch so the post-await `_vmActive` check can be exercised independently.
- **Awaitable pending-fetch hook**: lyrics fetches are kicked off as fire-and-forget `_ = LoadLyricsAsync(generation)` in production, but the implementation also stores the in-flight `Task` (or `Task[]`) on an `internal` field exposed only to tests, so the test can `await _vm.PendingLyricsFetch` after raising `TrackChanged` and then assert on `LyricsText` / `LyricsLoadState` deterministically. This avoids polling/sleeping for fire-and-forget races in the *non-race* tests (the synchronous mode + capturing-enqueue mode are what exercise the *race* tests).

The synchronous + awaitable-fetch + capturing-enqueue combination is what the prior-round test rows (stale-fetch, cache, null-track, fallback, **VM-active guard**) actually need to run deterministically. Tests in `Hypersonic.Tests` already use the synchronous-seam half of this for `NowPlayingTracker` ([NowPlayingTrackerTests.cs](../../../Hypersonic.Tests/Helpers/NowPlayingTrackerTests.cs)) and `IncrementalSongCollection` (synchronous core seam) — same shape, the capturing `Action<Action>` is the new addition.

The internal hooks are **internal**, not public — they exist to satisfy `[InternalsVisibleTo("Hypersonic.Tests")]`, not to be part of the production VM's API surface.

#### Algorithm

`HandleTrackChange()` runs on the UI thread (or inline in synchronous test mode), and only after the `_vmActive` guard above has passed:

1. **Read `IPlaybackService.NowPlaying` once into a local.** All branches below operate on this snapshot.

2. **Increment `_lyricsGeneration`.** Invalidates any in-flight fetch from the prior track (also covers no-track → some-track and some-track → no-track transitions).

3. **Null-track branch** (the page receives a `TrackChanged` after the queue is cleared / playback torn down):
   - `TechnicalDetail = null` (or empty).
   - `LyricsText = null`.
   - `LyricsLoadState = NoTrack`.
   - **Return** without fetching. The page renders its placeholder/empty state (per the `NoTrack` row in Lyrics → States). `NoTrack` — not `NoLyrics` — so the placeholder does not show the misleading "No lyrics available" message reserved for an actual track with no lyrics.

4. **Non-null track branch:**
   - Recompute `TechnicalDetail` from the new track.
   - **Cache hit** (key present in `_lyricsCache`):
     - Read `var cached = _lyricsCache[trackId]`.
     - `LyricsText = cached`.
     - `LyricsLoadState = string.IsNullOrWhiteSpace(cached) ? NoLyrics : Loaded`.
     - **Both** properties must be set on every cache hit. A previous track's `LyricsLoadState` (e.g. `Loaded`) must not leak into a cached-`null` hit, and a previous `NoLyrics` must not be displayed alongside cached non-blank text.
     - Return without fetching.
   - **Cache miss:**
     - `LyricsLoadState = Loading` immediately, `LyricsText = null`.
     - Fire-and-forget the fetch with the captured generation. On return, check **`_vmActive` AND** the generation; if either is invalid, discard. Otherwise:
       - Run the fallback algorithm (see Lyrics → Fallback algorithm). The legacy call is invoked whenever the structured outcome is **not** content — definitive empty included. Only `structured == content` short-circuits past legacy.
       - Cache the resolved text under the track ID **only** per the cache-write matrix in "Cache-write rule" (Lyrics section): cache `null` only when **both** the structured path AND the legacy path ended definitively (each side either success-empty or `Status="failed"` with a definitive code 20/30/70). If **either** side was transient (threw, or `Status="failed"` with a non-definitive code), **no cache write** — a successful empty legacy result only tells us legacy had no content, and a transient structured result tells us nothing about the canonical answer. The next track-change to this ID retries both calls.
       - Set `LyricsText` and `LyricsLoadState` together (`Loaded` for non-blank text, `NoLyrics` otherwise).

### Navigation lifecycle

- `OnNavigatedTo`: set `_vmActive = true`, subscribe to `TrackChanged`, perform initial state population from the current track (if any), kick off lyrics fetch if needed.
- `OnNavigatedFrom`: set `_vmActive = false` **first** (before any other teardown), then unsubscribe from `TrackChanged`, then increment `_lyricsGeneration` to invalidate any in-flight fetch. Setting the flag first ensures any callback already enqueued via `_vmDispatcher.TryEnqueue` will short-circuit when it runs, before it can re-bump the generation and start a fresh fetch.

---

## Navigation Registration

Reduced 3-step registration (no sidebar nav item — this page is reached via player-bar click only):

1. **App.xaml.cs** — register `NowPlayingViewModel` (transient) and `NowPlayingPage` (transient)
2. **PageService** — add `Configure<NowPlayingViewModel, NowPlayingPage>()`
3. **No sidebar nav entry** — the page is not exposed in `NavigationView`. Tap handlers on the player bar art and title are the sole entry points. **No NavigationView item** is added to `ShellPage.xaml`, and no `Resources.resw` label is added for a sidebar entry. (The player-bar tap handlers and hover cursor on the art and title **are** added to `ShellPage.xaml` / `ShellPage.xaml.cs` — see "Files to Modify" — those are entry-point glue, not nav-registration changes.)

### Why no `TopLevelPages` registration

The three `TopLevelPages` `HashSet`s in `ShellViewModel`, `DefaultActivationHandler`, and `ConnectViewModel` are not back-stack management — they are an allow-list for **persisting and restoring `LastPage`** across app launches. `ShellViewModel.OnNavigated` writes the current page to `AppState.Navigation.LastPage` only when its name is in the set; `DefaultActivationHandler` and `ConnectViewModel` restore that page on startup/login only when its name is in their sets.

Adding `NowPlayingViewModel` to those sets would cause the now-playing page to be restored on app startup or after login — including when there is no current track, which would render a blank/placeholder page as the user's entry point. That is not desired.

It also would not solve back-stack duplication. Back-stack clearing only happens through `INavigationService.NavigateTo(..., clearNavigation: true)`, independent of `TopLevelPages`. Duplicate-now-playing prevention is handled entirely by `NowPlayingTapPolicy` and the tap-handler glue (see Entry points), in two parts: (1) no-op when the current page is already `NowPlayingPage` (covers repeated taps while the page is visible); (2) prune any existing `NowPlayingPage` entries from `Frame.BackStack` before navigating (covers the sidebar-routed case, e.g. `MainPage → NowPlayingPage → AlbumsPage → tap`). Both parts are required — implementing only #1 reintroduces the duplicate-back-stack bug.

---

## Edge Cases

| Scenario | Behavior |
|----------|----------|
| No current track | Player bar tap handlers no-op (don't navigate). If somehow reached, page renders placeholder state — empty lyrics surface (`LyricsLoadState = NoTrack`, no "No lyrics available" message), empty queue, no metadata. |
| Track changes while page is open | All state updates: art, metadata, technical line, lyrics fetch (or cache hit), queue highlight. |
| Queue cleared / playback torn down while page is open | `TrackChanged` fires with `NowPlaying == null`. Page goes to placeholder state: `TechnicalDetail` and `LyricsText` cleared, `LyricsLoadState = NoTrack` (not `NoLyrics` — there is no track to have lyrics for), no lyrics fetch. Subsequent playback resume follows normal track-change flow. |
| Lyrics endpoint returns empty | Falls back to legacy `getLyrics`; if also empty, "No lyrics available". |
| Structured response has non-empty array but no usable lines | Treated as "no content" — falls back to legacy `getLyrics`; does not render a blank surface. |
| Structured response: first entry non-usable, later entry usable | Skips non-usable entries and renders the first usable one; no fallback. |
| Lyrics endpoint throws | Falls back to legacy `getLyrics`; if legacy also fails/empty, "No lyrics available". Logged at Warning. |
| Server doesn't support `getLyricsBySongId` | Subsonic `Status="failed"` with an unsupported-endpoint error code triggers the legacy fallback (same path as the empty/throw outcomes). If legacy also fails, show "No lyrics available". |
| Rapid track skipping | Stale-fetch guard via `_lyricsGeneration` discards old responses. |
| Track has no codec/bitrate metadata | Technical line is hidden (`null`/empty `TechnicalDetail`). |
| User navigates away during lyrics fetch | `OnNavigatedFrom` increments generation; in-flight response is discarded on return. |
| Same track appears twice in queue | Only the actually-playing row highlights (positional identity via `QueueItem.IsCurrent`). |
| Queue empty | Centered "Queue is empty" text — not reachable on initial navigation (player-bar entry points no-op when there's no track), but reachable defensively if playback is cleared while the page is already open (see the "Queue cleared / playback torn down" row above and manual smoke #22). |
| User toggles tabs rapidly | No effect — tab switch is local UI state; lyrics are cached, queue is always live. |
| Window resized very narrow | Two-column layout becomes cramped. v1 does not adapt; minimum window width is the existing app's minimum. Adaptive layout is deferred. |

---

## Files to Create

| File | Purpose |
|------|---------|
| `Hypersonic/ViewModels/NowPlayingViewModel.cs` | Page ViewModel (lyrics + tab selection + technical detail) |
| `Hypersonic/Views/NowPlayingPage.xaml` + `.xaml.cs` | Page view |
| `Hypersonic/Helpers/TechnicalDetailFormatter.cs` | Pure formatter for the codec/quality line |
| `Hypersonic/Helpers/QueueAutoScrollCoordinator.cs` | Pure coordinator for the queue tab's auto-scroll decision logic; takes `Action<int>` so the `ListView.ScrollIntoView` glue is the only piece in code-behind |
| `Hypersonic/Helpers/QueueScrollResolver.cs` | Pure `internal static` resolver: given `IReadOnlyList<QueueItem>` and an `effectiveIndex`, returns the target `QueueItem` or `null` (out-of-range no-op). Lifts the bounds check out of the page code-behind so it can be unit-tested without instantiating `ShellPage` or `ListView`. Mirrors the existing flyout's defensive guard. |
| `Hypersonic/Helpers/LastPagePersistencePolicy.cs` | Pure static helper: `GetPersistableViewModelKey(Type? sourcePageType)` returns the VM key string when the page should be persisted to `AppState.Navigation.LastPage`, or `null` when it should be skipped (`NowPlayingPage`, null, unknown types). Lifts the persistence decision out of `ShellViewModel.OnNavigated` so the no-restore-as-`LastPage` write-side contract has a unit-test target. |
| `Hypersonic/Helpers/NowPlayingTapPolicy.cs` | Pure helper holding the player-bar tap decision rules (when to navigate; which back-stack entries to prune). Returns a `NowPlayingTapDecision` record. `ShellPage` is thin glue around it. |
| `Hypersonic/Helpers/QueueTabSubscriptionGate.cs` | Pure helper that owns a single `_subscribed` flag and gates the two Queue-tab subscriptions (`Player.NowPlayingMetadataChanged` and `IPlaybackService.QueueChanged`) as one atomic unit. Constructed once with `attachAll` / `detachAll` callbacks that wrap both `+=` calls / both `-=` calls; exposes a single `Attach()` / `Detach()` pair (no per-event API — a per-event API would let one subscription attach and the second skip on a partial-attach race). The page code-behind constructs one instance in its constructor and forwards from `SelectionChanged` / `Unloaded`. Unit-testable in `Hypersonic.Tests` against fake event sources without instantiating `NowPlayingPage` or `ListView`. |

## Files to Modify

| File | Change |
|------|--------|
| `Hypersonic.Core/Models/PlaybackTrack.cs` | Add `Suffix`, `BitRate`, `BitDepth`, `SamplingRate` |
| `Hypersonic.Core/Contracts/Services/IPlaybackService.cs` | Add `JumpToQueueIndex(int effectiveIndex)` |
| `Hypersonic.Tests/Fakes/FakePlaybackService.cs` | Implement the new `JumpToQueueIndex(int)` member (throw-by-default; capturing field used by the new `PlayerViewModel.JumpToQueueIndex` tests). |
| `Hypersonic.Tests/ViewModels/TracksViewModelTests.cs` | Inline `private sealed class FakePlaybackService : IPlaybackService` — add `JumpToQueueIndex(int)` (throw or no-op). |
| `Hypersonic.Tests/ViewModels/PlayerViewModelMetadataTests.cs` | Inline `private sealed class FakePlaybackService : IPlaybackService` — add `JumpToQueueIndex(int)`. |
| `Hypersonic.Tests/ViewModels/PlayerViewModelToggleTests.cs` | Inline `private sealed class FakePlaybackService : IPlaybackService` — add `JumpToQueueIndex(int)`. |
| `Hypersonic.Tests/ViewModels/PlaybackTrackPropagationTests.cs` | Inline `private sealed class FakePlaybackService : IPlaybackService` — add `JumpToQueueIndex(int)`. |
| `Hypersonic.Tests/ViewModels/ShellViewModelSearchPlayTests.cs` | Inline `private sealed class CapturingPlaybackService : IPlaybackService` — add `JumpToQueueIndex(int)`. |
| `Hypersonic.Tests/ViewModels/ArtistDetailViewModelTopSongsTests.cs` | Inline `private sealed class NullPlaybackService : IPlaybackService` — add `JumpToQueueIndex(int)`. |
| `Hypersonic.Tests/ViewModels/ArtistDetailViewModelAlbumMappingTests.cs` | Inline `private sealed class NullPlaybackService : IPlaybackService` — add `JumpToQueueIndex(int)`. |
| `Hypersonic.Core/Services/PlaybackQueue.cs` | Implement `public PlaybackTrack? JumpToQueueIndex(int effectiveIndex)`. Returns the new `CurrentTrack` on a successful jump or `null` on no-op (out-of-range OR already-current). Push outgoing track to history before mutating `_currentIndex`, mirroring `JumpToTrack`. Implement effective-index → underlying-index translation via the existing shuffle map. **When shuffle is enabled, also set `_shufflePosition = effectiveIndex`** after the validation passes — the queue's two-part shuffled cursor (`_currentIndex` + `_shufflePosition`) must stay consistent so `EffectiveIndex` returns `effectiveIndex` and the next `AdvanceInternal` proceeds from the new position. Without this, `EffectiveQueueIndex`-driven UI (highlight, auto-scroll) and the next-track path both break. |
| `Hypersonic/Services/PlaybackService.cs` | Forward `JumpToQueueIndex` to `PlaybackQueue` and **only call `PlayCurrent()` when the queue layer returned non-null** — mirror `JumpToTrack`'s pattern at [line 330-344](../../../Hypersonic/Services/PlaybackService.cs). A `null` return (no-op) must short-circuit before `PlayCurrent()` to avoid spurious track restart and `TrackChanged` re-emission. Also call `FirePlaybackStateChangedIfHasChanged(prevHasNext, prevHasPrevious)` after `PlayCurrent()` for `HasNext`/`HasPrevious` updates. |
| `Hypersonic/ViewModels/PlayerViewModel.cs` | Replace/augment `JumpToQueueTrack` with `JumpToQueueIndex(QueueItem)` using a private reference-equality loop over `QueueItems` (which is `IReadOnlyList<QueueItem>` — no `IndexOf`). **Tighten `RefreshQueue` per "Refresh contract"**: change the in-place fast-path predicate from `existing[i].Track.Id == effective[i].Id` to `ReferenceEquals(existing[i].Track, effective[i])` so any same-ID new-instance replacement or duplicate same-ID reorder forces a full rebuild instead of preserving stale rows. The rebuild branch is unchanged. |
| `Hypersonic/Views/ShellPage.xaml.cs` | Migrate flyout `QueueList_DoubleTapped` to call `JumpToQueueIndex`; add tap handlers for player-bar art and title that delegate the decision to `NowPlayingTapPolicy.Decide` (project `Frame.BackStack` → `IReadOnlyList<Type>`, call the policy, apply the returned indices to `Frame.BackStack` in descending order, then call `NavigationService.NavigateTo`). The handler is thin glue — all decision rules live in `NowPlayingTapPolicy`. |
| `Hypersonic/ViewModels/AlbumDetailViewModel.cs` | Propagate new `PlaybackTrack` fields when building queues |
| `Hypersonic/ViewModels/PlaylistDetailViewModel.cs` | Same |
| `Hypersonic/ViewModels/ArtistDetailViewModel.cs` | Propagate the new `PlaybackTrack` fields in `BuildTopSongsPlaybackTracks` ([line 584](../../../Hypersonic/ViewModels/ArtistDetailViewModel.cs)) — the artist-page top-songs play path. The albums-on-artist path lands on `AlbumDetailViewModel.BuildPlaybackTracks` after navigation and is covered by that row. |
| `Hypersonic/ViewModels/TracksViewModel.cs` | Same |
| `Hypersonic.Core/Models/SearchSuggestionItem.cs` | Add `Suffix`, `BitRate`, `BitDepth`, `SamplingRate` to the song-fields group |
| `Hypersonic/ViewModels/ShellViewModel.cs` | `PerformSearchAsync`: populate the four new `SearchSuggestionItem` fields from the `Child`. `HandleSuggestionChosen`: propagate them when building the `PlaybackTrack` for the search-result play path. `OnNavigated`: replace the inline page-type → VM-key mapping + `TopLevelPages.Contains` check with a single call to `LastPagePersistencePolicy.GetPersistableViewModelKey(e.SourcePageType)`; write `LastPage` only when the result is non-null. The existing `TopLevelPages` `HashSet` collapses into the helper. |
| `Hypersonic/Helpers/SongMapper.cs` | **No change required** — already propagates the four fields from `Child` to `SongInfo` ([line 32-35](../../../Hypersonic/Helpers/SongMapper.cs)). Verify only. |
| `Hypersonic/Views/ShellPage.xaml` | Add tap handlers + hover cursor on player-bar art and title |
| `Hypersonic/App.xaml.cs` | Register `NowPlayingViewModel` + `NowPlayingPage` |
| `Hypersonic/Services/PageService.cs` | Add page mapping |

## Test Coverage

### Automated tests

| Area | Project | Tests |
|------|---------|-------|
| **TechnicalDetailFormatter** | `Hypersonic.Tests` | **Happy paths**: FLAC with bit depth + sample rate + bitrate produces three-component string; MP3 with no bit depth produces two-component string; sample rate `44100` formats as `44.1kHz`, `96000` as `96kHz`. **Missing-data per-component rules** (each numeric field treated as missing when `<= 0` since the underlying types are non-null `int` defaulting to `0`): `Suffix` null/whitespace → entire line hidden regardless of numerics; `BitRate <= 0` → bitrate component omitted (e.g. `FLAC · 24-bit/96kHz` with bitrate=0); `BitDepth <= 0` OR `SamplingRate <= 0` → composite component omitted (the composite requires **both** non-zero; half-data like bit-depth-without-sample-rate is **not** rendered partially); all numerics zero with valid suffix → entire line hidden (codec alone is noise); valid suffix + only bitrate → `MP3 · 320 kbps` (composite omitted, bitrate kept); valid suffix + only composite → `FLAC · 24-bit/96kHz` (bitrate omitted, composite kept). **Negative-input defensiveness**: any negative numeric is treated identically to zero. Bitrate-only with no codec → null/empty (line hidden). The helper lives in the WinUI app project (`Hypersonic/Helpers/TechnicalDetailFormatter.cs`) so the WinUI test project owns the tests; if it is later moved to `Hypersonic.Core/Helpers/`, the tests can move to `Hypersonic.Core.Tests` along with it (the formatter has no WinUI dependencies, so the Core move is technically possible — out of scope for this work). |
| **PlaybackTrack mapping** | `Hypersonic.Tests` | All four new fields (`Suffix`, `BitRate`, `BitDepth`, `SamplingRate`) round-trip across **all** `PlaybackTrack` construction paths: (a) `SongInfo` → `PlaybackTrack` for `AlbumDetailViewModel.BuildPlaybackTracks`, `PlaylistDetailViewModel.BuildPlaybackTracks`, `TracksViewModel.BuildPlaybackTracks`; (b) `SongInfo` → `PlaybackTrack` for `ArtistDetailViewModel.BuildTopSongsPlaybackTracks` — invoke `PlayFromTopSongCommand` with a capturing `IPlaybackService` fake (the inline `NullPlaybackService` in `ArtistDetailViewModelTopSongsTests` extended with a queue-capturing field) and assert the queued `PlaybackTrack` carries all four fields; (c) `Child` → `SearchSuggestionItem` → `PlaybackTrack` for `ShellViewModel.PerformSearchAsync` + `HandleSuggestionChosen`. Existing `SongMapper.MapFromChild` continues to populate `SongInfo` correctly (regression check). |
| **PlaybackQueue.JumpToQueueIndex** | `Hypersonic.Core.Tests` | State-only assertions on the pure queue layer (`PlaybackQueue` is event-free). **Return-value contract**: returns the new `CurrentTrack` on a successful jump, returns `null` on no-op (out-of-range OR already-current). Successful jumps: post-call `_currentIndex`/`NowPlaying` reflect the jump and the returned `PlaybackTrack` equals the new `CurrentTrack`; with shuffle off, effective index equals underlying index; **with shuffle on, jumping to a non-terminal effective index `i` (`i < GetEffectiveQueue().Count - 1`) lands on `GetEffectiveQueue()[i]` (not the underlying-queue track at the same index) AND post-call `EffectiveIndex == i` AND a subsequent `NextTrack`/`AdvanceManual` lands on `GetEffectiveQueue()[i + 1]`** — the latter two assertions are what catch the shuffle-cursor bug: a `JumpToQueueIndex` that only updates `_currentIndex` would still pass the "correct track is playing" assertion but `EffectiveIndex` would return the stale `_shufflePosition` and the next advance would land on `GetEffectiveQueue()[oldShufflePosition + 1]` instead of `GetEffectiveQueue()[i + 1]`. **Shuffled terminal-index case** (split out because `AdvanceInternal` returns `null` without mutating when shuffle is on, repeat is off, and `_shufflePosition == _shuffleOrder.Count - 1`): jump to effective index `Count - 1` with repeat off, assert post-call `EffectiveIndex == Count - 1` and `CurrentTrack == GetEffectiveQueue()[Count - 1]`, then call `NextTrack`/`AdvanceManual` and assert it returns `null` AND `_currentIndex`/`_shufflePosition`/`NowPlaying` are unchanged. **Optional shuffled terminal + repeat-all case**: same setup but `RepeatMode = All`, assert `AdvanceManual` wraps to `GetEffectiveQueue()[0]` of the (possibly-reshuffled) order — covers the wrap path through `RebuildShuffleOrder()`. **Duplicate-track scenario (unshuffled)**: a queue containing the same track at positions 0 and 3, current at 0, jumping to effective index 3 lands on position 3 (does not stay on position 0) and returns the position-3 track. **Shuffled duplicate scenario** (effective index of the duplicate is non-deterministic because `RebuildShuffleOrder()` keeps the current track at effective index 0 and randomizes the rest, but the test does not need that randomness — only the discovered position): construct the queue with two distinct `PlaybackTrack` instances `A` (current) and `B` (the duplicate occurrence) sharing the same `Track.Id`; enable shuffle; locate `B` in `GetEffectiveQueue()` by reference using a manual `ReferenceEquals` loop (do **not** use `effective.IndexOf(B)` — `GetEffectiveQueue()` returns `IReadOnlyList<PlaybackTrack>` which has no `IndexOf`, and the value-equality semantics of any `Contains`/`IndexOf` extension would conflate `A` and `B` since they share `Track.Id`). Call the discovered position `iB`. **Always-asserted (regardless of where `iB` lands)**: jump to `iB`, assert `CurrentTrack` is `B` (not `A`) and `EffectiveIndex == iB`. The next-advance assertion is **not** repeated here — the generic non-terminal shuffle-cursor case above already covers `EffectiveIndex == i + 1` after advance, and the terminal case already covers the `AdvanceManual` short-circuit; both apply to any `iB`. This makes the duplicate-specific case fully deterministic with no skip / re-roll / queue-size tuning. No-op cases: out-of-range (`< 0`, `>= Count`) returns `null` AND leaves `_currentIndex`/`_shufflePosition`/`NowPlaying` unchanged AND does not push to navigation history; index equal to current effective index returns `null` AND no state change AND no history push. Push-to-history pushes the outgoing track ID matching `JumpToTrack` (the duplicate-Previous limitation is documented and out of scope — see "Known limitation" in the design). |
| **PlaybackService.JumpToQueueIndex** | (manual + indirect) | Removed from the automated suite. The production `PlaybackService` constructs `new MediaPlayer()`, a dispatcher timer, and a `PlaybackQueue` directly in its constructor with no injection seams ([PlaybackService.cs:17-118](../../../Hypersonic/Services/PlaybackService.cs)), and existing tests fake `IPlaybackService` rather than instantiate the real service. Coverage is provided by: (1) `PlaybackQueue.JumpToQueueIndex` core tests for the queue-state mutation **and the return-value contract** that gates the service's `PlayCurrent()` call, (2) `PlayerViewModel.JumpToQueueIndex` tests using a fake `IPlaybackService` for the call-forwarding contract, (3) manual smoke tests #8–#11 for end-to-end behavior including `MediaPlayer.Source` updates, SMTC refresh, and `TrackChanged` propagation to subscribers. The "no-op doesn't restart current track" behavior depends on the queue layer returning `null` (covered by row #1) AND the service implementation following the documented `result == null → return` pattern (mirrors existing `JumpToTrack` at [PlaybackService.cs:330-344](../../../Hypersonic/Services/PlaybackService.cs); reviewable by reading the production code, not automatable without the seam refactor). Adding an automated service-layer test would require a testability refactor (extract `IMediaPlayer` and a timer abstraction; provide an internal constructor accepting them) — explicitly out of scope for this work. |
| **PlayerViewModel.JumpToQueueIndex** | `Hypersonic.Tests` | `JumpToQueueIndex(item)` resolves the position via the reference-equality loop and forwards to `IPlaybackService.JumpToQueueIndex` with the resolved index; with duplicate `Track.Id`, the second `QueueItem` instance jumps to its own row position; item not in `QueueItems` is a no-op (no service call) |
| **PlayerViewModel.RefreshQueue contract** | `Hypersonic.Tests` | Direct tests on `PlayerViewModel.RefreshQueue` against a fake `IPlaybackService` whose `GetEffectiveQueue()` returns a script-controllable list. Required cases (each asserts both the **identity** of `QueueItems` entries and their **displayed `Track` reference** at each position): **(a) Pure no-op** — same instances at the same positions, current index unchanged: `QueueItems` collection reference is preserved (no `INotifyPropertyChanged` for `QueueItems`), every `QueueItem` reference is preserved, only `IsCurrent` flags settle. **(b) Current-index advance only** — same instances at same positions, `EffectiveQueueIndex` moved from `i` to `i+1`: collection and items preserved, `IsCurrent` flips from row `i` to row `i+1`. **(c) Length change** — rebuild expected; new `QueueItems` collection. **(d) ID change at one position** — rebuild expected; new collection. **(e) Same-ID new-instance replacement** — service returns the same `Track.Id` sequence but the `PlaybackTrack` at position `k` is a new instance with different `Title`/`Artist`/`CoverArtUri`/`Suffix`/`BitRate`/`BitDepth`/`SamplingRate`. Assert: `QueueItems` is rebuilt (new collection reference) and `QueueItems[k].Track` is the **new** instance, not the old one. Without this rebuild, the row would silently show the old metadata; the test must read at least one of the diverging fields off the row's `Track` and assert it equals the new value. **(f) Duplicate same-ID swap** — queue contains the same `Track.Id` at positions `j` and `k` with two distinct `PlaybackTrack` instances `A` and `B`; the service returns the same length and same per-position IDs but with `A` and `B` swapped (position `j` now holds `B`, position `k` holds `A`). Assert: rebuild occurs and `ReferenceEquals(QueueItems[j].Track, B)` and `ReferenceEquals(QueueItems[k].Track, A)`. **(g) Duplicate same-ID reorder (3+ instances)** — three same-ID instances `A`/`B`/`C` at positions `(j, k, l)` reordered to `(B, C, A)`; assert per-position references match the new order. **(h) Empty → non-empty** and **(i) non-empty → empty** — both rebuild; `IsQueueEmpty` updates; `QueueCountText` updates. The fake's `GetEffectiveQueue()` must return distinct `PlaybackTrack` instances by reference for the swap/reorder cases — sharing a single instance across positions would let the production code "pass" the contract trivially. The "preserve collection on no-op" assertion (case a, b) is what the optimization exists for; cases (e)/(f)/(g) are what the contract protects against. |
| **Lyrics stale-fetch guard** | `Hypersonic.Tests` | Track-change increments generation; in-flight fetch with stale generation does not overwrite `LyricsText`; cache hit on track change skips fetch |
| **VM-active guard for late `TrackChanged`** | `Hypersonic.Tests` | The dispatcher race must be exercised through the `enqueueOverride` test seam, **not** the synchronous-only seam (synchronous mode unsubscribes in `OnNavigatedFrom` *before* the test could raise the event again — the test would assert against a no-op event handler and pass even if the production guard were missing). **Sequence using the capturing-enqueue fake**: (a) construct the VM with `synchronous: false` and an `enqueueOverride` that appends each `Action` to a `Queue<Action>` instead of running it. (b) `OnNavigatedTo` — `_vmActive` becomes true; `HandleTrackChange()` runs synchronously inside `OnNavigatedTo` (initial population) and increments `_lyricsGeneration` once; capture this generation as `g0`. (c) Raise `TrackChanged` — the production handler enqueues a body via `enqueueOverride`; the queue now has one captured `Action`; **state has not changed yet** (the body hasn't run). (d) Call `OnNavigatedFrom` — `_vmActive` becomes false; `-=` runs; `_lyricsGeneration` increments to `g0 + 1`. (e) Pop and invoke the captured `Action`. **Assert**: the body's `if (!_vmActive) return;` short-circuits → `_lyricsGeneration` stays at `g0 + 1` (no third increment from the late callback), `LyricsText`/`LyricsLoadState`/`TechnicalDetail` unchanged from their `OnNavigatedTo`-time values, no fetch was kicked off (the lyrics fetch hook records zero invocations beyond the one from step b). **The captured-enqueue invocation in step e is what actually exercises the guard** — running it post-`OnNavigatedFrom` is the production race, and the assertions above directly observe the guard's effect on state and generation. **Plus the back-navigation case**: a `TrackChanged` raised between `OnNavigatedFrom` and a subsequent `OnNavigatedTo` does not enqueue (handler is `-=`'d). **Plus the cross-await case** (uses `fetchOverride` + `TaskCompletionSource`): a fetch held across `OnNavigatedFrom` checks `_vmActive` post-await and discards the response without touching state, regardless of generation. |
| **Lyrics cache** | `Hypersonic.Tests` | Same track ID hits cache without re-fetch; cache bounded to N entries (FIFO eviction); **cache stores `string?`**: a definitive no-lyrics result that gets cached (per the cache-write matrix — *both* sides definitive empty/unsupported) is stored as `null`/blank and a subsequent track-change to that ID does not re-fetch (set up the precondition by mocking *both* structured and legacy to return successful empty in this test, otherwise the matrix forbids the cache write and the no-refetch assertion would not hold); **cache hit always sets both `LyricsText` and `LyricsLoadState` together**: cached non-blank → `Loaded`, cached null/blank → `NoLyrics`; cross-track state cleanup: returning to a cached `Loaded` track from a `NoLyrics` track flips state to `Loaded`; returning to a cached `NoLyrics` track from a `Loaded` track flips state to `NoLyrics` and clears `LyricsText`. The full per-row write/no-write matrix (including the structured-definitive + legacy-transient and structured-transient + legacy-definitive cases) is covered in the `Lyrics cache: transient failure not cached` row below — this row covers only the read-side / cross-track-state behavior on top of an already-written cache entry. |
| **Lyrics cache: transient failure not cached** | `Hypersonic.Tests` | Each row in the cache-write matrix from the design's "Cache-write rule" subsection gets at least one test case. **The legacy call must be invoked in every case except `structured == content`** — assert via the legacy mock's invocation count = 1 in every non-content row, count = 0 only when structured returned content. Critical cases: **(a)** structured throws + legacy throws → `NoLyrics`, no cache write. **(b)** structured `Status="failed"` with non-definitive code (e.g. 0, 10) on both calls → `NoLyrics`, no cache write. **(c)** structured throws + legacy returns successful empty → `NoLyrics`, **no cache write** (this is the case the previous spec got wrong — the empty legacy answer does **not** prove structured wouldn't have returned content). **(d)** structured returns successful empty + legacy throws → no cache write. **(e)** structured `Status="failed"` with definitive code 30 ("incompatible client") + legacy returns successful empty → `NoLyrics`, **cache write** (both sides agree definitively). **(f)** structured `Status="failed"` with definitive code 30 + legacy throws → `NoLyrics`, **no cache write** (legacy might recover). **(g)** **structured returns successful empty + legacy returns content → `Loaded` with the legacy text, cache the legacy lyrics** (server-with-no-structured-but-has-plain-lyrics scenario; this is the row a structured-empty short-circuit would silently break — the prior matrix marked legacy as "(not called)" and would have lost lyrics here). **(h)** structured `Status="failed"` with definitive code 70 ("data not found") + legacy returns successful empty → `NoLyrics`, cache write. **(i)** structured returns successful empty + legacy returns successful empty → `NoLyrics`, cache write. **(j)** structured returns successful with content → cache the lyrics, legacy not called (no double-fetch — assert the legacy mock's invocation count = 0 here, the **only** case this is allowed). **(k)** structured `Status="failed"` with definitive code + legacy returns content → cache the legacy lyrics. The classification table from the design specifies which `Status="failed"` codes are definitive (20, 30, 70) vs. transient (0, 10, anything else); tests should cover at least one definitive and one transient code on each side. |
| **Lyrics null-track handling** | `Hypersonic.Tests` | `TrackChanged` fires while `IPlaybackService.NowPlaying == null` (e.g. queue cleared while page open): `_lyricsGeneration` increments (invalidating any in-flight fetch), `TechnicalDetail` clears, `LyricsText` clears, `LyricsLoadState = NoTrack` (not `NoLyrics` — the placeholder rendering must not show "No lyrics available" when there is no track), no fetch is issued. Subsequent track-change to a real track follows normal cache-miss/cache-hit paths and lands on `Loading` → `Loaded` / `NoLyrics` per the cache-write matrix. |
| **Lyrics fallback** | `Hypersonic.Tests` | Three structured-call outcomes each trigger the legacy fallback: (a) structured returns no usable content (covered by sub-cases below); (b) structured returns `Status="failed"` with a definitive code that triggers fallback (cover both classifications: e.g. code 30 "incompatible client" — definitive **unsupported** — and code 70 "data not found" — definitive **empty**; the classification differs but both fall back to legacy); (c) structured throws. In each case, legacy `GetLyricsAsync` succeeding with content yields `Loaded` with that content. If legacy also returns empty / failed / throws → `NoLyrics`. Structured success with at least one usable entry does **not** call the legacy endpoint (no double-fetch). All failures logged at Warning. |
| **Lyrics "usable entry" selection** | `Hypersonic.Tests` | Sub-cases of "no usable content": empty `LyricsList`; non-empty `StructuredLyrics` with `Line == null`; `Line` containing only entries whose `Value` is null or whitespace; **first entry empty / non-usable but second entry usable** — selects the second entry, does **not** trigger fallback; all entries non-usable triggers fallback. Per-line filtering: blank/null `Value`s are skipped when flattening; a usable entry with a mix of usable and blank lines renders only the usable lines. |
| **Tab default** | `Hypersonic.Tests` | Each navigation to the page starts on Lyrics tab |
| **Queue tab auto-scroll (coordinator)** | `Hypersonic.Tests` | Tests target `QueueAutoScrollCoordinator` (pure logic, no `ListView`, no `[UITestMethod]`): `OnQueueRefreshed` invokes the injected `scrollIntoView` callback only when the queue tab is active AND `effectiveIndex >= 0`; lyrics-tab-active suppresses the callback; `effectiveIndex == -1` (e.g. queue cleared) suppresses; **`OnTabSelected(Queue, currentEffectiveIndex)` with `currentEffectiveIndex >= 0` MUST scroll immediately** — assert the callback fires exactly once on the transition to the Queue tab; `OnTabSelected(Queue, -1)` does **not** scroll (no current track); `OnTabSelected(Lyrics, …)` does not scroll regardless of index; transitioning Queue → Lyrics → Queue scrolls again on the second activation. **Same `OnQueueRefreshed` behavior whether the trigger is a track change or a queue mutation** — the coordinator is event-source-agnostic. The page-level wiring (subscriptions to **both** `Player.NowPlayingMetadataChanged` and `IPlaybackService.QueueChanged` while the queue tab is active, subscription on/off lifecycle, marshaling to the UI thread, the `ScrollIntoView` glue itself) is verified by manual smoke tests #20 and #21 — those paths cross the WinUI test-host boundary that crashes `[UITestMethod]` and aren't worth a workaround. |
| **QueueScrollResolver bounds** | `Hypersonic.Tests` | Tests target `QueueScrollResolver.ResolveScrollTarget` directly (pure static, no UI types): `effectiveIndex == 0` with non-empty list returns `items[0]`; `effectiveIndex == count - 1` returns `items[count - 1]`; `effectiveIndex < 0` returns `null` (defensive — the coordinator already gates this, but the resolver is the second line of defense); `effectiveIndex >= count` returns `null` (the race-against-queue-rebuild case the resolver exists to handle); empty `items` always returns `null` regardless of `effectiveIndex`. Stub `IReadOnlyList<QueueItem>` is sufficient — no real `Player`, no `ListView`. |
| **Coordinator post-`Reset` quiescence** | `Hypersonic.Tests` | After the page's `Unloaded` handler invokes the coordinator's reset path (e.g. `OnTabSelected(Lyrics, -1)` or an explicit `Reset()`), subsequent `OnQueueRefreshed` calls **must not** invoke the `scrollIntoView` callback — the coordinator's active flag is `false`. This guards against scroll calls being enqueued against an orphaned page after the user navigates away. The page-level test that handlers actually got `-=`'d is covered by manual smoke #20 (no stuck subscriptions / duplicated handlers across navigate-away-and-back). |
| **Tab-gated subscription idempotence** | `Hypersonic.Tests` | Tests target `QueueTabSubscriptionGate` directly (see "Files to Create") — **not** `internal` methods on `NowPlayingPage` (instantiating the page code-behind crosses the WinUI test-host boundary that other rows explicitly avoid). The helper is constructed with two callbacks (`attachAll`, `detachAll`) that the test backs with invocation counters and/or `+= … -= …` against fake event sources. The helper exposes a single `Attach()` / `Detach()` pair gating **both** subscriptions atomically — no per-event API. Cases: (a) `Attach()` followed by `Attach()` invokes `attachAll` exactly once (assert via the counter, AND raise both fake events to confirm the handlers fire exactly once each — should be 1, not 2). (b) `Detach()` followed by `Detach()` invokes `detachAll` at most once (no exception on the second call; raising the events after either call invokes the handlers 0 times). (c) `Attach()` → `Detach()` → `Attach()` → `Detach()` returns to the no-handler state for **both** events (0 invocations on a final raise of each). (d) Three rapid `Attach()` calls + 1 raise of each event = 1 invocation each; three rapid `Attach()`/`Detach()` cycles + 1 raise of each event after the last `Detach()` = 0 invocations each. (e) **Atomicity check** — after `Attach()`, both `attachAll` invocation count is 1 AND both fake events have exactly one handler attached; after `Detach()`, both are torn down together (no "first event attached, second skipped" partial state). The gate flag is exposed via the `internal IsSubscribed` property for direct assertion, but the behavioral invocation-count check is sufficient. |
| **Player-bar tap policy** | `Hypersonic.Tests` | Tests target `NowPlayingTapPolicy.Decide` — pure logic, no `Frame`, no `ShellPage`, no `[UITestMethod]`. Cases: `hasTrack == false` → `ShouldNavigate == false`, no prune; `currentPageType == typeof(NowPlayingPage)` → `ShouldNavigate == false`, no prune; non-`NowPlayingPage` current with empty back stack → navigate, no prune; back stack contains a single `NowPlayingPage` entry at index N → navigate, prune index N; back stack contains multiple `NowPlayingPage` entries (defensive) → navigate, prune all matching indices in the order returned (caller applies them descending so each `RemoveAt` keeps remaining indices valid); the **single-instance via sidebar route** scenario (`MainPage → NowPlayingPage → AlbumsPage`, current is `AlbumsPage`) → navigate, prune the `NowPlayingPage` index, leaving `MainPage`+`AlbumsPage` in history when combined with the post-navigate stack. The thin code-behind glue (projection of `Frame.BackStack`, in-place mutation, `NavigateTo` call) is verified by manual smoke tests #1–3 and #16–17. |
| **No restore as `LastPage` (write side)** | `Hypersonic.Tests` | Navigating to `NowPlayingPage` does **not** write `NowPlayingViewModel` into `AppState.Navigation.LastPage`. **Pure-helper extraction required for testability**: lift the persistence decision out of `ShellViewModel.OnNavigated` (which is private and takes a WinUI `NavigationEventArgs` that's awkward to construct in tests) into a `LastPagePersistencePolicy.GetPersistableViewModelKey(Type? sourcePageType)` static helper that returns the VM key string when the page should be persisted, or `null` when it should be skipped. `OnNavigated` becomes a thin caller — read the result, write it to `AppState.Navigation.LastPage` only when non-null. Tests target `LastPagePersistencePolicy.GetPersistableViewModelKey` directly with `typeof(NowPlayingPage)` (returns null), `typeof(SettingsPage)` (returns the existing `SettingsViewModel` key), `typeof(MainPage)` / `AlbumsPage` / etc. (return the corresponding VM key), and `null` (returns null). No `NavigationEventArgs`, no fake `INavigationService`, no reflection. |
| **No restore as `LastPage` (read side)** | manual + indirect | The "`DefaultActivationHandler` and `ConnectViewModel` do not restore `NowPlayingViewModel` on startup/login" assertion has no clean unit-test seam today: both classes hold private `TopLevelPages` `HashSet`s and run inside WinUI activation/navigation flow. Coverage: (1) the write-side test above guarantees `LastPage` never becomes `NowPlayingViewModel.FullName`, so the read side is starved of the precondition that would matter; (2) manual smoke test #18 ("Restart the app while last-viewed was `NowPlayingPage`") exercises the end-to-end behavior. **Future work** (out of scope): extending `LastPagePersistencePolicy` (or a sibling `RestorablePages.IsAllowed(string viewModelName)`) to be consumed by `DefaultActivationHandler` and `ConnectViewModel` too would unify the allow-list and let the read-side decision be unit-tested as well. Not required for this work. |

### Manual smoke tests

1. Click player bar art with a track playing — page opens with art, metadata, lyrics loading
2. Click track title in player bar — same
3. Click either with no track playing — nothing happens
4. Verify technical line: play a FLAC track (3 components: codec · bit-depth/sample-rate · bitrate), an MP3 (2 components: codec · bitrate). If a track in the library happens to have missing/zero numeric metadata, verify the line gracefully omits that component instead of rendering `0 kbps` or `0kHz` — and that a track with no usable metadata at all (no codec, or codec only with all numerics zero) hides the line entirely rather than showing a bare `FLAC` or empty separators.
5. Switch tabs — Lyrics ↔ Queue, verify content swaps and Lyrics doesn't re-fetch
6. Skip tracks rapidly — verify lyrics for the *current* track always end up displayed (no stale)
7. Play a track with no lyrics on the server — verify "No lyrics available" message
8. Double-tap a track in the queue tab — verify it jumps and the current-row highlight follows. Single click should **not** jump.
9. Add same track twice to queue — verify only the playing instance highlights, and double-tapping the **other** instance jumps to that one (not back to the first)
10. Repeat #9 with shuffle enabled — verify the double-tapped row plays regardless of underlying queue position
11. Repeat the duplicate test in the **player-bar flyout** — verify the same fixed behavior (shared `JumpToQueueIndex` API)
12. Click an artist hyperlink in the metadata — verify navigation to artist detail page
13. Click the album link — verify navigation to album detail
14. Use back navigation — verify return to previous page
15. Resize window — verify layout remains usable down to the minimum app window width
16. Tap the player-bar art **while already on `NowPlayingPage`** — verify nothing happens (no duplicate stack entry)
17. **Single-instance via sidebar route**: from `MainPage`, tap player-bar art (now on `NowPlayingPage`), navigate to `AlbumsPage` via sidebar, tap player-bar art again. Press back — verify return to `AlbumsPage` (not to a previous `NowPlayingPage`). Press back again — verify return to `MainPage`.
18. Restart the app while last-viewed was `NowPlayingPage` — verify the app restores to the home page (or whatever was the *previous* top-level page), not to a blank now-playing page
19. **Header behavior**: from `AlbumsPage` (sidebar shows "Albums" header), tap player-bar art — verify the shell's header does not still read "Albums" while `NowPlayingPage` is visible (`HeaderMode="Never"` is in effect)
20. **Queue auto-scroll wiring**: with a queue of 50+ tracks playing somewhere mid-list, open `NowPlayingPage` (which starts on the Lyrics tab), then switch to the Queue tab — the current row must already be visible (not scrolled to the top). Scroll the list manually elsewhere, then advance the track — verify the new current row is brought into view. Switch back to the Lyrics tab, advance the track again, switch back to Queue — verify the new current row is in view. Navigate away and back — no stuck subscriptions or duplicated handlers.
21. **Queue auto-scroll on shuffle toggle**: with the same 50+ track queue and Queue tab open, scroll manually so the current row is offscreen, then toggle shuffle from the player bar. Verify the current row scrolls back into view after the queue rebuild. Toggle shuffle off again — verify the current row scrolls into view at its new (unshuffled) position. Repeat the same check after a queue mutation triggered from elsewhere (e.g. add-to-end via a context menu, when those exist) — same expected behavior since both paths share `IPlaybackService.QueueChanged`.
22. **Queue auto-scroll defensive bounds**: with the Queue tab open, clear the entire queue via a real `ClearAll` path. Until a clear-queue UI exists, this requires either invoking `IPlaybackService.ClearAll()` from a temporary debug hook or extending the queue-flyout transport to expose it. The scroll glue must silently no-op rather than throw — the page should not crash even if a stale `effectiveIndex` arrives after the queue has shrunk to zero. **Note**: "play through the last track with repeat off" is **not** an equivalent fallback — `PlaybackService` leaves `NowPlaying` set after natural end-of-queue and does not shrink the queue or raise `QueueChanged`/`TrackChanged` in that path, so it does not exercise the stale-index/empty-queue case. Until the real path can be exercised end-to-end, the equivalent automated coverage is the `QueueScrollResolver bounds` test row, which directly exercises the empty-list and out-of-range branches.
23. **Metadata theme refresh**: open `NowPlayingPage` with a track playing whose metadata renders artist hyperlinks (multi-artist track preferred). With the page visible, switch the system theme between Light and Dark. The artist `RichTextBlock`, album hyperlink, and any imperatively-styled `Run`s must update their foreground brushes immediately to match the new theme — they should not stay tinted with the previous theme's brush until the next track change. (Mirrors the `ShellPage` player-bar artist row's existing behavior.)
24. **Tab-toggle subscription idempotence**: open `NowPlayingPage` on a queue with the current track mid-list. Without changing track, toggle Lyrics → Queue → Lyrics → Queue → Lyrics → Queue several times. Now trigger a single queue mutation (e.g. shuffle toggle from the player bar). Verify the scroll-into-view fires **exactly once** (visible as a single scroll animation, not a stutter or repeated jumps). Then advance a track and verify the same — single scroll animation. Navigate away from the page; navigate back; repeat the toggle sequence; trigger one mutation; same expected behavior. Without `QueueTabSubscriptionGate`'s `_subscribed` guard, each Queue activation would stack a fresh handler pair and a single event would fire N scroll calls.

## Implementation Sequence

1. Add `Suffix`, `BitRate`, `BitDepth`, `SamplingRate` to `PlaybackTrack`. Update **every `new PlaybackTrack` construction path** to propagate the four fields:
   - `AlbumDetailViewModel.BuildPlaybackTracks` — album track list play.
   - `PlaylistDetailViewModel.BuildPlaybackTracks` — playlist track list play.
   - `TracksViewModel.BuildPlaybackTracks` — all-tracks page play.
   - `ArtistDetailViewModel.BuildTopSongsPlaybackTracks` ([line 584](../../../Hypersonic/ViewModels/ArtistDetailViewModel.cs)) — artist-page top-songs play (the **easy-to-miss** site, name doesn't follow the `BuildPlaybackTracks` convention).
   - `ShellViewModel.HandleSuggestionChosen` (search-result play path) — also requires extending `SearchSuggestionItem` first with the four fields, populated in `PerformSearchAsync` from the source `Child`. See "Mapper update" for the full rationale.

   Re-grep `new PlaybackTrack` across the solution before declaring done — additional construction sites may have been added since this design was written. Add round-trip tests for **all** the paths above (see the `PlaybackTrack mapping` test row).
2. Add `JumpToQueueIndex(int)` to `IPlaybackService` (public surface stays `void`); implement on `PlaybackQueue` as `public PlaybackTrack? JumpToQueueIndex(int effectiveIndex)` returning the new `CurrentTrack` on success or `null` on no-op (out-of-range OR already-current) — same return-shape as the existing `JumpToTrack`; effective → underlying index translation including shuffle, **and assign `_shufflePosition = effectiveIndex` when shuffle is enabled** so the queue's two-part shuffled cursor stays consistent (failing this leaves `EffectiveIndex` stale and `AdvanceInternal` advances from the wrong position — see "New API"). Forward in `PlaybackService` with the **same `result == null → return` gate** as `PlaybackService.JumpToTrack` ([line 330-344](../../../Hypersonic/Services/PlaybackService.cs)) — a `void` queue method would let an out-of-range or already-current call still hit `PlayCurrent()` and restart the current track. Add `PlaybackQueue` tests covering ordered/shuffled/duplicate scenarios **and the return-value contract** (no-op cases must return `null` and not push to history) **and the shuffle-cursor invariant** — for a non-terminal target (`i < GetEffectiveQueue().Count - 1`) post-jump `EffectiveIndex == effectiveIndex` and the next advance lands on `GetEffectiveQueue()[effectiveIndex + 1]`; for the terminal target with repeat off, post-jump `EffectiveIndex == Count - 1` and the next advance returns `null` without mutating (`AdvanceInternal` short-circuit). The shuffled-duplicate case must discover the duplicate's effective index dynamically (locate the second occurrence in `GetEffectiveQueue()` by a `ReferenceEquals` loop — not `IndexOf`), not hard-code it — `RebuildShuffleOrder()` keeps the current track at effective index 0 and randomizes the rest, so the duplicate's effective position is non-deterministic. The duplicate-specific assertions (post-jump `CurrentTrack`/`EffectiveIndex`) hold for any discovered position, so no skip / re-roll is needed.
   - **Test-fake update**: adding a method to `IPlaybackService` is a binary-breaking change for **every** existing implementer in `Hypersonic.Tests`. The repo currently has both a shared fake (`Hypersonic.Tests/Fakes/FakePlaybackService.cs`) **and** several private inline implementations in test classes — each with its own type name (`FakePlaybackService` repeated as a `private sealed class` in four test files, plus `CapturingPlaybackService` and `NullPlaybackService` in three more). All of them need `JumpToQueueIndex(int)` added; otherwise the test project won't compile. The known sites (verified at design time):
     - `Hypersonic.Tests/Fakes/FakePlaybackService.cs` — shared fake. Add the new member with a throw-by-default body and a capturing field for the new `PlayerViewModel.JumpToQueueIndex` tests.
     - `Hypersonic.Tests/ViewModels/TracksViewModelTests.cs` — inline `FakePlaybackService`.
     - `Hypersonic.Tests/ViewModels/PlayerViewModelMetadataTests.cs` — inline `FakePlaybackService`.
     - `Hypersonic.Tests/ViewModels/PlayerViewModelToggleTests.cs` — inline `FakePlaybackService`.
     - `Hypersonic.Tests/ViewModels/PlaybackTrackPropagationTests.cs` — inline `FakePlaybackService`.
     - `Hypersonic.Tests/ViewModels/ShellViewModelSearchPlayTests.cs` — inline `CapturingPlaybackService`.
     - `Hypersonic.Tests/ViewModels/ArtistDetailViewModelTopSongsTests.cs` — inline `NullPlaybackService`.
     - `Hypersonic.Tests/ViewModels/ArtistDetailViewModelAlbumMappingTests.cs` — inline `NullPlaybackService`.
     For the inline fakes, add `JumpToQueueIndex(int) => throw new NotImplementedException();` (or `=> { }` — match what each fake does for the rest of its members). Re-grep `: IPlaybackService` inside `Hypersonic.Tests` before declaring done — additional inline fakes may have been added since this design was written.
3. Add `PlayerViewModel.JumpToQueueIndex(QueueItem)`; migrate the existing player-bar flyout `QueueList_DoubleTapped` to call it; verify flyout behavior unchanged for non-duplicate queues and **fixed** for duplicates. **Also tighten `PlayerViewModel.RefreshQueue` per "Refresh contract"**: change the in-place fast-path predicate from `existing[i].Track.Id == effective[i].Id` to `ReferenceEquals(existing[i].Track, effective[i])`. The rebuild branch is unchanged. Add the `PlayerViewModel.RefreshQueue contract` test row before declaring done.
4. Implement `TechnicalDetailFormatter` with unit tests
5. Create `NowPlayingViewModel` skeleton with `IPlaybackService.TrackChanged` subscription, technical-detail computation, lyrics fetching + caching + stale-fetch guard
6. Create `NowPlayingPage` XAML with two-column layout, left-side art + metadata + technical line, right-side SelectorBar + content surfaces. **Use a public parameterless constructor** (`public NowPlayingPage()`) — the navigation service activates the page via `Frame.Navigate(pageType, parameter)`, which requires a parameterless constructor. **Constructor injection is not an option** for frame-navigated pages; it's reserved for `ShellPage`-style pages resolved directly from `App.GetService`. **Resolve `ViewModel = App.GetService<NowPlayingViewModel>()`, `Player = App.GetService<PlayerViewModel>()`, and `_playbackService = App.GetService<IPlaybackService>()` BEFORE `InitializeComponent()`** so XAML bindings see them during component initialization, and so the navigation service's reflection-based VM lookup ([FrameExtensions.cs:7](../../../Hypersonic/Helpers/FrameExtensions.cs)) finds a non-null `ViewModel` to invoke `INavigationAware` on. The `_playbackService` is a private field (not a public property) used solely to wire the queue-tab `IPlaybackService.QueueChanged` subscription added in step 8 — it covers shuffle/add/remove/move/clear queue mutations that don't raise `Player.NowPlayingMetadataChanged`. Mirror `MainPage.xaml.cs:20-29` for the constructor pattern. Without the `ViewModel` assignment, `NowPlayingViewModel.OnNavigatedTo` never runs — lyrics/technical-detail subscriptions are dead. Without `_playbackService`, queue-mutation auto-scroll is broken (shuffle toggle on a long Queue tab leaves the current row offscreen). The page must **not** subscribe to `IPlaybackService.TrackChanged` — that subscription is owned by `NowPlayingViewModel` (lyrics + technical-detail recompute). **Wire the always-on left-column metadata-render subscription using the page's constructor + `Unloaded` event** (NOT `INavigationAware` hooks — `INavigationService` only invokes `OnNavigatedTo` / `OnNavigatedFrom` on `frame.GetPageViewModel()`, not on the page itself; methods named that way on `NowPlayingPage.xaml.cs` would be dead code. Match the existing pattern used by `MainPage`, `AlbumsPage`, `AlbumDetailPage`, etc.). In the constructor, after `InitializeComponent()`: capture `_pageDispatcher`, set `_pageActive = true`, subscribe `Player.NowPlayingMetadataChanged += OnPlayerMetadataChanged`, subscribe `ActualThemeChanged += OnActualThemeChanged`, call `RenderNowPlaying()` once for initial population. The `OnPlayerMetadataChanged` handler **marshals through `_pageDispatcher.TryEnqueue` with a `_pageActive` guard** before calling `RenderNowPlaying()` — see the concrete handler shape in "Always-on metadata render". The marshal is mandatory: `Player.NowPlayingMetadataChanged` originates from `PlayerViewModel.OnTrackChanged` which can run off the UI thread; calling `RenderNowPlaying()` directly from the event would race or throw. The `ActualThemeChanged += OnActualThemeChanged` wiring uses the same lifecycle pair (named handler, `+=` in constructor, `-=` in `Unloaded`) — fires on the UI thread, no marshal needed; the foreground brushes refresh on theme switches because the handler reruns `RenderNowPlaying()`. **In `Unloaded`, follow the canonical four-step cleanup defined in step 8's "Mandatory `Unloaded` cleanup"** — (1) `_pageActive = false`, (2) `_queueTabGate.Detach()`, (3) reset `_scrollCoordinator`, (4) `-=` both always-on handlers (`Player.NowPlayingMetadataChanged` AND `ActualThemeChanged`). The two always-on handlers added here in step 6 are the (4a)/(4b) detach pair in that ordering; (2)/(3) are queue-tab-specific and live in step 8 but the page's single `Unloaded` lambda runs them all together (see the constructor snippet in "Page activation contract" for the exact body). **The metadata-render subscription is always on while the page is alive — it is NOT gated by tab selection.** It is a separate, distinct handler from the Queue-tab auto-scroll subscription added in step 8 (which also listens to `Player.NowPlayingMetadataChanged` but only while the Queue tab is active and only to drive `ScrollIntoView`). Adding/removing the auto-scroll handler in step 8 must not touch this metadata-render handler — implementing them as the same subscription would stop metadata from refreshing while the user is on the Lyrics tab. `NowPlayingViewModel` keeps its own `INavigationAware` lifecycle for VM-side concerns (lyrics fetching, technical-detail recompute) — that path **does** work via the navigation service and is unrelated to the page-side wiring.
7. Wire up Lyrics surface (text rendering, loading/empty states)
8. Wire up Queue surface:
   - Bind to `Player.QueueItems` with the new DataTemplate; current-row highlight via `IsCurrent`.
   - Double-tap-to-jump via `JumpToQueueIndex` — single click must not jump.
   - **Auto-scroll has three pieces** that must all land together:
     - Create `QueueAutoScrollCoordinator` with its `OnTabSelected(NowPlayingTab, int)` and `OnQueueRefreshed(int)` methods; add `Hypersonic.Tests` coverage for both methods (queue-tab gating, `effectiveIndex >= 0` gating, immediate scroll on `OnTabSelected(Queue, …)`, event-source-agnostic behavior).
     - Create `QueueScrollResolver.ResolveScrollTarget(IReadOnlyList<QueueItem>, int)` — pure `internal static` returning the target item or `null`. Add `Hypersonic.Tests` coverage (`QueueScrollResolver bounds` test row).
     - Page-side `ScrollQueueToIndex(int)` glue: call the resolver, return on null, call `QueueListView.ScrollIntoView(target, ScrollIntoViewAlignment.Leading)`. **Wrap this in a dispatcher-marshaled callback before passing it to the coordinator** — see the captured-dispatcher example below. The coordinator invokes its `Action<int>` synchronously, so a raw `ScrollQueueToIndex` reference would (a) bypass the layout deferral required for the initial Queue-tab activation, and (b) execute on whatever thread `IPlaybackService.QueueChanged` fires on, violating the "all page-side scroll marshaling goes through `_pageDispatcher`" rule from the captured-dispatcher subsection.
   - Page subscribes to **both** `Player.NowPlayingMetadataChanged` and `_playbackService.QueueChanged` (the page-private `IPlaybackService` field resolved in the constructor — see step 6) while the Queue tab is active and calls `OnQueueRefreshed(Player.EffectiveQueueIndex)` from each. On initial Queue-tab activation, call `OnTabSelected(Queue, Player.EffectiveQueueIndex)`. **Route the attach/detach through a `QueueTabSubscriptionGate` instance** (see "Files to Create" and "Tab-gated subscription lifecycle") — the helper owns the `_subscribed` flag and gates **both** subscriptions atomically via a single `Attach()` / `Detach()` pair. The page constructs one gate instance in its own constructor (after `_playbackService` is resolved), passing `attachAll`/`detachAll` lambdas that wrap the two `+= … +=` and `-= … -=` calls together. The `SelectionChanged` handler calls `_queueTabGate.Attach()` on Queue-tab activation and `_queueTabGate.Detach()` on leaving; `Unloaded` calls `_queueTabGate.Detach()` unconditionally. The helper's flag prevents stacking duplicate handlers across rapid Lyrics → Queue → Lyrics → Queue toggles (each `+=` would otherwise pair with only one `-=`). **Both handlers are separate, second subscriptions to `Player.NowPlayingMetadataChanged` and `_playbackService.QueueChanged`** — the always-on metadata-render subscription added in step 6 must not be touched. The two `Player.NowPlayingMetadataChanged` handlers (the always-on one, plus the tab-gated one wired by the gate's `attachAll`) attach to the same event but have different lifetimes (always-on vs. queue-tab-active) and different work (rebuild metadata vs. scroll `ListView`). Use distinct method names (`OnPlayerMetadataChanged` for always-on, `OnQueueTabMetadataChanged` for the tab-gated one) so an accidental `-=` of the wrong handler is a compile/test failure rather than silent.
   - **Mandatory `Unloaded` cleanup**: the page's `Unloaded` handler MUST: (1) set `_pageActive = false` first, before any `-=` work, so that any callback already enqueued via `_pageDispatcher.TryEnqueue` will short-circuit when it runs (see "Page-active guard for enqueued callbacks"); (2) call `_queueTabGate.Detach()` unconditionally — the gate's idempotent guard handles all three cases (Queue tab currently active, Lyrics tab currently active after a prior Queue visit, Lyrics tab currently active without ever opening Queue); (3) reset the coordinator's active-state flag to `false` (e.g. `_scrollCoordinator.OnTabSelected(NowPlayingTab.Lyrics, -1)` or an explicit `Reset()` method on the coordinator if added); (4) `-=` **both** always-on handlers from step 6 — the `Player.NowPlayingMetadataChanged` metadata-render handler **and** the `ActualThemeChanged` theme-refresh handler. `PlayerViewModel` and `IPlaybackService` are singletons; `NowPlayingPage` is transient (no `NavigationCacheMode="Required"`). Skipping any cleanup keeps the orphaned page instance alive via the event subscriptions, can enqueue scrolls against a no-longer-visible page, and stacks duplicate handlers each time the user navigates away and back. **Note**: do **not** put this cleanup in an `OnNavigatedFrom` method on the page — `INavigationService` does not invoke that method on the page itself (only on the page's `ViewModel`), so it would never run. `Unloaded` is the correct page-side hook.
   - **Wiring sketch** (the `Action<int>` passed to the coordinator wraps the dispatcher marshal, the layout deferral, AND the page-active guard):

     ```csharp
     // Construction:
     _scrollCoordinator = new QueueAutoScrollCoordinator(MarshalAndScroll);

     private void MarshalAndScroll(int effectiveIndex)
     {
         // Always go through the captured page dispatcher: callers may be
         // on any thread (SelectorBar.SelectionChanged is UI-thread, but
         // IPlaybackService.QueueChanged / Player.NowPlayingMetadataChanged
         // may not be), and TryEnqueue also gives the layout pass time to
         // measure the just-shown Queue surface for the initial scroll.
         _pageDispatcher.TryEnqueue(() =>
         {
             if (!_pageActive) return;   // page-active guard, see captured-dispatcher subsection
             ScrollQueueToIndex(effectiveIndex);
         });
     }
     ```

     `TryEnqueue` covers UI-thread marshaling AND a one-tick deferral past the SelectorBar's layout pass; the `_pageActive` check inside the lambda guards against post-`Unloaded` execution. If the initial scroll proves flaky despite this (virtualization needing more than one tick), escalate the *initial* call only to the `Loaded`/`SizeChanged` latch pattern from "Layout-timing wrinkle"; subsequent calls keep using `TryEnqueue`.
9. Create `NowPlayingTapPolicy` (pure helper returning `NowPlayingTapDecision(ShouldNavigate, BackStackIndicesToRemove)`); add `Hypersonic.Tests` coverage for the policy. Add player-bar tap handlers in `ShellPage` for art and title: project `Frame.BackStack` to `IReadOnlyList<Type>`, call `NowPlayingTapPolicy.Decide(hasTrack, currentPageType, backStackPageTypes)`, return early when `ShouldNavigate == false`, apply the returned removals to `Frame.BackStack` in **descending** index order so each `RemoveAt` keeps remaining indices valid, then call `NavigationService.NavigateTo(typeof(NowPlayingViewModel).FullName!)`. Add hover cursor on both elements.
10. Create `LastPagePersistencePolicy` (pure static helper: `GetPersistableViewModelKey(Type? sourcePageType)` returns the VM key string when the page should be persisted to `AppState.Navigation.LastPage`, or `null` when not — `NowPlayingPage`, `null`, and unknown page types all return `null`). Refactor `ShellViewModel.OnNavigated` to call this helper: read the result, write to `AppState.Navigation.LastPage` only when non-null. The existing inline `Replace("Page", "ViewModel")` mapping plus `TopLevelPages.Contains` check collapses into the helper; the `TopLevelPages` `HashSet` on `ShellViewModel` goes away. Add `Hypersonic.Tests` coverage for the helper covering the cases listed in the "No restore as `LastPage` (write side)" test row. **Out of scope**: the read-side `TopLevelPages` `HashSet`s in `DefaultActivationHandler` and `ConnectViewModel` are not consolidated into the helper in this work — the read-side test row stays manual + indirect (smoke #18).
11. Navigation registration (App.xaml.cs, PageService) — **no `TopLevelPages` changes outside of step 10's `ShellViewModel` removal**
12. Automated tests for all areas in the test table
13. Manual smoke tests
