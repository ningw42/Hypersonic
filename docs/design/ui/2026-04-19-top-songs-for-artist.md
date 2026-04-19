# Top Songs for Artist

## Overview

Add a "Top Songs" section to the Artist Detail page, showing the most popular tracks for an artist. Users can switch between two data sources — **Community** (Last.FM via server) and **Personal** (play count) — using a `SelectorBar` control. When a source is unavailable, its option is disabled; when both are unavailable, the entire section is hidden.

## Data Sources

### Community (Last.FM via Subsonic API)

- **Endpoint**: `getTopSongs.view`
- **Parameter**: `artist` (artist **name**, not ID), `count` (default 20)
- **Client**: `SubsonicClient.GetTopSongsAsync(string artist, int count)` — already implemented
- **Behavior**: Navidrome fetches top tracks from Last.FM. Returns empty if Last.FM integration is not configured on the server.
- **Name ambiguity**: This endpoint is name-based. If the library contains two artists sharing the same name, the server may return tracks for the wrong one. This is a known limitation of the Subsonic API with no workaround — the community mode inherits whatever the server returns.

### Personal (Play Count)

- **Approach**: Collect songs from the artist's server-associated albums, filter to those credited to the artist, sort client-side by `playCount` descending, take top 20.
- **Data retrieval strategy**: `GetArtistAsync` (already called in `OnNavigatedTo`) returns the artist's album list. For each album, call `GetAlbumAsync` to get the full `Child[]` with `PlayCount`.
- **Coverage limitation**: This only covers albums that the server associates with the artist via `GetArtistAsync`. Guest appearances on another artist's album are included only if the server lists that album under this artist. This is a server-side indexing decision (Navidrome typically associates albums with their album artist, not with featured artists). The personal mode is therefore best-effort over the server's artist→album associations, not a guaranteed exhaustive list of every track the artist appears on.
- **Why not `Search3Async`**: Search3 is paged (`songCount`/`songOffset`) and text-based. It cannot guarantee exhaustive results for large discographies, and name matching is fuzzy. The album-based approach is at least complete over the server's own artist→album mapping.
- **Artist matching**: A song from a fetched album is credited to the current artist if any of the following match (checked in order, first match wins):
  1. `Child.Artists[]` contains an entry with matching `artistId` (preferred — structured OpenSubsonic data)
  2. `Child.ArtistId` matches (legacy Subsonic single-artist field)
  3. `Child.Artists[]` contains an entry with matching `artistName` (structured entry without usable ID — the codebase already handles name-plus-optional-ID pairs via `SongArtistMapper`)
  4. `Child.Artist` matches `artistName` (final fallback — legacy unstructured field)
  
  This correctly identifies collaborations where the artist appears in `Artists[]` by ID or by name, even when they are not the primary artist.
- **Name ambiguity in matching**: Steps 3 and 4 fall back to name-based matching. In libraries with two artists sharing a name, this could misattribute songs. This mirrors the same limitation as the community mode and the Subsonic API in general.
- **Concurrency**: Album fetches are parallelized via `Task.WhenAll` with a bounded fan-out of 5 concurrent requests (using `SemaphoreSlim`) to avoid overwhelming the server.
- **Behavior**: Unavailable if zero songs have `playCount > 0`.

### Availability Detection

- **Community**: Call `GetTopSongsAsync`; if the response contains zero songs, mark as unavailable.
- **Personal**: After fetching and filtering songs, if none have `playCount > 0`, mark as unavailable.
- **Both unavailable**: Hide the entire "Top Songs" section (no header, no selector, no list).

## UI Design

### Section Layout

```
Top Songs    [Community | Personal]        (SelectorBar right-aligned)
─────────────────────────────────────────────────────────────────────────────────────────────
#     Title              Album             Duration   Codec   Bitrate   Plays   BPM    ♥
1     Song Title         Album Name        3:45       flac    1411      42             ♡
2     Song Title         Album Name        4:12       mp3     320       38             ♡
3     Song Title         Album Name        3:33       flac    1411      25      120    ♡
...
```

### Row Template

The top songs rows use the **same column layout as the playlist track rows** in `PlaylistDetailPage.xaml`. This is a natural fit because both share:
- A rank/position column (not album track number) — `SongInfo.PositionNumber` set to 1..N after sorting
- An album column (needed here since songs come from different albums)
- Full interaction surface: click-to-play, album navigation, artist navigation, favorite toggle

Columns: `#` | Title | Album (clickable → album detail) | Artist (clickable → artist detail, though less useful here) | Duration | Codec | Bitrate | Play Count | BPM | Favorite

**Ranking**: Each cached list (`_communityTopSongs`, `_personalTopSongs`) has `PositionNumber` assigned at creation time (1..N), before it is ever bound to the UI. On mode switch, the `TopSongs` collection is replaced with the pre-numbered list — no in-place mutation of already-bound items. `SongInfo.PositionNumber` is a plain (non-observable) property, which is fine because it is set before binding and never changes afterward.

### Row Interactions

Full parity with playlist track rows:
- **Click row**: Play the song (queue all top songs, start at clicked position)
- **Album link**: Navigate to album detail page
- **Artist links**: Segmented clickable artist navigation, using the existing `ArtistDetailPage` pattern which suppresses self-links for the current artist (via `excludeArtistId` check in `ArtistDetailPage.xaml.cs`)
- **Favorite toggle**: Star/unstar via `SubsonicClient`. When a song appears in both cached lists (community and personal), toggling the star must sync `IsStarred` on the matching `SongInfo` (by song ID) in the other cache, so switching modes does not show stale state.
- **Theme-change rerender**: The clickable album/artist `RichTextBlock` cells use theme-colored brushes. `ArtistDetailPage` must add a theme-change rerender hook for the top songs `ListView` rows, matching the pattern in `AlbumDetailPage.xaml.cs` and `PlaylistDetailPage.xaml.cs` where `ActualThemeChanged` triggers `ContainerContentChanging` or equivalent refresh on the list.
- **Right-click context menu**: Out of scope for this change. No context menu exists on playlist track rows today; this will be added in a future feature across all track lists.

### Control Choice: `SelectorBar`

Use the native WinUI 3 `SelectorBar` control (available since Windows App SDK 1.5, we use 1.8):

```xml
<SelectorBar x:Name="TopSongsModeSelector"
             SelectionChanged="TopSongsModeSelector_SelectionChanged">
    <SelectorBarItem Text="Community" />
    <SelectorBarItem Text="Personal" />
</SelectorBar>
```

**Why `SelectorBar`**:
- Native WinUI 3 control, no third-party dependencies
- Designed for exactly this pattern: switching between mutually exclusive views
- `SelectorBarItem` inherits from `Control`, so `IsEnabled` works for graying out unavailable options
- Compact, inline appearance — fits next to a section subtitle

**Known issue**: There is a reported rendering bug when `CommunityToolkit.WinUI.Controls.SettingsControls` is also referenced (microsoft-ui-xaml#9589). We don't use that package, so this is not a concern.

### Disabled State

- When one source is unavailable: its `SelectorBarItem.IsEnabled = false`, auto-select the available one.
- When both are unavailable: collapse the entire section (`Visibility.Collapsed` on the containing panel).

### Song Count

Display up to **20** top songs by default to keep the section concise within the artist page.

## ViewModel Changes

### New Properties on `ArtistDetailViewModel`

```csharp
[ObservableProperty]
private ObservableCollection<SongInfo> topSongs = [];

[ObservableProperty]
private bool isTopSongsLoading;        // true while fetching, drives inline ProgressRing

[ObservableProperty]
private bool hasTopSongs;              // true if at least one source returned data (only meaningful after loading completes)

[ObservableProperty]
private bool isCommunityAvailable;     // community source has data

[ObservableProperty]
private bool isPersonalAvailable;      // personal source has data

[ObservableProperty]
private int selectedTopSongsMode;      // 0 = Community, 1 = Personal
```

**State transitions**:
- Initial: `IsTopSongsLoading = false`, `HasTopSongs = false` → section hidden
- Fetching: `IsTopSongsLoading = true` → section visible with inline `ProgressRing`
- Loaded, data available: `IsTopSongsLoading = false`, `HasTopSongs = true` → section visible with song list
- Loaded, no data: `IsTopSongsLoading = false`, `HasTopSongs = false` → section hidden

### Data Flow

1. `OnNavigatedTo` receives `artistId`. The existing load path currently awaits `Task.WhenAll(GetArtistAsync, GetArtistInfo2Async)`, which means an info2 fault aborts the entire page before top-songs logic runs. **This must be restructured**: `GetArtistAsync` should be awaited first (it is required — provides `artistName` and album list). `GetArtistInfo2Async` should be awaited separately in its own try/catch afterward (it is optional — bio, similar artists, image fallback). This decouples the required artist data from the optional info2 call, and ensures top-songs loading is not blocked by an info2 failure. The inner try/catch that already exists for processing info2 results remains, but is no longer masked by a `Task.WhenAll` that throws first. Both calls can still be launched in parallel for performance; only the await order changes.
2. **After** the existing load path completes (`IsLoading = false`), set `IsTopSongsLoading = true` and launch top-songs fetches in the background:
   - Community: `GetTopSongsAsync(artistName, count: 20)`
   - Personal: For each album from step 1, `GetAlbumAsync(albumId)` in parallel (max 5 concurrent via `SemaphoreSlim`) → collect all `Child` objects → filter by artist membership (see matching rules above) → filter `playCount > 0` → sort by `playCount` desc → assign `PositionNumber = 1..N` → take 20. **Individual album failures are logged and skipped** — the personal source is best-effort, so partial results from successfully fetched albums are still usable. The source is marked unavailable only if zero songs with `playCount > 0` survive after all album fetches (successful or failed) complete.
3. **Failure isolation**: Community and personal fetches are each wrapped in independent try/catch. A complete failure of the community call marks that source as unavailable. For personal mode, see the per-album skip behavior above. Neither source's failure propagates to the page-level error handler. The page remains functional even if both sources fail entirely.
4. Cache both result sets in private fields (`_communityTopSongs`, `_personalTopSongs`).
5. Set `IsTopSongsLoading = false`. Determine availability; default to Community if available, otherwise Personal. Set `HasTopSongs` accordingly.
6. On mode switch via `SelectorBar`, replace the `TopSongs` collection with the pre-numbered cached list — no additional API call, no in-place mutation.

## Existing Client Support

| Method | Status | Notes |
|---|---|---|
| `GetTopSongsAsync(string artist, int count)` | Implemented | Takes artist **name**, not ID |
| `GetAlbumAsync(string id)` | Implemented | Returns `Child[]` with `PlayCount` |
| `Child.ArtistId` (`string?`) | Available | Legacy single-artist ID |
| `Child.Artists` (`ArtistID3[]?`) | Available | OpenSubsonic structured artist list with IDs |
| `Child.PlayCount` (`long`) | Available | Populated by server |
| `SongInfo.PositionNumber` (`int`) | Available | Used by playlist page for rank display |

## Verification Checklist

The implementation plan should include test cases for:
- **Multi-artist tracks**: Songs where the current artist appears in `Artists[]` but not as the primary `ArtistId`
- **Name-only structured match**: Songs where `Child.Artists[]` contains the artist by name but with no usable ID — verify fallback to step 3 of the matching chain
- **Guest appearance not in album list**: A known guest feature on another artist's album that `GetArtistAsync` does not return — verify this is gracefully absent from personal results (expected limitation, not a bug)
- **Name ambiguity**: Two artists sharing a name — verify community mode returns server's choice and personal mode matches by ID first
- **Large discographies**: Artists with many albums — verify all albums are fetched with bounded concurrency
- **Single-source availability**: Only Community available, only Personal available, both available, neither available
- **Source failures**: API errors in top-songs fetches must not break the rest of the artist page
- **Empty play history**: All songs have `playCount == 0` → Personal marked unavailable
- **Loading regression**: Top-songs fetches must not delay the main page content (albums, bio, similar artists)
- **Ranking column**: Verify `PositionNumber` shows 1..20 rank, not album track numbers
- **Row interactions**: Click-to-play, album navigation, favorite toggle all functional
- **Favorite sync across caches**: Star a song in community mode, switch to personal — verify the same song shows updated star state
- **Artist self-link suppression**: Artist links in top songs rows must not link to the currently viewed artist

## Scope

- Wire up `GetTopSongsAsync` in `ArtistDetailViewModel` (Community mode)
- Implement Personal mode via album-based fetch + ID-matched filtering + client-side sort
- Add "Top Songs" section with `SelectorBar` to `ArtistDetailPage.xaml`
- Song rows follow the playlist track row pattern: rank, title, album, artist, duration, codec, bitrate, play count, BPM, favorite — with full click-to-play and navigation interactions
- `IsTopSongsLoading` state with inline `ProgressRing` for section-level loading
- Availability detection, failure isolation, and section collapse logic
