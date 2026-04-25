# Now-Playing Track Highlight in Track Lists

## Overview

When a track is playing or paused, track lists across the app (album detail, playlist detail, artist top songs) show no visual indication of which track is current. The queue popup in ShellPage already highlights the current track with an accent background and SemiBold text, but the browsing pages don't — the user has to rely on the player bar alone to know what's playing. This creates a disconnect when browsing an album they're currently listening to.

**Goal**: Visually distinguish the now-playing track in every track list view, with distinct states for playing vs. paused.

## Background

### Current queue popup highlighting

The queue popup in `ShellPage.xaml` already implements now-playing highlighting via two mechanisms:

1. **Background tint** — `BoolToHighlightBrushConverter` returns `SystemAccentColorLight2` at 20% opacity when `QueueItem.IsCurrent` is true (`ShellPage.xaml:355`).
2. **Font weight** — `BoolToFontWeightConverter` returns SemiBold (600) for the current track's title (`ShellPage.xaml:377`).

The `QueueItem` model has an `IsCurrent` observable property, updated by `PlayerViewModel.RefreshQueue()` based on the current queue index. This approach works for the queue because `QueueItem` is a presentation wrapper around `PlaybackTrack` — it's owned by the PlayerViewModel and lives only as long as the queue popup is rendered.

### Track list architecture

Track lists in browsing pages use `SongInfo` objects (not `QueueItem`). Each page has its own ViewModel and collection:

| Page | ViewModel | Collection | Item type |
|------|-----------|------------|-----------|
| AlbumDetailPage | AlbumDetailViewModel | `TrackListItems` (`ObservableCollection<object>`) | `SongInfo` (mixed with `DiscHeaderItem`) |
| PlaylistDetailPage | PlaylistDetailViewModel | `TrackListItems` (`ObservableCollection<object>`) | `SongInfo` |
| ArtistDetailPage | ArtistDetailViewModel | `TopSongs` (`ObservableCollection<SongInfo>`) | `SongInfo` |

All three ViewModels have `IPlaybackService` injected but none subscribe to playback events — they only call `PlayQueue()` to start playback.

`SongInfo` (`Hypersonic/Models/SongInfo.cs`) extends `ObservableObject` and has an `Id` property (string) that can be matched against `PlaybackService.NowPlaying?.Id`.

### Playback state tracking

`IPlaybackService` exposes:
- `NowPlaying` (`PlaybackTrack?`) — the currently loaded track, with an `Id` property
- `IsPlaying` (`bool`) — whether audio is actively playing (vs. paused or stopped)
- `TrackChanged` event — fires when the current track changes
- `PlaybackStateChanged` event — fires on play/pause/stop transitions

### Existing converters

| Converter | Input | Output | Registered in |
|-----------|-------|--------|---------------|
| `BoolToVisibilityConverter` | `bool` | `Visible` / `Collapsed` | `Themes/Generic.xaml` (global) |
| `InverseBoolToVisibilityConverter` | `bool` | `Collapsed` / `Visible` | `Themes/Generic.xaml` (global) |
| `BoolToHighlightBrushConverter` | `bool` | `SolidColorBrush(SystemAccentColorLight2, 0.2)` / `null` | `ShellPage.xaml` (local) |
| `BoolToFontWeightConverter` | `bool` | `SemiBold (600)` / `Normal (400)` | `ShellPage.xaml` (local) |

All four converter classes already exist. However, `BoolToHighlightBrushConverter` and `BoolToFontWeightConverter` are currently declared only in `ShellPage.xaml` (lines 20-21) as page-local resources — they are **not** in `Themes/Generic.xaml`. The target pages (AlbumDetailPage, PlaylistDetailPage, ArtistDetailPage) will not resolve `{StaticResource ...}` references to these converters unless they are promoted to app-global scope. This design requires moving both registrations into `Themes/Generic.xaml` alongside the other global converters (see §0 below).

### Existing animation patterns

The app uses Composition API animations in `MarqueeTextBlock` (`Hypersonic/Controls/MarqueeTextBlock.cs`): `ElementCompositionPreview.GetElementVisual()`, `ScalarKeyFrameAnimation`, `ExpressionAnimation`, and `IterationBehavior.Forever`. This establishes the Composition API as the animation approach for custom controls.

### Track number column structure

All three track list templates use a similar first column for the track/position number:

- **AlbumDetailPage** — 40px wide, right-aligned TextBlock showing `TrackNumber`
- **PlaylistDetailPage** — 40px wide, right-aligned TextBlock showing `PositionNumber`
- **ArtistDetailPage** — 40px wide, right-aligned TextBlock showing `PositionNumber`

These columns are widened to 56px by this design to accommodate both the equalizer bars (left-aligned) and the track/position number (right-aligned) side by side. The corresponding column header widths in each page must also be updated to match.

## Design

### Visual states

| State | Track # column (56px) | Row background | Title text weight |
|-------|----------------------|----------------|-------------------|
| **Not playing** | Normal number (right-aligned) | None | Normal (400) |
| **Paused / Stopped** | Frozen equalizer bars + number | Accent tint (20% opacity) | Normal (400) |
| **Playing** | Animated equalizer bars + number | Accent tint (20% opacity) | SemiBold (600) |

The track number column is widened from the original 40px to 56px to accommodate both the equalizer bars (left-aligned) and the track/position number (right-aligned) side by side. The track number remains visible in all states so the user always knows which position the track occupies. The equalizer bars appear alongside the number when the track is now-playing — animated when playing, frozen when paused/stopped.

The accent tint background is the same `SystemAccentColorLight2` at 20% opacity used in the queue popup, which is visually distinct from the `ListViewItem` hover state (`SubtleFillColorSecondaryBrush`).

**Stopped / end-of-queue behavior**: `PlaybackService.Stop()` and natural end-of-queue both keep `NowPlaying` set (the last track remains) and only flip `IsPlaying` to `false` (`PlaybackService.cs:164-173`, `PlaybackService.cs:447-472`). With the proposed `IsNowPlaying = (song.Id == NowPlaying?.Id)` logic, stopped state renders identically to paused state — frozen equalizer bars, accent background, normal text weight. This is the intended behavior: the highlight persists as a "last played" indicator until the user starts a different track or clears the queue. This matches how the existing queue popup behaves (it continues to highlight the last track after playback stops). `NowPlaying` only becomes `null` when the queue is explicitly cleared, at which point all highlights disappear.

### 0. Promote highlight converters to global scope

`BoolToHighlightBrushConverter` and `BoolToFontWeightConverter` are currently declared as page-local resources in `ShellPage.xaml` (lines 20-21). Move both registrations into `Themes/Generic.xaml` alongside the other global converters (`BoolToVisibilityConverter`, `InverseBoolToVisibilityConverter`, etc.), then remove the local declarations from `ShellPage.xaml`.

This is a zero-behavior-change refactor — `ShellPage` will resolve the converters from the global scope instead of its local resources, and the three target pages gain access.

```xml
<!-- Themes/Generic.xaml — add alongside existing converter registrations -->
<converters:BoolToHighlightBrushConverter x:Key="BoolToHighlightBrushConverter"/>
<converters:BoolToFontWeightConverter x:Key="BoolToFontWeightConverter"/>
```

### 1. Add observable now-playing properties to `SongInfo`

Add two `[ObservableProperty]` properties to `SongInfo`:

```csharp
// Hypersonic/Models/SongInfo.cs

[ObservableProperty]
public partial bool IsNowPlaying { get; set; }

[ObservableProperty]
public partial bool IsPlaying { get; set; }
```

- `IsNowPlaying` — true when this song's `Id` matches `PlaybackService.NowPlaying?.Id` (covers both playing and paused states)
- `IsPlaying` — true when `IsNowPlaying && PlaybackService.IsPlaying` (actively playing, not paused)

These drive all XAML bindings:
- Background highlight: bound to `IsNowPlaying`
- Font weight: bound to `IsPlaying`
- Equalizer visibility: bound to `IsNowPlaying`
- Equalizer animation: bound to `IsPlaying`

### 2. Create `NowPlayingTracker` helper class

A helper class that encapsulates the event subscription and state synchronization logic, eliminating duplication across the three ViewModels. Each ViewModel creates an instance and delegates to it.

```csharp
// Hypersonic/Helpers/NowPlayingTracker.cs (new file)

namespace Hypersonic.Helpers;

public class NowPlayingTracker
{
    private readonly IPlaybackService _playbackService;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly Func<IEnumerable<SongInfo>> _getSongs;
    private bool _isActive;
    private string? _lastNowPlayingId;
    private bool _lastIsPlaying;

    public NowPlayingTracker(
        IPlaybackService playbackService,
        Func<IEnumerable<SongInfo>> getSongs);

    public void Subscribe();       // hooks events, sets _isActive, calls Refresh(force: true)
    public void Unsubscribe();     // unhooks events, clears _isActive, clears all songs
    public void Refresh();         // no-op if effective state unchanged; skips if !_isActive
}
```

**`Subscribe()`** — Hooks `TrackChanged` and `PlaybackStateChanged` events on the playback service, then immediately calls `Refresh()` with a force flag that bypasses the cache check. The immediate refresh handles the case where the user navigates to a page while a track is already playing — the highlight appears on first render. Subscribe is a no-op if already subscribed (idempotent).

**`Unsubscribe()`** — Unhooks both events and clears all song states (sets `IsNowPlaying` and `IsPlaying` to `false` on every song). Called from `OnNavigatedFrom()`. Unsubscribe is a no-op if not subscribed (idempotent).

**`Refresh()`** — Core synchronization logic:
1. Read `_playbackService.NowPlaying?.Id` and `_playbackService.IsPlaying`
2. Compare against cached `(_lastNowPlayingId, _lastIsPlaying)` — if both match, return early (no-op)
3. Update the cache
4. Iterate all songs from `_getSongs()`
5. For each song: set `IsNowPlaying = (song.Id == nowPlayingId)`, `IsPlaying = (isNowPlaying && isPlaying)`

The effective-state cache is critical because `PlaybackStateChanged` fires on many unrelated state changes — volume, mute, shuffle, and repeat mode transitions all invoke this event (`PlaybackService.cs` lines 48, 65, 76, 91). Without the cache, every volume slider tick would iterate the entire song list. The cache reduces this to two field comparisons.

The `_getSongs` lambda is provided by each ViewModel, pointing to its song collection. This allows the tracker to work with different collection shapes without coupling to specific ViewModel types.

**Threading**: The tracker must always marshal `Refresh()` onto the UI thread. `PlaybackService` raises some events through `DispatcherQueue.TryEnqueue()` (e.g., `OnMediaEnded`, `OnCurrentStateChanged`), but also raises `PlaybackStateChanged` synchronously from property setters like `Volume`, `ShuffleEnabled`, `IsMuted`, and from methods like `PlayQueue()` and `ClearAll()`. These synchronous call sites may execute on the UI thread in practice, but the contract does not guarantee it. Since `Refresh()` mutates XAML-bound `ObservableProperty` values on `SongInfo`, it must run on the UI thread unconditionally.

The tracker captures the dispatcher internally via `DispatcherQueue.GetForCurrentThread()` in its constructor — this works because the tracker is always instantiated on the UI thread (from ViewModel async load methods that originate from `OnNavigatedTo`). Event handlers dispatch through it:

```csharp
private readonly DispatcherQueue _dispatcherQueue;

private void OnTrackChanged(object? sender, EventArgs e)
{
    _dispatcherQueue.TryEnqueue(() => Refresh());
}
```

This matches the pattern used by `PlaybackService` itself for `OnMediaEnded` (`PlaybackService.cs:449`).

**Async navigation race guard**: The current detail-page load methods (`LoadAlbumAsync`, `LoadPlaylistAsync`, `LoadArtistAsync`) are fire-and-forget (`async void OnNavigatedTo`). If the user navigates away before the load completes, `OnNavigatedFrom()` runs first, but the async continuation can still execute and call `Subscribe()` — leaving the ViewModel rooted by playback event handlers after the page is gone.

To prevent this, the tracker includes an `_isActive` flag:
- `Subscribe()` sets `_isActive = true` before hooking events
- `Unsubscribe()` sets `_isActive = false` before unhooking events
- `Refresh()` checks `_isActive` at entry and returns early if false

The ViewModel wiring pattern becomes:

```csharp
// In LoadXxxAsync(), after populating songs:
_nowPlayingTracker?.Unsubscribe();  // clean up any previous tracker
_nowPlayingTracker = new NowPlayingTracker(_playbackService, () => _allSongs);
_nowPlayingTracker.Subscribe();

// In OnNavigatedFrom():
_nowPlayingTracker?.Unsubscribe();
```

The key ordering guarantee: `OnNavigatedFrom()` always runs on the UI thread before the page is torn down, and it calls `Unsubscribe()` which sets `_isActive = false`. If the async load continuation runs after that, `Subscribe()` would set `_isActive = true` and hook events — but since `OnNavigatedFrom()` already ran, there won't be a second `OnNavigatedFrom()` to clean up. To close this gap, the ViewModels also set a `_navigatedAway` flag in `OnNavigatedFrom()` and check it before calling `Subscribe()`:

```csharp
private bool _navigatedAway;

public void OnNavigatedTo(object parameter)
{
    _navigatedAway = false;  // reset for this navigation lifetime
    // ... existing load logic
}

public void OnNavigatedFrom()
{
    _navigatedAway = true;
    _nowPlayingTracker?.Unsubscribe();
}

// In LoadXxxAsync(), after populating songs:
if (_navigatedAway) return;
_nowPlayingTracker?.Unsubscribe();
_nowPlayingTracker = new NowPlayingTracker(_playbackService, () => _allSongs);
_nowPlayingTracker.Subscribe();
```

This two-layer guard ensures no tracker is subscribed after navigation away, regardless of async timing.

### 3. Create `EqualizerBars` custom control

A lightweight custom control rendering three animated vertical bars using the Composition API, following the same WinUI custom-control pattern as `MarqueeTextBlock`.

```csharp
// Hypersonic/Controls/EqualizerBars.cs (new file)
```

**Class structure** (WinUI custom control boilerplate):
- Inherits from `Control`
- Constructor sets `DefaultStyleKey = typeof(EqualizerBars)` (required for Generic.xaml template resolution)
- Overrides `OnApplyTemplate()` to resolve template children (`PART_Bar1`, `PART_Bar2`, `PART_Bar3`)
- Subscribes to `Loaded` and `Unloaded` events for animation lifecycle
- `Unloaded` handler stops all running animations and nulls out Composition objects to avoid leaks

**Properties**:
- `IsAnimating` (`DependencyProperty`, `bool`) — bound to `SongInfo.IsPlaying`. Controls whether bars animate or freeze.

**Template** (added to `Themes/Generic.xaml`):
- Three `Border` elements (`PART_Bar1`, `PART_Bar2`, `PART_Bar3`) arranged in a horizontal `Grid`
- Each bar: 3px wide, 1.5px gap between bars, fill color `SystemAccentColorLight2`
- Container: 16x16 pixels (fits comfortably in the widened 56px column alongside the track number)
- Bars anchored at bottom of the container, with varying initial heights

**Animation approach** (Composition API):
1. On `OnApplyTemplate()`, resolve the three bar `Border` elements
2. On `Loaded`, get each bar's `Visual` via `ElementCompositionPreview.GetElementVisual()`
3. Set each visual's `CenterPoint` to its bottom edge so the scale transform grows upward
4. Create a `Vector3KeyFrameAnimation` on the `Scale` property for each bar, animating `Scale.Y` between a minimum and maximum value
5. Each bar uses a different animation duration for an organic look:
   - Bar 1: ~400ms
   - Bar 2: ~550ms
   - Bar 3: ~700ms
6. `IterationBehavior = AnimationIterationBehavior.Forever`
7. Use `CubicBezierEasingFunction` for smooth acceleration/deceleration

**State transitions**:
- `IsAnimating` → `true`: Call `visual.StartAnimation("Scale", animation)` on each bar
- `IsAnimating` → `false`: Call `visual.StopAnimation("Scale")` on each bar — Composition API preserves the last interpolated value, so bars freeze at their current height

**Initial state on template ready**: A page can load while playback is already active, meaning `IsAnimating` may already be `true` before the template visuals exist. The `IsAnimating` property-changed callback fires during XAML binding setup, but the bar visuals aren't resolved yet at that point — the callback would silently no-op. To handle this, after `OnApplyTemplate()` resolves the bar elements and `Loaded` creates the animations, the control must explicitly apply the current `IsAnimating` value once:

```csharp
private void OnLoaded(object sender, RoutedEventArgs e)
{
    SetupAnimations();  // create visuals + animations
    ApplyAnimationState(IsAnimating);  // apply current value
}
```

This ensures bars start animating immediately when the control first renders with `IsAnimating = true`, rather than waiting for a subsequent property change that may never come.

**Lifecycle**:
- `Loaded`: Creates Composition visuals and animations, applies current `IsAnimating` state
- `Unloaded`: Stops all running animations, detaches event handlers, nulls out Composition objects (`Visual`, `Animation` references) to prevent leaks from the Composition tree outliving the XAML element
- If the control is unloaded and reloaded (e.g., `ListView` virtualization), `OnApplyTemplate()` re-acquires template children; `Loaded` recreates animations and re-applies current state

### 4. Wire up `AlbumDetailViewModel`

```
// Hypersonic/ViewModels/AlbumDetailViewModel.cs
```

- Add fields: `private NowPlayingTracker? _nowPlayingTracker;` and `private bool _navigatedAway;`
- At the start of `OnNavigatedTo()` (line 98): reset `_navigatedAway = false;`
- After `_allSongs` is populated in `LoadAlbumAsync()` (after the LINQ `.ToList()` on line 166): guard and subscribe
  ```csharp
  if (_navigatedAway) return;
  _nowPlayingTracker?.Unsubscribe();
  _nowPlayingTracker = new NowPlayingTracker(_playbackService, () => _allSongs);
  _nowPlayingTracker.Subscribe();
  ```
- In `OnNavigatedFrom()` (currently empty, line 106-108): set guard and unsubscribe
  ```csharp
  _navigatedAway = true;
  _nowPlayingTracker?.Unsubscribe();
  ```

The `_navigatedAway` guard prevents a late-arriving async continuation from subscribing after the page is gone. The `() => _allSongs` lambda captures the field reference, so if `LoadAlbumAsync` runs again on the same ViewModel instance (unlikely given transient registration), the tracker reads the updated list.

### 5. Wire up `PlaylistDetailViewModel`

```
// Hypersonic/ViewModels/PlaylistDetailViewModel.cs
```

Same pattern as AlbumDetailViewModel:
- Add `_nowPlayingTracker` and `_navigatedAway` fields
- At the start of `OnNavigatedTo()`: reset `_navigatedAway = false;`
- After `_allSongs` is populated in `LoadPlaylistAsync()` (after the population loop, line 116): guard check, then create and subscribe
- In `OnNavigatedFrom()` (currently empty, line 66-68): set `_navigatedAway = true`, then unsubscribe

### 6. Wire up `ArtistDetailViewModel`

```
// Hypersonic/ViewModels/ArtistDetailViewModel.cs
```

The artist page has two separate song lists (`_communityTopSongs` and `_personalTopSongs`) that swap based on `SelectedTopSongsMode`. Both lists persist in memory even when not displayed — the non-active list's songs are just not in the `TopSongs` observable collection.

The tracker's `_getSongs` lambda must cover both lists so that when the user switches modes, the previously-hidden list already has the correct state:

- Add `_nowPlayingTracker` and `_navigatedAway` fields
- At the start of `OnNavigatedTo()`: reset `_navigatedAway = false;`
- After top songs are loaded in `LoadTopSongsAsync()` (after `HasTopSongs = true`, line 391): guard check, then create and subscribe
  ```csharp
  if (_navigatedAway) return;
  _nowPlayingTracker?.Unsubscribe();
  _nowPlayingTracker = new NowPlayingTracker(
      _playbackService,
      () => _communityTopSongs.Concat(_personalTopSongs));
  _nowPlayingTracker.Subscribe();
  ```
- In `OnNavigatedFrom()` (currently empty, line 135-137): set `_navigatedAway = true`, then unsubscribe

### 7. Update `AlbumDetailPage.xaml` track template

Add the controls namespace declaration to the page root element (not currently present):

```xml
xmlns:controls="using:Hypersonic.Controls"
```

In the TrackTemplate DataTemplate, three changes:

**a) Track number column** — Widen the first column from 40px to 56px and replace the single `TextBlock` layout with a `Grid` containing both the equalizer and the number side by side:

```xml
<Grid Grid.Column="0" Width="56">
    <controls:EqualizerBars
               HorizontalAlignment="Left" Margin="8,0,0,0" VerticalAlignment="Center"
               IsAnimating="{x:Bind IsPlaying, Mode=OneWay}"
               Visibility="{x:Bind IsNowPlaying, Mode=OneWay,
                   Converter={StaticResource BoolToVisibilityConverter}}"/>
    <TextBlock Text="{x:Bind TrackNumber}"
               HorizontalAlignment="Right" VerticalAlignment="Center"
               Foreground="{ThemeResource TextFillColorSecondaryBrush}"/>
</Grid>
```

The track number is always visible (right-aligned). When `IsNowPlaying` is true, the equalizer bars appear left-aligned alongside the number (animating or frozen based on `IsPlaying`). The column header width must also be updated from 40px to 56px to match.

**b) Row background** — Add to the row's root `Grid`:

```xml
Background="{x:Bind IsNowPlaying, Mode=OneWay,
    Converter={StaticResource BoolToHighlightBrushConverter}}"
```

**c) Title font weight** — Add to the title `TextBlock`:

```xml
FontWeight="{x:Bind IsPlaying, Mode=OneWay,
    Converter={StaticResource BoolToFontWeightConverter}}"
```

### 8. Update `PlaylistDetailPage.xaml` track template

Add `xmlns:controls="using:Hypersonic.Controls"` to the page root element.

Same three changes as AlbumDetailPage, including widening the first column from 40px to 56px. The track number column shows `PositionNumber` instead of `TrackNumber`:

```xml
<TextBlock Text="{x:Bind PositionNumber}" ... />
```

### 9. Update `ArtistDetailPage.xaml` top songs template

Add `xmlns:controls="using:Hypersonic.Controls"` to the page root element.

Same three changes, including widening the first column from 40px to 56px. The track number column shows `PositionNumber`.

## Edge cases

| Scenario | Behavior |
|----------|----------|
| Navigate to page with track already playing | `Subscribe()` calls `Refresh()` immediately — highlight appears on first render |
| Track changes while on page | `TrackChanged` event fires → `Refresh()` clears old highlight, sets new one |
| Play/pause toggle | `PlaybackStateChanged` fires → `Refresh()` toggles `IsPlaying` (animation + bold), `IsNowPlaying` unchanged (background stays) |
| Playing track not in current view | All songs have `IsNowPlaying = false` — no visual change |
| Duplicate track IDs in list | All instances with same ID get highlighted — correct behavior (same song appears multiple times) |
| Natural end-of-queue | `NowPlaying` stays set, `IsPlaying` becomes `false` → renders as paused/stopped state (frozen bars + highlight, no bold). Same as the queue popup — highlight persists as a "last played" indicator |
| `Stop()` called | Same as end-of-queue: `NowPlaying` stays set, `IsPlaying` false → paused/stopped state |
| `ClearAll()` / remove last queue item | `NowPlaying` becomes `null`, `TrackChanged` fires → all highlights cleared |
| Disc collapse/expand (album detail) | Same `SongInfo` instances are removed from and re-added to `TrackListItems` — observable properties are preserved on the objects |
| Top songs mode switch (artist) | Both `_communityTopSongs` and `_personalTopSongs` are tracked — switching modes shows pre-computed state |
| Same album loaded twice | `OnNavigatedTo` → `LoadAlbumAsync` creates new `_allSongs` and new tracker; old tracker is unsubscribed in `OnNavigatedFrom` of the previous navigation |

## Test coverage

### `NowPlayingTracker` unit tests

Add to `Hypersonic.Tests` using `[UITestMethod]` (requires UI thread for `DispatcherQueue`). Tests use a fake `IPlaybackService` that exposes settable `NowPlaying`, `IsPlaying` properties and manually invokable events.

| Test | Scenario |
|------|----------|
| `InitialRefresh_HighlightsMatchingTrack` | Subscribe when a track is already playing → matching `SongInfo` has `IsNowPlaying = true`, `IsPlaying = true` |
| `InitialRefresh_NoTrackPlaying` | Subscribe when `NowPlaying` is null → all songs have `IsNowPlaying = false` |
| `TrackChanged_UpdatesHighlight` | Fire `TrackChanged` with a new track ID → old song cleared, new song highlighted |
| `PlayPause_TogglesIsPlaying` | Fire `PlaybackStateChanged` with `IsPlaying` flipped → `IsNowPlaying` unchanged, `IsPlaying` toggles |
| `UnrelatedStateChange_NoOp` | Fire `PlaybackStateChanged` without changing `NowPlaying` or `IsPlaying` (e.g., volume) → no song properties mutated (effective-state cache short-circuits) |
| `ClearAll_ClearsHighlight` | Set `NowPlaying` to null and fire `TrackChanged` → all songs cleared |
| `Unsubscribe_ClearsAllSongs` | Call `Unsubscribe()` → all songs have `IsNowPlaying = false`, `IsPlaying = false` |
| `EventAfterUnsubscribe_NoOp` | Call `Unsubscribe()`, then fire `TrackChanged` → no song properties change (event handlers detached) |
| `SubscribeAfterNavigatedAway_NotReached` | Simulate: set `_navigatedAway = true`, then attempt subscribe in async continuation → subscribe not called (ViewModel-level guard, tested at ViewModel level or as integration) |

### Fake `IPlaybackService`

A minimal test double with:
- Settable `NowPlaying` and `IsPlaying` properties
- Public `RaiseTrackChanged()` and `RaisePlaybackStateChanged()` methods to fire events on demand
- Other members (`PlayQueue`, `Volume`, etc.) throw `NotImplementedException`

This fake is simple enough to hand-write without a mocking library, consistent with the project's existing test approach.

## Files to modify

| File | Change |
|------|--------|
| `Hypersonic/Models/SongInfo.cs` | Add `IsNowPlaying` and `IsPlaying` observable properties |
| `Hypersonic/Helpers/NowPlayingTracker.cs` | **New** — shared now-playing synchronization helper |
| `Hypersonic/Controls/EqualizerBars.cs` | **New** — animated equalizer bars custom control |
| `Hypersonic/Themes/Generic.xaml` | Add `EqualizerBars` control template/style; add `BoolToHighlightBrushConverter` and `BoolToFontWeightConverter` global registrations (promoted from ShellPage) |
| `Hypersonic/Views/ShellPage.xaml` | Remove local `BoolToHighlightBrushConverter` and `BoolToFontWeightConverter` declarations (now global) |
| `Hypersonic/ViewModels/AlbumDetailViewModel.cs` | Create and manage `NowPlayingTracker` with `_navigatedAway` guard |
| `Hypersonic/ViewModels/PlaylistDetailViewModel.cs` | Create and manage `NowPlayingTracker` with `_navigatedAway` guard |
| `Hypersonic/ViewModels/ArtistDetailViewModel.cs` | Create and manage `NowPlayingTracker` with `_navigatedAway` guard |
| `Hypersonic/Views/AlbumDetailPage.xaml` | Track number column update (equalizer + number side by side, 56px), background highlight, title font weight; add `xmlns:controls` |
| `Hypersonic/Views/PlaylistDetailPage.xaml` | Track number column update (equalizer + number side by side, 56px), background highlight, title font weight; add `xmlns:controls` |
| `Hypersonic/Views/ArtistDetailPage.xaml` | Track number column update (equalizer + number side by side, 56px), background highlight, title font weight; add `xmlns:controls` |
| `Hypersonic.Tests/Helpers/NowPlayingTrackerTests.cs` | **New** — tracker unit tests with fake `IPlaybackService` |
| `Hypersonic.Tests/Fakes/FakePlaybackService.cs` | **New** — test double for `IPlaybackService` |

## Scope exclusions

- **Queue popup** — Already has its own highlighting via `QueueItem.IsCurrent`. No change needed; it uses a different model (`QueueItem` vs `SongInfo`) and a different visual design (no equalizer bars, simpler layout).
- **Search results** — Intentionally out of scope for this design. The search popup uses `SearchSuggestionItem` (`Hypersonic.Core/Models/SearchSuggestionItem.cs`), not `SongInfo`, and renders results through `ShellViewModel` with a different layout (3-column popup with Artists | Albums | Songs). Adding now-playing highlighting to search results would require a separate tracking mechanism on `SearchSuggestionItem` and different visual treatment for the compact popup layout. This can be addressed in a follow-up change if needed.
- **Home page carousels** — These show album cards, not individual tracks. "Now playing" at the album level is a different feature.
