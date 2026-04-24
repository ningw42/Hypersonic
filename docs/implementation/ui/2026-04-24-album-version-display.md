# Clean Album Version Display — Implementation Notes

Companion to [`docs/design/ui/2026-04-24-album-version-display.md`](../../design/ui/2026-04-24-album-version-display.md). This document covers implementation-specific decisions, deviations from the design, and details that only appear in source.

---

## `AlbumID3.Version` property placement

The `Version` property was added between the `Moods` array and the existing `SortName` property in `AlbumID3.cs`, grouping it with the other OpenSubsonic extension fields. The XML attribute uses `[XmlAttribute("version")]` matching the OpenSubsonic spec's attribute-level placement (not `[XmlElement]`), consistent with how Navidrome serializes it.

---

## `SplitAlbumVersion` visibility

`SplitAlbumVersion` and `StripVersionSuffix` are `internal static` on `AlbumMapper`, not `private`. This is intentional — both `AlbumDetailViewModel` and `ArtistDetailViewModel` call them directly rather than going through `AlbumMapper.Map()`, because those ViewModels already have the raw `AlbumID3` entity and don't need a full mapping pass. `ShellViewModel` also calls `SplitAlbumVersion` directly for the same reason: it builds `SearchSuggestionItem` objects, not `AlbumInfo` objects.

The `Hypersonic.Tests` project accesses these via `[InternalsVisibleTo]` (already configured in the project for other internal members).

---

## `ArtistDetailViewModel` tuple collection

The design doc describes restructuring the personal top songs collection from `List<Child>` to `List<(Child Song, string? AlbumVersion)>`. The implementation uses the named tuple `(Client.Models.Entities.Child Song, string? AlbumVersion)` with the fully qualified `Child` type to avoid adding a `using` alias (the file already references `Client.Models.Entities.Child` through its existing usings).

The `AddRange` call was replaced with an explicit `foreach` + `Add` loop because `AddRange` doesn't accept a projected enumerable of tuples cleanly — the explicit loop is clearer and avoids an intermediate `Select` allocation. The `lock (lockObj)` block wraps the entire loop, not individual `Add` calls, to avoid interleaving with other album fetches.

The filtering and sorting pipeline chains through the tuple's named members (`c.Song.PlayCount`, `c.Song` for `IsArtistCredited`, `c.AlbumVersion` for mapping), keeping the lambda bodies compact.

---

## `MapChildToSongInfo` optional parameter

`ArtistDetailViewModel.MapChildToSongInfo` adds `string? albumVersion = null` as a defaulted parameter rather than an overload. This preserves the existing call site in `LoadCommunityTopSongsAsync` (which passes no album version) without modification. The conditional in the `Album` assignment:

```csharp
Album = albumVersion != null ? AlbumMapper.StripVersionSuffix(song.Album, albumVersion) : song.Album ?? string.Empty,
```

avoids calling `StripVersionSuffix` when no version is available, preserving the original `?? string.Empty` fallback path exactly. This differs from `AlbumDetailViewModel.MapSongInfo`, where `albumVersion` is a required parameter because the album context is always available.

---

## `AlbumDetailViewModel.MapSongInfo` — required parameter

Unlike `ArtistDetailViewModel`, `AlbumDetailViewModel.MapSongInfo` makes `albumVersion` a required (non-defaulted) parameter. Every call site in `AlbumDetailViewModel` has access to the loaded album entity, so there's no reason to allow omitting it. The `Select(s => MapSongInfo(s, client, album.Version))` lambda passes the raw `album.Version` directly — `StripVersionSuffix` internally delegates to `SplitAlbumVersion`, which handles the three-way case analysis.

---

## `HasAlbumVersion` computed property

`AlbumDetailViewModel.HasAlbumVersion` is a manual `bool` getter (`=> !string.IsNullOrEmpty(AlbumVersion)`) rather than an `[ObservableProperty]` because it's derived from `AlbumVersion`. The `[NotifyPropertyChangedFor(nameof(HasAlbumVersion))]` attribute on `AlbumVersion` ensures the XAML binding updates when the version changes. This matches the existing pattern used by `HasSongCount` and similar computed visibility properties in the codebase.

---

## Version subtitle XAML — no comment

The design doc's XAML snippet includes a `<!-- Version subtitle -->` comment. The implementation omits it — the `TextBlock`'s `Text="{x:Bind ViewModel.AlbumVersion}"` binding is self-documenting, and surrounding elements (artist row above, release date row below) provide sufficient context. This follows the project's convention of minimal XAML comments.

---

## Search disambiguation separator

The `·` (middle dot, U+00B7) used in `ShellViewModel` for version disambiguation (`"Artist · Deluxe Edition"`) matches the separator already used in `AlbumDetailViewModel.ReleaseDateGenre` (`"2023 · Alternative"`). The separator is inline in the interpolated string, not extracted to a constant, matching the existing usage pattern.

---

## Tooltip binding — `{Binding DisplayName}` not `{x:Bind}`

The tooltip bindings in `AlbumsPage.xaml`, `MainPage.xaml`, and `ArtistDetailPage.xaml` use `{Binding DisplayName}` (runtime binding) rather than `{x:Bind}` (compiled binding). This is because the album card templates are `DataTemplate` instances inside `ItemsRepeater` controls, where the data context is an `AlbumInfo` object resolved at runtime. The existing `Name` and `Artist` bindings in these templates already use `{Binding}`, so `DisplayName` follows suit.

---

## Test structure

### Client deserialization tests (`AlbumID3VersionTest`)

Five test cases cover the `Version` field deserialization:
1. Version present with embedded suffix — both `Name` and `Version` populated
2. Clean name with separate version — `Name` is short, `Version` populated
3. No version field — `Version` is `null`
4. Empty version string — `Version` is `""` (not coerced to null; that's `SplitAlbumVersion`'s job)
5. Other fields intact when version is present — regression guard

Tests use `SubsonicClient.DeserializeJson<AlbumID3>()` with inline JSON via raw string literals. XML deserialization tests were not added — the existing `AlbumID3Test` class covers XML round-tripping for other fields, and the `[XmlAttribute]` pattern is well-established.

### AlbumInfo model tests (`AlbumInfoTests`)

Nine test cases cover `DisplayName` and `Version`:
- `DisplayName` with version, without version, null version, empty version, empty name + version, single-word version, default state
- `Version` default value (null) and set/get round-trip

### AlbumMapper tests (`AlbumMapperTests`)

**`SplitAlbumVersion`** — 18 test cases organized by the three-way case analysis:

- **Null/empty guards** (6 tests): null name, empty name, null version, empty version, both null, both empty — all return `(name ?? "", null)`.
- **Case 1 — suffix embedded** (4 tests): standard `" (Version)"` suffix stripped for multi-word versions, single-word versions, "Remastered", and versions with numbers ("OKNOTOK 1997 2017").
- **Case 2 — already clean** (3 tests): name doesn't contain version text — both preserved. Includes a test where the name has unrelated parentheses ("Music (Original Motion Picture Soundtrack)" with version "Deluxe Edition").
- **Case 3 — non-standard embedded** (5 tests): version text found in name but not as standard suffix — version discarded. Covers brackets, dash-separated, version in middle of name, case mismatch (contains check is `OrdinalIgnoreCase`, suffix check is `Ordinal`), and version equals full name.

The case-sensitivity test (`SuffixMatch_IsCaseSensitive`) is notable: `" (deluxe edition)"` doesn't match the `Ordinal` suffix check for `"Deluxe Edition"`, but the `OrdinalIgnoreCase` contains check catches it, routing to Case 3 (discard). This is the correct behavior — if the server sends mismatched casing, we can't safely strip the suffix.

**`StripVersionSuffix`** — 6 tests verify the convenience wrapper delegates correctly to `SplitAlbumVersion` and returns only the name component.

---

## Deviations from design doc

None. The implementation matches the design doc exactly across all 8 sections. No scope changes, no additional features, no deferred items beyond those already listed in the design's scope exclusions.
