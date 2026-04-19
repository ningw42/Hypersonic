# Track List Artist & Album Columns — Design Doc

## Context

Track lists on AlbumDetailPage and PlaylistDetailPage currently show: #, Title, Duration, Codec, Bitrate, Plays, BPM, Favorite. Artist and album information is stored on each song but not displayed or navigable.

Users expect to see who performed each track and (in playlist context) which album it belongs to. Multi-artist collaborations are common in modern music — the Subsonic/OpenSubsonic API supports this via the `Artists[]` array on `Child` entities, but the app currently only maps the single `Artist`/`ArtistId` fields.

### Key distinction: album artist vs. track artist

An album has one or more "album artists" (e.g., "Artist A", or "Artist A & Artist B" on a collaborative album), but individual tracks may have different artists (e.g., "Artist A feat. Artist C"). When the server provides structured `Artists[]` data, artist names that can be located in the `DisplayArtist` string are rendered as individually clickable links. Artists whose names cannot be matched (e.g., aliases, abbreviations) are not rendered as links but remain discoverable via tooltip. On older servers without `Artists[]`, the renderer attempts to locate the primary artist name within `DisplayArtist`; if found, that portion is a clickable link and the rest is dimmed connector text. If the name cannot be matched, the full `DisplayArtist` string is shown as plain text to avoid misleading click targets.

The album entity (`AlbumID3`) supports multi-artist albums via `Artists[]` (array of `ArtistID3`) alongside the legacy single `ArtistId`. The album header uses the same segmented rendering approach as track rows (see Part 4), making all album artists individually navigable when `Artists[]` is available. On older servers where `Artists[]` is absent, the header follows the same conditional behavior — the primary artist name is linked if found in the display string, otherwise the text is plain and the artist thumbnail image provides the navigation path.

---

## Scope

### P0 (this design)

1. **Artist column** on both AlbumDetailPage and PlaylistDetailPage track lists.
2. **Album column** on PlaylistDetailPage only (redundant on AlbumDetailPage).
3. **DisplayArtist-preserving segmented rendering**: preserve the server's `DisplayArtist` string (e.g., "Dr. Dre feat. Eminem & Skylar Grey"), rendering artist name portions as individually clickable links and connector text ("feat.", "&") as dimmed non-clickable text. When some structured artists cannot be found in the display string (e.g., aliases), those artists are not rendered — the display string is shown as-is with only the matched portions linked. Unmatched artists remain accessible via the tooltip, which always shows all structured artist names.
4. **Album link**: clickable, navigating to AlbumDetailPage.
5. **Multi-artist model support**: map `Child.Artists[]` into `SongInfo` for proper multi-artist data.
6. **DisplayArtist fallback**: when `Artists[]` is unavailable, `SongArtistMapper` produces a single-entry artist list (with `hasStructured: false`) from `Artist` + `ArtistId`. The renderer attempts to locate that artist name within `DisplayArtist`; if found, it segments the string into a clickable artist portion and dimmed connectors. If the name cannot be matched, the full `DisplayArtist` is shown as plain text (not linked) — linking the entire multi-name string to one artist would create a misleading click target.
7. **Multi-artist album header**: replace the single `HyperlinkButton` in the album header with the same segmented renderer. When `Artists[]` is available, all album artists are individually navigable. On older servers without `Artists[]`, the primary artist name is linked if found in the display string; otherwise the text is plain and the thumbnail image provides the navigation path.

### Deferred

- **ArtistDetailPage** showing which tracks the user navigated from — breadcrumb/back context.
- **Context menu** on artist/album links (e.g., "Open in new tab", "Add to queue").
- **Sortable columns** — click column header to sort by artist/album.

---

## Part 1: Data Model

### New model: `SongArtistInfo`

File: `Hypersonic.Core/Models/SongArtistInfo.cs` (new)

A lightweight model representing one artist credit on a song. Placed in `Hypersonic.Core` (not the app project) because it has no WinUI dependencies and is needed by the shared mapping helper and Core tests.

```csharp
namespace Hypersonic.Core.Models;

public class SongArtistInfo
{
    public string Name { get; set; } = string.Empty;
    public string? Id { get; set; }
}
```

### SongInfo additions

File: `Hypersonic/Models/SongInfo.cs`

Add properties to hold the structured multi-artist list and its provenance:

```csharp
public List<Hypersonic.Core.Models.SongArtistInfo> Artists { get; set; } = [];
public bool HasStructuredArtists { get; set; }
```

`HasStructuredArtists` is `true` when the server provided `Child.Artists[]` with at least one non-empty-name entry, `false` when the list was synthesized from the legacy `Artist`/`ArtistId` fallback. This flag lets the renderer distinguish real structured data (where all artists should be navigable) from legacy fallback data (where the synthesized single-entry list may not safely match `DisplayArtist`).

The existing `Artist` (string), `ArtistId` (string?), and `DisplayArtist` (string?) fields remain for backward compatibility with playback and other consumers.

---

## Part 2: Data Mapping

### Shared helper: `SongArtistMapper`

File: `Hypersonic.Core/Helpers/SongArtistMapper.cs` (new)

Extract the artist-list mapping as a static helper in Core. It accepts a generic tuple input so it has no dependency on `Client.Models.Entities.Child`:

```csharp
public static (List<SongArtistInfo> Artists, bool HasStructured) MapArtists(
    IEnumerable<(string Name, string? Id)>? artists,
    string? fallbackName,
    string? fallbackId)
{
    if (artists != null)
    {
        var list = artists
            .Where(a => !string.IsNullOrEmpty(a.Name))
            .Select(a => new SongArtistInfo { Name = a.Name, Id = a.Id })
            .ToList();
        if (list.Count > 0) return (list, true);
    }
    return ([new SongArtistInfo { Name = fallbackName ?? string.Empty, Id = fallbackId }], false);
}
```

The `Where` filter discards entries with empty names before checking count. If the server sends malformed `Artists[]` with all-empty names but provides usable legacy `Artist`/`ArtistId`, the mapper falls through to the fallback path and preserves the legacy data. The `bool HasStructured` return value records provenance.

### ViewModel usage

Files: `Hypersonic/ViewModels/AlbumDetailViewModel.cs` (~line 323), `Hypersonic/ViewModels/PlaylistDetailViewModel.cs` (~line 207)

Both `MapSongInfo` methods call the shared helper, projecting `Child.Artists[]` into tuples:

```csharp
var (artists, hasStructured) = SongArtistMapper.MapArtists(
    song.Artists?.Select(a => (a.Name, (string?)a.Id)),
    song.Artist,
    song.ArtistId);
// Then set: Artists = artists, HasStructuredArtists = hasStructured
```

**Fallback priority** (when `Child.Artists` is null or empty — older Subsonic servers):
1. Create a single-element `Artists` list from `Child.Artist` + `Child.ArtistId`. This means `DisplayArtistRenderer.BuildSegments` always receives at least one artist entry.
2. The existing `DisplayArtist` field on `SongInfo` (already mapped) preserves the server-provided display string (e.g., "Artist A feat. Artist B"). At render time, `BuildSegments` receives `hasStructuredArtists: false` for this path. It will attempt to locate the single artist name within `DisplayArtist` and produce connector + artist segments. If the name is found, the artist portion is clickable via the `ArtistId`. If the name cannot be matched, the full `DisplayArtist` is shown as plain text (not linked) — linking the entire multi-name string to one `ArtistId` would be misleading.

### AlbumDetailViewModel — album-level artist data for header rendering

File: `Hypersonic/ViewModels/AlbumDetailViewModel.cs`

Add observable properties to carry the structured album artist list for the header's segmented renderer:

```csharp
[ObservableProperty]
public partial List<SongArtistInfo> AlbumArtists { get; set; } = [];

[ObservableProperty]
public partial string? AlbumDisplayArtist { get; set; }

public bool AlbumHasStructuredArtists { get; private set; }
```

Using `[ObservableProperty]` ensures `PropertyChanged` fires when the album loads, giving the page code-behind a reliable render trigger (see Part 6). `AlbumHasStructuredArtists` is a plain property set alongside `AlbumArtists`.

Populated in `LoadAlbumAsync`:

```csharp
AlbumDisplayArtist = album.DisplayArtist;
var (albumArtists, albumHasStructured) = SongArtistMapper.MapArtists(
    album.Artists?.Select(a => (a.Name, (string?)a.Id)),
    album.Artist,
    album.ArtistId);
AlbumHasStructuredArtists = albumHasStructured; // Set before AlbumArtists so the flag is correct when PropertyChanged fires
AlbumArtists = albumArtists;
```

The existing `AlbumArtist` (display string), `AlbumArtistId`, and `AlbumArtistImageUrl` properties remain — `AlbumArtistId` is still used for the artist thumbnail image beside the header.

### New column order

| Col | AlbumDetailPage | PlaylistDetailPage | Width |
|-----|-----|-----|-----|
| 0 | Track # | Position # | 40 |
| 1 | Title | Title | 2* |
| 2 | — | Album | * |
| 3 | Artist | Artist | * |
| 4 | Duration | Duration | 60 |
| 5 | Codec | Codec | 50 |
| 6 | Bitrate | Bitrate | 60 |
| 7 | Plays | Plays | 50 |
| 8 | BPM | BPM | 50 |
| 9 | Fav | Fav | 36 |

AlbumDetailPage has 9 columns (no Album column). PlaylistDetailPage has 10 columns.

Title, Album, and Artist use proportional `*` widths so the layout adapts to the window size. Title gets `2*` (double the share) to remain the dominant column.

**Minimum width note**: The existing track list layout (before this design) already does not fit at the app's 500px minimum window width — the fixed columns (346px) plus spacing (84px) and padding (24px) total 454px, leaving essentially no space for the Title column once the 48px navigation pane is subtracted. Adding Album and Artist columns makes this worse but does not introduce the problem. This design targets the default window width (1400px) and common usage widths (800px+). At 800px content width, the proportional columns share ~350px — comfortably readable. Responsive column hiding (collapsing lower-priority columns like Codec, Bitrate, BPM at narrow widths) is a pre-existing layout issue tracked separately, not in scope for this design.

Both the header grid and the item template grid must have matching column definitions.

---

## Part 4: Artist Column Rendering

### Challenge: dynamic multi-artist links in compiled DataTemplate

WinUI 3 `x:Bind` DataTemplates are compiled at build time. Rendering a variable number of clickable artist links (with comma separators) cannot be done purely in XAML without an `ItemsRepeater` or similar — which adds complexity.

### Approach: `RichTextBlock` with `DataContextChanged`

Place a `RichTextBlock` in the artist column cell. Use `DataContextChanged` (not `Loaded`) as the rendering hook. `Loaded` only fires once when the element is first added to the visual tree, but `ListView` recycles item containers — when a recycled container is rebound to a different data item, `Loaded` does not fire again, leaving stale artist links from the previous item. `DataContextChanged` fires both on initial bind and when the container is recycled to a new item, ensuring the artist links always match the current data.

**Rendering approach — DisplayArtist-preserving segmented links:**

The server provides both `Artists[]` (structured name+id array) and `DisplayArtist` (the human-readable string, e.g., `"Dr. Dre feat. Eminem & Skylar Grey"`). Instead of rendering artists as a comma-separated list (losing the original "feat.", "&", "with" semantics), we preserve the `DisplayArtist` string and make only the artist name portions clickable.

**Algorithm** (`DisplayArtistRenderer.BuildSegments` in `Hypersonic.Core/Helpers/`):

```csharp
public record BuildSegmentsResult(
    List<ArtistSegment> Segments,
    bool UsedStructuredFallback,
    bool HasUnmatchedArtists);

public static BuildSegmentsResult BuildSegments(
    string? displayArtist,
    List<SongArtistInfo> artists,
    bool hasStructuredArtists)
```

The return type includes:
- `UsedStructuredFallback` — true when `hasStructuredArtists` was true but **no** artist names could be matched, triggering comma-separated fallback.
- `HasUnmatchedArtists` — true when some (but not all) artists could not be matched in the display string. The caller uses these flags to choose the tooltip content.

Given `DisplayArtist`, `Artists[]`, and `hasStructuredArtists`, scan the display string left-to-right, greedily matching each artist name (longest match first to handle substring collisions). Artists with empty or null names are skipped during matching. Matches must be **case-insensitive** and **boundary-aware**: a candidate match is only accepted if the character immediately before the match (if any) and the character immediately after (if any) are not letters or digits. This prevents matching "Art" inside "Arthur" or "Em" inside "Eminem". Produce a list of segments:
- **Connector segments**: text between artist names (e.g., `" feat. "`, `" & "`). Rendered as dimmed `TextBlock` (tertiary foreground).
- **Artist segments**: the matched artist name. Rendered as a `Hyperlink` inline (clickable, secondary foreground) if the artist has an Id, or plain `Run` otherwise.

**Partial matches**: After scanning, if some artists from `Artists[]` were matched in `DisplayArtist` but others were not (e.g., the display string uses a nickname or abbreviation), the unmatched artists are **not** appended to the rendered text. Appending would mutate the server-provided credit string and risk showing duplicate names (e.g., "Dr. Dre feat. Slim Shady, Eminem" when Slim Shady is Eminem's alias). Instead, the display string is rendered as-is with only the matched portions linked. The tooltip always shows all structured artist names (comma-separated) so unmatched artists remain discoverable.

**Fallback** (when segmentation of `DisplayArtist` fails or is not possible):

The `hasStructuredArtists` flag determines the fallback behavior when no artist names can be matched in `DisplayArtist`:

1. **`hasStructuredArtists` is true, but no names found in `DisplayArtist`**: fall back to **comma-separated artist segments** from the `Artists[]` entries, preserving their Ids for navigation. The `DisplayArtist` string is preserved in the tooltip (set by the caller — see below). This preserves the P0 goal: structured artist data always produces clickable links, even if the display string uses unexpected formatting.
2. **`hasStructuredArtists` is false** (legacy fallback — synthesized from `Artist`/`ArtistId`), and the single artist name cannot be matched in `DisplayArtist`: produce a single artist segment using `DisplayArtist` as the text with a **null Id** (plain text, not linked). Linking the entire multi-name string (e.g., "Artist A feat. Artist B") to a single `ArtistId` would create a misleading click target where clicking "Artist B" navigates to Artist A. If `DisplayArtist` is also empty, use the artist's `Name` and `Id` directly (single clickable segment).
3. **`Artists[]` has entries but all names are empty** (e.g., malformed server data), and `DisplayArtist` is non-empty: produce a single artist segment using `DisplayArtist` as the text with a **null Id** (plain text, not linked). We cannot determine which part of the string maps to which Id.
4. **`Artists[]` is empty**: produce a single artist segment from `DisplayArtist` with a null-Id `SongArtistInfo`.
5. Both `Artists[]` and `DisplayArtist` are empty/null: return empty segments with `UsedStructuredFallback: false`.

**Tooltip**: The caller checks `result.UsedStructuredFallback`. When true and `DisplayArtist` is non-empty, the tooltip shows the raw `DisplayArtist` string (since the rendered text is the comma-separated fallback, not the server string). For partial matches (some artists matched, some not), the tooltip shows all structured artist names comma-separated so unmatched artists are discoverable. In all other cases, the tooltip shows `string.Concat(result.Segments.Select(s => s.Text))`.

**Clickability rules:** All artist segments with a non-null Id are clickable on both pages. Artists without an Id render as plain text (secondary foreground).

**Album header**: The same segmented rendering applies to the album header's artist display. The current header wraps both the artist avatar and text in a single `HyperlinkButton` (see `AlbumDetailPage.xaml:148`). Replace this with a horizontal `StackPanel` containing two separate elements:

1. **Avatar button**: A `HyperlinkButton` wrapping only the artist thumbnail image (28×28 circle), bound to `NavigateToArtistCommand`. This preserves the primary-artist navigation path regardless of text linkability.
2. **Artist text**: A `RichTextBlock x:Name="AlbumArtistText"` with `TextTrimming="CharacterEllipsis"`, rendered by `RenderAlbumHeaderArtists()` using `DisplayArtistRenderer.BuildSegments`. Handles long collaborative credits natively.

```xaml
<StackPanel Orientation="Horizontal" Spacing="8">
    <!-- Avatar — always clickable, navigates to primary album artist -->
    <HyperlinkButton Command="{x:Bind ViewModel.NavigateToArtistCommand}"
                     Padding="0" Margin="0" MinHeight="0" MinWidth="0">
        <Grid Width="28" Height="28" CornerRadius="14">
            <!-- ... existing avatar content ... -->
        </Grid>
    </HyperlinkButton>
    <!-- Artist text — segmented rendering -->
    <RichTextBlock x:Name="AlbumArtistText" VerticalAlignment="Center"
                    TextTrimming="CharacterEllipsis" MaxLines="1"
                    IsTextSelectionEnabled="False"
                    FontSize="20"
                    Foreground="{ThemeResource TextFillColorSecondaryBrush}"/>
</StackPanel>
```

On servers that provide `AlbumID3.Artists[]`, each album artist name in the text is individually clickable via `Hyperlink` inlines. On older servers where `Artists[]` is absent, if the artist name is found in `AlbumDisplayArtist`, that portion is clickable in the text. If not found, the text is plain — the avatar button always provides the primary-artist navigation path. **Note**: The current single-HyperlinkButton layout makes the entire avatar+text area one click target. The new split layout reduces the guaranteed click target to the 28×28 avatar on the legacy no-match path. This is an accepted trade-off for enabling per-artist navigation on the structured data path.

### Truncation and overflow strategy

The artist and album columns use proportional `*` widths, so their actual pixel widths vary with the window size. The rendering approach must handle overflow gracefully without creating invisible focus targets.

**Approach**: Instead of a StackPanel of separate `HyperlinkButton` and `TextBlock` elements (which cannot ellipsize and requires manual clipping), use a `RichTextBlock` with `Hyperlink` inlines. WinUI 3's `RichTextBlock` supports `Hyperlink` elements within a `Paragraph`, and `TextTrimming="CharacterEllipsis"` handles overflow natively:

```xaml
<RichTextBlock Grid.Column="..." VerticalAlignment="Center"
               TextTrimming="CharacterEllipsis" MaxLines="1"
               IsTextSelectionEnabled="False"
               DataContextChanged="ArtistLinks_DataContextChanged"/>
```

The code-behind handler populates the `RichTextBlock.Blocks` with a single `Paragraph` containing `Run` elements (for connector text) and `Hyperlink` elements (for clickable artist names). This eliminates all clipping concerns:
- `TextTrimming="CharacterEllipsis"` provides a native ellipsis when content overflows — a clear visual cue that more content exists.
- `Hyperlink` inlines are part of the text flow and cannot be focused independently when trimmed away, eliminating invisible focus targets.
- No `Border.Clip`, `SizeChanged` handler, or manual clipping geometry needed.

The same approach applies to the album column (`RichTextBlock` with a single `Hyperlink` for the album name) and the album header artist text.

**Album header**: Replace the current `AlbumArtistPanel` reference with `RichTextBlock x:Name="AlbumArtistText"` using the same inline approach. `TextTrimming="CharacterEllipsis"` handles long collaborative credits. The avatar button beside it remains unchanged.

### Hyperlink styling in track rows

`Hyperlink` inlines within `RichTextBlock` inherit font properties from the paragraph. Style them with:
- `Foreground` set to secondary text brush to match column visual weight
- `UnderlineStyle="None"` for a cleaner look (underline on hover is the default and provides click affordance)

### Handling row click vs. link click

`Hyperlink.Click` fires when a user clicks an artist/album link within a `RichTextBlock`. Since `RichTextBlock` has `IsTextSelectionEnabled="False"`, pointer events on the `Hyperlink` are consumed by the inline and do not propagate to the `ListViewItem`. This means `ListView.ItemClick` should **not** fire for clicks on hyperlinks — only for clicks on the rest of the row. This is the desired behavior (click artist → navigate to artist; click elsewhere → play track). If runtime testing reveals that `ItemClick` still fires for hyperlink clicks, add a `_suppressItemClick` flag set in the `Hyperlink.Click` handler and checked in `TrackList_ItemClick`.

### Keyboard navigation and accessibility

With the `RichTextBlock` + `Hyperlink` inline approach, `Hyperlink` inlines do not participate in Tab/XY focus navigation — they are text-level elements, not `Control` subclasses. The only per-row focusable `Control` remains the favorite `ToggleButton`, so no additional tab stops are introduced.

**Keyboard access to artist links**: `Hyperlink` inlines within `RichTextBlock` are not directly keyboard-focusable when `IsTextSelectionEnabled="False"`. This means artist/album links are mouse/touch-only for the initial implementation. Keyboard users can navigate to artists via the app's search feature (`Ctrl+K`). If keyboard activation of inline links is needed later, `IsTextSelectionEnabled="True"` can be enabled on the `RichTextBlock` — this makes the text block focusable and allows cursor-based hyperlink activation via Enter, but must be verified against `ListView` `ItemClick` behavior and row focus semantics.

### Theme-aware generated elements

`Run` and `Hyperlink` inline elements are created in code-behind with brush values from `Application.Current.Resources`. WinUI does not automatically update code-set foreground on theme change. However, `RichTextBlock` itself can use `ThemeResource` for its default `Foreground`, which applies to `Run` elements that don't set an explicit foreground. Only `Hyperlink` elements (which need a distinct foreground color) and dimmed `Run` connector elements require explicit brush assignment.

**Approach**: Subscribe to `ActualThemeChanged` on the page and re-render all generated artist/album cells. In-app theme switches via `ThemeSelectorService.SetThemeAsync` set `rootElement.RequestedTheme`, which propagates `ActualThemeChanged` down the element tree. For system-theme changes, `MainWindow.Settings_ColorValuesChanged` only updates caption buttons and taskbar — it does not set `RequestedTheme`. However, when the user's theme preference is `ElementTheme.Default` (follow system), the framework propagates the system theme change through the element tree automatically, firing `ActualThemeChanged` on descendant pages. So `ActualThemeChanged` on the page covers both in-app and system-theme changes when the preference is Default. When the preference is explicitly Light or Dark, system-theme changes are intentionally ignored (the user chose a fixed theme).

Since both detail pages wrap their `ListView` in an outer `ScrollViewer` (disabling virtualization), all rows are materialized and `DataContextChanged` does not fire on theme change. The `ActualThemeChanged` handler must re-render all track-row cells and the album header panel:

```csharp
public AlbumDetailPage()
{
    ViewModel = App.GetService<AlbumDetailViewModel>();
    InitializeComponent();
    ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    ActualThemeChanged += OnActualThemeChanged;
}

private void OnActualThemeChanged(FrameworkElement sender, object args)
{
    RenderAlbumHeaderArtists();
    ReRenderAllTrackRowCells();
}

private void ReRenderAllTrackRowCells()
{
    // Non-virtualized: all item containers are materialized.
    // Walk each container and re-trigger the DataContextChanged handlers
    // by re-assigning DataContext, which forces a fresh render with current theme brushes.
    for (var i = 0; i < TrackListView.Items.Count; i++)
    {
        if (TrackListView.ContainerFromIndex(i) is ListViewItem container)
        {
            // Find artist RichTextBlock(s) and album RichTextBlock within the container
            // and force re-render by clearing and re-setting DataContext
            var dataContext = container.DataContext;
            container.DataContext = null;
            container.DataContext = dataContext;
        }
    }
}
```

`TrackListView` is the `x:Name` on the ListView in the page XAML. Re-assigning `DataContext` (null then restore) triggers `DataContextChanged` on the artist and album `RichTextBlock` elements within each row, which re-creates all `Paragraph`/`Run`/`Hyperlink` inlines with fresh brush lookups from `Application.Current.Resources`. This covers both artist and album cells.

**Known brittleness**: This DataContext-toggle approach is a workaround for the lack of built-in `ThemeResource` support on code-created inlines. It works because `DataContextChanged` fully rebuilds the inline tree. An alternative approach that avoids the container walk: only use explicit `Foreground` on `Hyperlink`/`Run` elements that need a *different* color from the `RichTextBlock`'s inherited foreground. Connector `Run` elements can use `TextHighlighters` or a dedicated style, and the `RichTextBlock.Foreground` can be set via `ThemeResource` in XAML (which updates automatically on theme change). This would reduce the set of elements needing manual refresh to just `Hyperlink` foregrounds. Either approach is acceptable for the initial implementation; the DataContext-toggle is simpler to implement.

PlaylistDetailPage uses the same `ReRenderAllTrackRowCells` pattern (no album header to re-render).

The page must unsubscribe from both events when navigated away to avoid keeping disposed page instances alive across repeated navigations:

```csharp
// In Unloaded handler (pages don't receive INavigationAware callbacks):
ActualThemeChanged -= OnActualThemeChanged;
ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
```

PlaylistDetailPage follows the same pattern (no `ViewModel.PropertyChanged` subscription needed since it has no album header, but `ActualThemeChanged` and `Unloaded` still require subscribe/unsubscribe).

### Performance considerations

Both detail pages wrap the `ListView` in an outer `ScrollViewer`, which means the ListView does not virtualize — all item containers are materialized at once. The `DataContextChanged` handler creates `Paragraph`/`Run`/`Hyperlink` inlines per artist cell. For a typical album (10–20 tracks) this is negligible.

**Expected scale**: This design targets albums (typically 10–30 tracks) and playlists up to ~200 tracks without perceptible delay. For larger playlists (500+ tracks), the non-virtualized layout is already a performance concern independent of this feature — the existing columns already materialize all rows. The additional artist/album columns add incremental cost but are not the primary bottleneck. Improving large-playlist performance requires switching the PlaylistDetailPage ListView to a standalone `ListView` (not nested in `ScrollViewer`) so virtualization works — that is a separate layout change outside this design's scope.

**Theme re-rendering cost**: `ActualThemeChanged` re-renders all materialized artist panels. For the expected scale (≤200 rows), this is fast. For larger lists, the same virtualization fix would limit the re-render to visible rows only.

---

## Part 5: Album Column Rendering (PlaylistDetailPage only)

Simpler than artist — each song has exactly one album. If `AlbumId` is non-empty, render as a `Hyperlink` inline, clicking navigates to `AlbumDetailViewModel`. If `AlbumId` is null/empty, render as a plain `Run`. Uses the same `RichTextBlock` + `DataContextChanged` pattern:

```xaml
<RichTextBlock Grid.Column="2" VerticalAlignment="Center"
               TextTrimming="CharacterEllipsis" MaxLines="1"
               IsTextSelectionEnabled="False"
               DataContextChanged="AlbumLink_DataContextChanged"/>
```

```csharp
private void AlbumLink_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
{
    if (sender is not RichTextBlock rtb || rtb.DataContext is not SongInfo song)
        return;

    rtb.Blocks.Clear();
    ToolTipService.SetToolTip(rtb, song.Album);

    var paragraph = new Paragraph();
    if (!string.IsNullOrEmpty(song.AlbumId))
    {
        var link = new Hyperlink();
        link.Inlines.Add(new Run { Text = song.Album });
        link.UnderlineStyle = UnderlineStyle.None;
        link.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        link.Click += (s, e) =>
        {
            App.GetService<INavigationService>()
                .NavigateTo(typeof(AlbumDetailViewModel).FullName!, song.AlbumId);
        };
        paragraph.Inlines.Add(link);
    }
    else
    {
        paragraph.Inlines.Add(new Run
        {
            Text = song.Album,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        });
    }
    rtb.Blocks.Add(paragraph);
}
```

---

## Part 6: Code-Behind Handlers

### Shared rendering helper

Both pages share the same segment-to-inline logic. These helpers are duplicated in each page's code-behind (not shared across pages, since they need access to page-level state for theme re-rendering). Navigation uses `App.GetService<INavigationService>()` resolved in the `Hyperlink.Click` handler — neither ViewModel injects `INavigationService` for this purpose, and adding it would be unnecessary since navigation from artist/album links is a view concern.

```csharp
private void RenderArtistSegments(RichTextBlock rtb, List<ArtistSegment> segments)
{
    rtb.Blocks.Clear();
    var paragraph = new Paragraph();

    foreach (var segment in segments)
    {
        if (segment.Kind == ArtistSegmentKind.Connector)
        {
            paragraph.Inlines.Add(new Run
            {
                Text = segment.Text,
                Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
            });
        }
        else
        {
            var artist = segment.Artist!;
            if (!string.IsNullOrEmpty(artist.Id))
            {
                var link = new Hyperlink { UnderlineStyle = UnderlineStyle.None };
                link.Inlines.Add(new Run
                {
                    Text = segment.Text,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                });
                var artistId = artist.Id;
                link.Click += (s, e) =>
                {
                    App.GetService<INavigationService>()
                        .NavigateTo(typeof(ArtistDetailViewModel).FullName!, artistId);
                };
                paragraph.Inlines.Add(link);
            }
            else
            {
                paragraph.Inlines.Add(new Run
                {
                    Text = segment.Text,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                });
            }
        }
    }

    rtb.Blocks.Add(paragraph);
}
```

### AlbumDetailPage.xaml.cs

```csharp
private void ArtistLinks_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
{
    if (sender is not RichTextBlock rtb || rtb.DataContext is not SongInfo song)
        return;

    var result = DisplayArtistRenderer.BuildSegments(
        song.DisplayArtist, song.Artists, song.HasStructuredArtists);
    ToolTipService.SetToolTip(rtb, BuildArtistTooltip(result, song.DisplayArtist, song.Artists));
    RenderArtistSegments(rtb, result.Segments);
}

private static string BuildArtistTooltip(
    BuildSegmentsResult result, string? displayArtist, List<SongArtistInfo> artists)
{
    if (result.UsedStructuredFallback && !string.IsNullOrEmpty(displayArtist))
        return displayArtist;
    if (result.HasUnmatchedArtists)
        return string.Join(", ", artists.Where(a => !string.IsNullOrEmpty(a.Name)).Select(a => a.Name));
    return string.Concat(result.Segments.Select(s => s.Text));
}
```

The album header uses a `RichTextBlock x:Name="AlbumArtistText"` that replaces the current artist `TextBlock` inside the header. The avatar button remains separate (see Part 4). The page subscribes to `ViewModel.PropertyChanged` and `ActualThemeChanged` in the constructor, and unsubscribes when navigated away:

```csharp
public AlbumDetailPage()
{
    ViewModel = App.GetService<AlbumDetailViewModel>();
    InitializeComponent();
    ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    ActualThemeChanged += OnActualThemeChanged;
}

private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
{
    if (e.PropertyName == nameof(AlbumDetailViewModel.AlbumArtists))
    {
        RenderAlbumHeaderArtists();
    }
}

private void OnActualThemeChanged(FrameworkElement sender, object args)
{
    RenderAlbumHeaderArtists();
    ReRenderAllTrackRowCells();
}

// In Unloaded handler (pages don't receive INavigationAware callbacks — those go to ViewModels):
Unloaded += (s, e) =>
{
    ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
    ActualThemeChanged -= OnActualThemeChanged;
};

private void RenderAlbumHeaderArtists()
{
    var result = DisplayArtistRenderer.BuildSegments(
        ViewModel.AlbumDisplayArtist, ViewModel.AlbumArtists, ViewModel.AlbumHasStructuredArtists);
    ToolTipService.SetToolTip(AlbumArtistText,
        BuildArtistTooltip(result, ViewModel.AlbumDisplayArtist, ViewModel.AlbumArtists));
    RenderArtistSegments(AlbumArtistText, result.Segments);
}
```

This fires reliably because `AlbumArtists` is an `[ObservableProperty]` (see Part 2), and `LoadAlbumAsync` sets it on the UI thread after the fetch completes. The `AlbumArtistText` is the `x:Name`'d `RichTextBlock` in the header XAML.

### PlaylistDetailPage.xaml.cs

Same handler:

```csharp
private void ArtistLinks_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
{
    if (sender is not RichTextBlock rtb || rtb.DataContext is not SongInfo song)
        return;

    var result = DisplayArtistRenderer.BuildSegments(
        song.DisplayArtist, song.Artists, song.HasStructuredArtists);
    ToolTipService.SetToolTip(rtb, BuildArtistTooltip(result, song.DisplayArtist, song.Artists));
    RenderArtistSegments(rtb, result.Segments);
}

private static string BuildArtistTooltip(
    BuildSegmentsResult result, string? displayArtist, List<SongArtistInfo> artists)
{
    if (result.UsedStructuredFallback && !string.IsNullOrEmpty(displayArtist))
        return displayArtist;
    if (result.HasUnmatchedArtists)
        return string.Join(", ", artists.Where(a => !string.IsNullOrEmpty(a.Name)).Select(a => a.Name));
    return string.Concat(result.Segments.Select(s => s.Text));
}
```

The `RenderArtistSegments`, `BuildArtistTooltip`, `AlbumLink_DataContextChanged`, and `AlbumLink_Click` helpers are the same as defined in Part 5 and Part 6's shared helper section — duplicated in this page's code-behind.

---

## File Summary

| File | Action |
|---|---|
| `Hypersonic.Core/Models/SongArtistInfo.cs` | **Create** — Name + Id model for artist credits |
| `Hypersonic.Core/Helpers/SongArtistMapper.cs` | **Create** — shared `MapArtists` helper (tuple input, no Client dependency) |
| `Hypersonic.Core/Helpers/DisplayArtistRenderer.cs` | **Create** — `BuildSegments` helper: splits `DisplayArtist` into artist + connector segments. Also defines `ArtistSegment` and `ArtistSegmentKind` types |
| `Hypersonic/Models/SongInfo.cs` | **Edit** — add `List<SongArtistInfo> Artists` and `bool HasStructuredArtists` properties |
| `Hypersonic/ViewModels/AlbumDetailViewModel.cs` | **Edit** — map `Child.Artists[]` in `MapSongInfo`; add `AlbumArtists`, `AlbumDisplayArtist`, `AlbumHasStructuredArtists` properties for header rendering |
| `Hypersonic/ViewModels/PlaylistDetailViewModel.cs` | **Edit** — map `Child.Artists[]` in `MapSongInfo` |
| `Hypersonic/Views/AlbumDetailPage.xaml` | **Edit** — add Artist column to header + track template; replace single-artist header HyperlinkButton with avatar button + RichTextBlock for segmented text rendering |
| `Hypersonic/Views/AlbumDetailPage.xaml.cs` | **Edit** — add `ArtistLinks_DataContextChanged`, `RenderArtistSegments`, artist link click handler, album header artist rendering |
| `Hypersonic/Views/PlaylistDetailPage.xaml` | **Edit** — add Album + Artist columns to header + template |
| `Hypersonic/Views/PlaylistDetailPage.xaml.cs` | **Edit** — add `AlbumLink_Click`, `ArtistLinks_DataContextChanged`, `RenderArtistSegments`, artist link click handler |

---

## Verification

### Automated tests

Add unit tests in `Hypersonic.Core.Tests` for:

**`SongArtistMapper.MapArtists`** (defined in Part 2) — now returns `(List<SongArtistInfo>, bool HasStructured)`:
1. **`Artists[]` populated with non-empty names**: maps each entry to `SongArtistInfo`; `HasStructured` is `true`.
2. **`Artists[]` null, `Artist` + `ArtistId` present**: falls back to single-element list; `HasStructured` is `false`.
3. **`Artists[]` empty array**: same fallback as null; `HasStructured` is `false`.
4. **`Artists[]` null, `Artist` null**: produces single entry with empty name; `HasStructured` is `false`.
5. **`Artists[]` with all-empty names**: filters out empty entries, falls back to single-element list from `fallbackName`/`fallbackId`; `HasStructured` is `false`.

**`Child.Artists` / `Child.DisplayArtist` deserialization** (in `Hypersonic.Client.UnitTest`):

Existing album-level coverage for `Artists[]` and `DisplayArtist` is in `AlbumID3WithSongsTest`. Add track-level deserialization tests to verify `Child.Artists[]` and `Child.DisplayArtist` are correctly bound from both JSON and XML payloads. Test cases:
1. **JSON response with `Child.Artists[]` populated**: verify array length, each entry's `Name` and `Id`.
2. **JSON response with `Child.DisplayArtist`**: verify string value.
3. **JSON response without `Child.Artists[]`** (legacy server): verify `Artists` is null, `DisplayArtist` may or may not be present.
4. **XML equivalents** of the above if the client supports XML deserialization for these fields.

**`DisplayArtistRenderer.BuildSegments`** (defined in Part 4) — takes `hasStructuredArtists` parameter:
1. **Multi-artist with DisplayArtist** (`hasStructured: true`): e.g., `"Dr. Dre feat. Eminem & Skylar Grey"` with 3-element `Artists[]` → produces alternating connector/artist segments preserving "feat." and "&".
2. **Single artist, DisplayArtist matches name** (`hasStructured: true`): produces one artist segment, no connectors.
3. **Single artist, DisplayArtist differs** (`hasStructured: true`, e.g., "Artist A feat. Artist B" but only one entry in `Artists[]`): artist name portion is an artist segment; remainder is connector.
4. **Structured artists, no names found in DisplayArtist** (`hasStructured: true`): e.g., `DisplayArtist` is `"Various Artists"` but `Artists[]` has `[{Name: "Alice", Id: "1"}, {Name: "Bob", Id: "2"}]` — neither name appears in the string. Falls back to comma-separated artist segments: artist `"Alice"` (Id `"1"`) + connector `", "` + artist `"Bob"` (Id `"2"`). Both are clickable. The `DisplayArtist` string is preserved in the tooltip by the caller.
5. **Legacy fallback, name not found in DisplayArtist** (`hasStructured: false`): single artist entry with `Name = "Andre Young"` (real name), `DisplayArtist` is `"Dr. Dre feat. Eminem"` (stage name) — "Andre Young" does not appear in the display string. Produces a single artist segment with text `"Dr. Dre feat. Eminem"` and **null Id** (plain text, not linked). `UsedStructuredFallback` is `false`.
6. **Artists[] empty, DisplayArtist present**: returns a single artist segment with null-Id `SongArtistInfo` carrying the DisplayArtist text. Empty list if both are null.
7. **Substring collision** (`hasStructured: true`): e.g., artist names "Art" and "Artist" both appear — longer match wins at the same position.
8. **All artist names empty with DisplayArtist** (edge case — `SongArtistMapper` filters these, but `BuildSegments` should handle it defensively): `Artists[]` has `[{Name: "", Id: "1"}]`, `DisplayArtist` is `"Some Artist"` — no names can be matched. Since all names are empty, produces a single artist segment with text `"Some Artist"` and null Id (plain text, not linked).
9. **Boundary-aware matching** (`hasStructured: true`): `DisplayArtist` is `"Arthur feat. Art"`, `Artists[]` has `[{Name: "Art", Id: "1"}]` — "Art" must not match inside "Arthur". Only the standalone "Art" at the end matches, producing: connector `"Arthur feat. "` + artist `"Art"`.
10. **Partial match** (`hasStructured: true`): `DisplayArtist` is `"Dr. Dre feat. Slim Shady"`, `Artists[]` has `[{Name: "Dr. Dre", Id: "1"}, {Name: "Eminem", Id: "2"}]` — "Dr. Dre" matches but "Eminem" does not (display string uses the alias "Slim Shady"). Produces: artist `"Dr. Dre"` + connector `" feat. Slim Shady"` from the display string. "Eminem" is **not** appended. `HasUnmatchedArtists` is `true`. Tooltip shows all structured artist names: `"Dr. Dre, Eminem"`.

### Manual verification

1. **Build**: `dotnet build Hypersonic/Hypersonic.csproj -p:Platform=x64`
2. **Album detail page**: Artist column visible. Artist names matched in `DisplayArtist` show as clickable links (secondary foreground). Connector text ("feat.", "&") appears dimmed (tertiary foreground). Clicking an artist link navigates to ArtistDetailPage. Unmatched artists (aliases, abbreviations) appear as part of the connector text; their names are shown in the tooltip.
3. **Multi-artist album header**: On a collaborative album with `Artists[]` available (e.g., "Artist A & Artist B"), the header shows the full `DisplayArtist` string with each artist individually clickable. The "&" connector is dimmed. On older servers without `Artists[]`, if the primary artist name is found in the display string, that portion is a clickable link. If not found, the text is plain and the thumbnail image is the navigation path.
4. **Playlist detail page**: Album and Artist columns visible. Album name is clickable → navigates to AlbumDetailPage. Artist names matched in `DisplayArtist` are clickable → navigate to ArtistDetailPage. Connector text is dimmed.
5. **DisplayArtist fallback**: On a server without `Artists[]` array, if `DisplayArtist` is "Artist A feat. Artist B", the column shows that full string as visible text. If the primary artist name is found within the string, it is individually linked; connector text is dimmed. If the name cannot be matched, the entire string is shown as plain text (not linked) to avoid misleading click targets.
7. **Truncation**: At the default 1400px window width, artist and album columns are comfortably readable. Long multi-artist credits clip cleanly within the column. Tooltip shows discoverable content on hover: the raw `DisplayArtist` for structured no-match fallback, all structured artist names for partial matches, or the full rendered text otherwise (see Part 4 tooltip rules). Below 800px, proportional columns narrow significantly; this is a pre-existing layout limitation (see Part 2 minimum width note).
8. **Edge cases**: Songs with null `ArtistId` render as plain text. Songs with null `AlbumId` render album as plain text.
9. **Row click**: Clicking anywhere on the row outside of artist/album hyperlinks still plays the track.
10. **In-app theme switch**: While on AlbumDetailPage or PlaylistDetailPage, switch theme in Settings (Light → Dark or vice versa). All artist link text, connector text, and album header artist text should update to the new theme's brush colors immediately without navigating away.
11. **System theme switch**: While on a detail page with theme set to "Default" (follow system), change the Windows system theme. The framework propagates the change via `ActualThemeChanged` under `ElementTheme.Default`, so all generated artist cells (track rows and album header) should reflect the new colors. When theme is explicitly set to Light or Dark, system-theme changes are intentionally ignored.
