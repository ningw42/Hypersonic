# Scrollbar UX Improvements — Implementation Notes

Companion to [`docs/design/ui/2026-04-23-scrollbar-ux.md`](../../design/ui/2026-04-23-scrollbar-ux.md). This document covers implementation-specific decisions, details that only appear in source, and design alternatives that were attempted and rejected.

---

## CarouselPanel as a templated ContentControl

`CarouselPanel` extends `ContentControl` and sets `DefaultStyleKey = typeof(CarouselPanel)` in the constructor, causing WinUI to resolve the default `Style` from `Themes/Generic.xaml`. No `Unloaded` cleanup is needed — with `HorizontalScrollMode="Disabled"`, there are no `AddHandler`-registered handlers or P/Invoke calls to clean up. Event handlers on template children (`Click`, `ViewChanged`) are unwired-then-rewired in `OnApplyTemplate` to handle template reapplication safely.

---

## Disabled Disabled VisualState in CarouselArrowButtonStyle

The `CarouselArrowButtonStyle` declares an empty `<VisualState x:Name="Disabled"/>`. This prevents the default WinUI `Button` disabled state (which changes foreground/background to disabled-theme colors) from applying. The control manages disabled appearance itself via `Opacity = 0.4` in code-behind, keeping the button's icon and background visually consistent with the enabled state, just dimmed.

---

## Dual refresh path for button visibility

`UpdateButtonVisibility()` is called from two paths:

1. **`SizeChanged`** — fires when the control or its parent resizes (e.g., window resize). This catches viewport width changes that affect whether content overflows.
2. **`ViewChanged`** — fires when `ScrollableWidth` changes due to content loading, item rerenders, or programmatic `ChangeView` calls. This catches content-extent changes that don't resize the control itself.

Both paths call the same method, which checks `ScrollableWidth > 0` and updates button `Visibility`, then delegates to `UpdateArrowStates()` for `IsEnabled`/`Opacity`.

---

## ChangeView clamping

The click handlers clamp the target offset to `[0, ScrollableWidth]`:

```csharp
_scroller.ChangeView(Math.Max(0, newOffset), null, null);        // left
_scroller.ChangeView(Math.Min(_scroller.ScrollableWidth, newOffset), null, null); // right
```

`ChangeView` itself would clamp internally, but explicit clamping makes the intent clear and avoids relying on undocumented ScrollViewer behavior. The `null` parameters for vertical offset and zoom preserve their current values. Animation is enabled by default (the 4th `disableAnimation` parameter is not passed).

---

## Scrollbar spacing: Border wrappers on list pages

AlbumsPage and ArtistsPage (which use `UniformGridLayout`) and PlaylistsPage (which uses a vertical `StackLayout`) all have their `ItemsRepeater` as the direct child of a `ScrollViewer`. Since `ScrollViewer` only takes a single child, a `<Border Padding="0,0,16,0">` wrapper around the `ItemsRepeater` is the minimal structural change to add right padding. It doesn't affect `ItemsRepeater` data binding, incremental loading (`AlbumsPage`), or the `ViewChanged` scroll handler on `AlbumsScrollViewer`.

The detail pages (AlbumDetailPage, PlaylistDetailPage, ArtistDetailPage, MainPage) already have an inner `StackPanel` whose `Padding` was changed from `0,0,0,24` to `0,0,16,24`.

---

## What was considered and rejected

### Scroll event interception approaches

Three approaches were attempted before settling on `HorizontalScrollMode="Disabled"`:

1. **`AddHandler` with `handledEventsToo`** — The `PointerWheelChanged` handler fires *after* the ScrollViewer class handler, which has already scrolled horizontally. Restoring the offset via `ChangeView` causes visible flicker.

2. **Inner Grid intercept** — A `PointerWheelChanged` handler on a `Grid` wrapping the `ContentPresenter` inside the ScrollViewer, intended to fire before the class handler and mark `e.Handled = true`. The class handler still processed the event in some cases.

3. **`HorizontalScrollMode="Disabled"`** (adopted) — Completely prevents the ScrollViewer from processing any horizontal scroll input. No event interception needed. Arrow buttons call `ChangeView` directly, which works regardless of scroll mode.

### Overlay arrow buttons

Arrow buttons were initially overlaid on top of the carousel content with hover-to-show behavior. This caused two problems: (1) disabled buttons at scroll edges passed click events through to underlying album cards, and (2) buttons were hard to see against varied album artwork. Moving buttons outside the carousel to flanking columns solved both issues and eliminated the need for hover show/hide logic, gradient fade masks, and click-through suppression.
