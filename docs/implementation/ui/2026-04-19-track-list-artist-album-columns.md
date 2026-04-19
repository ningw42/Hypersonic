# Track List Artist & Album Columns — Implementation Notes

Companion to [`docs/design/ui/2026-04-18-track-list-artist-album-columns.md`](../../design/ui/2026-04-18-track-list-artist-album-columns.md). This document covers implementation-specific decisions, deviations from the design, and details that only appear in source.

---

## Theme-aware brush resolution

### Problem

The design doc specifies using `Application.Current.Resources["TextFillColorSecondaryBrush"]` in code-behind to style `Run` and `Hyperlink` inlines. This does not work correctly in WinUI 3. The app sets `RequestedTheme` on the root `FrameworkElement` (not `Application.RequestedTheme`), so `Application.Current.Resources[key]` always resolves the *default* (light) theme brush — even when the UI is in dark mode. `ThemeDictionaries` lookup on the application-level `ResourceDictionary` also fails because `TextFillColorSecondaryBrush` and `TextFillColorTertiaryBrush` are system-level resources not present in the app's own theme dictionaries.

### Solution: hidden XAML brush providers

Each page declares two collapsed `TextBlock` elements in XAML with `{ThemeResource}` foreground bindings:

```xaml
<TextBlock x:Name="SecondaryBrushProvider"
           Foreground="{ThemeResource TextFillColorSecondaryBrush}"
           Visibility="Collapsed"/>
<TextBlock x:Name="TertiaryBrushProvider"
           Foreground="{ThemeResource TextFillColorTertiaryBrush}"
           Visibility="Collapsed"/>
```

Code-behind reads the brush via properties:

```csharp
private Brush SecondaryBrush => SecondaryBrushProvider.Foreground;
private Brush TertiaryBrush => TertiaryBrushProvider.Foreground;
```

The framework automatically updates `ThemeResource` bindings on these elements when the theme changes. When `ActualThemeChanged` fires and triggers re-rendering, the properties return the correct brush for the current theme. This avoids any manual theme dictionary lookup.

### Why `Hyperlink.Foreground` is set on the Hyperlink, not the Run

The design doc shows `Foreground` set on the `Run` inside a `Hyperlink`. In practice, setting `Foreground` on the `Run` inside a `Hyperlink` controls the default text color, but `Hyperlink` applies its own foreground on hover/press states. Setting `Foreground` on the `Hyperlink` itself ensures consistent coloring across all visual states, so the implementation sets it there and omits it from the child `Run`.

---

## Album header layout: Grid instead of StackPanel

### Problem

The design doc's sample XAML uses a horizontal `StackPanel` containing the avatar `HyperlinkButton` and the `AlbumArtistText` `RichTextBlock`. In WinUI 3, a horizontal `StackPanel` measures children with unbounded horizontal space, so `TextTrimming="CharacterEllipsis"` on the `RichTextBlock` never engages — the text expands indefinitely and overflows.

### Solution

The implementation uses a `Grid` with `Auto` + `*` columns instead:

```xaml
<Grid ColumnSpacing="8">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="Auto"/>
        <ColumnDefinition Width="*"/>
    </Grid.ColumnDefinitions>
    <HyperlinkButton Grid.Column="0" .../>
    <RichTextBlock Grid.Column="1" x:Name="AlbumArtistText" .../>
</Grid>
```

The `*` column constrains the `RichTextBlock` to the remaining width, allowing `TextTrimming` to engage correctly. The avatar button uses `VerticalAlignment="Center"` (not present in the design sample).

---

## Empty-name artist filtering in BuildSegments

### Problem

`SongArtistMapper.MapArtists` always returns at least one entry. When both `fallbackName` and `fallbackId` are null, it produces `[{ Name: "", Id: null }]`. The design doc's `BuildSegments` algorithm operates on the `artists` list directly, which means this mapper-shaped single-empty-entry case falls into the non-empty-list code path and produces an empty-text artist segment instead of the design's expected "empty segments" result.

### Solution

`BuildSegments` filters empty-name artists into a `nonEmptyArtists` list as its first step, *before* branching on `displayArtist`. The empty-displayArtist path and the matching path both operate on `nonEmptyArtists`. When `nonEmptyArtists` is empty and `displayArtist` is also empty/null, the method returns an empty segment list.

The filtering happens once and is reused across all code paths:

```
artists list → filter empty names → nonEmptyArtists
  if displayArtist is empty:
    nonEmptyArtists.Count == 0 → empty result
    nonEmptyArtists.Count == 1 → single artist segment
    else → comma-separated
  if nonEmptyArtists.Count == 0 (but displayArtist is present):
    → single plain-text segment from displayArtist
  else → proceed to FindMatches with nonEmptyArtists
```

---

## FindMatches: occupied-position tracking

The design doc describes the matching algorithm at a high level (left-to-right scan, longest match first, boundary-aware). The implementation uses a `bool[] occupied` array indexed by character position in the display string to prevent overlapping matches. After a match is accepted, its character range is marked as occupied. Subsequent shorter candidates that overlap any occupied position are skipped and continue scanning from the next character.

Matches are collected in arbitrary order (longest-first iteration) and sorted by `Start` position before segment construction. This ensures the output segments follow the display string's left-to-right order regardless of which artist name was matched first.

---

## Types colocated in DisplayArtistRenderer.cs

The design doc lists `ArtistSegment`, `ArtistSegmentKind`, and `BuildSegmentsResult` as types defined in `DisplayArtistRenderer.cs`. In the implementation, all four types (`ArtistSegmentKind` enum, `ArtistSegment` class, `BuildSegmentsResult` record, and the `DisplayArtistRenderer` static class) are in a single file. `ArtistSegment` uses `init`-only properties. `BuildSegmentsResult` is a positional `record` (not a class), giving value equality for free in tests.

---

## RenderArtistSegments and BuildArtistTooltip are duplicated per page

Per the design doc's guidance, these helpers are duplicated in `AlbumDetailPage.xaml.cs` and `PlaylistDetailPage.xaml.cs` rather than shared. They reference page-level state (the brush provider elements) and are small enough that sharing would add indirection without meaningful deduplication. `BuildArtistTooltip` is `static` and identical in both files.

---

## AlbumDetailViewModel: property ordering for AlbumHasStructuredArtists

`AlbumHasStructuredArtists` is a plain `bool` property (not `[ObservableProperty]`) because the UI does not bind to it directly — it is only read by the code-behind when rendering. In `LoadAlbumAsync`, it is set *before* `AlbumArtists` so that when `PropertyChanged` fires for `AlbumArtists` and the code-behind calls `RenderAlbumHeaderArtists()`, the flag is already correct.

---

## Test coverage

| Test class | Project | Count | Scope |
|---|---|---|---|
| `SongArtistMapperTests` | Hypersonic.Core.Tests | 5 | Structured input, null/empty fallback, all-empty-names |
| `DisplayArtistRendererTests` | Hypersonic.Core.Tests | 14 | Multi-artist segmentation, boundary matching, substring collision, partial match, case insensitivity, structured/legacy fallback, mapper-shaped empty-name edge case |
| `ChildTest` | Hypersonic.Client.UnitTest | 6 | JSON and XML deserialization of `Child.Artists[]` and `Child.DisplayArtist`, including legacy-server absence |

The XML deserialization tests for `Child` use the `GetAlbumResponse` wrapper (`<subsonic-response><album><song>`) because that is the response shape this feature's UI code path consumes. `Child` does appear under other XML elements (`<child>` in `Directory`, `Indexes`, etc.), but the track-list artist columns read `Child` data via `GetAlbumAsync` and `GetPlaylistAsync`, so `GetAlbumResponse` is the most representative test wrapper.
