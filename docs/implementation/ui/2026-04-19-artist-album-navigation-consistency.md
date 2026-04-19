# Artist & Album Navigation Consistency — Implementation Record

**Commit:** `c6ceddc` — `feat(ui): segmented clickable artist navigation`
**Design doc:** [`docs/design/ui/2026-04-19-artist-album-navigation-consistency.md`](../../design/ui/2026-04-19-artist-album-navigation-consistency.md)

## What was implemented

All six P0 items from the design doc were completed in a single commit:

1. **Albums list page** — segmented `DisplayArtist` rendering with `RichTextBlock` + `Hyperlink` inlines, replacing the `HyperlinkButton` approach.
2. **Now Playing bar** — both artist (segmented) and album (single `Hyperlink`) replaced from `HyperlinkButton` to `RichTextBlock` + inline links.
3. **Artist detail page album cards** — segmented rendering with self-link suppression (current artist rendered as plain `Run`).
4. **Data model extensions** — `AlbumInfo`, `PlaybackTrack`, and `SearchSuggestionItem` all carry `DisplayArtist`, `Artists`, `HasStructuredArtists`.
5. **SMTC metadata** — uses `FlatArtistText` for the `MusicProperties.Artist` field.
6. **Queue flyout** — binds to `Track.FlatArtistText` instead of `Track.Artist`.

---

## Data model changes

### AlbumInfo (`Hypersonic.Core/Models/AlbumInfo.cs`)

Added three properties:

```csharp
public string? DisplayArtist { get; set; }
public List<SongArtistInfo> Artists { get; set; } = [];
public bool HasStructuredArtists { get; set; }
```

### PlaybackTrack (`Hypersonic.Core/Models/PlaybackTrack.cs`)

Added four properties:

```csharp
public string? DisplayArtist { get; set; }
public string FlatArtistText { get; set; } = string.Empty;
public List<SongArtistInfo> Artists { get; set; } = [];
public bool HasStructuredArtists { get; set; }
```

`FlatArtistText` is normalized at construction via `DisplayArtistRenderer.BuildFlatText()`. `DisplayArtist` is the raw server string preserved for tooltip and `BuildSegments` input.

### SearchSuggestionItem (`Hypersonic.Core/Models/SearchSuggestionItem.cs`)

Added `DisplayArtist`, `Artists`, `HasStructuredArtists` in the song-only fields section.

### DisplayArtistRenderer (`Hypersonic.Core/Helpers/DisplayArtistRenderer.cs`)

Added `BuildFlatText()` — runs `BuildSegments` and concatenates all segment text into a single string. Used by SMTC and queue flyout to get a normalized flat artist string consistent with what the segmented renderer displays.

---

## PlayerViewModel changes (`Hypersonic/ViewModels/PlayerViewModel.cs`)

- **Removed**: `ArtistName`, `ArtistId` properties, `NavigateToArtistCommand`, `NavigateToAlbumCommand`, and the `INavigationService` constructor dependency. Navigation moved to per-element `Hyperlink.Click` handlers in ShellPage code-behind.
- **Added**: `DisplayArtist`, `Artists` (observable), `HasStructuredArtists`, `FlatArtistText` properties.
- **Added**: `NowPlayingMetadataChanged` event — fires after all metadata properties are set in `OnTrackChanged`. ShellPage subscribes to this instead of `PropertyChanged` to render artist + album in a single pass.
- **Added**: Initial metadata hydration in the constructor — if a track is already playing when `PlayerViewModel` is constructed, all metadata fields (including `CoverArtUrl`, `IsPlaying`, `Duration`, `DurationText`) are populated. This ensures `RenderNowPlaying()` in ShellPage's constructor renders correct data on first load.
- **Constructor signature changed**: `(IPlaybackService, IAppStateService)` — removed `INavigationService`. Internal test constructor also updated.

---

## Mapping sites

All five construction sites now map structured artist data:

| Site | File | What it maps |
|---|---|---|
| Album list fetch | `AlbumsViewModel.cs` | `AlbumID3` → `AlbumInfo` with `DisplayArtist`/`Artists`/`HasStructuredArtists` |
| Album track playback | `AlbumDetailViewModel.cs` | `SongInfo` → `PlaybackTrack` with all four new fields |
| Playlist track playback | `PlaylistDetailViewModel.cs` | Same as album track playback |
| Artist discography | `ArtistDetailViewModel.cs` | `AlbumID3` → `AlbumInfo` (same as album list) + sets `CurrentArtistId` |
| Search-play | `ShellViewModel.cs` | `Child` → `SearchSuggestionItem` → `PlaybackTrack` with all fields |

Each site calls `SongArtistMapper.MapArtists()` once and destructures the result. `FlatArtistText` is computed via `DisplayArtistRenderer.BuildFlatText()` at `PlaybackTrack` construction.

### Visibility changes for testability

- `AlbumDetailViewModel._allSongs` and `BuildPlaybackTracks()` changed from `private` to `internal`.
- `PlaylistDetailViewModel._allSongs` and `BuildPlaybackTracks()` changed from `private` to `internal`.
- `ArtistDetailViewModel.LoadArtistAsync()` changed from `private` to `internal`.

---

## View changes

### AlbumsPage

**XAML** (`AlbumsPage.xaml`):
- Replaced `HyperlinkButton` wrapping a `TextBlock` with a `RichTextBlock` using `DataContextChanged="AlbumArtistLinks_DataContextChanged"`.
- Added `x:Name="AlbumsRepeater"` to the `ItemsRepeater`.
- Added hidden `SecondaryBrushProvider` and `TertiaryBrushProvider` `TextBlock` elements for theme-aware brush sourcing.

**Code-behind** (`AlbumsPage.xaml.cs`):
- `AlbumArtistLinks_DataContextChanged` — calls `DisplayArtistRenderer.BuildSegments()`, sets tooltip, calls `RenderArtistSegments()`.
- `RenderArtistSegments()` — builds `Paragraph` with `Hyperlink`/`Run` inlines. Artists with IDs get `Hyperlink` (click navigates to `ArtistDetailViewModel`), others get plain `Run`. Connectors use `TertiaryBrush`.
- `OnActualThemeChanged` → `ReRenderAllAlbumCards()` — iterates realized `AlbumsRepeater` elements, null-resets `DataContext` to re-trigger rendering.
- Removed `ArtistLink_Click` handler.

### ShellPage (Now Playing bar)

**XAML** (`ShellPage.xaml`):
- Replaced both `HyperlinkButton` elements (artist + album) with named `RichTextBlock` elements: `NowPlayingArtistText` (FontSize 12) and `NowPlayingAlbumText` (FontSize 11).
- Queue flyout artist binding changed from `Track.Artist` to `Track.FlatArtistText`.
- Added hidden brush-provider `TextBlock` elements.

**Code-behind** (`ShellPage.xaml.cs`):
- Subscribes to `Player.NowPlayingMetadataChanged` → calls `RenderNowPlaying()`.
- Initial render in constructor: calls `RenderNowPlaying()`, syncs `PlayPauseIcon.Glyph` and `PlayerCoverArt.Source`.
- `RenderNowPlayingArtist()` — same segmented rendering pattern as AlbumsPage. Artist links navigate to `ArtistDetailViewModel`.
- `RenderNowPlayingAlbum()` — single `Hyperlink` with `TertiaryBrush` foreground if `AlbumId` is non-null, plain `Run` otherwise. Click navigates to `AlbumDetailViewModel`.
- `ActualThemeChanged` handler augmented to call `RenderNowPlaying()` alongside `Player.NotifyThemeChanged()`.

### ArtistDetailPage

**XAML** (`ArtistDetailPage.xaml`):
- Replaced plain `TextBlock` (artist name in album cards) with `RichTextBlock` + `DataContextChanged`.
- Added `x:Name="DiscographyRepeater"` to the discography `ItemsRepeater`.
- Added hidden brush-provider `TextBlock` elements.

**Code-behind** (`ArtistDetailPage.xaml.cs`):
- `AlbumArtistLinks_DataContextChanged` — same as AlbumsPage but passes `ViewModel.CurrentArtistId` to `RenderArtistSegments()`.
- `RenderArtistSegments(rtb, segments, excludeArtistId)` — when `artist.Id == excludeArtistId`, renders as plain `Run` instead of `Hyperlink`. This suppresses self-links: the current artist shows as non-interactive text, co-artists remain clickable.
- Theme re-render via `DiscographyRepeater.TryGetElement(i)` null-reset pattern.

### PlaybackService (`Hypersonic/Services/PlaybackService.cs`)

Single line change: SMTC `MusicProperties.Artist` now reads `track.FlatArtistText` instead of `track.Artist`.

---

## ROADMAP update

Added to "Out of Scope (Post-MVP)":
- Segmented clickable artist links in the queue flyout (behind a user setting for track list interactivity level)

---

## Code duplication note

The `RenderArtistSegments` and `BuildArtistTooltip` methods are duplicated across `AlbumsPage.xaml.cs`, `ArtistDetailPage.xaml.cs`, and `ShellPage.xaml.cs`. This is consistent with the existing pattern from the prior commit (track list rows in `AlbumDetailPage` and `PlaylistDetailPage` have their own copies). A shared helper was considered but deferred — the rendering involves WinUI types (`Paragraph`, `Hyperlink`, `Run`, `Brush`) that couple to the view layer, and each site has slight variations (e.g., `excludeArtistId` on ArtistDetailPage, album rendering on ShellPage).

---

## Tests added

### Core tests (`Hypersonic.Core.Tests`)

| Test | What it verifies |
|---|---|
| `BuildFlatText_NormalMatch_ReturnsOriginalDisplayArtist` | Structured artists found in `DisplayArtist` → output equals original string |
| `BuildFlatText_CommaFallback_ReturnsCommaSeparatedNames` | Artists don't match `DisplayArtist` → comma-separated fallback |
| `BuildFlatText_EmptyArtists_ReturnsEmpty` | Null display artist + empty list → empty string |

### App tests (`Hypersonic.Tests`)

| Test class | What it verifies |
|---|---|
| `ArtistDetailViewModelAlbumMappingTests` | `LoadArtistAsync` maps `DisplayArtist`/`Artists`/`HasStructuredArtists` into `AlbumInfo`; `CurrentArtistId` is set |
| `PlaybackTrackPropagationTests` | `AlbumDetailViewModel.BuildPlaybackTracks()` and `PlaylistDetailViewModel.BuildPlaybackTracks()` propagate all four structured artist fields correctly |
| `PlayerViewModelMetadataTests` | `NowPlayingMetadataChanged` event fires with correct metadata; initial constructor hydration when a track is already playing |
| `ShellViewModelSearchPlayTests` | `HandleSuggestionChosen` with a manually constructed `SearchSuggestionItem` → `PlaybackTrack` carries all structured artist fields including `FlatArtistText` |
| `PlayerViewModelToggleTests` | Existing toggle-persistence suite; constructor calls updated to remove `INavigationService` parameter |

---

## Spike result

The album card interaction spike (Hyperlink inside a Button-contained RichTextBlock) was verified manually after implementation:

1. **Pointer routing**: Clicking `Hyperlink` fires only `Hyperlink.Click`, not `Button.Click`.
2. **Dead space routing**: Clicking connector text or non-hyperlink area fires `Button.Click` (album navigation).
3. **Tab focus**: `Hyperlink` inlines are tab-reachable inside the `Button`.
4. **Keyboard activation**: Enter/Space on focused `Hyperlink` fires `Hyperlink.Click`.

No fallback was needed.

---

## What was deferred

Per the design doc's "Deferred" section, the following were not implemented:
- Artist avatar in album cards
- Full-window Now Playing view
- Segmented clickable artist links in the queue flyout (added to ROADMAP)

## What diverged from the design doc

Nothing significant. The implementation followed the design doc closely across all eight parts.
