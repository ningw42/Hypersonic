# Coverflow Control - Implementation Notes

Companion to [`docs/design/ui/2026-05-07-coverflow-control.md`](../../design/ui/2026-05-07-coverflow-control.md). This document records implementation details, review-driven fixes, and deviations or clarifications that are intentionally not repeated in the design doc.

---

## Source layout

The implementation is split across the reusable control, its per-item container, the default template, and the Home Page integration:

| File | Responsibility |
|---|---|
| [`Hypersonic/Controls/CoverflowControl.cs`](../../../Hypersonic/Controls/CoverflowControl.cs) | Templated control, `ItemsRepeater` factory, item-source observation, selection coercion, scroll centering, transform recompute, hit testing, keyboard and pointer activation. |
| [`Hypersonic/Controls/CoverflowItemContainer.cs`](../../../Hypersonic/Controls/CoverflowItemContainer.cs) | Focusable wrapper for each realized item. Stores owner/index state and exposes dimming/geometry helpers used by the parent control. |
| [`Hypersonic/Themes/Generic.xaml`](../../../Hypersonic/Themes/Generic.xaml) | Default templates for `CoverflowControl` and `CoverflowItemContainer`, including the horizontal `ScrollViewer`, `ItemsRepeater`, arrow buttons, `ContentPresenter`, and dimming overlay. |
| [`Hypersonic/Views/MainPage.xaml`](../../../Hypersonic/Views/MainPage.xaml) | Uses the control for the top `Random Picks` hero, with a cover-art-only item template and fixed metadata below the carousel. |
| [`Hypersonic/Views/MainPage.xaml.cs`](../../../Hypersonic/Views/MainPage.xaml.cs) | Handles `ItemInvoked` and routes the selected album to the existing album-detail navigation path. |

---

## Item source observation and cached view

`ItemsSource` is always counted and indexed through a cached `ItemsSourceView`, matching the design's "do not narrow to `IList`" rule. The cached view also supplies `CollectionChanged`, so the control can respond when `ObservableCollection<AlbumInfo>` is populated after the initial binding.

The loaded/unloaded lifecycle is deliberately conservative:

- `Loaded` attaches `CollectionChanged`, reapplies `ItemsSource` to the repeater, and recomputes selection, padding, transforms, and arrow state.
- `Unloaded` detaches `CollectionChanged`, cancels pending press invocation, clears hover/press state, clears the cached `ItemsSourceView` references, and detaches template event handlers.
- `ClearItemsSourceView()` only clears references. It deliberately does not attempt `IDisposable`-style cleanup; the control's owned lifecycle work is detaching `CollectionChanged` and dropping the cached view/source references. Revisit that only with an explicit unload smoke test because `ItemsSourceView` is a WinUI-projected object, not a control-owned resource.

Collection changes are handled on the dispatcher. Non-`Add` changes clear the pending selection request because replace/reset/remove operations invalidate the old index request. Incremental `Add` preserves a pending out-of-range selection so an initial `SelectedIndex="9"` can settle correctly as `RandomPicks` fills one item at a time.

---

## Pending selection during incremental population

The Home Page starts the hero at `SelectedIndex="9"` so the usual 20 random picks open with cards on both sides. At binding time, however, `RandomPicks` may still be empty and then populate incrementally.

Implementation detail:

- If the source is empty, the requested `SelectedIndex` is stored in `_pendingSelectionIndex` and `SelectedItem` becomes `null`.
- While the source grows but still has fewer items than the pending index, the control internally falls back to the highest available index so `SelectedItem` remains meaningful.
- Those fallback steps are internal only: `OnSelectedIndexChanged` skips external `SelectionChanged` and `CenterOnIndex` side effects while `_isSettingSelectedIndexInternally && _pendingSelectionIndex >= 0`.
- Once the pending index exists, the control applies it internally, clears the pending request, updates `SelectedItem`, and then normal selection behavior resumes.

The invariant is that externally meaningful selection changes fire events and centering once; internal coercion steps used to walk toward a pending index stay silent until the requested index becomes valid.

This is a deliberate narrowing of the design doc's broad statement that `SelectionChanged` is raised when `SelectedIndex` changes. User-facing selection changes do raise it; transient internal fallback steps while honoring an out-of-range pending request do not.

---

## Repeater factory and container ownership

The `ItemsRepeater.ItemTemplate` is replaced with a control-owned `CoverflowContainerFactory`. The user's `ItemTemplate` is forwarded to each `CoverflowItemContainer.ContentTemplate`; the user template remains visual-only.

The factory owns both `Content` and `ContentTemplate`:

- `GetElement` assigns `Owner`, `Content`, and `ContentTemplate`.
- `RecycleElement` clears `Content`, resets visual state, and stores the container for reuse.
- `CoverflowItemContainer` does not repair or infer its own content/template on `Loaded`; that ownership stays in one place.

The recycle stack has no explicit cap. For the current 20-item hero this is expected to stay small, roughly bounded by the difference between source count and the realized window; add a cap before reusing the control for much larger long-lived sources.

`ElementPrepared` and `ElementIndexChanged` have different contracts:

- `ElementPrepared` sets owner/index, applies metrics, resets visual state synchronously, sets the composition press scale to `1.0`, updates selection, and queues the real transform recompute until layout has resolved sizes.
- `ElementIndexChanged` updates only index/metrics and queues transform recompute. It does not reassign `Content`; a reindex means the same realized element moved to a different index, not that the framework swapped its data item.
- Collection changes call `UpdateRealizedItemContent()` to refresh realized content for replace/reset-like paths where the data at an index may have changed.

The synchronous visual reset in `RecycleElement`, `OnElementPrepared`, and `OnElementClearing` is an implementation tightening beyond the design doc. It prevents a recycled container from showing one frame of the previous card's projection, scale, dimming, or z-index before the queued transform recompute runs.

---

## Square item invariant

`ApplyContainerMetrics()` sets both `Width` and `Height` to `ItemWidth`. This is intentional: coverflow items are square album-art cards, and `ItemWidth` is the single size knob for both axes. `CoverflowItemContainer.ArtworkSide`, `ArtworkCenterY`, and `ArtworkCenterYRatio` all depend on that invariant when choosing the rotation center and press animation center.

The surrounding Home Page control has a taller `Height` than the item width so there is room for focus visuals, projection, and metadata spacing; the item itself stays square.

---

## Scroll centering and suppression

The control keeps XAML snap points disabled and owns centering in code. `CenterOnIndex()` calculates the target offset from leading padding plus `index * (ItemWidth + ItemSpacing)` and calls `ScrollViewer.ChangeView`.

Because `ChangeView` completes asynchronously, `_suppressViewChanged` is not cleared on the next dispatcher tick. Instead, `OnScrollerViewChanged` clears it on the terminal non-intermediate `ViewChanged` event and returns before running the manual snap fallback. This keeps programmatic scrolls from being mistaken for user scroll completion.

For user scrolling, the terminal `ViewChanged` path computes the nearest item by center, updates `SelectedIndex` if needed, and otherwise calls `CenterOnIndex(nearest, animate: true)` to remove any residual sub-pixel drift.

---

## Transform and hit-test details

The design doc covers the main transform formulas. Implementation details worth preserving:

- The all-item recompute computes count, step, scroller center, and nearest center index once, then mutates each realized container's existing `PlaneProjection` and `ScaleTransform`.
- `MinimumScale`, `MaxRotationDegrees`, `RotationPerStep`, `MaxDimmingOpacity`, and press scale are clamped through small safety helpers before use.
- Side cards use a two-plane depth model: the active center card has `LocalOffsetZ = 0`; all non-center cards share `NonCenterItemOffsetZ`.
- `Canvas.ZIndex` is sorted by raw center distance so nearer side cards draw over farther side cards, while the center card always receives the highest z-index.
- Pointer targeting does not trust the unprojected element bounds. `ResolveContainerAt()` projects each realized artwork square into a convex quad, checks the pointer against that quad, then breaks ties by z-index and distance to the quad center.

The geometry helpers remain inside the WinUI assembly for this pass. No pure helper extraction or automated unit tests were added.

---

## Press and invocation lifecycle

The invocation contract is "side item centers, already-centered item invokes." Pointer, tap, and keyboard paths all route through the control instead of through the supplied item template.

Center-card pointer press uses a composition scale animation. On release, the control restores the scale first and raises `ItemInvoked` after `PressOutDurationMilliseconds`, which keeps navigation from cutting off the release animation.

The delayed invoke is guarded by `_pendingRestoreInvokeVersion`, `_pendingRestoreInvokeIndex`, `_isLoaded`, and `SelectedIndex == index`. `Unloaded` clears the pending index and increments the version, so a delayed callback from an old mount no-ops even if the page is re-entered quickly.

Rapid repeated center taps for the same index are debounced while a restore invoke is already pending. That is intentional for this hero interaction and is not exposed as a separate configuration option in v1.

---

## Home Page integration details

The Home Page uses the coverflow only for `Random Picks`:

- `SelectedIndex="9"` is tuned for the normal 20-item random-picks response.
- `ItemWidth="240"` and `Height="280"` make the hero larger than the existing flat carousels while leaving vertical room around the projected cards.
- `ItemSpacing="-180"` intentionally overlaps side covers underneath the centered cover; the XAML includes a comment so future cleanup does not "fix" the negative spacing.
- The item template contains only the square cover-art visual. Title, artist, and year render once below the carousel through `RandomPicksCoverflow.SelectedItem`.
- Album title and artist rendering reuse the existing `RichTextBlock`/hyperlink helpers from the flat album cards, while the album-detail navigation path is shared with the existing `AlbumCard_Click` behavior.

No `MainViewModel` or service changes were required.

---

## Deviations and clarifications from the design doc

| Design statement | Implemented shape |
|---|---|
| `SelectionChanged` is raised when `SelectedIndex` changes. | Suppressed for internal fallback steps while an out-of-range pending selection is waiting for incremental `Add` events. Normal user-facing changes still raise it. |
| Cache an `ItemsSourceView`. | Cached and reused, but cleared without disposal on unload because the attempted WinRT disposable cast crashed. |
| Re-apply transform/index state on `ElementPrepared` and `ElementIndexChanged`; clear state on `ElementClearing`. | Also reset projection, scale, dimming, opacity, and z-index synchronously on prepare/recycle/clear to avoid a stale recycled pose. |
| `ItemWidth` controls card width. | In code it controls both width and height; coverflow cards are square by invariant. |
| Programmatic scroll suppression was unspecified. | `CenterOnIndex()` sets `_suppressViewChanged`; `OnScrollerViewChanged` clears it on the terminal non-intermediate completion event so control-owned `ChangeView` calls do not run the snap fallback. |
| Automated verification includes behavior checks. | No new automated tests or documented manual checklist results landed with this pass. The design doc's verification list remains the manual smoke plan; recorded evidence for the staged implementation is build-level only. |
