# All Tracks Page — Design Spec

## Context

The app has no way to browse the full song library. Users can only see tracks scoped to an album, artist, or playlist. The roadmap item 2.3 calls for a dedicated page listing all tracks from the server with incremental loading, sortable columns, and search.

### API constraints

The Subsonic API has no "get all songs" endpoint. The standard approach (used by feishin and aonsoku) is `search3` with an empty query, paginated via `songCount`/`songOffset`. Key limitations:

- **No server-side sorting** — search3 accepts no sort parameters. All sort options in the Subsonic API map to `undefined` (confirmed in feishin's subsonic-controller).
- **No total count** — search3 returns no `totalRecordCount`. The client cannot know how many songs exist without fetching to exhaustion.
- **Empty query support** — OpenSubsonic spec mandates servers support empty queries to return all data. Navidrome complies. Non-OpenSubsonic servers may reject or ignore empty queries — see Server Compatibility below.
- **Batch size** — Navidrome caps at 500 per request, but 100 is the common batch size used by both feishin and aonsoku.

### Server compatibility

The all-tracks page depends on `search3("")` returning all songs. This is guaranteed by the OpenSubsonic spec and supported by Navidrome, but legacy Subsonic servers may return empty results or an error for an empty query.

**MVP scope**: target OpenSubsonic-compliant servers only (the app's primary target is Navidrome). Two distinct UI states for empty results:

- **Empty state** (successful response, zero songs): Show "No tracks found" via a dedicated empty-state visual (not `ErrorMessage`). A truly empty library and a server that silently ignores empty queries are indistinguishable — the same message covers both.
- **Error state** (exception thrown): Set `ErrorMessage` to "Failed to load tracks. Your server may not support browsing all tracks." for the initial empty-query fetch. For subsequent batch errors, `ErrorMessage` is set to a generic load-failure message without the compatibility hint.

No alternative endpoint fallback for MVP — `getSongsByGenre` requires a genre, `getRandomSongs` is non-deterministic, and walking all albums is prohibitively slow.

### API bug: Search3Request query encoding

`Search3Request.ToQueryString()` appends the query as `&query={this.Query}` without `Uri.EscapeDataString`. Queries containing spaces, `&`, `#`, or other special characters produce malformed requests. This must be fixed and tested as part of this work — the all-tracks page is the first feature that exposes arbitrary user text to this endpoint (the existing Shell search also uses search3 but faces the same bug).

### Reference implementations

| App | Loading | Sorting | Search |
|-----|---------|---------|--------|
| **Feishin** (Subsonic path) | Incremental via search3, 100/batch | None — server order only | Server-side via search3 query |
| **Aonsoku** | Incremental via search3, 100/batch | Client-side within loaded data (TanStack Table) | Server-side via search3 query |

Both use virtual scrolling to handle large libraries efficiently.

---

## Feature Overview

A new top-level page ("Tracks") accessible from the sidebar, positioned between Artists and Playlists. It displays all songs from the server in a scrollable track list with:

1. **Incremental loading** — songs fetched in 100-item batches as the user scrolls, using the same `ISupportIncrementalLoading` pattern as the Albums page.
2. **Client-side sorting** — sortable columns that reorder the in-memory collection. Sorting only covers loaded data; new batches appended at the bottom in server order.
3. **Server-side search** — a search box that replaces the empty query with user text, clears loaded data, and re-fetches from the server. Clearing the box restores the full list.
4. **Standard track list features** — now-playing highlight, favorite toggle, click-to-play, artist/album navigation links.

### Not in scope

- Context menus (none exist in any track list yet — a separate cross-cutting feature)
- Grid/card view toggle
- Genre, year, or other advanced filters
- Total song count display
- Persisting sort/search state across navigations

---

## Data Loading

### IncrementalSongCollection

New collection class mirroring `IncrementalAlbumCollection` — an `ObservableCollection<SongInfo>` implementing `ISupportIncrementalLoading`. Constructor takes a `Func<uint, Task<IList<SongInfo>?>>` callback (note: nullable return) and an `int pageSize` parameter (default 100). The page size is stored as a read-only property (`PageSize`) and used by `LoadMoreItemsAsync` (passed to the callback as `count`) and `AddExternalBatch` (for EOF determination).

**Initial state**: `HasMoreItems = false`. The collection does not self-load on construction. The ViewModel must explicitly load the first batch and call `AddExternalBatch`, which sets `HasMoreItems` based on the batch size. Only after this initial population can the ListView trigger `LoadMoreItemsAsync` for subsequent batches.

**Callback return contract**:

| Return value | Meaning | Collection behavior |
|---|---|---|
| Non-null list with items | Normal batch | Add items to self; set `HasMoreItems = (items.Count >= pageSize)` |
| Non-null empty list | End of data | Set `HasMoreItems = false` |
| `null` | Stale/discarded load | **No-op** — do not add items, do not change `HasMoreItems` |

This three-way contract lets the collection distinguish "no more items" from "this result should be ignored," preventing a stale cross-generation load from falsely marking the collection as exhausted.

**Mutation ownership**: the collection owns all mutations to its item list. The ViewModel never directly calls `Add()`, `Clear()`, or `Insert()` on `Tracks`. Instead, the collection exposes purpose-built APIs:

- `Reset()` — clears items and sets `HasMoreItems = false`. The ViewModel must call `AddExternalBatch` after loading the first batch to re-enable incremental loading. Setting `HasMoreItems = false` (not true) is critical: it prevents the ListView from calling `LoadMoreItemsAsync` while the ViewModel's explicit first-batch load is in flight.
- `AddExternalBatch(IList<SongInfo> items)` — appends items and sets `HasMoreItems = (items.Count >= PageSize)`. Used by the ViewModel's explicit first-batch load (search reset and `OnNavigatedTo`). This is the sole transition from `HasMoreItems = false` to `true` outside of `LoadMoreItemsAsync`.
- `ReplaceAll(IList<SongInfo> items)` — clears all items and repopulates from the provided list without changing `HasMoreItems`. Used by the ViewModel after sorting `_allSongs` — the pagination state is unaffected by a reorder.

`LoadMoreItemsAsync` (called by the ListView) adds items internally via the callback return contract. All four mutation paths go through the collection's own methods.

### Load serialization

Same-generation loads are serialized by two mechanisms:

1. **Between explicit first-batch and ListView-driven loads**: The collection starts with `HasMoreItems = false` and `Reset()` also sets it to `false`. While the ViewModel's explicit first-batch load is in flight, the ListView sees `HasMoreItems = false` and does not call `LoadMoreItemsAsync`. Only after `AddExternalBatch` sets `HasMoreItems = true` (if the batch was full) can the ListView trigger incremental loading.

2. **Between consecutive ListView-driven loads**: `ISupportIncrementalLoading` awaits each `LoadMoreItemsAsync` before requesting the next batch.

Therefore, within a single generation, loads are strictly sequential and `_offset` is always consistent. No in-flight flag is needed.

### Backing list synchronization

The ViewModel maintains `List<SongInfo> _allSongs` alongside `Tracks` for play queue construction, sorting, and index lookup.

**Invariant**: `_allSongs` and `Tracks` contain the same `SongInfo` instances in the same order. They are mutated at four points:

1. **Batch loaded (via ListView)** — the callback appends to `_allSongs` before returning items. The collection then adds the same items to `Tracks` via its `LoadMoreItemsAsync` loop.
2. **Batch loaded (explicit first-batch)** — the ViewModel calls the callback directly, then calls `Tracks.AddExternalBatch(items)`. The callback already appended to `_allSongs`.
3. **Search reset** — the ViewModel clears `_allSongs` and calls `Tracks.Reset()`.
4. **Sort applied** — the ViewModel sorts `_allSongs` in place, recomputes position numbers, then calls `Tracks.ReplaceAll(_allSongs)`.

### Stale-result guard

`Search3Async` does not accept a `CancellationToken`, so in-flight HTTP requests cannot be cancelled. When the user types a new search query while a previous fetch is still in flight, the old response could arrive after `_searchQuery` has changed and corrupt `_offset` or append wrong results.

The ViewModel maintains a `_loadGeneration` counter (long). The generation is incremented on **both** search reset and `OnNavigatedFrom`. The load callback captures the current generation before awaiting the API call. After the response returns, it checks whether the captured generation still matches `_loadGeneration` — if not, the result is discarded by returning `null` (which the collection treats as a no-op, preserving `HasMoreItems`). The callback also checks `_navigatedAway` at the same point as a belt-and-suspenders guard.

Incrementing the generation on `OnNavigatedFrom` is critical: if the user navigates away while a load is in flight and then navigates back, `OnNavigatedTo` resets `_navigatedAway = false`, but the old load's captured generation no longer matches (it was incremented during `OnNavigatedFrom`). This prevents the stale response from mutating `_offset`, `_allSongs`, or `Tracks` in the new navigation session.

The generation check handles cross-generation overlap (search reset while old load is in flight). Same-generation serialization is handled structurally by the collection's `HasMoreItems` gating — see Load serialization above.

### Fetch flow

The ViewModel callback (receives `count` from the collection's `PageSize`):

1. Capture `_loadGeneration` as `capturedGeneration`
2. Await `search3(query: _searchQuery, songCount: count, songOffset: _offset, artistCount: 0, albumCount: 0)` inside a try/catch
3. **Post-await guard**: if `_loadGeneration != capturedGeneration` or `_navigatedAway`, return `null` (stale — collection treats as no-op). This guard applies to **both** the success path and the catch path — a stale exception must not surface an error message or compatibility warning for a query the user has already moved on from.
4. Increment `_offset` by the number of songs returned
5. Assign `PositionNumber` to new items (continuing from `_allSongs.Count + 1`)
6. Append items to `_allSongs`
7. Call `_nowPlayingTracker.Refresh()` on the newly appended items — if the currently playing track was not in the initial load but appears in a later batch, this ensures it gets highlighted immediately rather than waiting for the next playback event.
8. Return items (the collection adds them to `Tracks` via `LoadMoreItemsAsync` or `AddExternalBatch`)

**Exception handling**: on catch, check the generation/navigation guard first. If the request is stale, log at Debug and return `null`. If current-generation: set `ErrorMessage` for user display, log at Error, and return an **empty list** (not `null`). Returning an empty list signals EOF to the collection, which sets `HasMoreItems = false` — this stops further load attempts and leaves the collection in a deterministic state. For the initial empty-query fetch specifically, a current-generation exception (vs. an empty result) additionally appends the server-compatibility hint to `ErrorMessage` (e.g., "Failed to load tracks. Your server may not support browsing all tracks."). The compatibility warning is not a separate property — it is part of `ErrorMessage`, so clearing `ErrorMessage` on retry clears both.

**Retry after partial-load failure**: if a batch fails after some data has already been loaded (e.g., batch 3 of N), `HasMoreItems` becomes `false`, `ErrorMessage` is set, and the ViewModel sets `_lastLoadFailed = true`. The loaded data in `_allSongs` and `Tracks` is preserved. The user can retry by: (a) searching then clearing the search (which resets everything including `_lastLoadFailed`), or (b) navigating away and back (see `OnNavigatedTo` below). On a successful load, `_lastLoadFailed` is reset to `false`. Search reset also resets it.

### Song mapping

The `Child → SongInfo` mapping logic is currently duplicated across three ViewModels with slightly different method names:

- `AlbumDetailViewModel.MapSongInfo(Child, ISubsonicClient, string? albumVersion)`
- `PlaylistDetailViewModel.MapSongInfo(Child, ISubsonicClient)`
- `ArtistDetailViewModel.MapChildToSongInfo(Child, ISubsonicClient, string? albumVersion)`

Extract this into a shared static helper (e.g., `SongMapper.MapFromChild`) so the new page and existing pages can reuse it.

Pivotal difference: the album detail and artist detail mappers call `AlbumMapper.StripVersionSuffix` with a known album version string. The shared mapper should accept an optional `albumVersion` parameter (defaulting to null) and only strip the suffix when provided. The all-tracks page passes null since it doesn't have album context.

The `ArtistDetailViewModel` also has call sites that pass album version from a pre-fetched context (`c.AlbumVersion`) — the shared mapper's optional parameter covers this without special-casing.

---

## Sorting

Sorting is a **local reorder** of already-loaded data. It does not reset pagination, re-fetch from the server, or touch the collection's `HasMoreItems` state. This is a separate operation from search, which resets pagination and re-fetches.

### Approach

When the user selects a sort option or toggles direction:

1. Sort `_allSongs` in place using the selected comparer
2. Recompute `PositionNumber` on each item (1-based sequential)
3. Call `Tracks.ReplaceAll(_allSongs)` (clears and repopulates without changing `HasMoreItems`)
4. Call `_nowPlayingTracker.Refresh()` — `ReplaceAll` creates new row bindings that need highlight state

Incremental loading continues from the current `_offset` in server order. When an active sort is selected (anything other than Default), newly loaded batches are re-sorted into the existing data: after appending to `_allSongs`, the ViewModel re-sorts the entire list, recomputes position numbers, and calls `Tracks.ReplaceAll(_allSongs)`. This keeps the displayed order consistent with the sort selector. When Default is selected, new batches simply append at the bottom in server order.

The first sort option is "Default" — this is the initial state with no active sort applied. After sorting by another column then switching back to "Default", the list retains whatever order it was last sorted into (original server order is not tracked). The label and tooltip should avoid implying restoration of server order — use "Default" rather than "Server order" or "Original order".

### Sort options

The ComboBox presents these options:

| Option | Field | Comparer | Default direction |
|--------|-------|----------|-------------------|
| Default | — | No sort applied; items remain in current order | — |
| Title | `SongInfo.Title` | String, ordinal case-insensitive | Ascending |
| Artist | `SongInfo.Artist` | String, ordinal case-insensitive | Ascending |
| Album | `SongInfo.Album` | String, ordinal case-insensitive | Ascending |
| Duration | `SongInfo.Duration` | Numeric | Ascending |
| Year | `SongInfo.Year` | Numeric | Descending |

When the user switches sort field, the direction resets to that field's default. This prevents inheriting a descending direction from Year when switching to Title. Selecting Default disables the direction toggle (direction is meaningless without an active sort).

### UI

Follow the same pattern as `AlbumsPage`: a `ComboBox` for sort field selection and a toggle button for sort direction, placed in a toolbar above the track list.

---

## Search

### Behavior

A `TextBox` in the toolbar area. When the user types and the debounce fires:

1. Increment `_loadGeneration` (any in-flight load from the previous generation will discard its result on return)
2. Clear `ErrorMessage` (a successful retry must not show a stale error from a previous query)
3. Set `_searchQuery` to the entered text
4. Reset `_offset` to 0
5. Clear both `_allSongs` and `Tracks` (via `Reset()`, which also sets `HasMoreItems = false` — the ListView cannot trigger loads during the await below)
6. Reset the active sort to "Default" (see Interaction with sorting)
7. **Explicitly load the first batch** — call the ViewModel's load callback directly (passing `Tracks.PageSize` as count). If the callback returns non-null items, call `Tracks.AddExternalBatch(items)` which adds the items and sets `HasMoreItems` based on batch size, re-enabling ListView-driven loading for subsequent batches.

When the search box is cleared, the same reset flow runs with an empty query, restoring the full track list.

### Debounce

Use the same 300ms debounce + `CancellationTokenSource` pattern from `ShellViewModel`'s search implementation. Each keystroke cancels the previous pending debounce. The `_loadGeneration` guard handles the case where a cancelled debounce's API call was already in flight — the response is discarded.

The search text is trimmed before use. A query that trims to empty is treated as a cleared search (restores the full track list), preventing whitespace-only queries from producing confusing no-result states.

### Interaction with sorting

Search resets the active sort to "Default" (no sort). Rationale: search results arrive in server-relevance order; applying a client-side sort to partial search results produces confusing ranking. The user can re-apply a sort after search results load.

---

## Track List Layout

### Page structure

`TracksPage` must set `NavigationCacheMode="Required"` so the page instance (and its `TracksViewModel`) survives navigation. Without caching, `_allSongs`, loaded batches, scroll position, and sort/search state would be discarded on every navigation away. The `OnNavigatedTo`/`OnNavigatedFrom` lifecycle handles re-subscription and guard resets for the cached instance.

The page layout must ensure the `ListView` owns the scroll viewport — it must **not** be nested inside an outer `ScrollViewer`. Existing detail pages wrap their content in a `ScrollViewer` because they have header sections (album art, metadata) above the track list, and the entire page scrolls as one unit. The all-tracks page has no such header — its toolbar (sort + search) is fixed above the list. Place the `ListView` in a `Grid` row with `Height="*"` so it fills the remaining space and manages its own scrolling and virtualization. Wrapping it in an outer `ScrollViewer` would defeat `ISupportIncrementalLoading` (the ListView would render at its full content height and never trigger incremental loads) and disable UI virtualization for large datasets.

### Columns

Reuse the column layout from `PlaylistDetailPage` and `ArtistDetailPage` (both show tracks outside of album context and include Album + Artist columns):

| Column | Width | Content |
|--------|-------|---------|
| # | 56px | Position number + equalizer bars (now-playing) |
| Title | 2* | Song title, bold when playing |
| Album | * | Album name, clickable link to album detail |
| Artist | * | Artist name(s), clickable links to artist detail |
| Duration | 60px | Formatted time |
| Codec | 50px | File suffix |
| Bitrate | 60px | Bitrate display |
| Play Count | 50px | Play count |
| BPM | 50px | BPM display |
| Favorite | 36px | Star toggle button |

### Position numbering

Use a sequential position number (1-based) reflecting order within `_allSongs`. `SongInfo.PositionNumber` is a plain (non-observable) property, so it must be recomputed **before** clearing and repopulating `Tracks` — the repopulation creates fresh row bindings that read the updated value. Recompute at two points:

1. After sorting `_allSongs` (step 2 of the sort flow)
2. After appending a new batch (assign position numbers to the new items only)

### Click-to-play

Single click on a track row calls `PlayFromTrack`, which builds a `PlaybackTrack` list from `_allSongs` and starts playback at the clicked index. This follows the same pattern as all existing detail pages.

**Pivotal consideration**: the play queue is built from whatever songs are currently loaded. If the user has only loaded 200 of 5000 songs, the queue contains 200 tracks. This matches how the list appears to the user — they can only see and interact with loaded songs.

### Artist and album links

Use the same code-behind RichTextBlock rendering pattern from `PlaylistDetailPage` and `ArtistDetailPage` for clickable artist/album navigation. The handler names in existing pages are:

- `PlaylistDetailPage`: `AlbumLink_DataContextChanged`, `ArtistLinks_DataContextChanged`
- `ArtistDetailPage`: `TopSongAlbumLink_DataContextChanged`, `TopSongArtistLinks_DataContextChanged`

The TracksPage will define its own handlers (the upcoming 3.6 Unified TrackListControl will consolidate these).

---

## ViewModel

`TracksViewModel` extends `ObservableRecipient`, implements `INavigationAware`.

### Dependencies

- `ISubsonicClientService` — API access
- `IPlaybackService` — play queue control + now-playing tracking
- `ILoggingService` — logging

### Key state

- `IncrementalSongCollection Tracks` — UI-bound collection
- `List<SongInfo> _allSongs` — backing list (synchronized with `Tracks`, see Data Loading section)
- `NowPlayingTracker` — reuse existing helper for now-playing highlight
- `long _loadGeneration` — stale-result guard (incremented on search reset **and** `OnNavigatedFrom`)
- `bool _navigatedAway` — set in `OnNavigatedFrom`, checked in post-await guard alongside generation
- `bool _lastLoadFailed` — set on current-generation exception, cleared on successful load or search reset
- Sort state: selected sort index, sort direction
- Search state: search text, debounce CTS

### Navigation lifecycle

- `OnNavigatedTo`: reset `_navigatedAway = false` (required for back-navigation or ViewModel reuse). Clear `ErrorMessage`. Then:
  - If the collection is **empty**: call the load callback directly (passing `Tracks.PageSize` as count) and pass results to `Tracks.AddExternalBatch(items)`. The collection starts with `HasMoreItems = false`, so the ListView cannot race the initial load. `AddExternalBatch` sets `HasMoreItems` based on batch size, enabling subsequent ListView-driven loading.
  - If `_lastLoadFailed` is **true** (partial-load failure recovery): clear `_lastLoadFailed`, call the load callback at the current `_offset`, and pass results to `Tracks.AddExternalBatch(items)` to re-enable incremental loading from where it left off. Loaded data is preserved.
  - **In all cases** where `_allSongs` has items: re-subscribe the `NowPlayingTracker` and call `Refresh(force: true)` — `OnNavigatedFrom` unsubscribes it, so a back-navigation with already-loaded data must re-subscribe to resume highlight updates.
- `OnNavigatedFrom`: increment `_loadGeneration` (invalidates any in-flight loads from this navigation session), unsubscribe `NowPlayingTracker`, set `_navigatedAway = true`

### Favorite toggle

Same optimistic toggle pattern as existing pages: flip `IsStarred` immediately, call `StarAsync`/`UnstarAsync`, revert on failure.

### BuildPlaybackTracks

Same mapping pattern as existing pages: convert `_allSongs` to `PlaybackTrack` list with `FlatArtistText`, `StreamUri`, `CoverArtUri`, etc. This is another piece of duplicated logic that the 3.6 refactor will consolidate.

---

## Navigation Registration

Standard 5-step registration:

1. **App.xaml.cs** — register `TracksViewModel` (transient) and `TracksPage` (transient)
2. **PageService** — add `Configure<TracksViewModel, TracksPage>()`
3. **TopLevelPages** — add `typeof(TracksViewModel).FullName!` to all three HashSets (`ShellViewModel`, `DefaultActivationHandler`, `ConnectViewModel`)
4. **ShellPage.xaml** — add NavigationViewItem between Artists and Playlists with glyph `` (MusicNote) and `x:Uid="Shell_Tracks"`
5. **Resources.resw** — add `Shell_Tracks.Content` = `"Tracks"`

---

## Edge Cases

| Scenario | Behavior |
|----------|----------|
| Empty library | Show "No tracks found" empty-state visual (not `ErrorMessage`) |
| Server rejects empty query with error | Set `ErrorMessage`: "Failed to load tracks. Your server may not support browsing all tracks." |
| Search with no results | Show "no results" message, clear on search box clear |
| Sort with partially loaded data | Sorts within loaded items; new batches re-sorted into existing data when an active sort is selected |
| Navigate away during load | `OnNavigatedFrom` increments `_loadGeneration` and sets `_navigatedAway = true`; in-flight load's captured generation no longer matches, so response is discarded even if user navigates back (which resets `_navigatedAway`) |
| Now-playing track not yet loaded | No highlight visible until the track's batch loads; `Refresh()` called after each batch append ensures immediate highlight |
| Now-playing track loaded via later batch | `_nowPlayingTracker.Refresh()` called after appending to `_allSongs` — highlight appears without waiting for a playback event |
| Same song appears multiple times | All instances with matching ID get highlighted — correct |
| Server returns duplicate songs across batches | Possible with search3 — accept duplicates for MVP |
| Stale API response after search change | `_loadGeneration` guard causes callback to return `null`; collection treats as no-op (no items added, `HasMoreItems` unchanged) |
| Stale API exception after search change | Generation guard checked in catch path — stale exception logged at Debug, not surfaced to user |
| Partial-load failure (batch N fails) | `ErrorMessage` set, `HasMoreItems = false`, loaded data preserved; re-navigation detects error state and retries from current `_offset` |
| Rapid search typing | Debounce CTS cancels pending delays; generation guard discards in-flight responses |
| Search box cleared during in-flight search | Generation increments, old response discarded, empty-query fetch begins |
| Search reset while old load is in flight | No blocking — generation increment causes old load to discard on return; first-batch load proceeds immediately |

---

## Files to Create

| File | Purpose |
|------|---------|
| `Hypersonic/Collections/IncrementalSongCollection.cs` | `ISupportIncrementalLoading` for `SongInfo` |
| `Hypersonic/ViewModels/TracksViewModel.cs` | Page ViewModel |
| `Hypersonic/Views/TracksPage.xaml` + `.xaml.cs` | Page view |
| `Hypersonic/Helpers/SongMapper.cs` | Shared `Child → SongInfo` mapping |

## Files to Modify

| File | Change |
|------|--------|
| `Hypersonic.Client/Models/Requests/Search3Request.cs` | Fix query encoding: `Uri.EscapeDataString(this.Query)` |
| `Hypersonic/App.xaml.cs` | Register TracksViewModel + TracksPage |
| `Hypersonic/Services/PageService.cs` | Add page mapping |
| `Hypersonic/ViewModels/ShellViewModel.cs` | Add to TopLevelPages |
| `Hypersonic/ViewModels/ConnectViewModel.cs` | Add to TopLevelPages |
| `Hypersonic/Activation/DefaultActivationHandler.cs` | Add to TopLevelPages |
| `Hypersonic/Views/ShellPage.xaml` | Add sidebar nav item |
| `Hypersonic/Strings/en-us/Resources.resw` | Add `Shell_Tracks.Content` |
| `Hypersonic/ViewModels/AlbumDetailViewModel.cs` | Replace private `MapSongInfo` with shared `SongMapper` |
| `Hypersonic/ViewModels/PlaylistDetailViewModel.cs` | Replace private `MapSongInfo` with shared `SongMapper` |
| `Hypersonic/ViewModels/ArtistDetailViewModel.cs` | Replace private `MapChildToSongInfo` with shared `SongMapper` |

## Test Coverage

### Automated tests

| Area | Project | Tests |
|------|---------|-------|
| **SongMapper** | `Hypersonic.Tests` | Mapping from `Child` to `SongInfo` preserves all fields; null/empty artist handling; album version stripping applies only when provided; null album version passes through raw album name |
| **Search3Request encoding** | `Hypersonic.Client.UnitTest` | Query with spaces, `&`, `#`, unicode produces correctly escaped query string |
| **IncrementalSongCollection** | `Hypersonic.Tests` | `null` callback return is no-op (no items added, `HasMoreItems` unchanged); empty list sets `HasMoreItems = false`; starts with `HasMoreItems = false`; `Reset()` clears items and sets `HasMoreItems = false`; `AddExternalBatch` with full page sets `HasMoreItems = true`; `AddExternalBatch` with short page sets `HasMoreItems = false`; `AddExternalBatch` with empty list sets `HasMoreItems = false`; `ReplaceAll` preserves `HasMoreItems`; ListView cannot trigger `LoadMoreItemsAsync` between `Reset()` and `AddExternalBatch` |
| **Stale-result guard** | `Hypersonic.Tests` | Load callback returns `null` when `_loadGeneration` has changed; does not mutate `_offset` or `_allSongs`; stale exceptions are not surfaced to user; `OnNavigatedFrom` increments generation so pre-navigation loads are stale even after back-navigation |
| **Current-generation errors** | `Hypersonic.Tests` | Current-generation exception sets `ErrorMessage`, returns empty list, collection sets `HasMoreItems = false`; initial empty-query exception includes compatibility hint; partial-load failure preserves loaded data; re-navigation clears error and re-enables loading via `AddExternalBatch` |
| **Navigation guard** | `Hypersonic.Tests` | Load callback returns `null` when `_navigatedAway` is true, even if generation matches; `OnNavigatedTo` resets `_navigatedAway` so subsequent loads succeed; navigate away + back: old load discarded via generation, new load succeeds |
| **NowPlayingTracker refresh** | `Hypersonic.Tests` | Tracker refreshed after batch append — track loaded in later batch gets highlighted; tracker refreshed after `ReplaceAll` (sort); tracker refreshed after search population |
| **Search reset during load** | `Hypersonic.Tests` | Search reset increments generation; explicit first-batch load proceeds via `AddExternalBatch`; old in-flight load returns `null` on completion |
| **Sort + position update** | `Hypersonic.Tests` | After sort, `_allSongs` order matches comparer, `PositionNumber` values are sequential 1..N, `Tracks` order matches `_allSongs`; new batch after sort is re-sorted into existing data; switching sort field resets direction to field default |
| **Search reset** | `Hypersonic.Tests` | Search clears both collections, resets offset to 0, increments generation, loads first batch explicitly, resets sort to Default; whitespace-only query treated as clear |

### Manual smoke tests

1. Browse: scroll through track list, verify batches load as scrolling continues
2. Sort: apply each sort option, verify order, scroll to load more, verify new items at bottom
3. Search: type a query, verify results replace list, clear search, verify full list returns
4. Play: click a track, verify queue contains loaded tracks starting from clicked index
5. Favorite: toggle star, verify persistence after navigating away and back
6. Navigation: click album/artist links, verify correct detail pages open
7. Now-playing: play a track, navigate to Tracks page, verify highlight appears

## Implementation Sequence

1. Fix `Search3Request` query encoding, add encoding test
2. Extract shared `SongMapper` from existing ViewModels, update all three callers, verify build
3. Create `IncrementalSongCollection`
4. Create `TracksViewModel` with loading (including stale-result guard), sort, search, favorite toggle, now-playing tracking
5. Create `TracksPage` XAML + code-behind
6. Navigation registration (App.xaml.cs, PageService, TopLevelPages ×3, ShellPage, Resources.resw)
7. Automated tests for all areas in the test table: SongMapper, Search3Request encoding, IncrementalSongCollection, stale-result guard, navigation guard, search reset, sort + position
8. Manual smoke tests
