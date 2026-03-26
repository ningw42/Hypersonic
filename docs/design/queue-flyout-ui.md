# Queue Flyout UI — Design Doc

## Context

The enhanced playback service (implemented in the previous milestone) added queue manipulation, shuffle, repeat, and history — but no UI to view the current queue. Users have no way to see what's coming up next. This design adds a floating queue panel that surfaces the current playback queue as a lightweight, dismissable overlay anchored to the player bar.

---

## Scope (P0)

1. **Queue list**: Shows all tracks in **effective (shuffle-aware) playback order** with per-track cover art, track name, album name, artist name, and duration.
2. **Toggle button**: On the player bar, between the progress bar's duration text and the volume controls.
3. **Floating panel**: A `Popup` that appears above the player bar, anchored to the toggle button. Light-dismiss (closes on outside click). Composition API fade+slide animation on manual toggle.
4. **Now-playing highlight**: The currently playing track is visually distinguished via data-driven `IsCurrent` state on the bound row model, styled in XAML.
5. **Double-click to play**: Double-clicking a track in the queue jumps playback to that track within the current queue session, preserving navigation history and shuffle state. The bound row model carries the track ID; the jump is by ID, not by index or container position.

### Semantic invariant

The queue list, the now-playing highlight, and the double-click jump target all operate on the **effective queue** (`IPlaybackService.GetEffectiveQueue()` / `EffectiveQueueIndex`). In shuffle mode this is the shuffled order; without shuffle it is the insertion order. This is stated once here and assumed throughout.

---

## Dependency: Playback Service Jump API

The queue flyout requires an in-session cursor jump that does **not** exist today.

### Problem

`PlayQueue(tracks, startIndex, source)` replaces the entire queue session: it clears navigation history, resets shuffle order, and re-initializes source context. Using it to implement "jump to row in the queue UI" would destroy session state — that is the wrong abstraction.

### Required addition

A new `JumpToTrack(string trackId)` method on `IPlaybackService` and `PlaybackQueue`.

**`PlaybackQueue.JumpToTrack(string trackId)`:**
- Find the track by ID in `_tracks`. If not found, no-op (return null).
- If the found track is the current track, return null (no-op — see same-track behavior in Section 7).
- Push current track to both navigation and play history (same as `AdvanceInternal`).
- Set `_currentIndex` to the found track's index.
- If shuffle is enabled, update `_shufflePosition` to the corresponding position in `_shuffleOrder`.
- Return the new `CurrentTrack`.

**`PlaybackService.JumpToTrack(string trackId)`:**
- Snapshot `HasNext`/`HasPrevious`.
- Delegate to `_playbackQueue.JumpToTrack(trackId)`.
- If result is null, no-op (track not found or already current).
- If result is non-null, call `PlayCurrent()` (which fires `TrackChanged`).
- Fire `PlaybackStateChanged` if `HasNext`/`HasPrevious` changed.
- Does **not** fire `QueueChanged` — see event contract rationale below.

**`IPlaybackService` addition:**
```csharp
void JumpToTrack(string trackId);
```

**What this preserves:** Queue contents, source context, navigation history (current track is pushed before jump, so GoBack returns to it), shuffle order and position, repeat mode.

### Event contract

| Event | Fires |
|---|---|
| `TrackChanged` | Always (via `PlayCurrent`) |
| `PlaybackStateChanged` | If `HasNext`/`HasPrevious` changed |
| `QueueChanged` | **No** |

**Rationale:** The existing playback-service design treats `QueueChanged` as a queue contents/order mutation signal. `JumpToTrack` moves the cursor within the existing queue without mutating contents or order — it is semantically a track change, not a queue change. The UI subscribes to `TrackChanged` (already), which triggers `RefreshQueue()` in `PlayerViewModel` to update `IsCurrent` and scroll position. No `QueueChanged` expansion needed.

### Files changed for this dependency

| File | Changes |
|---|---|
| `Hypersonic/Services/PlaybackQueue.cs` | Add `JumpToTrack(string trackId)` method |
| `Hypersonic/Services/PlaybackService.cs` | Add `JumpToTrack(string trackId)` with event wiring |
| `Hypersonic/Contracts/Services/IPlaybackService.cs` | Add `void JumpToTrack(string trackId)` |
| `Hypersonic.UnitTest/PlaybackQueueTests.cs` | Add jump tests (see Verification) |

---

## Files to Modify (UI)

| File | Changes |
|---|---|
| `Hypersonic/ViewModels/PlayerViewModel.cs` | Add queue properties, commands, event handlers |
| `Hypersonic/Views/ShellPage.xaml` | Add toggle button + queue popup XAML |
| `Hypersonic/Views/ShellPage.xaml.cs` | Add popup open/close logic, animation, double-click handler |
| `Hypersonic/Converters/BoolToHighlightBrushConverter.cs` | New converter (see Section 8) |
| `Hypersonic/Converters/BoolToFontWeightConverter.cs` | New converter (see Section 8) |

## New File

| File | Purpose |
|---|---|
| `Hypersonic/Models/QueueItem.cs` | Observable model wrapping `PlaybackTrack` with mutable `IsCurrent` for queue display |

---

## 1. Queue Row Model

File: `Hypersonic/Models/QueueItem.cs`

```csharp
namespace Hypersonic.Models;

public partial class QueueItem : ObservableObject
{
    public PlaybackTrack Track { get; init; } = null!;

    [ObservableProperty]
    private bool isCurrent;
}
```

`QueueItem` extends `ObservableObject` with a mutable, observable `IsCurrent` property. This enables in-place updates when the cursor moves within the same queue — `RefreshQueue()` can toggle `IsCurrent` on existing items without replacing the `ItemsSource`, which preserves the ListView's scroll position (see Section 5).

**Why observable (not plain data object):** The original design used a plain `init`-only `IsCurrent` and rebuilt the list on every state change. This caused the ListView to reset its scroll position on double-click-to-jump, because replacing `ItemsSource` resets the `ScrollViewer`. Making `IsCurrent` observable allows the common case (cursor moved, queue unchanged) to update in-place without a list rebuild.

**Why this exists** (instead of binding raw `PlaybackTrack`):
- `IsCurrent` is a view-display concern that does not belong on the service-layer `PlaybackTrack` model.
- Styling the now-playing highlight is fully data-driven in XAML — no imperative code-behind container manipulation.

**What was removed:** The earlier draft included `EffectiveIndex` on this model. It had no consumer: the jump path uses `Track.Id`, the highlight uses `IsCurrent`, and auto-scroll uses the ViewModel-level `EffectiveQueueIndex`. It was removed to avoid carrying dead state.

---

## 2. Player Bar Layout Change

Current Column 2 inner grid (Controls + progress) has 6 columns. We add a 7th:

```
Col 0: Position text
Col 1: Previous button
Col 2: Play/Pause button
Col 3: Next button
Col 4: Seek slider (*)
Col 5: Duration text
Col 6: Queue toggle button (NEW — Auto width)
```

Queue toggle button:
- `ToggleButton` (not `Button`), 32x32, no border
- `IsChecked` bound two-way to `Player.IsQueueOpen`
- Glyph: `\uEA37` (Segoe MDL2 "BulletedList" / queue icon)
- Margin: `8,0,0,0` to separate from duration text
- When checked (popup open), the built-in `ToggleButton` visual states provide accent background and contrasting foreground — no imperative code-behind color manipulation needed
- When unchecked (popup closed), reverts to the default subtle style automatically

**Why ToggleButton over Button:** A `Button` with manual foreground color changes (setting `Foreground` to accent on open, resetting on close) is fragile — setting `Foreground` to `null` to "revert" actually removes the brush entirely, making the icon invisible. A `ToggleButton` with a two-way `IsChecked` binding expresses the open/closed state declaratively and delegates visual feedback to the control's built-in visual state manager, which correctly handles both foreground and background across all states (normal, hover, pressed, checked).

Positioned inside the Controls+progress area (Column 2 of the outer player bar grid) — to the left of volume (Column 3).

**What this replaces:** The `ToggleQueueCommand` relay command is no longer needed — the two-way `IsChecked` binding toggles `IsQueueOpen` directly. The `PropertyChanged` handler still reacts to open/close the popup. Light dismiss sets `Player.IsQueueOpen = false`, which unchecks the button automatically.

---

## 3. Queue Popup

Use a `Popup` element (same pattern as the search results popup).

**Why Popup over Flyout:** `Flyout` auto-manages opening/closing and cannot be easily animated with Composition API. `Popup` gives full control over positioning, animation, and dismiss — matching the existing search popup pattern.

### Popup structure

```xml
<Popup x:Name="QueuePopup" IsLightDismissEnabled="True">
    <Border x:Name="QueuePopupBorder"
            Background="{ThemeResource SolidBackgroundFillColorBaseBrush}"
            BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}"
            BorderThickness="1" CornerRadius="8" Padding="0"
            Width="400" MaxHeight="500">
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="*"/>
            </Grid.RowDefinitions>

            <!-- Header -->
            <Grid Padding="16,12,16,8">
                <TextBlock Text="Queue" FontWeight="SemiBold" FontSize="14"/>
                <TextBlock Text="{x:Bind Player.QueueCountText, Mode=OneWay}"
                           HorizontalAlignment="Right"
                           Foreground="{ThemeResource TextFillColorSecondaryBrush}"
                           FontSize="12" VerticalAlignment="Center"/>
            </Grid>

            <!-- Track list -->
            <ListView x:Name="QueueListView" Grid.Row="1"
                      ItemsSource="{x:Bind Player.QueueItems, Mode=OneWay}"
                      SelectionMode="None"
                      IsItemClickEnabled="True"
                      Padding="4,0,4,4"
                      MaxHeight="440"
                      DoubleTapped="QueueList_DoubleTapped"
                      Visibility="{x:Bind Player.IsQueueEmpty, Mode=OneWay,
                                   Converter={StaticResource InverseBoolToVisibilityConverter}}">
                <ListView.ItemContainerStyle>
                    <Style TargetType="ListViewItem">
                        <Setter Property="Padding" Value="0"/>
                        <Setter Property="MinHeight" Value="0"/>
                        <Setter Property="CornerRadius" Value="0"/>
                        <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
                    </Style>
                </ListView.ItemContainerStyle>
                <ListView.ItemTemplate>
                    <DataTemplate x:DataType="models:QueueItem">
                        <!-- see Section 4 -->
                    </DataTemplate>
                </ListView.ItemTemplate>
            </ListView>

            <!-- Empty state -->
            <TextBlock Grid.Row="1"
                       Text="Queue is empty"
                       HorizontalAlignment="Center" VerticalAlignment="Center"
                       Foreground="{ThemeResource TextFillColorTertiaryBrush}"
                       FontSize="13" Margin="0,40"
                       Visibility="{x:Bind Player.IsQueueEmpty, Mode=OneWay,
                                    Converter={StaticResource BoolToVisibilityConverter}}"/>
        </Grid>
    </Border>
</Popup>
```

### Dimensions and scrolling

- **Fixed width**: 400px.
- **Max height**: 500px (60px header + 440px list area). Internal `ListView` handles scrolling via its built-in `ScrollViewer`.
- **No minimum height**: if few tracks, the popup sizes to content.

### ListView item container

The `ItemContainerStyle` overrides are necessary for the desired visual behavior:

- **`IsItemClickEnabled="True"`**: Required to activate the `ListViewItem` PointerOver visual states. Without this, WinUI 3 `ListViewItem` containers show no hover highlight at all — even with `SelectionMode="None"`. This is a WinUI 3 behavior: the PointerOver visual state group is only active when the item is clickable. The same pattern is used on album and playlist track lists elsewhere in the app.
- **`CornerRadius="0"`**: The default `ListViewItem` container has rounded corners on its hover/pressed highlight. For a queue list the highlight should be a full-width square rectangle, edge to edge.
- **`Padding="0"` / `MinHeight="0"`**: Removes default container padding so the item template controls its own spacing.
- **`HorizontalContentAlignment="Stretch"`**: Ensures the item template fills the full container width.

### Positioning

The popup is positioned **every time it opens** (recalculated on each toggle), anchored to the queue toggle button. A two-pass layout is used: the popup is opened off-screen, its `ActualHeight` is measured after layout, and then the offset is set so the popup's bottom edge sits flush above the player bar.

```csharp
private void OpenQueuePopup()
{
    var transform = QueueToggleButton.TransformToVisual(this);
    var buttonPos = transform.TransformPoint(new Windows.Foundation.Point(0, 0));

    // Right-align popup to button
    QueuePopup.HorizontalOffset = buttonPos.X - 400 + QueueToggleButton.ActualWidth;

    // First open: place off-screen to measure actual height
    QueuePopup.VerticalOffset = -10000;
    QueuePopup.IsOpen = true;

    // After layout, reposition using measured height
    QueuePopupBorder.UpdateLayout();
    var actualHeight = QueuePopupBorder.ActualHeight;
    QueuePopup.VerticalOffset = -(actualHeight + 8);

    AnimateQueuePopup();

    // Auto-scroll to current track
    if (Player.EffectiveQueueIndex >= 0 && Player.EffectiveQueueIndex < Player.QueueItems.Count)
    {
        QueueListView.ScrollIntoView(Player.QueueItems[Player.EffectiveQueueIndex],
            ScrollIntoViewAlignment.Leading);
    }
}
```

- The 8px gap separates the popup bottom from the player bar top edge.
- **Short queues**: The popup hugs the player bar because offset is based on `ActualHeight`, not `MaxHeight`. No floating gap.
- **Window resize while open**: Light-dismiss closes the popup. Reopening recalculates position.

### Light dismiss

`IsLightDismissEnabled="True"` handles click-outside auto-close natively. Sync toggle state:

```csharp
QueuePopup.Closed += (_, _) =>
{
    Player.IsQueueOpen = false;
};
```

---

## 4. Queue Item Template

Each row: cover art (32x32), track title + "artist — album", duration. Now-playing highlight via `IsCurrent`.

```xml
<DataTemplate x:DataType="models:QueueItem">
    <Grid Padding="8,4" ColumnSpacing="10" Height="40"
          Background="{x:Bind IsCurrent, Mode=OneWay, Converter={StaticResource BoolToHighlightBrushConverter}}">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="Auto"/>
            <ColumnDefinition Width="*"/>
            <ColumnDefinition Width="Auto"/>
        </Grid.ColumnDefinitions>

        <!-- Cover art -->
        <Grid Grid.Column="0" Width="32" Height="32" CornerRadius="2">
            <Border Background="{ThemeResource CardBackgroundFillColorSecondaryBrush}" CornerRadius="2">
                <FontIcon Glyph="&#xE8D6;" FontSize="14"
                          Foreground="{ThemeResource TextFillColorTertiaryBrush}"
                          HorizontalAlignment="Center" VerticalAlignment="Center"/>
            </Border>
            <Image Source="{Binding Track.CoverArtUri}" Stretch="UniformToFill"
                   Width="32" Height="32"/>
        </Grid>

        <!-- Track info -->
        <StackPanel Grid.Column="1" VerticalAlignment="Center" Spacing="0">
            <TextBlock Text="{x:Bind Track.Title}" FontSize="13" MaxLines="1"
                       TextTrimming="CharacterEllipsis"
                       FontWeight="{x:Bind IsCurrent, Mode=OneWay, Converter={StaticResource BoolToFontWeightConverter}}"/>
            <TextBlock FontSize="11" MaxLines="1" TextTrimming="CharacterEllipsis"
                       Foreground="{ThemeResource TextFillColorSecondaryBrush}">
                <Run Text="{Binding Track.Artist}"/>
                <Run Text=" — " Foreground="{ThemeResource TextFillColorTertiaryBrush}"/>
                <Run Text="{Binding Track.Album}"/>
            </TextBlock>
        </StackPanel>

        <!-- Duration -->
        <TextBlock Grid.Column="2"
                   Text="{x:Bind Track.Duration, Converter={StaticResource SecondsToTimeConverter}}"
                   FontSize="11" Foreground="{ThemeResource TextFillColorTertiaryBrush}"
                   VerticalAlignment="Center"/>
    </Grid>
</DataTemplate>
```

### Binding modes

`IsCurrent` bindings use `Mode=OneWay` because `QueueItem.IsCurrent` is an observable property that changes in-place (see Section 1). Without `OneWay`, the binding would be one-time and would not reflect cursor changes.

### WinUI 3 binding constraints

`Image.Source` and `Run.Text` use `{Binding}` (classic binding) rather than `{x:Bind}`:
- **`Run.Text`**: WinUI 3's `x:Bind` does not support `Run.Text` — the XAML compiler crashes without a useful error.
- **`Image.Source`**: The `Uri` → `ImageSource` automatic type conversion only works with `{Binding}`, not `x:Bind`. With `x:Bind`, the compiler cannot resolve the type coercion.

All other bindings use `x:Bind` for compile-time safety and performance.

### Now-playing highlight (data-driven)

`IsCurrent` on `QueueItem` drives two visual cues:
- **Background**: Accent-tinted highlight via `BoolToHighlightBrushConverter` — returns `SystemAccentColorLight2` at 20% opacity when true, `null` when false (see Section 8 for rationale).
- **Font weight**: Track title is `SemiBold` when current, `Normal` otherwise.

No imperative code-behind container manipulation. `IsCurrent` is updated in-place by `RefreshQueue()` (see Section 5), so the highlight follows the cursor without rebuilding the list.

---

## 5. PlayerViewModel Changes

File: `Hypersonic/ViewModels/PlayerViewModel.cs`

### New properties

```csharp
[ObservableProperty]
private IReadOnlyList<QueueItem> queueItems = Array.Empty<QueueItem>();

[ObservableProperty]
private int effectiveQueueIndex = -1;

[ObservableProperty]
private bool isQueueOpen;

[ObservableProperty]
private bool isQueueEmpty = true;

[ObservableProperty]
private string queueCountText = string.Empty;
```

### New commands

No new commands are needed. The `ToggleButton` two-way binding on `IsQueueOpen` (see Section 2) handles toggling directly — a `ToggleQueueCommand` relay command is unnecessary.

### Event subscriptions

In constructor, add:

```csharp
_playbackService.QueueChanged += OnQueueChanged;
```

Handlers:

```csharp
private void OnQueueChanged(object? sender, EventArgs e)
{
    RefreshQueue();
}
```

Also call `RefreshQueue()` from the existing `OnTrackChanged` handler (to update `EffectiveQueueIndex` and `IsCurrent` state when the cursor moves without a queue mutation).

### RefreshQueue (public)

This method is **public** because the code-behind calls it when the popup opens to guarantee fresh data before display. It is also called internally from event handlers.

`RefreshQueue` uses a **two-path strategy** to balance correctness with scroll preservation:

1. **In-place update (common case):** If the queue contents are unchanged (same tracks in the same order, just a different cursor position), update `IsCurrent` on the existing `QueueItem` objects without replacing `ItemsSource`. Because `QueueItem.IsCurrent` is observable (Section 1), the bindings update automatically. This preserves the ListView's scroll position.

2. **Full rebuild (queue mutation):** If the queue contents changed (different tracks or different order — e.g. shuffle toggled, tracks added/removed), rebuild the entire list and assign it to `QueueItems`.

The comparison is by track ID at each position, not by reference equality — this correctly detects reordering.

```csharp
public void RefreshQueue()
{
    var effective = _playbackService.GetEffectiveQueue();
    var currentIndex = _playbackService.EffectiveQueueIndex;

    // Path 1: In-place update if queue contents unchanged
    var existing = QueueItems;
    if (existing.Count == effective.Count)
    {
        var match = true;
        for (var i = 0; i < effective.Count; i++)
        {
            if (existing[i].Track.Id != effective[i].Id)
            {
                match = false;
                break;
            }
        }

        if (match)
        {
            for (var i = 0; i < existing.Count; i++)
            {
                existing[i].IsCurrent = i == currentIndex;
            }

            EffectiveQueueIndex = currentIndex;
            QueueCountText = existing.Count > 0
                ? $"{currentIndex + 1} / {existing.Count}"
                : string.Empty;
            return;
        }
    }

    // Path 2: Queue contents changed — rebuild
    var items = new List<QueueItem>(effective.Count);
    for (var i = 0; i < effective.Count; i++)
    {
        items.Add(new QueueItem
        {
            Track = effective[i],
            IsCurrent = i == currentIndex,
        });
    }

    QueueItems = items;
    EffectiveQueueIndex = currentIndex;
    IsQueueEmpty = items.Count == 0;
    QueueCountText = items.Count > 0
        ? $"{currentIndex + 1} / {items.Count}"
        : string.Empty;
}
```

**Why this matters:** Without the in-place path, double-clicking a track to jump replaces `QueueItems`, which causes the ListView to reset its `ScrollViewer` to the top. For long queues this is disorienting — the user expects the scroll position to stay put and only the highlight to move.

### Queue count text semantics

`QueueCountText` displays `"<current position> / <total tracks>"`, e.g. `"3 / 12"`. Reads as "track 3 of 12".
- **Populated queue**: `"{EffectiveQueueIndex + 1} / {Count}"`.
- **Empty queue**: empty string. The header shows just "Queue" with no counter.

### Double-click jump

```csharp
public void JumpToQueueTrack(QueueItem item)
{
    _playbackService.JumpToTrack(item.Track.Id);
}
```

---

## 6. ShellPage.xaml.cs Changes

### Popup open/close

Driven by the `IsQueueOpen` property change:

```csharp
Player.PropertyChanged += (_, e) =>
{
    // ... existing IsPlaying / CoverArtUrl handlers ...
    if (e.PropertyName == nameof(Player.IsQueueOpen))
    {
        if (Player.IsQueueOpen)
        {
            Player.RefreshQueue();
            OpenQueuePopup();
        }
        else if (QueuePopup.IsOpen)
        {
            QueuePopup.IsOpen = false;
        }
    }
};
```

### Animation

Open animation only (manual toggle). Light-dismiss close is instant — P0 simplification that avoids intercepting native dismiss for animation sequencing.

```csharp
private void AnimateQueuePopup()
{
    var visual = ElementCompositionPreview.GetElementVisual(QueuePopupBorder);
    var compositor = visual.Compositor;
    var duration = TimeSpan.FromMilliseconds(200);
    var easing = compositor.CreateCubicBezierEasingFunction(
        new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f));

    var fadeAnimation = compositor.CreateScalarKeyFrameAnimation();
    fadeAnimation.InsertKeyFrame(0f, 0f);
    fadeAnimation.InsertKeyFrame(1f, 1f, easing);
    fadeAnimation.Duration = duration;

    var slideAnimation = compositor.CreateScalarKeyFrameAnimation();
    slideAnimation.InsertKeyFrame(0f, 8f);
    slideAnimation.InsertKeyFrame(1f, 0f, easing);
    slideAnimation.Duration = duration;

    visual.StartAnimation("Opacity", fadeAnimation);
    visual.StartAnimation("Translation.Y", slideAnimation);
}
```

### Double-click handler

```csharp
private void QueueList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
{
    var element = e.OriginalSource as FrameworkElement;
    while (element != null && element.DataContext is not QueueItem)
    {
        element = VisualTreeHelper.GetParent(element) as FrameworkElement;
    }

    if (element?.DataContext is QueueItem item)
    {
        Player.JumpToQueueTrack(item);
    }
}
```

**Why visual-tree walking remains:** WinUI's `DoubleTapped` event fires on the hit-tested element (e.g. a `TextBlock` inside the template), not on the `ListViewItem` container. The handler walks up the tree to find the nearest `FrameworkElement` whose `DataContext` is a `QueueItem` — this recovers the bound data object, not a container index. It does not use `IndexFromContainer` or depend on virtualization state. The walk is short (typically 2–4 parents to the template root) and reliable because the `DataContext` is set by the `DataTemplate` binding. This is the standard WinUI pattern for `DoubleTapped` on `ListView` items.

---

## 7. Edge Cases

| Case | Behavior |
|---|---|
| **Empty queue** | Toggle button always visible. ListView hidden via `IsQueueEmpty` + `InverseBoolToVisibilityConverter`. "Queue is empty" text shown via `IsQueueEmpty` + `BoolToVisibilityConverter`. |
| **Queue changes while open** | `QueueItems` is an `[ObservableProperty]`; ListView re-binds automatically. If only the cursor moved (same tracks, same order), `IsCurrent` is updated in-place without replacing the list — preserving scroll position. If contents changed, the list is rebuilt. |
| **Track ends while open** | `TrackChanged` → `RefreshQueue()` → `IsCurrent` moves to new track via in-place update (scroll preserved). |
| **Shuffle toggled while open** | `QueueChanged` → `RefreshQueue()` → list order changes; full rebuild (scroll resets — expected, since the list order itself changed). |
| **Double-click to jump (scroll preservation)** | Jump triggers `TrackChanged` → `RefreshQueue()`. Queue contents are unchanged (only cursor moved), so `RefreshQueue` takes the in-place update path: `IsCurrent` toggles on existing items, `ItemsSource` is not replaced, ListView scroll position is preserved. The highlight moves to the new track without the list jumping to the top. |
| **Double-click current track** | **Explicit product choice: no-op.** `JumpToTrack` returns null when the target is the current track, so no history push, no restart, no state change. Rationale: users double-clicking the already-playing row most likely did so unintentionally or to confirm it's playing; restarting the track would be surprising. If a user wants to restart the current track, the seek bar (drag to 0) or the Previous button (within 3 seconds) already serve that purpose. |
| **Window resize while open** | Light-dismiss closes popup. Reopening recalculates position. |

---

## 8. Converters

Two new converter classes in `Hypersonic/Converters/`, registered in `ShellPage.xaml` page resources. These are page-scoped because they serve only the queue flyout; if reuse emerges later, they can be promoted to `Generic.xaml`.

| File | Converter | Input | Output |
|---|---|---|---|
| `Converters/BoolToHighlightBrushConverter.cs` | `BoolToHighlightBrushConverter` | `bool` | `SystemAccentColorLight2` at 20% opacity if true, `null` if false |
| `Converters/BoolToFontWeightConverter.cs` | `BoolToFontWeightConverter` | `bool` | `SemiBold` if true, `Normal` if false |

### BoolToHighlightBrushConverter design decisions

- **Accent color over theme brush:** The original design used `SubtleFillColorSecondaryBrush`, but this was too subtle to visually distinguish the now-playing track. `SystemAccentColorLight2` at 20% opacity provides a clear, user-personalized highlight that adapts to the system accent color.
- **`null` over `Transparent` for the false case:** Returning `Transparent` creates a `SolidColorBrush` that occupies the `Background` property, which occludes the `ListViewItem` container's built-in PointerOver (hover) highlight. Returning `null` leaves the `Background` property unset, allowing the container's visual states to show through. This is critical for hover feedback on non-current items.

Registration in `ShellPage.xaml`:
```xml
<Page.Resources>
    <!-- existing converters -->
    <converters:BoolToHighlightBrushConverter x:Key="BoolToHighlightBrushConverter"/>
    <converters:BoolToFontWeightConverter x:Key="BoolToFontWeightConverter"/>
</Page.Resources>
```

---

## Implementation Sequence

1. **PlaybackQueue.JumpToTrack** + unit tests
2. **PlaybackService.JumpToTrack** + `IPlaybackService` interface addition
3. **QueueItem** model (`Hypersonic/Models/QueueItem.cs`)
4. **New converters** (`Hypersonic/Converters/BoolToHighlightBrushConverter.cs`, `BoolToFontWeightConverter.cs`)
5. **PlayerViewModel** queue properties, commands, `RefreshQueue()` (public)
6. **ShellPage.xaml** toggle button + popup XAML + converter registration
7. **ShellPage.xaml.cs** popup logic, positioning, animation, double-click handler
8. Build + test + manual verification

---

## Verification

### Build

```bash
dotnet build Hypersonic.sln -p:Platform=x64
```

### Automated tests

```bash
dotnet test Hypersonic.UnitTest/Hypersonic.UnitTest.csproj
dotnet test Hypersonic.Client.UnitTest/Hypersonic.Client.UnitTest.csproj
```

### New PlaybackQueue.JumpToTrack unit tests

| Test | Verifies |
|---|---|
| `JumpToTrack_MovesToCorrectTrack` | `CurrentTrack` matches target after jump |
| `JumpToTrack_PushesHistoryEntry` | Navigation history contains the pre-jump track |
| `JumpToTrack_PreservesShuffleOrder` | Shuffle order unchanged; shuffle position updated |
| `JumpToTrack_InvalidId_NoOp` | No state change when track ID not found |
| `JumpToTrack_SameTrack_NoOp` | Returns null, no history push, no state change |
| `JumpToTrack_GoBackReturnsToPreJump` | `GoBack()` after jump returns to the track that was playing before the jump |

### Manual smoke tests

- Play an album → click queue button → flyout shows all tracks with correct art, names, durations
- Current track is highlighted (background + bold title) and scrolled into view
- Double-click another track → playback jumps, highlight moves, previous track accessible via back
- Double-click the current track → no-op (no restart, no history push)
- Click outside → flyout dismisses; toggle button state resets
- Toggle button opens/closes correctly with animation on open
- Enable shuffle → reopen queue → order reflects shuffled effective queue
- Short queue (3 tracks) → popup hugs player bar, no floating gap
- Play to end of queue → queue still viewable, no crash
- Empty queue → "Queue is empty" message displayed, ListView hidden
- Jump to track in shuffle mode → shuffle order preserved, back button returns to pre-jump track
