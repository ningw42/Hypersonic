# Artist & Album Navigation Consistency — Design Doc

## Context

The previous design ([2026-04-18-track-list-artist-album-columns.md](2026-04-18-track-list-artist-album-columns.md)) introduced segmented `DisplayArtist` rendering on AlbumDetailPage and PlaylistDetailPage track lists. Artist names are rendered as inline `Hyperlink` elements inside `RichTextBlock`, with connector text ("feat.", "&") dimmed — no button chrome, just underline-free text with a hover hint.

Three other places in the app navigate to artists using a different, inconsistent pattern:

1. **Albums list page** (`AlbumsPage`): Artist name is a `HyperlinkButton` wrapping a `TextBlock`. Shows button hover effect (background highlight). Uses only flat `Artist`/`ArtistId` fields — no `DisplayArtist` or `Artists[]` support.
2. **Now Playing bar** (`ShellPage` player bar): Both artist and album are `HyperlinkButton` elements bound to `PlayerViewModel` commands. Same button hover effect issue. Only flat `Artist`/`ArtistId` and `Album`/`AlbumId` — no structured artist data.
3. **Artist detail page** (`ArtistDetailPage`): Album cards in the discography grid show artist names as a plain `TextBlock` — not navigable, not segmented, and not using the `DisplayArtist` or `Artists[]` data.

### Goals

- Consistent visual language: all artist (and album) navigation uses inline `Hyperlink` in `RichTextBlock` — no button chrome, no background hover effect.
- Albums list page and artist detail page album cards support multi-artist display via `DisplayArtist` + `Artists[]`, matching the album detail header rendering (without the avatar).
- Artist detail page suppresses self-links: the artist whose page the user is viewing renders as plain text, while co-artists remain clickable.
- Now Playing supports multi-artist display and uses the same inline link style for both artist and album.

---

## Scope

### P0 (this design)

1. **Albums list page**: Replace `HyperlinkButton` artist link with segmented `DisplayArtist` rendering using `DisplayArtistRenderer`. Reuse the same `RichTextBlock` + `Hyperlink` inline pattern from track list rows.
2. **Now Playing bar**: Replace both `HyperlinkButton` elements (artist and album) with `RichTextBlock` + `Hyperlink` inlines. Artist uses segmented `DisplayArtist` rendering. Album uses a single `Hyperlink` inline (same pattern as PlaylistDetailPage album column).
3. **Artist detail page album cards**: Replace plain `TextBlock` with segmented `DisplayArtist` rendering. Suppress self-links: the current artist renders as plain text, co-artists are clickable. See Part 8 for full specification.
4. **Data model changes**: Extend `AlbumInfo` and `PlaybackTrack` to carry structured artist data.
5. **SMTC metadata**: Use `FlatArtistText` (normalized via `BuildFlatText`) for the SMTC `MusicProperties.Artist` field so lock screen / media overlay shows the full credit string (e.g., "Dr. Dre feat. Eminem" instead of "Dr. Dre"), consistent with the segmented renderer's output.
6. **Queue flyout display text**: Use `FlatArtistText` in queue item rows so the full credit string is visible and consistent with other surfaces. The queue remains plain text — no segmented clickable links (see Deferred).

### Deferred

- Artist avatar in album cards (available in the API but adds visual complexity to the grid).
- Full-window Now Playing view (separate feature).
- Segmented clickable artist links in the queue flyout. The queue is a compact list (40px rows, 11px font) where clickable per-artist links would be fiddly to hit and over-interactive for a transient flyout. Tracked in ROADMAP as a post-MVP item behind a user setting for configurable track list interactivity.

---

## Part 1 — Data model changes

### AlbumInfo (Hypersonic.Core/Models/AlbumInfo.cs)

Add three fields:

```csharp
public string? DisplayArtist { get; set; }
public List<SongArtistInfo> Artists { get; set; } = [];
public bool HasStructuredArtists { get; set; }
```

The `Artist` and `ArtistId` fields remain for backward compatibility (used as fallback input to `SongArtistMapper`).

### PlaybackTrack (Hypersonic.Core/Models/PlaybackTrack.cs)

Add four fields:

```csharp
public string? DisplayArtist { get; set; }
public string FlatArtistText { get; set; } = string.Empty;
public List<SongArtistInfo> Artists { get; set; } = [];
public bool HasStructuredArtists { get; set; }
```

`DisplayArtist` is the raw server string (nullable), preserved for `BuildSegments`/`BuildArtistTooltip`. `FlatArtistText` is the normalized non-null string from `BuildFlatText`, used by SMTC and queue flyout.

### SearchSuggestionItem (Hypersonic.Core/Models/SearchSuggestionItem.cs)

The search flow flattens `Child` into `SearchSuggestionItem`, which currently only keeps flat `Artist`/`ArtistId`. Add three fields to the song-only section:

```csharp
// Song-only fields for playback
public string? DisplayArtist { get; set; }
public List<SongArtistInfo> Artists { get; set; } = [];
public bool HasStructuredArtists { get; set; }
```

**ShellViewModel mapping** (line ~148, inside the `result.Song` loop): When constructing `SearchSuggestionItem` from `Child`, add:

```csharp
DisplayArtist = song.DisplayArtist,
Artists = SongArtistMapper.MapArtists(
    song.Artists?.Select(a => (a.Name, (string?)a.Id)),
    song.Artist, song.ArtistId).Artists,
HasStructuredArtists = SongArtistMapper.MapArtists(
    song.Artists?.Select(a => (a.Name, (string?)a.Id)),
    song.Artist, song.ArtistId).HasStructured,
```

(Call `MapArtists` once and destructure.) Then in `HandleSuggestionChosen` (song case), pass these through to `PlaybackTrack`.

### PlayerViewModel (Hypersonic/ViewModels/PlayerViewModel.cs)

Replace the flat `ArtistName`/`ArtistId` properties with structured data:

```csharp
[ObservableProperty]
public partial string? DisplayArtist { get; set; }

[ObservableProperty]
public partial List<SongArtistInfo> Artists { get; set; } = [];

public bool HasStructuredArtists { get; private set; }

[ObservableProperty]
public partial string FlatArtistText { get; set; } = string.Empty;
```

Keep `AlbumName` and `AlbumId` as-is (album is always single-valued). Remove `ArtistName` and `ArtistId` — the new structured fields replace them. Remove `NavigateToArtist` and `NavigateToAlbum` commands and their backing methods. Remove the `INavigationService` constructor dependency — it was only used by those two commands.

The constructor currently hydrates playback preferences (volume, mute, shuffle, repeat) from `_playbackService` but does not hydrate track metadata. Add initial metadata sync so that if a track is already playing when `PlayerViewModel` is constructed (or when ShellPage loads), the fields are populated:

```csharp
// Hydrate current track metadata (may already be playing on startup).
var track = _playbackService.NowPlaying;
if (track != null)
{
    HasTrack = true;
    TrackTitle = track.Title;
    AlbumName = track.Album;
    AlbumId = track.AlbumId;
    HasStructuredArtists = track.HasStructuredArtists;
    DisplayArtist = track.DisplayArtist;
    Artists = track.Artists;
    FlatArtistText = track.FlatArtistText;
    CoverArtUrl = track.CoverArtUri?.ToString();
    Duration = track.Duration;
    DurationText = FormatTime(track.Duration);
    IsPlaying = _playbackService.IsPlaying;
}
```

This ensures `RenderNowPlaying()` in ShellPage's constructor renders correct data on first load, not empty text. It also sets `CoverArtUrl` and `IsPlaying` — ShellPage's `PropertyChanged` handler uses these to update the cover art `Image` and the play/pause glyph imperatively (not via `x:Bind`), so they must be populated before the handler subscribes. ShellPage's constructor should call its initial sync *after* subscribing to `PropertyChanged`, or explicitly set cover art and play/pause glyph in its initial render pass alongside `RenderNowPlaying()`:

Update `OnTrackChanged` to set all metadata, then raise a dedicated event for the UI:

```csharp
public event EventHandler? NowPlayingMetadataChanged;

private void OnTrackChanged(object? sender, EventArgs e)
{
    var track = _playbackService.NowPlaying;
    HasTrack = track != null;
    TrackTitle = track?.Title ?? string.Empty;
    AlbumName = track?.Album ?? string.Empty;
    AlbumId = track?.AlbumId;
    HasStructuredArtists = track?.HasStructuredArtists ?? false;
    DisplayArtist = track?.DisplayArtist;
    Artists = track?.Artists ?? [];
    FlatArtistText = track?.FlatArtistText ?? string.Empty;
    // ... existing position/duration/cover art updates ...
    NowPlayingMetadataChanged?.Invoke(this, EventArgs.Empty);
}
```

ShellPage subscribes to `Player.NowPlayingMetadataChanged` (not `PropertyChanged`) to render both artist and album in a single `RenderNowPlaying()` call. This decouples the render trigger from property assignment order — all fields are guaranteed to be set before the event fires, and future metadata fields can be added without worrying about which property is the "last" one.

### Mapping sites

**AlbumsViewModel** (`IncrementalAlbumCollection` fetch, line ~154): When constructing `AlbumInfo` from `AlbumID3`, add:

```csharp
DisplayArtist = album.DisplayArtist,
Artists = SongArtistMapper.MapArtists(
    album.Artists?.Select(a => (a.Name, (string?)a.Id)),
    album.Artist, album.ArtistId).Artists,
HasStructuredArtists = SongArtistMapper.MapArtists(
    album.Artists?.Select(a => (a.Name, (string?)a.Id)),
    album.Artist, album.ArtistId).HasStructured,
```

(Call `MapArtists` once and destructure to avoid double call.)

**PlaybackTrack construction** (`AlbumDetailViewModel.BuildPlaybackTracks`, `PlaylistDetailViewModel`): Each site constructs `PlaybackTrack` from a `SongInfo`. Map `Artists` and `HasStructuredArtists` directly from the source. Pass through raw `DisplayArtist` for tooltip use. Normalize `FlatArtistText` via `DisplayArtistRenderer.BuildFlatText(source.DisplayArtist, mappedArtists, hasStructured)` for SMTC and queue (see Part 4).

**ShellViewModel search-play** (line ~230, song case in `HandleSuggestionChosen`): Constructs `PlaybackTrack` from `SearchSuggestionItem`. Map `Artists` and `HasStructuredArtists` directly. Pass through raw `DisplayArtist`. Normalize `FlatArtistText` via `BuildFlatText` (same as above).

The API response for `getAlbumList2` returns `AlbumID3[]` which includes `DisplayArtist` and `Artists[]` — no additional API call is needed. The `search3` response returns `Child[]` for songs, which also includes these fields.

---

## Part 2 — Albums list page

### Current state

```xaml
<HyperlinkButton Click="ArtistLink_Click" Padding="0" Margin="12,0,12,2">
    <TextBlock Text="{Binding Artist}" .../>
</HyperlinkButton>
```

`ArtistLink_Click` reads `AlbumInfo.ArtistId` and navigates. This shows a button hover background and only supports single-artist navigation.

### Target state

Replace with:

```xaml
<RichTextBlock Grid.Row="2"
               Margin="12,0,12,2"
               VerticalAlignment="Top"
               TextTrimming="CharacterEllipsis"
               MaxLines="1"
               IsTextSelectionEnabled="False"
               Foreground="{ThemeResource TextFillColorSecondaryBrush}"
               DataContextChanged="AlbumArtistLinks_DataContextChanged"/>
```

### Click routing: artist links inside album card Button

The `RichTextBlock` with `Hyperlink` inlines lives inside the album card `Button` (which fires `AlbumCard_Click`). The assumption is that `Hyperlink.Click` inside a `RichTextBlock` is handled internally by WinUI and does not propagate as a routed event to the parent `Button`. If correct, clicking an artist name fires only the `Hyperlink.Click` handler, not `AlbumCard_Click`.

**This assumption must be verified with a spike before implementation.** Create a minimal test: a `Button` containing a `RichTextBlock` with a `Hyperlink` inline, with click handlers on both. Confirm:
1. **Pointer routing**: Clicking the `Hyperlink` fires only `Hyperlink.Click`, not `Button.Click`.
2. **Dead space routing**: Clicking non-hyperlink text (connector/dead space) in the `RichTextBlock` fires `Button.Click`.
3. **Tab focus**: The `Hyperlink` inline is reachable via Tab when nested inside the `Button`. This is critical because the change removes the previous `HyperlinkButton` focus stop — if inline `Hyperlink` is not tab-focusable inside a `Button`, keyboard users lose artist navigation entirely.
4. **Keyboard activation**: With focus on the `Hyperlink`, Enter/Space fires `Hyperlink.Click` (not `Button.Click`).

> **Spike result (2026-04-19):** All four checks confirmed manually after implementation. Pointer routing, dead space routing, tab focus, and keyboard activation all behave as expected — `Hyperlink` inside a `Button`-contained `RichTextBlock` works correctly in WinUI 3 (Windows App SDK 1.8). No fallback was needed.

**If the spike fails on pointer/keyboard routing** (both fire, or `Hyperlink` is not focusable), the fallback is to move the `RichTextBlock` outside the card `Button` entirely — place it in a `Grid` row below the card, outside the `Button`'s visual tree. This avoids nested click targets and focus conflicts at the cost of minor layout restructuring. The alternative of `IsHitTestVisible="False"` with overlay buttons is too complex for variable-width segmented text.

Clicking the *connector text* between artist names (e.g., " feat. ") or any non-hyperlink area of the `RichTextBlock` bubbling up to the parent `Button` and triggering album navigation is acceptable — the non-linked portions are not interactive targets, and clicking "dead space" in the artist area navigating to the album is consistent with clicking anywhere else on the card.

### Code-behind (AlbumsPage.xaml.cs)

Add the same rendering infrastructure as AlbumDetailPage/PlaylistDetailPage:

- Hidden `TextBlock` elements for `SecondaryBrush` / `TertiaryBrush` (theme-aware brush sources).
- `AlbumArtistLinks_DataContextChanged`: calls `DisplayArtistRenderer.BuildSegments()` with `AlbumInfo.DisplayArtist`, `AlbumInfo.Artists`, `AlbumInfo.HasStructuredArtists`. Renders segments as `Hyperlink`/`Run` inlines.
- `ActualThemeChanged` handler to re-render on theme switch.

The rendering logic is identical to `AlbumDetailPage.RenderArtistSegments()`. Consider extracting a shared helper if the duplication becomes a maintenance concern, but for now inline code-behind is consistent with the existing pattern across pages.

### Fallback behavior

Same as track list rows:
- **Structured `Artists[]` available**: artist name portions of `DisplayArtist` are clickable links, connectors are dimmed.
- **No structured data** (legacy server): `SongArtistMapper` produces single-entry list from `Artist`/`ArtistId`. Renderer tries to locate that name in `DisplayArtist`; if found, that portion is linked. If not, plain text.

### Re-render on theme change

`ItemsRepeater` does not expose `ContainerFromIndex` like `ListView`. Add `x:Name="AlbumsRepeater"` to the `ItemsRepeater` in `AlbumsPage.xaml` (currently unnamed, line ~62). Use `AlbumsRepeater.TryGetElement(index)` to iterate realized containers and null/reset their `DataContext` to re-trigger `DataContextChanged` (same null-then-restore pattern as `AlbumDetailPage.ReRenderAllTrackRowCells`). Do **not** reset `ItemsSource` — that recreates all realized items, disturbs scroll position, and resets the incremental loading state.

---

## Part 3 — Now Playing bar

### Current state (ShellPage.xaml, player bar)

```xaml
<!-- Artist -->
<HyperlinkButton Command="{x:Bind Player.NavigateToArtistCommand}"
                 Padding="0" Margin="0,-2,0,-2" MinHeight="0" MinWidth="0">
    <TextBlock Text="{x:Bind Player.ArtistName, Mode=OneWay}" .../>
</HyperlinkButton>

<!-- Album -->
<HyperlinkButton Command="{x:Bind Player.NavigateToAlbumCommand}"
                 Padding="0" Margin="0,-2,0,0" MinHeight="0" MinWidth="0">
    <TextBlock Text="{x:Bind Player.AlbumName, Mode=OneWay}" .../>
</HyperlinkButton>
```

Both use `HyperlinkButton` with `Command` binding. The hover state shows a subtle background highlight that looks inconsistent with the inline hyperlink style used in track lists.

### Target state

Replace both `HyperlinkButton` elements with named `RichTextBlock` elements:

```xaml
<StackPanel Grid.Column="1" VerticalAlignment="Center" Margin="0,0,16,0">
    <TextBlock Text="{x:Bind Player.TrackTitle, Mode=OneWay}"
               FontWeight="SemiBold" TextTrimming="CharacterEllipsis" MaxLines="1"/>
    <!-- Artist: segmented display artist rendering -->
    <RichTextBlock x:Name="NowPlayingArtistText"
                   Margin="0,-2,0,-2"
                   TextTrimming="CharacterEllipsis" MaxLines="1"
                   IsTextSelectionEnabled="False"
                   FontSize="12"/>
    <!-- Album: single hyperlink -->
    <RichTextBlock x:Name="NowPlayingAlbumText"
                   Margin="0,-2,0,0"
                   TextTrimming="CharacterEllipsis" MaxLines="1"
                   IsTextSelectionEnabled="False"
                   FontSize="11"/>
</StackPanel>
```

### Code-behind (ShellPage.xaml.cs)

Since ShellPage already has a code-behind, add:

**Brush sourcing**: Add hidden `TextBlock` elements in the XAML (same pattern as AlbumDetailPage):

```xaml
<TextBlock x:Name="SecondaryBrushProvider" Foreground="{ThemeResource TextFillColorSecondaryBrush}" Visibility="Collapsed"/>
<TextBlock x:Name="TertiaryBrushProvider" Foreground="{ThemeResource TextFillColorTertiaryBrush}" Visibility="Collapsed"/>
```

These provide theme-aware brush instances for code-behind inline creation.

**Lifecycle**:

1. **Subscribe to `Player.NowPlayingMetadataChanged`** — this dedicated event fires after all track metadata properties are set (see Part 1). Call `RenderNowPlaying()` which renders both artist and album in one pass.

2. **Initial render**: In the page constructor (after `InitializeComponent` and after subscribing to `PropertyChanged` and `NowPlayingMetadataChanged`), call `RenderNowPlaying()` and also sync the imperative visuals that are driven by `PropertyChanged` but won't fire during startup:

   ```csharp
   // Initial sync for track already playing at startup.
   RenderNowPlaying();
   PlayPauseIcon.Glyph = Player.IsPlaying ? "\uE769" : "\uE768";
   if (!string.IsNullOrEmpty(Player.CoverArtUrl))
   {
       PlayerCoverArt.Source = new BitmapImage(new Uri(Player.CoverArtUrl));
   }
   ```

   This is needed because `PlayerCoverArt.Source` and `PlayPauseIcon.Glyph` are set imperatively in `PropertyChanged` handlers, not via `x:Bind`, so they are stale if the ViewModel was already hydrated before the page subscribed.

3. **`RenderNowPlaying`** — single method that renders both artist and album:

   **Artist** (`RenderNowPlayingArtist`):
   - Call `DisplayArtistRenderer.BuildSegments(Player.DisplayArtist, Player.Artists, Player.HasStructuredArtists)`.
   - Render segments into `NowPlayingArtistText` using the same `Hyperlink`/`Run` pattern.
   - Each artist `Hyperlink.Click` navigates via `App.GetService<INavigationService>().NavigateTo(typeof(ArtistDetailViewModel).FullName!, artistId)`.
   - Set tooltip via `BuildArtistTooltip`.

   **Album** (`RenderNowPlayingAlbum`):
   - Clear `NowPlayingAlbumText.Blocks`, create a `Paragraph`.
   - If `Player.AlbumId` is non-null, create a `Hyperlink` (no underline, `TertiaryBrush` foreground) with `Run` text = `Player.AlbumName`. Click navigates to `AlbumDetailViewModel`.
   - If `AlbumId` is null, render as plain `Run` with `TertiaryBrush`.
   - Set tooltip to `Player.AlbumName`.

4. **Theme change**: ShellPage already subscribes to `ActualThemeChanged` in `OnLoaded` (line 93) to call `Player.NotifyThemeChanged()` — this drives theme-dependent converters for shuffle/repeat/queue controls and must be preserved. Augment that handler to also call `RenderNowPlaying()`:

   ```csharp
   ActualThemeChanged += (_, _) =>
   {
       Player.NotifyThemeChanged();
       RenderNowPlaying();
   };
   ```

### Brush colors

Match existing convention:
- Artist names: `TextFillColorSecondaryBrush` (same as track list artist segments).
- Album name: `TextFillColorTertiaryBrush`. Note: the PlaylistDetailPage album column uses `SecondaryBrush`, but in the Now Playing bar the album sits below the artist and needs visual hierarchy differentiation — tertiary matches the current `HyperlinkButton` inner `TextBlock` color and the smaller `FontSize="11"`.
- Connectors in artist text: `TextFillColorTertiaryBrush`.

### Navigation command and dependency cleanup

`PlayerViewModel.NavigateToArtistCommand` and `NavigateToAlbumCommand` become unused — navigation moves to per-element `Hyperlink.Click` handlers in ShellPage code-behind. Remove both commands and their backing methods. The `INavigationService` constructor dependency was only used by these commands — remove it and the `_navigationService` field. Update `App.xaml.cs` DI registration if it passes `INavigationService` to `PlayerViewModel` explicitly (though CommunityToolkit.Mvvm DI typically resolves constructor params automatically, so removing the parameter is sufficient).

---

## Part 4 — SMTC metadata & queue flyout display text

### SMTC (System Media Transport Controls)

Windows SMTC `MusicProperties` exposes a single flat `Artist` string — there is no structured multi-artist support or clickable navigation. The only change is to prefer `DisplayArtist` over `Artist` so the lock screen and media overlay show the full credit string.

In `PlaybackService` (line ~357), change:

```csharp
displayProperties.MusicProperties.Artist = track.FlatArtistText;
```

`FlatArtistText` is guaranteed non-null on `PlaybackTrack` (normalized at construction via `BuildFlatText`).

### Queue flyout

The queue popup shows tracks in a compact list (40px rows, 11px font). The artist is currently a plain `Run` bound to `Track.Artist` (ShellPage.xaml line ~380). Change to bind to `Track.FlatArtistText`:

```xaml
<Run Text="{Binding Track.FlatArtistText}"/>
```

### Normalized flat artist string

Both SMTC and the queue flyout need a single flat string that shows the full credit text, consistent with what the segmented renderer displays in rich-text surfaces. The `DisplayArtistRenderer` has a fallback path: when structured artists exist but none match the `DisplayArtist` string, it renders a comma-separated list of artist names instead of the raw `DisplayArtist`. The flat-text normalization must follow the same logic to avoid divergence.

Add a static helper to `DisplayArtistRenderer`:

```csharp
public static string BuildFlatText(string? displayArtist, List<SongArtistInfo> artists, bool hasStructuredArtists)
{
    var result = BuildSegments(displayArtist, artists, hasStructuredArtists);
    return string.Concat(result.Segments.Select(s => s.Text));
}
```

This runs the same segmentation algorithm and concatenates the rendered segments into a single string. When the renderer falls back to comma-separated names, `BuildFlatText` returns that same text. When it matches portions of `DisplayArtist`, it returns the original `DisplayArtist` (since concatenating all segments reproduces it).

**Important**: The raw server `DisplayArtist` string must be preserved separately for tooltip use. `BuildArtistTooltip` needs the original string to show "Various Artists" when the renderer fell back to comma-separated names. Therefore `PlaybackTrack` carries two fields:

- `DisplayArtist` — the raw server string (nullable), used by `BuildArtistTooltip` and as input to `BuildSegments` for rich-text rendering.
- `FlatArtistText` — the normalized, guaranteed non-null string from `BuildFlatText`, used by SMTC and queue flyout.

Add `FlatArtistText` to `PlaybackTrack`:

```csharp
public string FlatArtistText { get; set; } = string.Empty;
```

Set at all `PlaybackTrack` construction sites:

```csharp
DisplayArtist = source.DisplayArtist,
FlatArtistText = DisplayArtistRenderer.BuildFlatText(
    source.DisplayArtist, mappedArtists, hasStructured),
```

SMTC reads `track.FlatArtistText`. Queue XAML binds to `Track.FlatArtistText`. `PlayerViewModel` exposes both `DisplayArtist` (raw, for `BuildSegments`/`BuildArtistTooltip`) and `FlatArtistText` (for any future flat-text consumer). ShellPage's `RenderNowPlaying` calls `BuildSegments` with raw `DisplayArtist` and uses `BuildArtistTooltip` with the raw string — tooltip behavior matches existing pages exactly.

The queue remains plain text — no segmented `RichTextBlock` or clickable artist links. The compact row height makes per-artist click targets impractical, and the queue flyout is a transient navigation aid, not a browsing surface. Segmented artist links in the queue are tracked as a post-MVP item in `ROADMAP.md`.

---

## Part 5 — Summary of visual behavior

| Location | Artist rendering | Album rendering |
|---|---|---|
| **Album detail header** | Avatar + segmented `RichTextBlock` (existing) | N/A (page is the album) |
| **Album detail track rows** | Segmented `RichTextBlock` (existing) | N/A |
| **Playlist detail track rows** | Segmented `RichTextBlock` (existing) | `Hyperlink` in `RichTextBlock` (existing) |
| **Albums list page** | Segmented `RichTextBlock` (**new**) | N/A (card click = album nav) |
| **Artist detail album cards** | Segmented `RichTextBlock`, self-link suppressed (**new, Part 8**) | N/A (card click = album nav) |
| **Now Playing bar** | Segmented `RichTextBlock` (**new**) | `Hyperlink` in `RichTextBlock` (**new**) |
| **Queue flyout** | Plain text, `FlatArtistText` (**updated**) | Plain text (unchanged) |
| **SMTC** | `FlatArtistText` (**updated**) | N/A |

All locations with `RichTextBlock`: no button chrome, no background hover. Hover shows underline cursor on the `Hyperlink` inline only.

---

## Part 6 — Files to modify

| File | Change |
|---|---|
| `Hypersonic.Core/Helpers/DisplayArtistRenderer.cs` | Add `BuildFlatText()` static helper |
| `Hypersonic.Core/Models/AlbumInfo.cs` | Add `DisplayArtist`, `Artists`, `HasStructuredArtists` |
| `Hypersonic.Core/Models/PlaybackTrack.cs` | Add `DisplayArtist`, `FlatArtistText`, `Artists`, `HasStructuredArtists` |
| `Hypersonic.Core/Models/SearchSuggestionItem.cs` | Add `DisplayArtist`, `Artists`, `HasStructuredArtists` |
| `Hypersonic/ViewModels/PlayerViewModel.cs` | Replace `ArtistName`/`ArtistId` with structured fields; remove `NavigateToArtist`/`NavigateToAlbum` commands; remove `INavigationService` dependency |
| `Hypersonic/ViewModels/AlbumsViewModel.cs` | Map `DisplayArtist` + `Artists[]` when constructing `AlbumInfo` |
| `Hypersonic/ViewModels/AlbumDetailViewModel.cs` | Map structured artist data into `PlaybackTrack` in `BuildPlaybackTracks` |
| `Hypersonic/ViewModels/PlaylistDetailViewModel.cs` | Map structured artist data into `PlaybackTrack` |
| `Hypersonic/ViewModels/ShellViewModel.cs` | Map structured artist data into `SearchSuggestionItem` and `PlaybackTrack` for search-play |
| `Hypersonic/Views/AlbumsPage.xaml` | Replace `HyperlinkButton` with `RichTextBlock` for artist; add hidden brush-provider `TextBlock` elements; add `x:Name="AlbumsRepeater"` to `ItemsRepeater` |
| `Hypersonic/Views/AlbumsPage.xaml.cs` | Add segmented artist rendering, theme handling |
| `Hypersonic/Services/PlaybackService.cs` | Use `track.FlatArtistText` for SMTC `MusicProperties.Artist` |
| `Hypersonic/Views/ShellPage.xaml` | Replace both `HyperlinkButton` elements with named `RichTextBlock`; add hidden brush-provider `TextBlock` elements; update queue item artist binding to `FlatArtistText` |
| `Hypersonic/Views/ShellPage.xaml.cs` | Add artist/album rendering, `NowPlayingMetadataChanged` subscription, initial render, theme handling |
| `Hypersonic/ViewModels/ArtistDetailViewModel.cs` | Add `CurrentArtistId` property; map `DisplayArtist`/`Artists`/`HasStructuredArtists` into `AlbumInfo` (Part 8) |
| `Hypersonic/Views/ArtistDetailPage.xaml` | Replace plain `TextBlock` with `RichTextBlock`; add `x:Name` to discography repeater; add brush providers (Part 8) |
| `Hypersonic/Views/ArtistDetailPage.xaml.cs` | Add segmented rendering with self-link suppression, tooltip, theme handling (Part 8) |

---

## Part 7 — Verification

### Spike

1. **Album card interaction spike**: Before implementation, verify `Hyperlink` behavior inside a card `Button`. See Part 2 "Click routing" for the four checks (pointer routing, dead space, tab focus, keyboard activation) and fallback plan.

### Build & existing tests

2. **Build**: `dotnet build Hypersonic.sln -p:Platform=x64` — no errors.
3. **Existing tests**: `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj` and `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64` — existing `DisplayArtistRenderer`, `SongArtistMapper`, and `PlayerViewModel` tests still pass (the latter will need constructor signature updates to reflect `INavigationService` removal).

### New tests

4. **SearchSuggestionItem → PlaybackTrack mapping** (`Hypersonic.Tests`): Given a `SearchSuggestionItem` with `DisplayArtist`, `Artists` (multi-entry), and `HasStructuredArtists = true`, verify that `ShellViewModel.HandleSuggestionChosen` produces a `PlaybackTrack` carrying all three fields plus `FlatArtistText` through correctly. This test belongs in the app test project since the mapping logic lives in `ShellViewModel`.
5. **PlayerViewModel.NowPlayingMetadataChanged** (`Hypersonic.Tests`): Set up a `PlayerViewModel` with a mock `IPlaybackService`, fire `TrackChanged` with a track that has structured artists. Verify `NowPlayingMetadataChanged` fires and all metadata properties (`DisplayArtist`, `Artists`, `HasStructuredArtists`, `FlatArtistText`, `AlbumName`, `AlbumId`) are set correctly when the handler runs.
6. **BuildFlatText** (`Hypersonic.Core.Tests`): Two cases: (a) normal match — structured artists found in `DisplayArtist`, verify output equals the original `DisplayArtist` string; (b) comma fallback — structured artists present but none match `DisplayArtist`, verify output is the comma-separated artist names (matching the renderer's fallback).
7. **PlaybackTrack propagation — album and playlist paths** (`Hypersonic.Tests`): Two separate tests: (a) `AlbumDetailViewModel.BuildPlaybackTracks` — build tracks from an album with multi-artist songs, verify each `PlaybackTrack` carries `Artists`, `HasStructuredArtists`, `DisplayArtist` (raw), and `FlatArtistText` (normalized); (b) `PlaylistDetailViewModel` playback — same assertions for the playlist construction path. These are separate construction sites with independent mapping code, so both need coverage.
8. **ArtistDetailViewModel AlbumInfo mapping** (`Hypersonic.Tests`): Set up an `ArtistDetailViewModel` with a fake `ISubsonicClientService` whose `Client.GetArtistAsync` returns an artist with multi-artist albums (including `DisplayArtist` and `Artists[]` on the `AlbumID3` entries) and whose `Client.GetArtistInfo2Async` returns a valid (possibly empty) response (both calls are awaited in `LoadArtistAsync`). Call `OnNavigatedTo` with the artist ID. After loading completes, verify: (a) `CurrentArtistId` equals the navigation parameter; (b) each `AlbumInfo` in `Albums` carries `DisplayArtist`, `Artists`, and `HasStructuredArtists` correctly mapped from the API response.

### Manual verification

9. **Albums page**: Navigate to Albums. Verify artist text under each album card uses inline link style (no button background on hover). Multi-artist albums (if present) show segmented text. Click an artist name → navigates to ArtistDetailPage.
10. **Now Playing bar (album/playlist playback)**: Play a track from album detail or playlist detail. Verify artist text uses inline link style. Verify album text uses inline link style with tertiary color. Click artist → ArtistDetailPage. Click album → AlbumDetailPage. Multi-artist tracks show segmented artist text.
11. **Now Playing bar (search playback)**: Use the search box (`Ctrl+K`) to find a song, click to play it. Verify the Now Playing bar shows segmented artist text with clickable links and clickable album — confirms the `SearchSuggestionItem` → `PlaybackTrack` path preserves structured artist data.
12. **Theme switch**: Change theme in Settings. Verify all artist/album links in albums page, artist detail page, and now playing bar update correctly (no stale brush colors).
13. **SMTC metadata**: Play a track with a multi-artist `DisplayArtist`. Verify the Windows lock screen / media overlay shows the full credit string (e.g., "Dr. Dre feat. Eminem") instead of the flat single artist name.
14. **Queue flyout**: Open the queue flyout. Verify queue items show the full normalized artist credit text (e.g., "Dr. Dre feat. Eminem — Album Name") instead of the flat single artist name. In the structured-fallback case (artist names don't match `DisplayArtist`), the queue should show comma-separated artist names — matching the segmented renderer's output, not the raw server `DisplayArtist`.
15. **Legacy server fallback**: If possible, test with a server that doesn't provide `Artists[]` — verify single artist name is still navigable, and multi-name `DisplayArtist` strings render as plain text when the single artist name can't be matched.

### Keyboard & accessibility

16. **Tab focus**: Covered by the album card spike (Part 2, check 3). For the Now Playing bar (no parent `Button`), verify inline `Hyperlink` elements are tab-reachable independently.
17. **Keyboard activation**: With focus on an artist or album `Hyperlink`, press Enter/Space to confirm navigation fires.
18. **Narrator (albums page / now playing)**: Run Narrator (Win+Ctrl+Enter) and navigate to an artist link in the albums page and now playing bar. Verify Narrator announces the artist name and identifies it as a link. WinUI `Hyperlink` exposes its `Run` text content to UIA automatically, so individual links should be announced correctly. If Narrator output is poor (e.g., announces the full `RichTextBlock` as one block without distinguishing links), this becomes a spike item: investigate whether `AccessibilityView`, `AutomationProperties.Name` on individual `Hyperlink` inlines, or an `AutomationPeer` override can improve announcements. If no WinUI-native fix exists, document the gap and defer to post-MVP accessibility work.
19. **Narrator (artist detail self-link suppression)**: On an artist detail page with co-authored albums, run Narrator and navigate through the album card artist text. Verify that (a) the co-artist `Hyperlink` is announced as a link with the artist name, (b) the suppressed current-artist `Run` is announced as plain text (not a link), and (c) the two elements are distinguishable — Narrator should not announce the entire `RichTextBlock` as a single opaque block. This surface is unique because it mixes focusable and non-focusable segments within a `RichTextBlock` inside a `Button`. If Narrator conflates them, investigate the same UIA mitigations as item 18.

---

## Part 8 — Artist detail page album cards

### Context

The artist detail page (`ArtistDetailPage`) displays a discography grid of album cards. The album card artist text (line 184-190 in `ArtistDetailPage.xaml`) is currently a plain `TextBlock` bound to `AlbumInfo.Artist` — not navigable, not segmented, and not consistent with the pattern established in Parts 1-3.

Additionally, the artist detail page has a unique contextual optimization opportunity: the user is already viewing a specific artist, so linking that artist's own name in a co-authored album's display string is redundant navigation (clicking "Lady Gaga" while on Lady Gaga's page navigates to the same page). Only co-artists should be linked.

### Goals

- Apply the same segmented `RichTextBlock` + `Hyperlink` inline pattern to artist detail album cards, consistent with AlbumsPage (Part 2).
- Suppress the self-link: when rendering artist segments, the artist whose page the user is currently viewing should render as plain styled text (not a `Hyperlink`), while all other artists remain clickable links.

### Data model changes

`ArtistDetailViewModel.LoadArtistAsync` (line 180-193) currently constructs `AlbumInfo` without `DisplayArtist`, `Artists`, or `HasStructuredArtists`. Add the same `SongArtistMapper.MapArtists` call used in `AlbumsViewModel`:

```csharp
var (artists, hasStructured) = SongArtistMapper.MapArtists(
    a.Artists?.Select(x => (x.Name, (string?)x.Id)),
    a.Artist, a.ArtistId);

// ... in the AlbumInfo initializer:
DisplayArtist = a.DisplayArtist,
Artists = artists,
HasStructuredArtists = hasStructured,
```

### Self-link suppression

The `ArtistDetailViewModel` knows the current artist's ID (derived from the navigation parameter passed to `OnNavigatedTo`). Expose it as a property:

```csharp
public string? CurrentArtistId { get; private set; }
```

Set it at the top of `LoadArtistAsync` from the `artistId` parameter.

The rendering code-behind in `ArtistDetailPage` passes `CurrentArtistId` to the segment renderer. When building `Hyperlink` inlines, if `segment.Artist.Id == CurrentArtistId`, render as a plain `Run` with `SecondaryBrush` instead of a `Hyperlink`. The segment text, tooltip, and brush color remain identical — only the interactive element changes.

This is a rendering-time decision, not a model-level concern. The `AlbumInfo.Artists` list retains all artists with their IDs. The suppression happens only in the `RenderArtistSegments` call on `ArtistDetailPage`, not in `DisplayArtistRenderer` or any shared helper. Other surfaces (AlbumsPage, Now Playing bar) continue to link all artists.

### XAML changes (`ArtistDetailPage.xaml`)

Replace the plain `TextBlock` (line 184-190):

```xaml
<!-- Artist name (was plain TextBlock) -->
<RichTextBlock Grid.Row="2"
               Margin="12,0,12,2"
               VerticalAlignment="Top"
               TextTrimming="CharacterEllipsis"
               MaxLines="1"
               IsTextSelectionEnabled="False"
               Foreground="{ThemeResource TextFillColorSecondaryBrush}"
               DataContextChanged="AlbumArtistLinks_DataContextChanged"/>
```

Add `x:Name="DiscographyRepeater"` to the discography `ItemsRepeater` (line 113). Add hidden brush-provider `TextBlock` elements (same pattern as AlbumsPage).

### Code-behind (`ArtistDetailPage.xaml.cs`)

Add the same rendering infrastructure as AlbumsPage:

- `SecondaryBrush` / `TertiaryBrush` properties from hidden brush providers.
- `AlbumArtistLinks_DataContextChanged`: calls `DisplayArtistRenderer.BuildSegments()` with `AlbumInfo.DisplayArtist`, `AlbumInfo.Artists`, `AlbumInfo.HasStructuredArtists`. Sets tooltip via `ToolTipService.SetToolTip(rtb, BuildArtistTooltip(...))` (preserving the tooltip that the removed plain `TextBlock` provided via `ToolTipService.ToolTip="{Binding Artist}"`). Renders segments with `RenderArtistSegments`, passing `ViewModel.CurrentArtistId` as the excluded ID.
- `ActualThemeChanged` handler to re-render via `DiscographyRepeater.TryGetElement(i)` null-reset pattern.
- `RenderArtistSegments(RichTextBlock rtb, List<ArtistSegment> segments, string? excludeArtistId)`: same as AlbumsPage's `RenderArtistSegments` except that when `segment.Artist.Id == excludeArtistId`, it emits a plain `Run` instead of a `Hyperlink`.

### Visual behavior example

On **Lady Gaga**'s artist page, album "Cheek to Cheek" with `DisplayArtist = "Tony Bennett & Lady Gaga"`:
- "Tony Bennett" → `Hyperlink`, clickable, navigates to Tony Bennett's artist page.
- " & " → `Run`, connector text, tertiary brush.
- "Lady Gaga" → `Run`, plain text (not a link), secondary brush. Same visual weight as linked artists, but no hover underline or inline artist navigation. Because the `RichTextBlock` sits inside the album card `Button`, clicking the suppressed name still routes to `AlbumCard_Click` and opens the album — this is consistent with the dead-space behavior documented in the Part 2 spike.

On **Tony Bennett**'s artist page, the same album renders the inverse:
- "Tony Bennett" → `Run`, plain text.
- " & " → `Run`, connector.
- "Lady Gaga" → `Hyperlink`, clickable.

### Files to modify

| File | Change |
|---|---|
| `Hypersonic/ViewModels/ArtistDetailViewModel.cs` | Add `CurrentArtistId` property; map `DisplayArtist`/`Artists`/`HasStructuredArtists` into `AlbumInfo` |
| `Hypersonic/Views/ArtistDetailPage.xaml` | Replace plain `TextBlock` with `RichTextBlock`; add `x:Name` to discography repeater; add brush providers |
| `Hypersonic/Views/ArtistDetailPage.xaml.cs` | Add segmented rendering with self-link suppression, theme handling |

### Verification

1. **Build**: `dotnet build Hypersonic.sln -p:Platform=x64` — no errors.
2. **Multi-artist album on artist page**: Navigate to an artist who has co-authored albums. Verify co-artist names are clickable links. Verify the current artist's name is rendered as plain text (no hover underline, no inline artist navigation — clicking it opens the album via the parent card button).
3. **Click co-artist link**: Click a co-artist name in an album card → navigates to that artist's page. On the new page, verify the roles are swapped (the artist you came from is now the clickable link).
4. **Single-artist albums**: Albums with only the current artist show the artist name as plain text (no links).
5. **Theme switch**: Change theme → artist text in album cards re-renders with correct brush colors.
6. **Keyboard**: Tab to a co-artist link in an album card, press Enter → navigates. The current artist's plain text is not a tab stop.

### Summary table update

| Location | Artist rendering | Album rendering |
|---|---|---|
| **Artist detail album cards** | Segmented `RichTextBlock`, self-link suppressed (**new**) | N/A (card click = album nav) |
