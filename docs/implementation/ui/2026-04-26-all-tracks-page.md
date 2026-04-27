# All Tracks Page — Implementation Notes

Companion to [`docs/design/ui/2026-04-26-all-tracks-page.md`](../../design/ui/2026-04-26-all-tracks-page.md). This document covers implementation-specific decisions, deviations from the design, and details that only appear in source.

---

## IncrementalSongCollection — `OnBatchLoaded` callback and re-sort on append

The design doc states that when an active sort is selected, newly loaded batches should be "re-sorted into the existing data." The implementation hooks this via an `Action? OnBatchLoaded` property on the collection, which `TracksViewModel` sets to `OnListViewBatchLoaded` in its constructor:

```csharp
Tracks = new IncrementalSongCollection(LoadMoreSongsAsync);
Tracks.OnBatchLoaded = OnListViewBatchLoaded;
```

`OnBatchLoaded` fires at the end of `LoadBatchCoreAsync` (after items are added to the collection), and only when the callback returned a non-null, non-empty list. The ViewModel's handler checks `SelectedSortIndex > 0` and calls `ApplySortingInternal()` if an active sort is in effect. This same re-sort path is used for explicit first-batch loads in `LoadFirstBatchAsync` and `RetryFailedLoadAsync`.

The callback approach avoids the collection needing to know about sort logic, keeping it reusable.

---

## IncrementalSongCollection — `LoadBatchCoreAsync` as internal test seam

The WinUI test host crashes before `[UITestMethod]` tests run, which prevents testing `LoadMoreItemsAsync` (it uses `AsyncInfo.Run` which requires WinUI infrastructure). The implementation exposes `LoadBatchCoreAsync()` as an `internal` method that contains the actual callback-invoke-and-mutate logic, decoupled from `AsyncInfo.Run`.

`LoadMoreItemsAsync` is a thin wrapper:

```csharp
public IAsyncOperation<LoadMoreItemsResult> LoadMoreItemsAsync(uint count)
{
    return AsyncInfo.Run(async _ =>
    {
        var loaded = await LoadBatchCoreAsync();
        return new LoadMoreItemsResult { Count = loaded };
    });
}
```

The `LoadBatchCore_*` tests in `IncrementalSongCollectionTests` call `LoadBatchCoreAsync` directly (the remaining tests — constructor, `Reset`, `AddExternalBatch`, `ReplaceAll` — exercise synchronous collection APIs and don't need the test seam). The test file includes a comment block explaining how to convert back to `LoadMoreItemsAsync` once the test host issue is resolved. This parallels the `NowPlayingTracker` synchronous test seam pattern documented in [`2026-04-24-now-playing-track-highlight.md`](2026-04-24-now-playing-track-highlight.md).

The test host limitation is also documented in `ROADMAP.md` under "Known Issues."

---

## IncrementalSongCollection — `LoadBatchCoreAsync` always passes `PageSize`, not `count`

`LoadMoreItemsAsync` receives a `count` parameter from the ListView, but `LoadBatchCoreAsync` ignores it and always passes `(uint)PageSize` to the callback. The ListView's `count` hint can be unpredictable (it varies by viewport size and prefetch distance), and the page size must be deterministic for the EOF heuristic (`items.Count >= PageSize`).

This is a deliberate deviation from `IncrementalAlbumCollection`, which passes the ListView-supplied `count` through to `_loadMoreFunc(count)` and does not use a short-page EOF heuristic. The album collection doesn't need one because it has no stale-result or multi-generation concerns — it loads a single unbounded list. The song collection's EOF heuristic (`items.Count >= PageSize`) requires a predictable batch size, so `PageSize` is always used regardless of what the ListView requests.

---

## TracksViewModel — `_suppressSortOnDirectionChange` flag

The design doc specifies that changing sort field resets the direction to that field's default. Since both `SelectedSortIndex` and `IsSortDescending` are `[ObservableProperty]` fields with partial change handlers, changing the sort field triggers two property changes: the field change, then the direction change. Without coordination, this would cause two sorts.

The implementation uses a `_suppressSortOnDirectionChange` flag:

```csharp
partial void OnSelectedSortIndexChanged(int oldValue, int newValue)
{
    if (newValue == 0) return;

    _suppressSortOnDirectionChange = true;
    IsSortDescending = DefaultDirectionDescending[newValue] == 1;
    _suppressSortOnDirectionChange = false;
    ApplySorting();
}
```

`OnIsSortDescendingChanged` checks this flag and skips the sort when it's set. The `DoesNotDoubleSortWhenDirectionChanges` test verifies this by counting `CollectionChanged` Reset events — exactly one occurs per field change.

The default directions are stored as `static readonly int[] DefaultDirectionDescending = [0, 0, 0, 0, 0, 1]`, indexed by sort option position. Only Year (index 5) defaults to descending.

---

## TracksViewModel — `HasLoadedOnce` property for empty state

The design doc describes an empty state ("No tracks found") but doesn't detail how to distinguish "haven't loaded yet" from "loaded and got zero results." The implementation adds a `HasLoadedOnce` bool property that gates the `IsEmpty` computed property:

```csharp
public bool IsEmpty => HasLoadedOnce && _allSongs.Count == 0 && string.IsNullOrEmpty(ErrorMessage);
```

`HasLoadedOnce` is set to `true` in `LoadFirstBatchAsync` and `RetryFailedLoadAsync` only when the callback returns non-null (i.e., the result wasn't discarded as stale). This prevents a flash of "No tracks found" during the initial loading state.

---

## TracksViewModel — `EmptyMessage` varies by search state

The design doc calls out two empty states (empty library vs. no search results) but treats them as a single visual. The implementation uses a computed `EmptyMessage` property:

```csharp
public string EmptyMessage => string.IsNullOrEmpty(_searchQuery) ? "No tracks found" : "No results";
```

This gives contextually appropriate messaging without a separate property for search-vs-browse empty state.

---

## TracksViewModel — `IsLoading` property

The design doc doesn't mention a loading indicator, but the implementation adds an `IsLoading` bool that wraps the explicit first-batch loads (`LoadFirstBatchAsync`, `RetryFailedLoadAsync`). The XAML shows a `ProgressRing` bound to it. This only covers the initial/retry loads — ListView-driven incremental loads happen transparently without a full-page spinner (they fire as the user scrolls, and partial data is already visible).

---

## TracksViewModel — `OnSearchTextChangedAsync` exposed as public async method

The design doc describes search as a debounced text box event. The implementation exposes `OnSearchTextChangedAsync(string text)` as a public method (not a command) because it's called from the code-behind `SearchBox_TextChanged` event handler. The TextBox `TextChanged` event fires synchronously and doesn't support command binding, so the code-behind does the fire-and-forget:

```csharp
private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
{
    _ = ViewModel.OnSearchTextChangedAsync(SearchBox.Text);
}
```

The ViewModel method handles the CancellationTokenSource lifecycle, 300ms debounce, trim, and delegation to `ExecuteSearchAsync`.

Note: `TracksViewModel` declares a `[ObservableProperty] SearchText` property, but it is not bound to the `TextBox` in XAML and is not used by the search logic. The code-behind reads `SearchBox.Text` directly. `SearchText` exists as a bindable surface for potential future use (e.g., programmatic search-box population), but currently has no consumers.

---

## TracksViewModel — transient registration despite `NavigationCacheMode="Required"`

The ViewModel is registered as transient in DI (`services.AddTransient<TracksViewModel>()`), but the Page uses `NavigationCacheMode="Required"`. This works because the Page constructor resolves the ViewModel once, and the cached Page instance holds the same ViewModel reference across navigations. The navigation framework caches the Page, not the ViewModel — so the ViewModel's lifecycle is effectively tied to the Page's cache lifetime. This is the same pattern used by all other cached pages in the app.

---

## TracksPage — theme change re-rendering via `DataContext` toggle

The artist and album link `RichTextBlock` cells use `DataContextChanged` to render hyperlinks with theme-appropriate brush colors. When the app theme changes, these cells need to re-render with the new colors. The implementation handles this by toggling each visible container's `DataContext` to null and back, which re-fires `DataContextChanged`:

```csharp
private void ReRenderAllTrackRowCells()
{
    for (var i = 0; i < TrackListView.Items.Count; i++)
    {
        if (TrackListView.ContainerFromIndex(i) is ListViewItem container)
        {
            var dataContext = container.DataContext;
            container.DataContext = null;
            container.DataContext = dataContext;
        }
    }
}
```

This is the same pattern used by `PlaylistDetailPage` and `ArtistDetailPage`. The brushes are read from hidden `TextBlock` elements (`SecondaryBrushProvider`, `TertiaryBrushProvider`) that resolve `ThemeResource` brushes — this avoids hardcoding color values and automatically picks up theme changes.

---

## TracksPage — column header row visibility

The column header `Grid` (row 2) is bound to `IsLoading` via `InverseBoolToVisibilityConverter` — it hides during the initial loading state to avoid showing column headers above an empty list with a spinner. Once data loads (or an error appears), the headers become visible. This is a minor UI polish not mentioned in the design doc.

---

## TracksPage — `ListView.ItemContainerTransitions` reduced to `RepositionThemeTransition`

The default `ListView` transitions include `AddDeleteThemeTransition`, `ContentThemeTransition`, `ReorderThemeTransition`, and `EntranceThemeTransition`. For a list that may `ReplaceAll` hundreds of items on sort, these animations cause visual stuttering. The implementation explicitly sets only `RepositionThemeTransition`, which provides smooth reorder animation without entrance/deletion effects on bulk operations.

---

## SongMapper — behavioral difference from `AlbumDetailViewModel`'s original mapper

The design doc notes the optional `albumVersion` parameter. A subtle difference from the original `AlbumDetailViewModel.MapSongInfo`: that mapper always called `AlbumMapper.StripVersionSuffix(song.Album, albumVersion)` regardless of whether `albumVersion` was null. `StripVersionSuffix` with a null version returns the input unchanged, so the behavior was equivalent — but the shared `SongMapper` makes the null-check explicit and falls back to `song.Album ?? string.Empty`:

```csharp
Album = albumVersion != null
    ? AlbumMapper.StripVersionSuffix(song.Album, albumVersion)
    : song.Album ?? string.Empty,
```

The `PlaylistDetailViewModel` mapper had no version stripping at all (it never had album context), and `ArtistDetailViewModel`'s `MapChildToSongInfo` had the same null-check pattern. The shared mapper unifies all three with the optional parameter.

---

## SongMapper tests — full `ISubsonicClient` fake

`SongMapperTests` requires an `ISubsonicClient` because the mapper calls `GetCoverArtUri` and `GetStreamUri`. Since there's no mocking library in the project, the test creates a `FakeSubsonicClient` that implements the full interface. Only `GetCoverArtUri` and `GetStreamUri` return real values; all other methods throw `NotImplementedException`. The same pattern is used in `TracksViewModelTests` with a more capable fake that also implements `Search3Async`.

---

## TracksViewModelTests — `FakeSubsonicClient` with sync and async handlers

The test fake supports two handler patterns:

- `Search3Handler`: synchronous `Func<string, int?, int?, Search3Response>` for simple tests
- `Search3AsyncHandler`: async `Func<string, int?, int?, Task<Search3Response>>` for tests that need `TaskCompletionSource` control over timing (stale-result guard tests)

The async handler takes precedence when both are set. The `Search3ShouldThrow` flag provides a simple way to test the error path without custom handlers.

---

## Search3Request — fix was a one-line change

The design doc describes this as a bug fix and test addition. The actual fix was changing:

```csharp
// Before:
sb.Append($"&query={this.Query}");

// After:
sb.Append($"&query={Uri.EscapeDataString(this.Query)}");
```

The test suite covers spaces, ampersands, hash characters, unicode (`été`), empty strings, and `songCount`/`songOffset` parameter propagation. This fix also benefits the existing Shell search (which uses the same `Search3Request`), though that wasn't explicitly tested as part of this change.

---

## Navigation registration — sidebar ordering

The design doc specifies "between Artists and Playlists." In `ShellPage.xaml`, the new `NavigationViewItem` is inserted immediately after the Artists item. In `PageService.Configure()`, it's placed between `ArtistsViewModel` and `PlaylistsViewModel` for consistency, though PageService ordering is cosmetic (it's a dictionary, not ordered). In `App.xaml.cs`, the DI registration follows the same visual order.

The sidebar glyph is `&#xE142;` (MusicNote from Segoe MDL2 Assets), matching the empty-state icon used on the page itself.

---

## What's not in the design doc

1. **`IsLoading` / `ProgressRing`** — The design doc has no loading indicator spec. The implementation adds a centered `ProgressRing` visible during initial and retry loads.

2. **Column header hiding during load** — Headers are hidden while `IsLoading` is true to avoid visual awkwardness.

3. **`ListView.ItemContainerTransitions` override** — Reduced to `RepositionThemeTransition` only to prevent animation stuttering on bulk sort operations.

4. **`OnBatchLoaded` callback pattern** — The design doc says "re-sort new batches" but doesn't prescribe the mechanism. The callback property on the collection is a clean separation.

5. **`HasLoadedOnce` gating** — Needed to distinguish "loading" from "empty" in the UI, not discussed in the design doc.

6. **`_suppressSortOnDirectionChange` coordination** — The design doc describes the "direction resets to default" behavior but not the property-change-handler coordination needed to avoid double-sorting.

7. **Theme change re-rendering** — Inherited from existing page patterns, not mentioned in the design doc.

8. **`ROADMAP.md` Known Issues section** — Documents the WinUI test host crash and the workarounds (synchronous `NowPlayingTracker`, internal `LoadBatchCoreAsync`) so future contributors know why these test seams exist.
