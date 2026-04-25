# Now-Playing Track Highlight — Implementation Notes

Companion to [`docs/design/ui/2026-04-24-now-playing-track-highlight.md`](../../design/ui/2026-04-24-now-playing-track-highlight.md). This document covers implementation-specific decisions, deviations from the design, and details that only appear in source.

---

## `NowPlayingTracker` dual constructor — synchronous test seam

The production constructor requires a `DispatcherQueue` (captured via `GetForCurrentThread()`) and throws `InvalidOperationException` if called off the UI thread. An `internal` constructor accepts a `bool synchronous` parameter that bypasses the dispatcher entirely — event handlers call `Refresh()` directly on the calling thread.

This exists because the WinUI test host crashes before `[UITestMethod]` tests run (the design doc specifies `[UITestMethod]` for tracker tests). All tests use the synchronous constructor to exercise the same `event → handler → Refresh()` logic without needing a dispatcher. The `_dispatcherQueue` field is left `null` in synchronous mode; `DispatchRefresh()` branches on the `_synchronous` flag to avoid the null dereference.

```csharp
internal NowPlayingTracker(
    IPlaybackService playbackService,
    Func<IEnumerable<SongInfo>> getSongs,
    bool synchronous)
```

The `internal` visibility allows `Hypersonic.Tests` to access it via the existing `[InternalsVisibleTo]` configuration.

---

## `DispatchRefresh()` branching pattern

The design doc describes event handlers as simple `_dispatcherQueue.TryEnqueue(() => Refresh())` calls. The implementation adds an intermediate `DispatchRefresh()` method that both `OnTrackChanged` and `OnPlaybackStateChanged` call, consolidating the synchronous/async branching:

```csharp
private void DispatchRefresh()
{
    if (_synchronous)
    {
        Refresh();
    }
    else
    {
        _dispatcherQueue!.TryEnqueue(() => Refresh());
    }
}
```

This avoids duplicating the branch in two event handlers and keeps the test seam contained to a single method.

---

## `Refresh()` — `force` parameter as default argument

The design doc describes `Refresh()` with no parameters (public API) and mentions a "force flag" for `Subscribe()`. The implementation uses a default parameter `bool force = false` rather than a separate internal method, since `Subscribe()` is the only caller that needs to bypass the cache. The parameter is technically public (callers can pass `force: true`), but it exists solely for `Subscribe()`'s initial forced refresh — no other call site should need it.

---

## `EqualizerBars` — accent color applied in code-behind, not XAML

The design doc specifies bar fill color as `SystemAccentColorLight2`. The implementation applies this color in `SetupAnimations()` via code-behind rather than in the Generic.xaml template:

```csharp
var accentColor = (Windows.UI.Color)Application.Current.Resources["SystemAccentColorLight2"];
var brush = new SolidColorBrush(accentColor);
_bar1.Background = brush;
_bar2.Background = brush;
_bar3.Background = brush;
```

The `Border` elements in the template have no `Background` set — they receive it at `Loaded` time. This is because `SystemAccentColorLight2` is a `Windows.UI.Color` resource, not a `Brush` resource, so it can't be referenced directly as a `Background` value in XAML without an intermediate converter or `SolidColorBrush` wrapper. Applying it in code avoids adding a one-off converter for a single control.

The color is resolved once at `Loaded` time and shared across all three bars via the same `SolidColorBrush` instance. If the system accent color changes while the app is running, the bars won't update until the control is unloaded and reloaded (e.g., navigating away and back). This is acceptable — the queue popup's `BoolToHighlightBrushConverter` has the same limitation.

---

## `EqualizerBars` — initial scale values on visuals

The design doc mentions "varying initial heights" for the bars but doesn't specify values. The implementation sets explicit initial `Scale.Y` values on each bar's visual before any animation starts:

```csharp
_visual1.Scale = new Vector3(1f, 0.6f, 1f);
_visual2.Scale = new Vector3(1f, 0.35f, 1f);
_visual3.Scale = new Vector3(1f, 0.8f, 1f);
```

These values serve two purposes:
1. **Frozen state appearance** — When `IsAnimating` is false (paused/stopped), bars display at these heights rather than all being full-height (which would look like a solid block) or zero-height (which would make them invisible).
2. **Animation start position** — If the bars are never animated (control loads in paused state), these provide the visual representation.

The values are chosen to look like a mid-animation freeze: staggered heights that suggest motion was interrupted.

---

## `EqualizerBars` — bar grid layout details

The template uses a 5-column `Grid` (3 bar columns + 2 gap columns) rather than `ColumnSpacing` on a 3-column grid:

```xml
<ColumnDefinition Width="3"/>   <!-- Bar 1 -->
<ColumnDefinition Width="2"/>   <!-- Gap -->
<ColumnDefinition Width="3"/>   <!-- Bar 2 -->
<ColumnDefinition Width="2"/>   <!-- Gap -->
<ColumnDefinition Width="3"/>   <!-- Bar 3 -->
```

Total width: 3 + 2 + 3 + 2 + 3 = 13px content within a 16px container (centered via `HorizontalAlignment="Center"`). The gap columns are empty — no elements are placed in them. This approach gives explicit pixel control over bar width and spacing, which matters at this small scale where even 1px differences are visible.

Each bar `Border` has `Height="16"` (full container height) and `VerticalAlignment="Bottom"`. The visual's `Scale.Y` controls apparent height — scaling from the bottom `CenterPoint` at `(1.5f, 16f, 0f)` (the center-x of a 3px-wide bar, bottom of the 16px container). The `CornerRadius="1"` on each border softens the bar caps slightly.

---

## `EqualizerBars` — `IsTabStop="False"` in style

The control's default style in Generic.xaml includes `<Setter Property="IsTabStop" Value="False"/>`. Without this, the `EqualizerBars` control would be focusable via Tab key navigation within the `ListView` item, creating a confusing focus stop on a decorative element. The control has no interactive behavior — it's purely visual feedback.

---

## `EqualizerBars` — `Unloaded` does not detach event handlers

The design doc says the `Unloaded` handler "stops all running animations, detaches event handlers, nulls out Composition objects." The staged implementation stops animations and nulls Composition references, but does not detach the `Loaded`/`Unloaded` event handlers subscribed in the constructor. This is intentional: the handlers are instance-level subscriptions (`this.Loaded += OnLoaded`) that are naturally collected with the control. Explicitly removing them in `Unloaded` would also prevent the control from re-initializing if it's unloaded and reloaded by `ListView` virtualization — the `Loaded` handler needs to fire again to recreate the Composition objects.

---

## Animation easing — ease-in-out cubic bezier

The `CreateBarAnimation` method uses a symmetric cubic bezier easing `(0.42, 0) → (0.58, 1)`, which is CSS's standard `ease-in-out`. The keyframe structure is:

- `0.0` → min scale (ease in)
- `0.5` → max scale (ease out, then ease in)
- `1.0` → min scale (ease out)

With `IterationBehavior.Forever`, this creates a continuous bounce between min and max. The three bars use different min/max ranges and durations:

| Bar | Min | Max | Duration | Character |
|-----|-----|-----|----------|-----------|
| 1 | 0.3 | 1.0 | 400ms | Fast, full range |
| 2 | 0.2 | 0.9 | 550ms | Medium, slightly shorter |
| 3 | 0.4 | 1.0 | 700ms | Slow, starts taller |

The different durations and phase offsets (they all start simultaneously but drift apart due to different periods) create an organic, non-mechanical look without requiring explicit phase offsets or randomization.

---

## Row background `CornerRadius="4"` addition

The design doc specifies a background tint on the row grid but doesn't mention corner radius. The implementation adds `CornerRadius="4"` to the row `Grid` in all three page templates:

```xml
<Grid ColumnSpacing="12" Padding="0,6"
      Background="{x:Bind IsNowPlaying, Mode=OneWay, Converter={StaticResource BoolToHighlightBrushConverter}}"
      CornerRadius="4">
```

This rounds the accent tint background's corners to match the visual language of WinUI's `ListViewItem` hover/selected states, which use rounded rectangles. Without it, the accent tint would have sharp corners that look inconsistent with the rest of the UI.

---

## `ArtistDetailPage.xaml` — existing `xmlns:controls` declaration

The design doc specifies adding `xmlns:controls="using:Hypersonic.Controls"` to all three page root elements. `AlbumDetailPage` and `PlaylistDetailPage` needed explicit additions. `ArtistDetailPage` already had the declaration present from a prior control usage — no addition was needed. The EqualizerBars references in the template resolve through the existing namespace.

---

## `ArtistDetailViewModel` — `_navigatedAway` guard uses `if` block

The design doc shows the `_navigatedAway` guard as an early return pattern:

```csharp
if (_navigatedAway) return;
```

The album and playlist ViewModels use this single-line form. The artist ViewModel wraps the tracker creation in an `if (!_navigatedAway)` block instead:

```csharp
if (!_navigatedAway)
{
    _nowPlayingTracker?.Unsubscribe();
    _nowPlayingTracker = new NowPlayingTracker(
        _playbackService,
        () => _communityTopSongs.Concat(_personalTopSongs));
    _nowPlayingTracker.Subscribe();
}
```

The tracker setup sits inside a nested `if (IsCommunityAvailable || IsPersonalAvailable)` block. An early `return` at that point would exit the entire `LoadTopSongsAsync` method, not just skip the tracker — the inverted condition scopes the guard to the tracker block only without altering control flow of the surrounding method.

---

## Test file structure — `Fakes/` directory

The `FakePlaybackService` is placed in `Hypersonic.Tests/Fakes/FakePlaybackService.cs`, establishing a new `Fakes` directory in the test project. This follows the naming convention of "Fake" for hand-written test doubles (as opposed to "Mock" which implies a mocking framework the project doesn't use). The fake is intentionally minimal:

- `NowPlaying` and `IsPlaying` are simple auto-properties (no backing field logic)
- `RaiseTrackChanged()` and `RaisePlaybackStateChanged()` are public methods that invoke the events
- All other `IPlaybackService` members throw `NotImplementedException` — tests that accidentally call them will fail fast with a clear error rather than silently returning defaults

---

## Test class — no `[UITestMethod]`, uses synchronous constructor

The design doc specifies `[UITestMethod]` for tracker tests to exercise the `DispatcherQueue.TryEnqueue` path. The implementation uses plain `[TestMethod]` with the synchronous internal constructor instead, due to the WinUI test host crash issue. A comment at the top of the test class documents this decision and the intent to revisit:

```csharp
// Ideally these tests would use [UITestMethod] with the production constructor to exercise
// the DispatcherQueue.TryEnqueue path, but the WinUI test host crashes before [UITestMethod]
// tests run. The synchronous internal constructor covers the same event → handler → Refresh()
// logic. Revisit when the test host issue is resolved.
```

This means the `TryEnqueue` dispatch path is untested in automated tests. It is exercised only at runtime when the feature is used in the app.

---

## Additional test: `DuplicateTrackIds_AllHighlighted`

The design doc's test table doesn't include a dedicated test for duplicate track IDs (it's listed as an edge case but not a test case). The implementation adds a `DuplicateTrackIds_AllHighlighted` test that creates two `SongInfo` objects with `Id = "1"` and verifies both get `IsNowPlaying = true`. This validates the "all instances with same ID get highlighted" edge case behavior documented in the design.

---

## Additional test: `SubscribeIdempotent`

Not in the design doc's test table. Verifies that calling `Subscribe()` twice doesn't corrupt state. The test subscribes twice, confirms the highlight is still correct, then unsubscribes once and confirms all songs are cleared. This exercises the idempotency early-return in `Subscribe()`, but does not verify the absence of double-subscription directly — if `Subscribe()` double-hooked events and `Unsubscribe()` only removed one handler, this test would still pass. A stronger assertion (e.g., counting handler invocations after an event) is not included.

---

## `NavigatedAwayGuard` test — ViewModel-level simulation

The design doc notes this test as "tested at ViewModel level or as integration." The implementation tests it at the tracker's consumer level by simulating the ViewModel's `_navigatedAway` guard pattern directly in the test method:

```csharp
navigatedAway = true;
tracker.Unsubscribe();

if (!navigatedAway)
{
    tracker = new NowPlayingTracker(fake, () => songs, synchronous: true);
    tracker.Subscribe();
}
```

This mirrors the actual ViewModel code pattern rather than testing a tracker API, since the guard is a ViewModel responsibility, not a tracker feature. The test verifies that after the simulated navigation-away, no new tracker is created and events have no effect on song state.
