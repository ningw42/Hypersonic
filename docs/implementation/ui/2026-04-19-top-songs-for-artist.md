# Top Songs for Artist — Implementation Notes

Companion to [`docs/design/ui/2026-04-19-top-songs-for-artist.md`](../../design/ui/2026-04-19-top-songs-for-artist.md). This document covers implementation-specific decisions, deviations from the design, and details that only appear in source.

---

## Section placement

The design doc does not specify where the Top Songs section should appear relative to other sections. During implementation, it was initially placed before the Discography. This was changed to appear **after the Discography album grid and before the Similar Artists section**, keeping albums (the primary content) front and center and surfacing popularity data as a secondary section. The design doc was updated to reflect this decision.

---

## OnNavigatedTo restructuring

### Design doc requirement

The design doc calls for `GetArtistAsync` to be awaited first (required), with `GetArtistInfo2Async` awaited separately afterward. Both can still be launched in parallel for performance.

### Implementation

Both tasks are launched immediately, but `artistTask` is awaited first. The `infoTask` is typed as `Task<GetArtistInfo2Response>?` and declared outside the try block. A `finally` block observes the info task on every exit path to prevent unobserved task exceptions:

```csharp
finally
{
    IsLoading = false;
    if (infoTask != null)
    {
        try { await infoTask; } catch { }
    }
}
```

This handles three early-exit scenarios: (1) `GetArtistAsync` throws, (2) artist is null, (3) outer exception. Without this, the fire-and-forget `infoTask` would leave an unobserved `Task` if it also faulted.

### Top songs guard

`LoadTopSongsAsync` is only called when the artist fetch succeeds. A `loadedSuccessfully` flag is set at the end of the try block (after `ApplySorting()`) and checked after the finally block. This prevents calling `GetTopSongsAsync` with an empty artist name or fetching albums for an artist that failed to load.

---

## IsArtistCredited matching chain

The design doc defines a 4-step matching chain. Step 3 ("name match on structured `Artists[]` entries without usable ID") was tightened during review: entries that have a non-empty ID different from `artistId` are **skipped** in the name fallback. Only entries with an empty or null ID fall through to name comparison. This prevents misattribution in same-name collisions where the structured data already identifies a different artist by ID.

```csharp
// Step 3: only match by name when the entry has no usable ID
if (child.Artists != null)
{
    foreach (var a in child.Artists)
    {
        if (!string.IsNullOrEmpty(a.Id))
            continue;
        if (string.Equals(a.Name, artistName, StringComparison.OrdinalIgnoreCase))
            return true;
    }
}
```

The method is `internal static` to enable direct unit testing without WinUI infrastructure.

---

## Cross-cache favorite sync

### Design doc requirement

Toggling a star must sync `IsStarred` on the matching `SongInfo` (by song ID) in the other cache, so switching modes does not show stale state.

### Implementation: both caches, not "other" cache

The initial implementation used `SyncStarInOtherCache`, which determined the "other" cache based on `SelectedTopSongsMode`. This was racy: if the user toggled a star, switched modes, and the async API call then failed, the rollback would update the wrong cache.

The fix is `SyncStarInBothCaches`, which unconditionally iterates both `_communityTopSongs` and `_personalTopSongs` by song ID. This is safe regardless of mode switch timing because:

- The currently displayed `SongInfo` is already updated directly on the instance (via `song.IsStarred`).
- The cache scan catches the corresponding entry in the other list, and also the entry in the current list if it's a different object instance (which can happen since each cache builds its own `SongInfo` objects from independent API responses).

---

## SelectorBar auto-selection on initial load

### Problem

On first load, `SelectedTopSongsMode` defaults to `0` (community). When community is available, setting it to `0` again does not fire `PropertyChanged`, so the `SelectorBar` in the XAML never receives a programmatic selection — it renders with no item visually selected.

### Solution: two-part fix

**ViewModel ordering**: `SelectedTopSongsMode` and `ApplyTopSongsMode()` are set *before* `HasTopSongs = true`. This ensures the mode and collection are correct before the section becomes visible.

**Code-behind sync on `HasTopSongs`**: The page subscribes to `ViewModel.PropertyChanged` and calls `SyncTopSongsModeSelector()` when either `SelectedTopSongsMode` or `HasTopSongs` changes. Since `HasTopSongs` transitions from `false` to `true` (always a change), this reliably triggers the selector sync even when the mode stays at `0`.

```csharp
private void SyncTopSongsModeSelector()
{
    if (!ViewModel.HasTopSongs) return;
    var target = ViewModel.SelectedTopSongsMode == 0 ? CommunityModeItem : PersonalModeItem;
    if (TopSongsModeSelector.SelectedItem != target)
        TopSongsModeSelector.SelectedItem = target;
}
```

---

## Loading UX: inline ProgressRing

### Design doc requirement

`IsTopSongsLoading` should drive an inline `ProgressRing` within the section, and the section should be visible during loading.

### Implementation

A computed property `IsTopSongsSectionVisible` (`IsTopSongsLoading || HasTopSongs`) drives the section's visibility. This makes the "Top Songs" header appear as soon as loading starts.

Inside the header row, the `ProgressRing` is in column 1 (between the title and the `SelectorBar`), visible only while `IsTopSongsLoading` is true. The `SelectorBar`, column headers, and `ListView` are each gated behind `HasTopSongs`, so they appear only when data is ready. The result is: title + spinner during load, title + selector + track list after load.

Both `IsTopSongsLoading` and `HasTopSongs` carry `[NotifyPropertyChangedFor(nameof(IsTopSongsSectionVisible))]` to keep the computed property current.

---

## Track row template reuse

The top songs rows use the same column layout as `PlaylistDetailPage.xaml`. The column definitions are duplicated rather than extracted into a shared resource because the design doc notes that column layout should remain separable for future per-page configurability. The code-behind event handlers (`TopSongAlbumLink_DataContextChanged`, `TopSongArtistLinks_DataContextChanged`) follow the same patterns as `PlaylistDetailPage.xaml.cs` but are separate methods because:

1. The artist self-link suppression logic passes `ViewModel.CurrentArtistId` as `excludeArtistId`, which `PlaylistDetailPage` does not do.
2. The `RenderArtistSegments` method on `ArtistDetailPage` already existed with the `excludeArtistId` parameter (for album card artist links), so the top song rows reuse it.

---

## Theme-change rerender

`ArtistDetailPage` already subscribed to `ActualThemeChanged` for the discography album cards. The handler now also calls `ReRenderTopSongsRows()`, which iterates the `TopSongsListView` containers and resets `DataContext` to trigger `DataContextChanged` on the `RichTextBlock` cells. This follows the same pattern as `PlaylistDetailPage.ReRenderAllTrackRowCells()`.

---

## DI registration

`ArtistDetailViewModel` gained an `IPlaybackService` constructor parameter (for click-to-play). No change to `App.xaml.cs` was needed — `IPlaybackService` is already registered as a singleton and the DI container resolves it automatically.

---

## Files changed

| File | Change |
|---|---|
| `Hypersonic/ViewModels/ArtistDetailViewModel.cs` | Added top songs properties, loading logic, artist matching, star sync, playback commands |
| `Hypersonic/Views/ArtistDetailPage.xaml` | Added Top Songs section with SelectorBar, column headers, track list ListView |
| `Hypersonic/Views/ArtistDetailPage.xaml.cs` | Added SelectorBar sync, track click, album/artist link rendering, theme rerender |
| `Hypersonic/Strings/en-us/Resources.resw` | Added `ArtistDetail_TopSongs.Text` |
| `Hypersonic.Tests/ViewModels/ArtistDetailViewModelTopSongsTests.cs` | New: 22 tests covering matching, availability, failure isolation, sync, ranking |
| `Hypersonic.Tests/ViewModels/ArtistDetailViewModelAlbumMappingTests.cs` | Added `NullPlaybackService`, updated constructor call |
| `docs/design/ui/2026-04-19-top-songs-for-artist.md` | Added section placement note |

---

## Test coverage

22 tests in `ArtistDetailViewModelTopSongsTests`:

| Category | Tests | What's verified |
|---|---|---|
| IsArtistCredited | 8 | All 4 matching steps, name-with-different-ID rejection, multi-artist, case insensitivity |
| Source availability | 5 | Both available, community-only, personal-only, neither, zero play count |
| Failure isolation | 4 | Community fetch throws, album fetch throws, both throw, artist returns null (no top songs attempted) |
| Loading state | 1 | Both `IsLoading` and `IsTopSongsLoading` are false after `LoadArtistAsync` completes; artist name is populated |
| Mode switching | 1 | Collection replacement on mode toggle |
| Ranking | 1 | PlayCount descending sort, sequential PositionNumber |
| Cross-cache star sync | 2 | Sync on success, rollback on API failure |

Tests use a `FakeSubsonicClient` with configurable return values and failure injection (`TopSongsShouldThrow`, `GetAlbumShouldThrow`, `StarShouldThrow`). The `ArtistFetchFails` test exercises the null-artist guard path (artist not found), not a network-level `GetArtistAsync` exception — the latter is covered by the existing outer try/catch in `LoadArtistAsync` but is not separately tested here. Tests live in the `Hypersonic.Tests` WinUI test project (which sets `UseWinUI=true` and has testhost configuration), but they only exercise ViewModel logic and do not require the WinUI test host to run.
