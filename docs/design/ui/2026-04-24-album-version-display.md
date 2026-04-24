# Clean Album Version Display

## Overview

OpenSubsonic servers may return album version information (e.g., "Deluxe Edition", "Remastered") — either embedded in the album name, as a separate `version` field, or both. Navidrome's default configuration embeds the version into the `name` field (e.g., "Abbey Road (Super Deluxe Edition)"), making album cards, player bar, queue items, and search results unnecessarily verbose. The version information is useful context but only when the user is looking at the album detail page.

**Goal**: Show only the main album name in compact contexts (album cards, player bar, queue, search, SMTC). On the album detail page, show the version as a styled subtitle row below the artist name, matching the visual weight of the release date / genre line.

## Background

### OpenSubsonic `version` field

The OpenSubsonic spec adds an optional `version` field to `AlbumID3`:

```json
{
  "id": "ad0f112b6dcf83de5e9cae85d07f0d35",
  "name": "8-bit lagerfeuer",
  "version": "Deluxe Edition",
  "artist": "pornophonique",
  "year": 2007
}
```

- **Type**: `string`, optional
- **Description**: "The album version name (Remastered, Anniversary Box Set, ...)"
- **Navidrome behavior** (v0.61.0+, PR [#5111](https://github.com/navidrome/navidrome/pull/5111)): Navidrome returns **both** fields. `Album.FullName()` appends `(version)` to the `name` field for backward compatibility with legacy clients, while `buildOSAlbumID3` separately populates the OpenSubsonic `version` field from the album's tags. A config option `Subsonic.AppendAlbumVersion` (default: `true`) controls whether the version is embedded in `name`. When enabled (the default), the response looks like:

```json
{
  "name": "Abbey Road (Super Deluxe Edition)",
  "version": "Super Deluxe Edition"
}
```

When `AppendAlbumVersion` is set to `false`, the `name` field is clean and the version exists only in its own field:

```json
{
  "name": "Abbey Road",
  "version": "Super Deluxe Edition"
}
```

For songs (`Child`), `MediaFile.FullAlbumName()` performs the same embedding into the `album` attribute. There is no separate `version` field on `Child` — version information is only available at the album level.

The Hypersonic client entity (`AlbumID3.cs`) does not currently deserialize the `version` field.

### Current album name flow

Album names surface in three independent paths:

1. **Album-level via AlbumMapper** (`AlbumID3.Name`) — Used by `AlbumMapper` to populate `AlbumInfo.Name`, which feeds album cards (AlbumsPage, MainPage carousels, ArtistDetailPage discography) and `AlbumDetailViewModel.AlbumName`.

2. **Album-level via ShellViewModel** (`AlbumID3.Name`) — Album search results in `ShellViewModel` build `SearchSuggestionItem.PrimaryText` directly from `album.Name` (`ShellViewModel.cs:137`), bypassing `AlbumMapper`.

3. **Song-level** (`Child.Album`) — Each song carries its album name as a flat string. This feeds `SongInfo.Album`, `PlaybackTrack.Album`, the player bar (`PlayerViewModel.AlbumName`), queue flyout items, SMTC metadata (`AlbumTitle`), and search result secondary text.

All three paths may receive version-embedded names from the server (depending on server configuration), or clean names with version only in the separate `AlbumID3.version` field.

### Affected display locations

| Location | Current source | Binding/Render | Context |
|----------|---------------|----------------|---------|
| **Album cards** (AlbumsPage grid) | `AlbumInfo.Name` via AlbumMapper | `{Binding Name}` | Compact — name only |
| **Home carousels** (MainPage) | `AlbumInfo.Name` via AlbumMapper | `{Binding Name}` | Compact — name only |
| **Artist discography** (ArtistDetailPage) | `AlbumInfo.Name` via AlbumMapper | `{Binding Name}` | Compact — name only |
| **Album detail header** | `AlbumDetailViewModel.AlbumName` | `{x:Bind ViewModel.AlbumName}` | Full — name + version subtitle |
| **Player bar** (ShellPage bottom) | `PlayerViewModel.AlbumName` | Code-behind `RenderNowPlayingAlbum()` | Compact — name only |
| **Queue flyout items** | `PlaybackTrack.Album` via `QueueItem.Track` | `{Binding Track.Album}` | Compact — name only |
| **SMTC** (lock screen / media overlay) | `PlaybackTrack.Album` | `displayProperties.MusicProperties.AlbumTitle` | Compact — name only |
| **Search results** (albums) | `AlbumID3.Name` via ShellViewModel | `{x:Bind PrimaryText}` | Compact — name + version in secondary text |
| **Search results** (songs) | `Child.Album` via ShellViewModel | `{x:Bind SecondaryText}` | Compact (excluded — see §4) |
| **Artist top songs** (ArtistDetailPage) | `Child.Album` via ArtistDetailViewModel | `{x:Bind Album}` in track list | Compact (partially covered — see §4) |

### Song-level `Child` entity

The `Child` entity (used for songs) does **not** have a `version` field in the OpenSubsonic spec. Navidrome's `MediaFile.FullAlbumName()` embeds the version into the song's `album` attribute (e.g., `"Abbey Road (Super Deluxe Edition)"`), controlled by the same `AppendAlbumVersion` config. There is no structured way to extract the version from a song's album name without either:

1. Parsing the parenthetical suffix from the string (fragile), or
2. Cross-referencing the song's `albumId` against a known album to retrieve the version (requires an extra lookup or cached album data).

For contexts where the album entity is already loaded (album detail, personal top songs), we pass the album's `version` through to the song mapper. For contexts where it isn't available (community top songs, playlists, search), song album names remain unstripped.

### Disambiguation of multiple editions

A library may contain multiple editions of the same album (e.g., "OK Computer" and "OK Computer (OKNOTOK 1997 2017)"). Stripping the version suffix makes these visually identical in compact contexts. Disambiguation is provided through:

- **Album card tooltips**: Bind to `DisplayName` (computed "Name (Version)" property). Users hover to distinguish editions.
- **Album search results**: Append ` · Version` to the secondary text line (which already shows the artist), giving e.g., "pornophonique · Deluxe Edition". This provides inline disambiguation without a separate tooltip.
- **Album detail page**: The version subtitle is shown prominently, so clicking into either card immediately reveals which edition it is.
- **Existing metadata differences**: Editions of the same album typically differ in year or song count, providing some visual distinction even in compact card views.

## Design

### 1. Add `version` field to `AlbumID3` entity

Add the OpenSubsonic `version` field to the client entity so it's deserialized from API responses.

```csharp
// Hypersonic.Client/Models/Entities/AlbumID3.cs
[JsonPropertyName("version")]
[XmlAttribute("version")]
public string? Version { get; set; }
```

### 2. Add `Version` and `DisplayName` properties to `AlbumInfo` model

```csharp
// Hypersonic.Core/Models/AlbumInfo.cs
public string? Version { get; set; }

/// Full display name including version suffix, for tooltips and disambiguation.
public string DisplayName => string.IsNullOrEmpty(Version) ? Name : $"{Name} ({Version})";
```

`DisplayName` is a computed property — no serialization, no setter, always consistent with `Name` and `Version`.

### 3. Compute short name in `AlbumMapper`

`AlbumMapper.Map()` strips the version suffix from `album.Name` when a `version` is available, and propagates both the short name and the version.

```csharp
// Hypersonic/Helpers/AlbumMapper.cs
var (shortName, effectiveVersion) = SplitAlbumVersion(album.Name, album.Version);

return new CoreAlbumInfo
{
    Id = album.Id,
    Name = shortName,
    Version = effectiveVersion,
    // ... rest unchanged
};
```

The `SplitAlbumVersion` helper returns a tuple — both the display name and the effective version to use. It handles three cases:

| Case | `name` | `version` | Result |
|------|--------|-----------|--------|
| **Suffix embedded** | `"Abbey Road (Super Deluxe Edition)"` | `"Super Deluxe Edition"` | `("Abbey Road", "Super Deluxe Edition")` |
| **Already clean** | `"Abbey Road"` | `"Super Deluxe Edition"` | `("Abbey Road", "Super Deluxe Edition")` |
| **Non-standard embedded** | `"Abbey Road [Super Deluxe Edition]"` | `"Super Deluxe Edition"` | `("Abbey Road [Super Deluxe Edition]", null)` |

```csharp
internal static (string Name, string? Version) SplitAlbumVersion(string? name, string? version)
{
    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(version))
    {
        return (name ?? string.Empty, null);
    }

    // Case 1: Standard suffix — strip " (Version)" and keep version
    var suffix = $" ({version})";
    if (name.EndsWith(suffix, StringComparison.Ordinal))
    {
        return (name[..^suffix.Length], version);
    }

    // Case 2: Name is already clean — server sent name and version as separate fields
    if (!name.Contains(version, StringComparison.OrdinalIgnoreCase))
    {
        return (name, version);
    }

    // Case 3: Version text appears in name but not as the standard " (Version)" suffix.
    // The name uses a non-standard format (brackets, inline text, etc.) that we can't
    // safely parse. Discard the version to prevent double-display downstream:
    // DisplayName would append "(Version)" to a name that already contains it,
    // and the detail subtitle would duplicate information already in the title.
    return (name, null);
}
```

**Case 2** is the key distinction from the previous two-case approach. When the server sends a clean `name` with a separate `version` field (no embedding), the name is already short and the version is valid structured data — both are preserved as-is.

**Case 3** discards the version because the name already contains the version text in a format we don't recognize. Keeping the version would cause `DisplayName` to produce "Album [Version] (Version)" and the detail page to show both the embedded version in the title and a duplicate subtitle.

The companion helper for song-level stripping delegates to `SplitAlbumVersion`:

```csharp
internal static string StripVersionSuffix(string? name, string? version)
{
    return SplitAlbumVersion(name, version).Name;
}
```

### 4. Strip version from song-level album names

Songs (`Child`) don't have a `version` field. To strip the version suffix from song album names, the mapping methods that build `SongInfo` and `PlaybackTrack` from `Child` entities need access to the album's version string.

**Approach**: Pass an optional `albumVersion` parameter through the mapping path where album context is available.

- **`AlbumDetailViewModel.MapSongInfo()`** — Has access to the loaded `AlbumID3` entity. Pass `album.Version` and call `StripVersionSuffix(song.Album, album.Version)` when setting `SongInfo.Album`.
- **`AlbumDetailViewModel.BuildPlaybackTracks()`** — Already uses `AlbumName` (which is now the short name). No change needed.
- **`ArtistDetailViewModel` — personal top songs** (`LoadPersonalTopSongsAsync`) — Fetches each album via `GetAlbumAsync`, which returns an `AlbumID3` entity with the `Version` field. Restructure the collection to pair each `Child` with its album's version: collect `(Child, string? version)` tuples instead of bare `Child` entities, then pass the version through `MapChildToSongInfo`. This is the only way to strip versions for personal top songs without extra API calls.
- **`ArtistDetailViewModel` — community top songs** (`LoadCommunityTopSongsAsync`) — Songs come from `GetTopSongsAsync`, which returns `Child` entities with no album-level context. **No stripping** — these songs lack a reliable version source.
- **`PlaylistDetailViewModel`** — Songs come from `GetPlaylistAsync`, which returns `Child` entities with no album-level context. Since each song may belong to a different album, cross-referencing versions would require extra API calls per song. **No stripping.**
- **`ShellViewModel` (song search)** — Song search results come from `Search3Async`, which returns `Child` entities. Same as playlists — **no stripping** for individual songs.

Note: `SplitAlbumVersion`'s three-way logic ensures safety across all song-level cases:
- **Suffix embedded** in `Child.Album`: strips it, returns the short name.
- **Already clean** `Child.Album` (server didn't embed version): returns the name unchanged — already short.
- **Non-standard embedded**: returns the name unchanged and discards version — no double-appending risk since `StripVersionSuffix` only returns the name, not the version.

#### SMTC metadata

`PlaybackService.UpdateSmtc()` sets `AlbumTitle = track.Album`. Since `PlaybackTrack.Album` will contain the short name when built from album-detail or artist-detail (personal top songs) contexts, SMTC automatically shows the short name. For tracks originating from playlists, search, or community top songs (where version context is unavailable), the original server name flows through — which may or may not include an embedded version depending on server configuration.

### 5. Album search results — strip version and disambiguate in ShellViewModel

`ShellViewModel` builds album search suggestions directly from `AlbumID3.Name` (`ShellViewModel.cs:137`), bypassing `AlbumMapper`. This path must strip the version from `PrimaryText` and provide disambiguation through `SecondaryText`.

The search result popup renders two lines per album: `PrimaryText` (album name, FontSize 13) and `SecondaryText` (artist, FontSize 11). When a version is present, append ` · Version` to the secondary text so users can distinguish editions of the same album by the same artist:

```csharp
// ShellViewModel.cs, in the album search results loop
var (shortName, effectiveVersion) = AlbumMapper.SplitAlbumVersion(album.Name, album.Version);
var secondaryText = album.Artist ?? string.Empty;
if (!string.IsNullOrEmpty(effectiveVersion))
{
    secondaryText = $"{secondaryText} · {effectiveVersion}";
}

albums.Add(new SearchSuggestionItem
{
    Type = SearchResultType.Album,
    Id = album.Id,
    PrimaryText = shortName,
    SecondaryText = secondaryText,
    // ...
});
```

This uses the same `·` separator already used elsewhere in the app (e.g., `ReleaseDateGenre` in `AlbumDetailViewModel`). When `SplitAlbumVersion` discards the version (strip-failed case), `effectiveVersion` is `null` and the secondary text shows only the artist — no duplication.

### 6. Album detail page — version as subtitle

On `AlbumDetailPage`, display the version as a separate row in the metadata stack, positioned between the artist name and the release date / genre line.

#### ViewModel changes (`AlbumDetailViewModel`)

```csharp
[ObservableProperty]
[NotifyPropertyChangedFor(nameof(HasAlbumVersion))]
public partial string? AlbumVersion { get; set; }

public bool HasAlbumVersion => !string.IsNullOrEmpty(AlbumVersion);
```

Set in `LoadAlbumAsync`:
```csharp
var (shortName, effectiveVersion) = AlbumMapper.SplitAlbumVersion(album.Name, album.Version);
AlbumName = shortName;
AlbumVersion = effectiveVersion;
```

Note: `AlbumVersion` receives `effectiveVersion` (not raw `album.Version`). The three-way split in §3 ensures correct behavior:
- **Suffix embedded**: version preserved, subtitle shown, title is short.
- **Already clean**: version preserved, subtitle shown, title is already short.
- **Non-standard embedded**: version discarded (`null`), subtitle hidden, title shows the full server name — no duplication.

The `HasAlbumVersion` computed property provides a clean `bool` for XAML visibility binding, matching the existing codebase pattern where visibility is driven by `BoolToVisibilityConverter` on `bool` properties (e.g., `IsLoading` → `InverseBoolToVisibilityConverter` on line 274 of `AlbumDetailPage.xaml`).

#### XAML changes (`AlbumDetailPage.xaml`)

Insert a new `TextBlock` after the artist row, before the `ReleaseDateGenre` line:

```xml
<!-- Version subtitle (between artist and release date) -->
<TextBlock Text="{x:Bind ViewModel.AlbumVersion, Mode=OneWay}"
           Foreground="{ThemeResource TextFillColorSecondaryBrush}"
           Visibility="{x:Bind ViewModel.HasAlbumVersion, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}"
           FontSize="14"/>
```

This follows the established pattern: a `bool` property converted to `Visibility` via `BoolToVisibilityConverter` (registered globally in `Themes/Generic.xaml`). The `TextBlock` is hidden entirely when there's no version.

#### Style

The version subtitle uses the same `TextFillColorSecondaryBrush` foreground as the artist and release date lines, matching their visual weight. `FontSize="14"` places it between the header-sized album name and the body-sized release date line, creating a clear visual hierarchy:

```
Album Name                    ← HeaderTextBlockStyle, Bold
[Artist avatar] Artist Name   ← FontSize 20, secondary brush
Deluxe Edition                ← FontSize 14, secondary brush (NEW)
2023 · Alternative            ← Default body, secondary brush
12 songs · 42 min             ← Default body, tertiary brush
[Play button]
```

#### Album art sizing

The current album art is `200x200`. The metadata stack has `VerticalAlignment="Center"`, so adding an extra text row shifts the vertical center slightly. At 200px height with 5 text rows + button, the stack is approximately 160–180px tall, still fitting comfortably. **No art size change needed for the common case.** If the album name wraps to 2 lines AND a version is present, the stack could exceed 200px — but `VerticalAlignment="Center"` handles this gracefully by allowing overflow. If the user later prefers larger art, bump to `240x240` — but that's a separate decision.

### 7. Album card tooltips — show full name for disambiguation

Album cards currently bind `ToolTipService.ToolTip="{Binding Name}"`. Since `Name` is now the short name, tooltips lose the version info. Update tooltip bindings to use `DisplayName` (the computed "Name (Version)" property) so users can hover to distinguish editions.

Affected XAML files:
- `AlbumsPage.xaml` — album grid cards
- `MainPage.xaml` — all 5 carousel sections
- `ArtistDetailPage.xaml` — discography album cards

```xml
<!-- Before -->
ToolTipService.ToolTip="{Binding Name}"

<!-- After -->
ToolTipService.ToolTip="{Binding DisplayName}"
```

When there's no version (`DisplayName` returns `Name` unchanged), the tooltip is identical to the visible text — no visual regression.

### 8. `PlayerViewModel.AlbumName` — use short name

`PlayerViewModel` sets `AlbumName` from `PlaybackTrack.Album`:

```csharp
AlbumName = track.Album;  // line 140, 356
```

Since `PlaybackTrack.Album` will already contain the short name (when built from album-detail or artist-detail personal top songs contexts), no change is needed in `PlayerViewModel`.

For tracks built from playlist-detail, search, or community top songs (where version context is unavailable), the original server name flows through unmodified — this is acceptable as those contexts don't have reliable version information.

## Files to modify

| File | Change |
|------|--------|
| `Hypersonic.Client/Models/Entities/AlbumID3.cs` | Add `Version` property with JSON/XML attributes |
| `Hypersonic.Core/Models/AlbumInfo.cs` | Add `Version` property and `DisplayName` computed property |
| `Hypersonic/Helpers/AlbumMapper.cs` | Add `SplitAlbumVersion` (returns tuple) and `StripVersionSuffix` (convenience wrapper) helpers; strip version from `Name`, propagate effective version |
| `Hypersonic/ViewModels/AlbumDetailViewModel.cs` | Add `AlbumVersion` and `HasAlbumVersion` properties; use `SplitAlbumVersion` for both `AlbumName` and `AlbumVersion`; pass version to `MapSongInfo` |
| `Hypersonic/Views/AlbumDetailPage.xaml` | Add version subtitle `TextBlock` between artist row and release date row |
| `Hypersonic/ViewModels/ArtistDetailViewModel.cs` | Pass album version when mapping songs in `LoadPersonalTopSongsAsync`; restructure collection to pair children with album version |
| `Hypersonic/ViewModels/ShellViewModel.cs` | Use `SplitAlbumVersion` for album search results: strip version from `PrimaryText`, append ` · Version` to `SecondaryText` for disambiguation |
| `Hypersonic/Views/AlbumsPage.xaml` | Update album card tooltip to `{Binding DisplayName}` |
| `Hypersonic/Views/MainPage.xaml` | Update 5 carousel card tooltips to `{Binding DisplayName}` |
| `Hypersonic/Views/ArtistDetailPage.xaml` | Update discography card tooltip to `{Binding DisplayName}` |

## Scope exclusions

- **Playlist song album names** — Songs in playlists span multiple albums; cross-referencing each song's album version would require extra API calls per song. These remain unstripped.
- **Search result song album names** — Same as playlists; songs from `Search3Async` lack album-level context.
- **Community top songs album names** — Songs from `GetTopSongsAsync` lack album-level context; no version field available.
- **Fallback parenthetical parsing** — When the server returns a `version` field, we use it. When it doesn't (non-OpenSubsonic servers), we don't guess by parsing parentheses from the name — that would misfire on albums like "Music (Original Motion Picture Soundtrack)" where the parenthetical is part of the actual name.
- **Album art resizing** — The current 200x200 art comfortably fits the additional subtitle row. Enlarging art is deferred unless user feedback indicates otherwise.
