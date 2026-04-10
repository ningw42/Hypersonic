# Shuffle & Repeat UI — Implementation Details

Companion to [docs/design/ui/shuffle-repeat-ui.md](../../design/ui/shuffle-repeat-ui.md). This document records what was actually built and implementation specifics not covered in the design doc.

---

## Files Changed

### New files

| File | Purpose |
|---|---|
| `Hypersonic/Converters/RepeatModeGlyphConverter.cs` | Maps `RepeatMode` to Segoe MDL2 glyph (`\uE8EE` for Off/All, `\uE8ED` for One) |
| `Hypersonic/Converters/RepeatModeToOpacityConverter.cs` | Maps `RepeatMode` to opacity (0.5 for Off, 1.0 for All/One) |
| `Hypersonic/Converters/RepeatModeToAccentBrushConverter.cs` | Maps `RepeatMode` to `AccentTextFillColorPrimaryBrush` or `TextFillColorTertiaryBrush` |

### Modified files

| File | Changes |
|---|---|
| `Hypersonic/ViewModels/PlayerViewModel.cs` | Added `ShuffleEnabled` (bool) and `RepeatMode` observable properties; `ToggleShuffle()` and `CycleRepeatMode()` relay commands; constructor hydration from service state; extended `OnPlaybackStateChanged` to sync both properties |
| `Hypersonic/Views/ShellPage.xaml` | Expanded player bar controls grid from 7 to 9 columns; added shuffle `ToggleButton` (Col 1) and repeat `Button` (Col 5) flanking the Prev/Play-Pause/Next cluster; restructured queue flyout header into a two-column grid with title+count on the left and shuffle/repeat buttons on the right |
| `Hypersonic/Themes/Generic.xaml` | Registered `RepeatModeGlyphConverter`, `RepeatModeToOpacityConverter`, `RepeatModeToAccentBrushConverter` |

### Unchanged

- `Hypersonic/Services/PlaybackService.cs` — already fires `PlaybackStateChanged` on shuffle/repeat changes
- `Hypersonic/Services/PlaybackQueue.cs` — shuffle/repeat logic already complete
- `Hypersonic/Models/RepeatMode.cs` — enum already defined
- `Hypersonic/Contracts/Services/IPlaybackService.cs` — interface already exposes `ShuffleEnabled` and `RepeatMode`
- `Hypersonic/App.xaml.cs` — no new DI registrations (converters are XAML resources, properties are on existing singleton `PlayerViewModel`)

---

## ViewModel Additions

### Observable properties (PlayerViewModel.cs, lines 80–84)

```csharp
[ObservableProperty]
private bool shuffleEnabled;

[ObservableProperty]
private RepeatMode repeatMode;
```

Both are thin reflections of `IPlaybackService` state. No partial `On*Changed` handlers — the command pushes state to the service, the service fires `PlaybackStateChanged`, and the event handler reads it back.

### Commands (PlayerViewModel.cs, lines 134–150)

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

Both read current state from the service (not the ViewModel property) to avoid any stale-state window.

### Constructor hydration (PlayerViewModel.cs, lines 99–100)

```csharp
ShuffleEnabled = _playbackService.ShuffleEnabled;
RepeatMode = _playbackService.RepeatMode;
```

Today this is a no-op (defaults are `false` / `Off`). It becomes load-bearing when persistence (ROADMAP 1.2) restores saved state on `IPlaybackService` during `ActivationService` startup, before `ShellPage` and `PlayerViewModel` exist.

### Event sync (PlayerViewModel.cs, OnPlaybackStateChanged)

Two lines added at the end of the existing handler:

```csharp
ShuffleEnabled = _playbackService.ShuffleEnabled;
RepeatMode = _playbackService.RepeatMode;
```

No dedicated event handler — `PlaybackService` already fires `PlaybackStateChanged` on every shuffle/repeat change, so piggybacking is simpler than a new event and subscription.

---

## Player Bar Layout

### Column grid expansion

The inner controls grid in Column 2 of the player bar was expanded from 7 to 9 columns:

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

The `Margin="0,0,8,0"` that was on the Next button (Col 3 old, Col 4 new) was moved to the Repeat button (Col 5) to maintain the visual gap before the seek slider. The Next button itself now has `Margin="0,0,0,0"`.

### Shuffle button

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

- **One-way `IsChecked` + Command pattern**: `IsChecked` reflects ViewModel state but does not write back. The `Command` pushes to the service, which fires `PlaybackStateChanged`, which updates `ShuffleEnabled`, which flows back to `IsChecked`. This avoids a two-way binding loop where `ToggleButton` writes `true` → ViewModel writes to service → service fires event → ViewModel writes `true` again.
- **`ToggleButton` visual states**: Checked state shows accent background and contrasting foreground automatically — same as the existing queue toggle button. No custom converters needed.

### Repeat button

```xml
<Button Grid.Column="5"
        Command="{x:Bind Player.CycleRepeatModeCommand}"
        Width="32" Height="32" Padding="0"
        Background="Transparent" BorderThickness="0"
        VerticalAlignment="Center" Margin="0,0,8,0">
    <FontIcon Glyph="{x:Bind Player.RepeatMode, Mode=OneWay,
                      Converter={StaticResource RepeatModeGlyphConverter}}"
              FontSize="14"
              Opacity="{x:Bind Player.RepeatMode, Mode=OneWay,
                        Converter={StaticResource RepeatModeToOpacityConverter}}"
              Foreground="{x:Bind Player.RepeatMode, Mode=OneWay,
                           Converter={StaticResource RepeatModeToAccentBrushConverter}}"/>
</Button>
```

- **`Button` (not `ToggleButton`)**: Repeat has three states — `ToggleButton` only models two.
- **Three converters on the `FontIcon`**: Glyph changes between RepeatAll and RepeatOne icons. Opacity dims the icon when off. Foreground switches between accent and tertiary brush. All three bind to the same `RepeatMode` property.

---

## Queue Flyout Header

The flyout header was restructured from a flat `Grid` with two `TextBlock` children to a two-column grid:

- **Column 0 (`*`)**: `StackPanel` with "Queue" title and count text side-by-side (was right-aligned before, now left-aligned next to the title).
- **Column 1 (`Auto`)**: `StackPanel` with shuffle `ToggleButton` and repeat `Button`, sized 28x28 with FontSize 12 (smaller than the 32x32 / FontSize 14 player bar buttons to fit the compact flyout header).

Same bindings and commands as the player bar — toggling from either location updates both simultaneously via the shared ViewModel.

---

## Converters

### RepeatModeGlyphConverter

```csharp
return mode switch
{
    RepeatMode.One => "\uE8ED",  // RepeatOne
    _ => "\uE8EE",               // RepeatAll
};
```

Off and All share the same glyph (`RepeatAll` arrows). The inactive state is communicated through opacity and foreground color, not a different icon. Only `One` gets a distinct glyph (arrows with "1" badge).

### RepeatModeToOpacityConverter

```csharp
return mode == RepeatMode.Off ? 0.5 : 1.0;
```

Returns `double` directly — WinUI coerces to the `Opacity` property type.

### RepeatModeToAccentBrushConverter

```csharp
var isActive = value is RepeatMode mode && mode != RepeatMode.Off;
var key = isActive
    ? "AccentTextFillColorPrimaryBrush"
    : "TextFillColorTertiaryBrush";
return (Brush)Application.Current.Resources[key];
```

Reads theme-aware brushes from `Application.Current.Resources` at convert time. These are WinUI system theme resources that respond to theme changes — but since the converter is called via `x:Bind` with `Mode=OneWay` on `RepeatMode`, it only re-evaluates when `RepeatMode` changes, not on theme change. A theme change while the app is running will not update the repeat button foreground until the next repeat mode toggle. This is acceptable — theme changes mid-session are rare, and the brush will correct itself on the next interaction.

### Converter registration

All three converters are registered in `Hypersonic/Themes/Generic.xaml` (global resource dictionary), not in `ShellPage.xaml`'s `Page.Resources`. This keeps them available if other pages need repeat mode display in the future (e.g., a full-window Now Playing view — ROADMAP 2.2).

---

## Test Coverage

### Existing tests

All 80 existing unit tests pass (`dotnet test Hypersonic.UnitTest/Hypersonic.UnitTest.csproj`). The `PlaybackQueue` shuffle and repeat logic is already covered by tests in `PlaybackQueueTests.cs`.

### No new tests

The new ViewModel properties and commands are thin wrappers — each is a single property read or single-line service setter call. The converters are pure functions with trivial logic (switch expressions, ternary operators). The risk of regression is in the XAML bindings and visual states, which require the WinUI runtime.

### Testing gaps

- **No converter unit tests.** The converters depend on `Microsoft.UI.Xaml.Data.IValueConverter` and (for `RepeatModeToAccentBrushConverter`) `Application.Current.Resources`, both of which require the WinUI runtime.
- **No ViewModel property/command tests.** `PlayerViewModel` depends on `IPlaybackService` and there is no mocking library in the project.
- **Visual state verification is manual-only.** The `ToggleButton` checked/unchecked states and converter-driven opacity/foreground changes are verified through the smoke tests listed in the design doc.

---

## Known Limitations

1. **No tooltips** — Shuffle and repeat buttons have no tooltips. Localized tooltip strings in `Resources.resw` are deferred.
2. **No keyboard shortcuts** — `Ctrl+S` (shuffle) and `Ctrl+R` (repeat) are ROADMAP item 4.3.
3. **No session persistence** — Shuffle/repeat state resets on app restart. ROADMAP item 1.2.
4. **No SMTC projection** — `UpdateSmtcState()` does not set `ShuffleEnabled` or `AutoRepeatMode` on `SystemMediaTransportControls`. OS media overlays will not reflect shuffle/repeat state.
5. **Theme change stale brush** — `RepeatModeToAccentBrushConverter` reads theme brushes at convert time. A runtime theme change will not update the repeat button foreground until the next `RepeatMode` change.
