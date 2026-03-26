# Queue Flyout UI — Implementation Details

Companion to [docs/design/queue-flyout-ui.md](../design/queue-flyout-ui.md). This document records what was actually built and implementation specifics not covered in the design doc.

---

## Files Changed

### New files

| File | Purpose |
|---|---|
| `Hypersonic/Models/QueueItem.cs` | Observable wrapper around `PlaybackTrack` with mutable `IsCurrent` |
| `Hypersonic/Converters/BoolToHighlightBrushConverter.cs` | `bool` → accent-tinted `SolidColorBrush` or `null` |
| `Hypersonic/Converters/BoolToFontWeightConverter.cs` | `bool` → `FontWeight` (600 or 400) |

### Modified files

| File | Changes |
|---|---|
| `Hypersonic/Contracts/Services/IPlaybackService.cs` | Added `void JumpToTrack(string trackId)` |
| `Hypersonic/Services/PlaybackQueue.cs` | Added `JumpToTrack(string trackId)` method |
| `Hypersonic/Services/PlaybackService.cs` | Added `JumpToTrack(string trackId)` with event wiring |
| `Hypersonic/ViewModels/PlayerViewModel.cs` | Added queue properties (`QueueItems`, `EffectiveQueueIndex`, `IsQueueOpen`, `IsQueueEmpty`, `QueueCountText`), `RefreshQueue()`, `JumpToQueueTrack()`, `QueueChanged` subscription |
| `Hypersonic/Views/ShellPage.xaml` | Added queue popup XAML, toggle button (7th column in controls grid), two converter registrations in `Page.Resources` |
| `Hypersonic/Views/ShellPage.xaml.cs` | Added popup open/close/positioning, Composition API animation, double-click handler, `IsQueueOpen` PropertyChanged handler, `QueuePopup.Closed` sync handler |
| `Hypersonic.UnitTest/PlaybackQueueTests.cs` | Added 6 JumpToTrack tests |

### Unchanged

- `Hypersonic/Models/PlaybackTrack.cs`
- `Hypersonic/Services/TaskbarService.cs`
- `Hypersonic/App.xaml.cs` (no new DI registrations — converters are page-scoped XAML resources, `QueueItem` is not a service)

---

## JumpToTrack

### PlaybackQueue.JumpToTrack (line 426)

```csharp
public PlaybackTrack? JumpToTrack(string trackId)
{
    var foundIndex = _tracks.FindIndex(t => t.Id == trackId);
    if (foundIndex < 0) return null;
    if (foundIndex == _currentIndex) return null;

    PushCurrentToHistory();
    _currentIndex = foundIndex;

    if (_shuffleEnabled)
    {
        var pos = _shuffleOrder.IndexOf(_currentIndex);
        if (pos >= 0) _shufflePosition = pos;
    }

    return CurrentTrack;
}
```

Lookup is by track ID (`FindIndex` O(n)), consistent with the ID-based navigation history established in the enhanced playback service. `PushCurrentToHistory()` pushes to both navigation history and play history, same as `AdvanceInternal`.

### PlaybackService.JumpToTrack (line 298)

```csharp
public void JumpToTrack(string trackId)
{
    var prevHasNext = HasNext;
    var prevHasPrevious = HasPrevious;
    var result = _playbackQueue.JumpToTrack(trackId);
    if (result == null) return;

    PlayCurrent();
    FirePlaybackStateChangedIfHasChanged(prevHasNext, prevHasPrevious);
}
```

Uses the same snapshot/compare/fire pattern as other service methods. `PlayCurrent()` creates a `MediaSource`, starts playback, updates SMTC, and fires `TrackChanged`.

### Event contract

| Event | Fires on JumpToTrack |
|---|---|
| `TrackChanged` | Always (via `PlayCurrent`) |
| `PlaybackStateChanged` | Only if `HasNext`/`HasPrevious` changed |
| `QueueChanged` | Never — cursor move, not queue mutation |

---

## Queue Popup

### Popup placement

The popup is a child of the `PlayerBar` grid. WinUI `Popup` offsets are relative to the popup's parent in the visual tree, so horizontal/vertical offsets are calculated relative to the player bar.

### Two-pass positioning (ShellPage.xaml.cs `OpenQueuePopup`)

1. `TransformToVisual(this)` maps the toggle button position into page coordinates.
2. `HorizontalOffset` is set to right-align the 400px popup to the button's right edge: `buttonPos.X - 400 + QueueToggleButton.ActualWidth`.
3. `VerticalOffset` is initially set to `-10000` (off-screen) and the popup is opened. This triggers layout so `QueuePopupBorder.ActualHeight` returns the real measured height.
4. `QueuePopupBorder.UpdateLayout()` forces synchronous layout.
5. `VerticalOffset` is set to `-(actualHeight + 8)` — popup sits above the player bar with an 8px gap.

This two-pass approach handles variable-height content (short queues are smaller than `MaxHeight`). The off-screen trick avoids a visible flicker.

### Animation (Composition API)

Open animation only — 200ms fade (0→1) + slide (Y: +8→0) using a cubic bezier easing `(0.1, 0.9), (0.2, 1.0)`. Translation is via `ElementCompositionPreview.SetIsTranslationEnabled` + `Translation.Y` animation, which is additive and does not affect layout.

Close is instant (light-dismiss). Intercepting native light-dismiss for a close animation would require canceling the dismiss, running the animation, then manually closing — deferred as not worth the complexity.

### Light-dismiss sync

`QueuePopup.Closed` sets `Player.IsQueueOpen = false`, which unchecks the `ToggleButton` via the two-way binding. The `PropertyChanged` handler's `else if (QueuePopup.IsOpen)` branch closes the popup when `IsQueueOpen` becomes false through other paths (e.g. programmatic).

### Auto-scroll

After opening and positioning, `QueueListView.ScrollIntoView` scrolls the current track into view with `ScrollIntoViewAlignment.Leading` (positions at top of viewport).

---

## ListView Behavior

### IsItemClickEnabled

`IsItemClickEnabled="True"` is set on the `ListView` even though no `ItemClick` handler is used. This is required to activate WinUI 3 `ListViewItem` PointerOver visual states — without it, items show no hover highlight regardless of other settings. `SelectionMode="None"` is also set (no selection, just hover feedback).

### ItemContainerStyle

```xml
<Style TargetType="ListViewItem">
    <Setter Property="Padding" Value="0"/>
    <Setter Property="MinHeight" Value="0"/>
    <Setter Property="CornerRadius" Value="0"/>
    <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
</Style>
```

- `Padding="0"` / `MinHeight="0"` — removes default container chrome; the `DataTemplate` grid controls its own 40px height and 8,4 padding.
- `CornerRadius="0"` — default `ListViewItem` has rounded hover/pressed highlights; the queue uses full-width square highlights.
- `HorizontalContentAlignment="Stretch"` — ensures the template grid spans the full container width (required for the full-width accent background to work).

### WinUI 3 binding constraints in DataTemplate

| Element | Binding | Why |
|---|---|---|
| `Image.Source` (cover art) | `{Binding Track.CoverArtUri}` | `x:Bind` cannot resolve the `Uri` → `ImageSource` type coercion at compile time |
| `Run.Text` (artist, separator, album) | `{Binding Track.Artist}` etc. | WinUI 3's `x:Bind` does not support `Run.Text` — the XAML compiler crashes (MSB3073 exit code 1, no useful error) |
| `IsCurrent` (background, font weight) | `{x:Bind IsCurrent, Mode=OneWay}` | Observable property — `Mode=OneWay` required for in-place updates |
| All other properties | `{x:Bind ...}` | Compile-time safe, default one-time mode sufficient for immutable track data |

### Double-click handler

`DoubleTapped` fires on the hit-tested element inside the template (e.g. a `TextBlock`), not on the `ListViewItem`. The handler walks up `VisualTreeHelper.GetParent` until it finds a `FrameworkElement` with `DataContext is QueueItem`. Typically 2–4 parents. Does not use `IndexFromContainer`.

---

## In-Place Queue Refresh

`PlayerViewModel.RefreshQueue()` uses two paths:

### Path 1 — In-place update (cursor moved, queue unchanged)

Compares the existing `QueueItems` list against the effective queue by track ID at each position. If counts match and all IDs match in order, toggles `IsCurrent` on the existing `QueueItem` objects. Because `QueueItem.IsCurrent` is `[ObservableProperty]` (fires `PropertyChanged`), the XAML bindings update without replacing `ItemsSource`.

This preserves the `ListView`'s `ScrollViewer` position. Without this, double-click-to-jump would reset scroll to top on every track change.

### Path 2 — Full rebuild (queue contents changed)

If counts differ or any track ID mismatches, builds a new `List<QueueItem>` and assigns it to `QueueItems`. The `ListView` re-binds entirely. Scroll position resets — expected when the list order itself changed (e.g. shuffle toggle).

### Call sites

- `OnTrackChanged` — cursor moved (path 1 in most cases)
- `OnQueueChanged` — queue mutated (path 2 in most cases)
- `OpenQueuePopup` via `Player.RefreshQueue()` — ensures fresh data before display

---

## Converters

### BoolToHighlightBrushConverter

```csharp
if (value is true)
{
    var accentColor = (Windows.UI.Color)Application.Current.Resources["SystemAccentColorLight2"];
    return new SolidColorBrush(accentColor) { Opacity = 0.2 };
}
return null!;
```

- Reads `SystemAccentColorLight2` from app resources at convert time (not cached — a new brush is created each call, but this is infrequent enough to not matter).
- Returns `null!` (not `Transparent`) for the false case. A `Transparent` brush occupies the `Background` property and occludes the `ListViewItem` container's built-in PointerOver highlight. `null` leaves `Background` unset so hover visuals show through.
- The `null!` suppresses the nullable warning; WinUI handles null converter returns correctly (clears the property to its default).

### BoolToFontWeightConverter

```csharp
return value is true
    ? new FontWeight { Weight = 600 }   // SemiBold
    : new FontWeight { Weight = 400 };  // Normal
```

Uses raw weight values instead of `FontWeights.SemiBold` / `FontWeights.Normal` — both are equivalent. `FontWeight` is a struct, so new instances are cheap.

---

## Toggle Button

The queue toggle is a `ToggleButton` (not a `Button`) with `IsChecked="{x:Bind Player.IsQueueOpen, Mode=TwoWay}"`. The `PropertyChanged` handler reacts to `IsQueueOpen` changes to open/close the popup.

No `RelayCommand` is needed — the two-way binding toggles `IsQueueOpen` directly. The `ToggleButton`'s built-in visual states handle checked/unchecked appearance (accent background when checked, subtle when unchecked) across all interaction states (normal, hover, pressed).

---

## Test Coverage

### JumpToTrack unit tests

All tests in `Hypersonic.UnitTest/PlaybackQueueTests.cs`:

| Test | Verifies |
|---|---|
| `JumpToTrack_MovesToCorrectTrack` | `CurrentTrack` matches target after jump |
| `JumpToTrack_PushesHistoryEntry` | Pre-jump track is in navigation history |
| `JumpToTrack_PreservesShuffleOrder` | Effective queue order unchanged; `_shufflePosition` updated |
| `JumpToTrack_InvalidId_NoOp` | No state change for nonexistent track ID |
| `JumpToTrack_SameTrack_NoOp` | Returns null, no history push |
| `JumpToTrack_GoBackReturnsToPreJump` | `GoBack()` after jump returns to pre-jump track |

Run: `dotnet test Hypersonic.UnitTest/Hypersonic.UnitTest.csproj`

### Testing gaps

- **No UI-layer tests.** The popup positioning, animation, scroll preservation, and converter behavior are verified only by manual testing. These depend on the WinUI runtime.
- **No service-level JumpToTrack event tests.** The event-firing pattern (TrackChanged always, PlaybackStateChanged conditionally, QueueChanged never) is verified by code review and manual testing. Service-level tests would require the WinUI runtime.
- **RefreshQueue in-place path** is not unit-tested. It depends on `[ObservableProperty]` code generation and `ListView` binding behavior, neither of which can be tested outside WinUI.

---

## Known Limitations

1. **No drag-to-reorder** — `PlaybackQueue.MoveTrack` exists but is not wired to the queue popup. Would require `CanReorderItems` + `ReorderMode` on the `ListView` and a `Drop` handler.
2. **No remove-from-queue** — `PlaybackQueue.RemoveAt` exists but no swipe-to-delete or context menu is implemented on queue items.
3. **No close animation** — light-dismiss close is instant. An animated close would require intercepting the native dismiss, running a Composition animation, then manually closing the popup.
4. **No queue persistence** — the queue popup shows the in-memory session queue only. Subsonic `savePlayQueue` / `getPlayQueue` API integration is not implemented.
5. **Cover art creates a new BitmapImage per item** — the `{Binding Track.CoverArtUri}` in the `DataTemplate` creates a `BitmapImage` for each visible item. No shared image cache exists at the UI layer (the HTTP layer may cache responses).
6. **Converter creates a new SolidColorBrush per call** — `BoolToHighlightBrushConverter` allocates on each convert. Queue lists are small enough that this is not a performance concern.
