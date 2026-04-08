# Shuffle & Repeat UI — Design Doc

## Context

The shuffle and repeat logic is fully implemented in the service layer (`PlaybackQueue`, `PlaybackService`). `IPlaybackService` exposes `ShuffleEnabled` (bool, get/set) and `RepeatMode` (enum `Off`/`All`/`One`, get/set) — setting either fires `PlaybackStateChanged` and (for shuffle) fires `QueueChanged` to rebuild the effective queue order.

The gap is purely UI. The player bar has no shuffle or repeat buttons, and `PlayerViewModel` has no properties or commands for them. Users cannot control shuffle/repeat from the app UI.

**Note on SMTC:** `PlaybackService.UpdateSmtcState()` currently only projects `HasNext`/`HasPrevious` into SMTC enabling rules. It does **not** set `SystemMediaTransportControls.ShuffleEnabled` or `AutoRepeatMode`. Projecting shuffle/repeat into SMTC is out of scope for this design — it requires service-layer changes and should be addressed separately.

This is ROADMAP item 1.1 and a dependency for: 1.2 Persistence (persists shuffle/repeat state), 2.2 Now Playing (shares `PlayerViewModel`), and 4.3 Keyboard Shortcuts (`Ctrl+S` / `Ctrl+R`).

---

## Scope

### P0 (this design)

1. **Player bar buttons**: Shuffle and repeat buttons in the transport controls area, flanking the Prev/Play-Pause/Next cluster.
2. **Queue flyout buttons**: Duplicate shuffle and repeat controls in the queue flyout header — same bindings, same visual treatment. Users managing their queue have shuffle/repeat intent in the same context.
3. **ViewModel surface**: Observable properties, relay commands, constructor hydration, and event sync on `PlayerViewModel` backed by `IPlaybackService`.
4. **Visual feedback**: Shuffle uses `ToggleButton` checked/unchecked visual states (accent background when active). Repeat uses converter-driven opacity and foreground color (dimmed when off, accent when active), cycling through three states with glyph changes.
5. **New converters**: `RepeatModeGlyphConverter` and visual state converters for opacity/foreground.

### Deferred

- **Tooltips with localized strings**: Shuffle and repeat buttons will not have tooltips in P0. Add localized tooltip strings (`Resources.resw`) in a future pass.
- **Keyboard shortcuts**: `Ctrl+S` (shuffle) and `Ctrl+R` (repeat) are ROADMAP item 4.3.
- **Persist shuffle/repeat state across sessions**: ROADMAP item 1.2.
- **SMTC shuffle/repeat projection**: `UpdateSmtcState()` needs to set `ShuffleEnabled` and `AutoRepeatMode` on `SystemMediaTransportControls`. Separate service-layer change.

---

## Part 1: ViewModel Additions

File: `Hypersonic/ViewModels/PlayerViewModel.cs`

### New observable properties

```csharp
[ObservableProperty]
private bool shuffleEnabled;

[ObservableProperty]
private RepeatMode repeatMode;
```

### New relay commands

```csharp
[RelayCommand]
private void ToggleShuffle()
{
    _playbackService.ShuffleEnabled = !_playbackService.ShuffleEnabled;
}

[RelayCommand]
private void CycleRepeatMode()
{
    _playbackService.RepeatMode = _playbackService.RepeatMode switch
    {
        RepeatMode.Off => RepeatMode.All,
        RepeatMode.All => RepeatMode.One,
        RepeatMode.One => RepeatMode.Off,
        _ => RepeatMode.Off,
    };
}
```

**Cycle order**: Off → All → One → Off. This matches the standard music player convention (Spotify, Apple Music, Windows Media Player). Off → All is the most common first action ("I want to loop this playlist"), then All → One narrows to single-track loop, then One → Off returns to linear.

### Constructor hydration

The constructor must read initial state from the service, not rely solely on events:

```csharp
public PlayerViewModel(IPlaybackService playbackService, INavigationService navigationService)
{
    _playbackService = playbackService;
    _navigationService = navigationService;
    _playbackService.PlaybackStateChanged += OnPlaybackStateChanged;
    _playbackService.TrackChanged += OnTrackChanged;
    _playbackService.PositionChanged += OnPositionChanged;
    _playbackService.QueueChanged += OnQueueChanged;

    // Hydrate from current service state — events alone are not sufficient
    // because state may have been set before this VM was constructed (e.g.,
    // persisted state restored by ActivationService in ROADMAP 1.2).
    ShuffleEnabled = _playbackService.ShuffleEnabled;
    RepeatMode = _playbackService.RepeatMode;
}
```

**Why this is necessary:** `PlayerViewModel` is singleton but resolved lazily when `ShellPage` is created. If a future persistence layer (ROADMAP 1.2) restores shuffle/repeat state on `IPlaybackService` before shell creation, the `PlaybackStateChanged` event will fire before the ViewModel exists — the subscription misses it. Even without persistence, defensive hydration costs two property reads and prevents a class of startup bugs.

### Event sync

Extend the existing `OnPlaybackStateChanged` handler (line 274) to keep shuffle/repeat in sync on subsequent changes:

```csharp
private void OnPlaybackStateChanged(object? sender, EventArgs e)
{
    IsPlaying = _playbackService.IsPlaying;
    HasNext = _playbackService.HasNext;
    HasPrevious = _playbackService.HasPrevious;
    VolumePercent = Math.Round(_playbackService.Volume * 100);
    Duration = _playbackService.Duration;
    DurationText = FormatTime(_playbackService.Duration);
    ShuffleEnabled = _playbackService.ShuffleEnabled;
    RepeatMode = _playbackService.RepeatMode;
}
```

**Why sync here and not in a dedicated handler:** `PlaybackService` already fires `PlaybackStateChanged` on every shuffle/repeat change (lines 46, 63 of `PlaybackService.cs`). Adding two property reads to the existing handler is simpler than a new event — no new subscription, no new handler method.

---

## Part 2: New Converter — `RepeatModeGlyphConverter`

File: `Hypersonic/Converters/RepeatModeGlyphConverter.cs`

```csharp
using Hypersonic.Models;

using Microsoft.UI.Xaml.Data;

namespace Hypersonic.Converters;

public class RepeatModeGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is RepeatMode mode)
        {
            return mode switch
            {
                RepeatMode.One => "\uE8ED",  // RepeatOne
                _ => "\uE8EE",               // RepeatAll (used for both Off and All)
            };
        }

        return "\uE8EE";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
```

### Glyph mapping

| `RepeatMode` | Glyph | Codepoint | Visual |
|---|---|---|---|
| `Off` | `\uE8EE` | RepeatAll | Repeat arrows (dimmed via opacity) |
| `All` | `\uE8EE` | RepeatAll | Repeat arrows (full opacity, accent) |
| `One` | `\uE8ED` | RepeatOne | Repeat arrows with "1" badge (full opacity, accent) |

**Why `Off` uses the same glyph as `All`:** The inactive state is communicated through opacity and foreground color, not a different icon. Keeping the repeat arrows visible (but dimmed) maintains discoverability. The `One` glyph gets its own icon because it communicates a qualitatively different behavior (single-track loop).

### Shuffle glyph

Shuffle uses a static glyph `\uE8B1` (Shuffle) for both enabled and disabled states. The active/inactive visual state is conveyed by the `ToggleButton`'s built-in checked/unchecked visual states (see Part 3). No glyph converter is needed for shuffle.

---

## Part 3: Player Bar Layout

File: `Hypersonic/Views/ShellPage.xaml`

### Current layout (Column 2 inner grid)

```
Col 0: Position text       (Auto)
Col 1: Previous button     (Auto)
Col 2: Play/Pause button   (Auto)
Col 3: Next button          (Auto)
Col 4: Seek slider          (*)
Col 5: Duration text        (Auto)
Col 6: Queue toggle button  (Auto)
```

### New layout

Insert shuffle before Previous (new Col 1) and repeat after Next (new Col 5). Shift remaining columns right:

```
Col 0: Position text       (Auto)
Col 1: Shuffle toggle       (Auto)  ← NEW
Col 2: Previous button     (Auto)
Col 3: Play/Pause button   (Auto)
Col 4: Next button          (Auto)
Col 5: Repeat button        (Auto)  ← NEW
Col 6: Seek slider          (*)
Col 7: Duration text        (Auto)
Col 8: Queue toggle button  (Auto)
```

This places shuffle/repeat symmetrically around the transport cluster: `Shuffle | Prev | Play/Pause | Next | Repeat`. The pattern mirrors Spotify, Apple Music, and most desktop music players.

### Shuffle button XAML

```xml
<ToggleButton Grid.Column="1"
              IsChecked="{x:Bind Player.ShuffleEnabled, Mode=OneWay}"
              Command="{x:Bind Player.ToggleShuffleCommand}"
              Width="32" Height="32" Padding="0"
              Background="Transparent" BorderThickness="0"
              VerticalAlignment="Center">
    <FontIcon Glyph="&#xE8B1;" FontSize="14"/>
</ToggleButton>
```

**Why `ToggleButton` with one-way `IsChecked` + command:** Shuffle is a binary state — `ToggleButton` provides the correct semantic (checked/unchecked visual states with accent background when checked), matching the existing queue toggle pattern (`ShellPage.xaml:455`). The binding is **one-way** so `IsChecked` reflects ViewModel state but does not write back. The `Command` handles the click action, pushing state to the service via `ToggleShuffleCommand`. The service fires `PlaybackStateChanged`, which updates the ViewModel's `ShuffleEnabled`, which flows back to `IsChecked`. This avoids a two-way binding loop while preserving `ToggleButton` semantics.

**Why not a plain `Button`:** A `Button` would require manual opacity/foreground converters to simulate active/inactive states. `ToggleButton` provides this for free via its built-in visual state manager — checked state shows accent background and contrasting foreground automatically, matching the queue toggle button. Fewer converters, more consistent.

### Repeat button XAML

```xml
<Button Grid.Column="5"
        Command="{x:Bind Player.CycleRepeatModeCommand}"
        Width="32" Height="32" Padding="0"
        Background="Transparent" BorderThickness="0"
        VerticalAlignment="Center">
    <FontIcon Glyph="{x:Bind Player.RepeatMode, Mode=OneWay,
                      Converter={StaticResource RepeatModeGlyphConverter}}"
              FontSize="14"
              Opacity="{x:Bind Player.RepeatMode, Mode=OneWay,
                        Converter={StaticResource RepeatModeToOpacityConverter}}"
              Foreground="{x:Bind Player.RepeatMode, Mode=OneWay,
                           Converter={StaticResource RepeatModeToAccentBrushConverter}}"/>
</Button>
```

**Why `Button` (not `ToggleButton`):** Repeat has three states (`Off`/`All`/`One`). `ToggleButton` only models two. A `Button` with a cycling command is the natural fit — each click cycles Off → All → One → Off. Visual state is driven entirely by converters on the `FontIcon`.

---

## Part 4: Visual Feedback

### Design principle

Active state is communicated through **two channels simultaneously**: opacity and foreground color. This ensures the state is perceivable for users with color vision deficiencies (opacity alone is sufficient) while also being instantly recognizable for typical users (accent color is the primary signal).

### Shuffle visual states

Handled entirely by `ToggleButton` built-in visual states — when `IsChecked` is true, the button shows accent background and contrasting foreground automatically. No custom converters needed.

### Repeat visual states

| `RepeatMode` | Glyph | Opacity | Foreground |
|---|---|---|---|
| `Off` | `\uE8EE` (RepeatAll) | 0.5 | `TextFillColorTertiaryBrush` |
| `All` | `\uE8EE` (RepeatAll) | 1.0 | `AccentTextFillColorPrimaryBrush` |
| `One` | `\uE8ED` (RepeatOne) | 1.0 | `AccentTextFillColorPrimaryBrush` |

### New converters

Three new converters for repeat visual state — shuffle needs none (handled by `ToggleButton`):

**File: `Hypersonic/Converters/RepeatModeToOpacityConverter.cs`**

```csharp
using Hypersonic.Models;

using Microsoft.UI.Xaml.Data;

namespace Hypersonic.Converters;

public class RepeatModeToOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is RepeatMode mode)
        {
            return mode == RepeatMode.Off ? 0.5 : 1.0;
        }

        return 0.5;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
```

**File: `Hypersonic/Converters/RepeatModeToAccentBrushConverter.cs`**

```csharp
using Hypersonic.Models;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace Hypersonic.Converters;

public class RepeatModeToAccentBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var isActive = value is RepeatMode mode && mode != RepeatMode.Off;
        var key = isActive
            ? "AccentTextFillColorPrimaryBrush"
            : "TextFillColorTertiaryBrush";
        return (Brush)Application.Current.Resources[key];
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
```

### Converter registration

File: `Hypersonic/Themes/Generic.xaml`

Add after existing converter declarations (line 16):

```xml
<converters:RepeatModeGlyphConverter x:Key="RepeatModeGlyphConverter"/>
<converters:RepeatModeToOpacityConverter x:Key="RepeatModeToOpacityConverter"/>
<converters:RepeatModeToAccentBrushConverter x:Key="RepeatModeToAccentBrushConverter"/>
```

---

## Part 5: Queue Flyout Controls

File: `Hypersonic/Views/ShellPage.xaml`

Add shuffle and repeat buttons to the queue flyout header. Currently the header is:

```xml
<Grid Padding="16,12,16,8">
    <TextBlock Text="Queue" FontWeight="SemiBold" FontSize="14"/>
    <TextBlock Text="{x:Bind Player.QueueCountText, Mode=OneWay}"
               HorizontalAlignment="Right" .../>
</Grid>
```

Replace with a structured header that includes controls:

```xml
<Grid Padding="16,12,16,8">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
    </Grid.ColumnDefinitions>

    <!-- Title + count -->
    <StackPanel Grid.Column="0" Orientation="Horizontal" Spacing="8"
                VerticalAlignment="Center">
        <TextBlock Text="Queue" FontWeight="SemiBold" FontSize="14"/>
        <TextBlock Text="{x:Bind Player.QueueCountText, Mode=OneWay}"
                   Foreground="{ThemeResource TextFillColorSecondaryBrush}"
                   FontSize="12" VerticalAlignment="Center"/>
    </StackPanel>

    <!-- Shuffle + Repeat -->
    <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="0">
        <ToggleButton IsChecked="{x:Bind Player.ShuffleEnabled, Mode=OneWay}"
                      Command="{x:Bind Player.ToggleShuffleCommand}"
                      Width="28" Height="28" Padding="0"
                      Background="Transparent" BorderThickness="0"
                      VerticalAlignment="Center">
            <FontIcon Glyph="&#xE8B1;" FontSize="12"/>
        </ToggleButton>
        <Button Command="{x:Bind Player.CycleRepeatModeCommand}"
                Width="28" Height="28" Padding="0"
                Background="Transparent" BorderThickness="0"
                VerticalAlignment="Center">
            <FontIcon Glyph="{x:Bind Player.RepeatMode, Mode=OneWay,
                              Converter={StaticResource RepeatModeGlyphConverter}}"
                      FontSize="12"
                      Opacity="{x:Bind Player.RepeatMode, Mode=OneWay,
                                Converter={StaticResource RepeatModeToOpacityConverter}}"
                      Foreground="{x:Bind Player.RepeatMode, Mode=OneWay,
                                   Converter={StaticResource RepeatModeToAccentBrushConverter}}"/>
        </Button>
    </StackPanel>
</Grid>
```

**Why duplicate the controls here:** Users open the queue flyout when they're thinking about playback order — "what's next?" is directly related to "should I shuffle?" and "should this repeat?" Placing shuffle/repeat controls in the queue header eliminates the need to close the flyout, find the buttons on the player bar, toggle, and reopen. The controls share the same ViewModel bindings and commands — no additional state or logic.

**Size difference:** The queue flyout buttons are 28x28 with FontSize 12 (vs. 32x32 / FontSize 14 on the player bar) to fit the more compact flyout header proportionally.

---

## Edge Cases

### State sync on app launch

The constructor hydrates `ShuffleEnabled` and `RepeatMode` directly from `IPlaybackService` (see Part 1). This ensures correct initial state regardless of whether events fired before the ViewModel was constructed. Today the defaults match (`false` / `Off`), so this is a no-op — but it becomes load-bearing when persistence (ROADMAP 1.2) restores saved state on `IPlaybackService` during `ActivationService` startup, before `ShellPage` and `PlayerViewModel` are created.

### Rapid toggling

Each click calls the service setter synchronously (no async). The service setter is idempotent (guards against same-value assignment). `PlaybackStateChanged` fires synchronously on the UI thread. No race condition is possible.

### Shuffle with empty queue

If the queue is empty, toggling shuffle calls `PlaybackQueue.SetShuffle(true)` which has no tracks to shuffle — `RebuildShuffleOrder()` produces an empty order. The button correctly shows the enabled state. When tracks are later queued via `PlayQueue()`, the shuffle state is preserved and `RebuildShuffleOrder()` runs on the new tracks (existing behavior in `PlaybackQueue.PlayQueue()`).

### SMTC state

`UpdateSmtcState()` currently only projects `HasNext`/`HasPrevious` into SMTC enabling rules. It does **not** set `SystemMediaTransportControls.ShuffleEnabled` or `AutoRepeatMode`. This means OS-level media overlays will not reflect shuffle/repeat state. This is a pre-existing service-layer gap, not introduced by this design. Fixing it requires adding `ShuffleEnabled` and `AutoRepeatMode` writes to `UpdateSmtcState()` — tracked as a separate follow-up.

### Repeat mode with no queue

`RepeatMode` can be set even with an empty queue. The mode is stored and applied when tracks are queued. `HasNext` correctly reflects repeat state (returns `true` for `RepeatMode.All` even at end-of-queue).

---

## Files Summary

### New files

| File | Purpose |
|---|---|
| `Hypersonic/Converters/RepeatModeGlyphConverter.cs` | Maps `RepeatMode` → Segoe MDL2 glyph string |
| `Hypersonic/Converters/RepeatModeToOpacityConverter.cs` | Maps `RepeatMode` → opacity (0.5 for Off, 1.0 otherwise) |
| `Hypersonic/Converters/RepeatModeToAccentBrushConverter.cs` | Maps `RepeatMode` → accent or tertiary brush |

### Modified files

| File | Changes |
|---|---|
| `Hypersonic/ViewModels/PlayerViewModel.cs` | Add `ShuffleEnabled`, `RepeatMode` properties; `ToggleShuffle()`, `CycleRepeatMode()` commands; constructor hydration; extend `OnPlaybackStateChanged` |
| `Hypersonic/Views/ShellPage.xaml` | Add shuffle/repeat buttons to player bar (expand grid to 9 columns); add shuffle/repeat buttons to queue flyout header |
| `Hypersonic/Themes/Generic.xaml` | Register 3 new converters |

### Unchanged

| File | Why |
|---|---|
| `Hypersonic/Services/PlaybackService.cs` | Service layer already complete (SMTC projection is a separate follow-up) |
| `Hypersonic/Services/PlaybackQueue.cs` | Queue logic already complete |
| `Hypersonic/Models/RepeatMode.cs` | Enum already defined |
| `Hypersonic/Contracts/Services/IPlaybackService.cs` | Interface already has `ShuffleEnabled` and `RepeatMode` |

---

## Implementation Sequence

1. **Converters** — Create the 3 new converter files. No dependencies on other changes. Register in `Generic.xaml`.
2. **ViewModel** — Add properties, commands, constructor hydration, and event sync to `PlayerViewModel.cs`.
3. **Player bar XAML** — Expand Column 2 inner grid from 7 to 9 columns. Add shuffle `ToggleButton` (Col 1) and repeat `Button` (Col 5). Re-number existing columns.
4. **Queue flyout XAML** — Restructure the flyout header grid to include shuffle/repeat buttons.

Steps 1–2 can proceed in parallel. Steps 3–4 depend on 1–2 but are independent of each other.

---

## Verification

### Build

```bash
dotnet build Hypersonic.sln -p:Platform=x64
```

### Unit tests

No new unit tests required. `PlaybackQueue` shuffle/repeat logic is covered by existing tests in `Hypersonic.UnitTest`. `PlaybackService` is not unit-tested (no mock infrastructure exists for `MediaPlayer`). The new ViewModel properties and commands are thin wrappers (property reads + single-line service calls) — verified via the manual smoke tests below.

### Manual smoke tests

1. **Shuffle toggle (player bar)**: Click shuffle `ToggleButton` → accent background appears (checked state). Click again → reverts to default (unchecked). Open queue flyout to verify effective queue order changed.
2. **Repeat cycle (player bar)**: Click repeat button → Off to All (accent foreground, full opacity, RepeatAll glyph). Click again → All to One (accent, RepeatOne glyph with "1" badge). Click again → One to Off (dimmed, RepeatAll glyph). Verify playback behavior: in Repeat All, queue wraps; in Repeat One, track restarts; in Off, playback stops at end.
3. **Shuffle toggle (queue flyout)**: Open queue flyout → click shuffle `ToggleButton` in header → accent checked state, and player bar shuffle button updates simultaneously.
4. **Repeat cycle (queue flyout)**: Same as test 2 but triggered from the flyout header. Player bar repeat button updates simultaneously.
5. **State persistence across queue changes**: Enable shuffle → add tracks via "Play Next" → verify shuffle state preserved. Change repeat mode → clear upcoming → verify repeat mode preserved.
6. **Empty queue**: Toggle shuffle/repeat with no tracks playing. Verify buttons respond visually. Queue tracks → verify shuffle/repeat state is applied to new queue.
