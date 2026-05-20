# Coverflow Control for Home Page Hero Carousel

## Overview

Roadmap item 3.2 calls for a `CoverflowControl` — a 3D perspective carousel where the centered item is enlarged head-on and flanked by tilted, receding neighbors. The Home Page now uses the control as the top `Random Picks` hero strip, bound to `ViewModel.RandomPicks`. The former flat `Random Picks` carousel is removed from the bottom of the page.

## Why Now

The Home Page (Roadmap 2.1) ships a row of flat horizontal carousels, all visually identical. A coverflow-style hero adds depth and draws the eye to featured content without requiring a redesign of any other surface. Building it as a reusable `Control` (with `ItemsSource` + `ItemTemplate`) means later adoption — on Recently Released, Random Picks, or anywhere else — is a one-line XAML swap, not a re-implementation.

## Approach

### Two 3D pipelines, implemented with `PlaneProjection`

WinUI 3 ships two independent 3D rendering paths on the project's runtime target (Windows 10 1809+):

1. **`UIElement.Transform3D`** (`PerspectiveTransform3D` + `CompositeTransform3D`) — XAML-level, preserves normal hit-testing, focus, and the underlying automation tree.
2. **`Microsoft.UI.Composition`** Visual transforms — GPU-driven, faster for large item counts, but Composition-only visuals don't participate in hit testing or focus.

The shipped comparison build uses the XAML-level path, but specifically via per-item `PlaneProjection` plus a `ScaleTransform`, not a parent `PerspectiveTransform3D` + child `CompositeTransform3D` stack. This matches the existing WinUI practices in the app: keep items as real `UIElement`s for focus and accessibility, use XAML transforms for the coverflow pose, and reserve Composition for the press shrink/restore animation.

This differs from the earlier feasibility sketch, which proposed `UIElement.Transform3D` with `PerspectiveTransform3D`. The `PlaneProjection` implementation proved simpler for the current card-only template and works with the manual projected-quad hit testing used for overlapped side cards. Composition + `InteractionTracker` + `ExpressionAnimation` remains a known optimization path if profiling later shows jank — it is not needed for the current hero strip.

Note: XAML projection is a *visual* transform — it doesn't add coverflow-specific accessibility semantics. Screen readers don't know which card is "centered." If this control becomes a long-term shared navigation surface, the container should set `AutomationProperties.Name` per item and expose selected state via an automation peer.

### Architecture

`CoverflowControl` is a templated `Control` that owns its item containers. The page-supplied `ItemTemplate` is **visual-only** — it must not contain click handlers or buttons that swallow input, because activation policy belongs to the control.

```
CoverflowControl
├── PART_Scroller (ScrollViewer — horizontal manipulation enabled,
│                  manual center snap fallback, scrollbar collapsed)
│   └── PART_ScrolledContent (Border — owns leading/trailing centering padding)
│       └── PART_Repeater (ItemsRepeater)
│           └── CoverflowContainerFactory produces:
│               CoverflowItemContainer (focusable wrapper —
│                   receives PlaneProjection + scale + Canvas.ZIndex)
│               └── ContentPresenter (forwards user's ItemTemplate)
├── PART_LeftArrowBtn
└── PART_RightArrowBtn
```

The wrapper is created by a control-owned `IElementFactory` assigned to the repeater's `ItemTemplate`. The factory recycles `CoverflowItemContainer` instances and forwards the user's `ItemTemplate` through the container's `ContentTemplate`. `ItemsRepeater.ElementPrepared` gets the already-realized container, applies metrics, transform, z-index, and index state. `ItemsRepeater.ElementClearing` is the symmetric cleanup hook: clear owner/index state so a recycled container cannot be resolved under a stale index.

### Public contract

| Member | Purpose |
|---|---|
| `ItemsSource` (DP, `object?`) | Any source `ItemsRepeater` accepts — `ObservableCollection<T>`, `IEnumerable`, `ItemsSourceView`. Internally counted/indexed through one cached `ItemsSourceView`; don't narrow to `IList`. |
| `ItemTemplate` (DP, `DataTemplate`) | Visual-only. The control wraps it in its own focusable container. |
| `SelectedIndex` (DP, two-way, `int`) | The currently centered item. Settable; reacting to changes drives a smooth `ChangeView` to center that item. |
| `SelectedItem` (DP, `object?`) | The data item at `SelectedIndex`. Keeps fixed metadata displays outside the transformed carousel simple. |
| `ItemWidth` (DP, `double`) | Per-card width used by the transform math. Default `220` so the hero is visibly larger than the flat 180px carousel. |
| `ItemSpacing` (DP, `double`) | Horizontal gap between cards. Default `16`. |
| `HoverRotationXDegrees` / `HoverRotationYDegrees` (DP, `double`) | Maximum mouse-tracked tilt for the center item. Separate axes because vertical tilt reads stronger perceptually than horizontal tilt. |
| `PressedScale` (DP, `double`) | Composition-layer scale applied to the center item while pressed. |
| `PressInDurationMilliseconds` / `PressOutDurationMilliseconds` (DP, `double`) | Press shrink and restore animation durations. Navigation waits for the restore duration. |
| `MaxRotationDegrees` / `RotationPerStep` (DP, `double`) | Coverflow side-card rotation tuning. Defaults are `55` and `45`. |
| `MinimumScale` (DP, `double`) | Lower bound for side-card scale. Default `0.78`. |
| `MaxDimmingOpacity` (DP, `double`) | Maximum opacity for the non-center dimming overlay. Default `0.20`. |
| `NonCenterItemOffsetZ` (DP, `double`) | Recessed depth for non-center items. Default `-160`. |
| `PressCancelDistance` (DP, `double`) | Pointer travel threshold that cancels a center-card press. Default `8`. |
| `DimmingOverlayCornerRadius` (DP, `CornerRadius`) | Shape of the dimming overlay above each cover. Default `10`. |
| `ItemInvoked` (event) | Raised only when the **already-selected** item is activated (Enter/Space, click, or tap on the centered container). Side-card clicks center, they do not invoke. |
| `SelectionChanged` (event) | Raised when `SelectedIndex` changes. |

### Transform math

The transform math is driven by an explicit `ItemWidth` DP rather than reading `container.ActualWidth`. Rationale: predictable, lets the Home Page pick a deliberately larger hero size, and avoids first-paint races where `ActualWidth` is still `0`.

For each realized container at index `i`, compute a signed fractional offset from the centered position:

```
delta = (centerOfItemI − scrollerCenterX) / (ItemWidth + ItemSpacing)
absDelta = clamp(|delta|, 0, MaxAbsDelta)   // MaxAbsDelta ~ 3 — beyond this, fade to 0
```

Apply to the container:

| Property | Formula |
|---|---|
| `PlaneProjection.CenterOfRotationX` | `0.5` |
| `PlaneProjection.CenterOfRotationY` | `ArtworkCenterY / containerHeight` so the item rotates around the album artwork, not any surrounding template surface |
| `PlaneProjection.RotationY` | `clamp(delta * RotationPerStep, −MaxRotationDegrees, +MaxRotationDegrees)`; side cards face toward center |
| `PlaneProjection.RotationX` | `0` for side cards; center card receives mouse-tracked hover tilt |
| `ScaleTransform.ScaleX/Y` | `max(MinimumScale, 1.0 − (1.0 − MinimumScale) * visualDistance)` |
| `PlaneProjection.LocalOffsetZ` | Two-plane depth: active center item = `0`; every non-center item = `NonCenterItemOffsetZ` |
| Dim overlay opacity | `lerp(0.0, MaxDimmingOpacity, visualDistance)` capped at `MaxDimmingOpacity`; the card element itself stays `Opacity=1.0` so overlapped cards never show through |
| `Canvas.ZIndex` | Center item gets the highest z-index; non-center items use `10000 − round(rawCenterDistance * 1000)` so side cards closer to center draw above side cards farther from center |

The recompute pass runs on `ScrollViewer.ViewChanged`, on `ItemsRepeater.ElementPrepared` / `ElementIndexChanged` (after virtualization realizes or reindexes a container), and after relevant size/layout metric changes. The all-item pass computes shared scroll geometry once, then mutates each container's existing `PlaneProjection` and `ScaleTransform` instead of allocating new transform objects on every scroll tick.

### Scroll and centering padding

- The ScrollViewer uses `HorizontalSnapPointsType="None"` in the template. Centering is owned by the control instead of by XAML snap points because the control uses negative `ItemSpacing`, explicit leading/trailing padding, and a manual nearest-item fallback.
- **Leading and trailing centering padding is required.** Without it, the first and last cards can never sit centered: the target scroll offset would have to be negative for the first card or past `ScrollableWidth` for the last. The control applies horizontal padding to the scrolled content of approximately `(scroller.ViewportWidth − ItemWidth) / 2` on both ends. Recompute on `Scroller.SizeChanged` and on `ItemWidth` changes.
- Manual center fallback: on `ScrollViewer.ViewChanged` with `IsIntermediate == false`, compute the index whose center is closest to the scroller center and `ChangeView(targetOffset, null, null, disableAnimation: false)` to align it precisely. Update `SelectedIndex` accordingly.
- Arrow buttons mutate `SelectedIndex` by ±1, then `ChangeView` to center the new selected item. **Do not** model the buttons after the existing `CarouselPanel` — that scrolls by viewport width and disables horizontal scroll mode, which is the wrong contract here.

### Activation policy

The control owns the click contract:

- Pointer events on a **non-selected** container set `SelectedIndex` to that container's index. The control re-centers on the new index. No invocation.
- Pointer press/release on the **already-selected** container animates the center item to `PressedScale`, restores it to normal scale on release, then raises `ItemInvoked` after the restore duration.
- `OnKeyDown`: Left/Right move `SelectedIndex` by ±1. Home/End jump to first/last. Enter or Space raise `ItemInvoked` for the current `SelectedIndex`.

Implement at the container level so the visual-only `ItemTemplate` doesn't need to participate in input.

### Spring smoothing — deferred

A v2 polish pass can replace per-frame XAML transform recompute with `Compositor.CreateSpringVector3Animation` driving per-container `CompositionPropertySet` values, started on `SelectedIndex` change. The synchronous XAML recompute on `ViewChanged` is sufficient for the current item count and keeps focus/hit-testing simple.

## Home Page Integration

Place the `Random Picks` section at the top of `MainPage.xaml`. The strip:

- Uses the new `controls:CoverflowControl`.
- Binds `ItemsSource` to `ViewModel.RandomPicks`.
- Starts at `SelectedIndex="9"` so the initial view has albums on both sides when the section has the usual 20 items.
- Supplies an `ItemTemplate` containing **cover art only** (`SquarePanel` + cached `Image`). No album-card frame and no per-card text area are shown inside the transformed carousel.
- Renders the selected album metadata once below the carousel, centered and bound to `RandomPicksCoverflow.SelectedItem`.

The `MainPage.xaml.cs` code-behind has a `RandomPicksCoverflow_ItemInvoked` handler that performs the same album-detail navigation as `AlbumCard_Click`.

### Metadata display

The existing flat cards keep their `RichTextBlock` + `AlbumArtistLinks_DataContextChanged` behavior. The Coverflow hero does not put metadata or artist hyperlinks inside rotated cards. Instead, it renders title, artist, and year in a single fixed block under the carousel using `SelectedItem`. This matches classic Cover Flow and avoids unreadable rotated text.

### No ViewModel or service work

`MainViewModel.RandomPicks` is `ObservableCollection<AlbumInfo>` and already populated by the existing data flow. The image cache pipeline (`helpers:CachedImage.Reference` → `IImageService` → SQLite-backed disk cache) handles cover art unchanged. No new packages.

## Files

| File | Change |
|---|---|
| `Hypersonic/Controls/CoverflowControl.cs` | **NEW.** Templated control with the public contract above, including `SelectedItem` for fixed metadata display. |
| `Hypersonic/Themes/Generic.xaml` | Add `<Style TargetType="controls:CoverflowControl">` with `PART_Scroller` (horizontal-enabled, scrollbar hidden, snap points disabled for manual centering), `PART_ScrolledContent`, `PART_Repeater`, and `PART_LeftArrowBtn` / `PART_RightArrowBtn`. **Do not** start from the `controls:CarouselPanel` style — that template disables horizontal scroll mode. |
| `Hypersonic/Views/MainPage.xaml` | Move `Random Picks` to the top as a `CoverflowControl` hero and remove the old bottom flat `Random Picks` carousel. |
| `Hypersonic/Views/MainPage.xaml.cs` | Add `RandomPicksCoverflow_ItemInvoked`; keep album navigation shared with `AlbumCard_Click`. |

## Reuse — Existing Utilities

- `helpers:CachedImage.Reference` / `helpers:CachedImage.Size` — image cache pipeline, unchanged.
- `controls:SquarePanel` — keeps the cover image 1:1 inside the carousel item, unchanged.
- The `MarqueeTextBlock` pattern — same templated-control + `OnApplyTemplate` + `PART_*` lookup conventions as this control will use.

## Risks

| Risk | Mitigation |
|---|---|
| `PlaneProjection` foreshortening looks wrong on the lowest supported runtime target (Windows 10 1809) | Verify visually before tuning. If broken, fall back to Composition `Visual.TransformMatrix` with explicit `M34 = -1/depth` perspective matrix on the parent ContainerVisual. |
| `ItemsRepeater` virtualization recycles or reindexes a focused container, dropping or changing its transform / z-index / index state | Re-apply transform, z-index, and index state in `ElementPrepared` and update index state in `ElementIndexChanged`. Clear owner/index state in `ElementClearing` so a recycled container doesn't resolve under a stale index. |
| Hit-testing on heavily-rotated side cards feels mushy at the edges | Capping rotation at 55° and resolving pointer targets against each visible artwork's projected quad keeps side-card clicks aligned with what the user sees. |
| ScrollViewer snap points don't fit the negative-spacing coverflow layout | Keep XAML snap points disabled and use the manipulation-complete center fallback (`ViewChanged` with `IsIntermediate == false` → compute nearest item → `ChangeView`). |
| Coverflow item template accidentally includes an outer `Button` and side-card clicks navigate immediately | The control owns activation; the Coverflow item template **must not** include an outer Button. |
| `Canvas.ZIndex` reset on container recycle | Re-apply z-index in the same `ElementPrepared` pass that re-applies the transform. |
| Album card template duplication remains in the flat carousels | Pre-existing tech debt; sibling extraction is warranted later. **Out of scope for this pass.** |

## Verification

1. **Build**: `dotnet build Hypersonic.sln -p:Platform=x64` succeeds.
2. **Launch via Visual Studio (F5)** — the WinUI app cannot run via `dotnet run`.
3. **Random Picks renders first** as a coverflow hero; there is no duplicate flat `Random Picks` carousel at the bottom.
4. **Center cover art is enlarged** and head-on; immediate neighbors are tilted toward the camera and rotate around their own visual centers (not pivoting from a corner); further neighbors are smaller, dimmer, pushed back.
5. **Z-order is correct**: when neighbors visually overlap the centered card, the centered card always renders on top.
6. **Scroll**: mouse wheel and arrow buttons advance one card; on manipulation end, the nearest card snaps precisely to center.
7. **Click contract**:
   - Clicking a side card centers it (no navigation).
   - Clicking the already-centered card navigates to the album detail page.
8. **Keyboard**: Tab into the control; Left/Right move the centered selection; Home/End jump to ends; Enter/Space on the centered card navigates.
9. **Metadata**: title, artist, and year for the centered album render once below the carousel and update as selection changes.
10. **No regression** in the remaining flat carousels — Recently Played, Recently Released, Recently Added, and Most Played keep their existing bindings and click behavior.
11. **No new errors** in the per-launch log file.

## Out of Scope

- Replacing any other Home Page carousel with Coverflow.
- Extracting the album card into a shared `UserControl` or shared `DataTemplate` resource.
- Spring-animated transform interpolation via Composition (v2 polish).
- Touch / gamepad input tuning beyond what `ScrollViewer` and `Control.OnKeyDown` give for free.
- Redesigning the rest of the Home Page layout.
- Persisting `SelectedIndex` across navigation.
- An automation peer / accessibility wiring beyond what the underlying real `UIElement` tree already preserves.
