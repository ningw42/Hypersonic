# Hypersonic MVP Roadmap

> **Goal**: Ship a polished Subsonic desktop music player with complete playback controls, session persistence, and a rich listening experience. The MVP remains fully functional online-only; disk-backed image and lyrics caches have since shipped, while metadata and audio caching remain stretch goals.
>
> **Status basis**: Reviewed against `master` at `20d693a` on 2026-07-18. An item is marked complete only when its implementation is present in source, with tests or implementation notes where available.

| Item | Size | Status |
|------|------|--------|
| **Phase 1 — Core Playback Polish** | | |
| 1.1 Shuffle & Repeat UI | S | ✅ Complete |
| 1.2 Persistence (Credentials, Settings & State) | M | ✅ Complete |
| **Phase 2 — Experience Essentials** | | |
| 2.1 Home Page | M | ✅ Complete |
| 2.2 Now Playing / Full-Window Player | L | ✅ Complete |
| 2.3 All Tracks Page | M | ✅ Complete |
| **Phase 3 — Ecosystem & Polish** | | |
| 3.1 Scrobbling | S | ✅ Complete |
| 3.2 CoverflowControl for Home Carousel | M | ✅ Complete |
| 3.3 Carousel Scrollbar Polish | S | ✅ Complete |
| 3.4 Now-Playing Track Highlight | S | ✅ Complete |
| 3.5 Clean Album Version Display | S | ✅ Complete |
| 3.6 Unified TrackListControl | M | 🔲 Not started |
| 3.7 Cross-Surface Favorites | M | ✅ Complete |
| **── MVP ship line ──** | | |
| **Phase 4 — Post-MVP Enhancements** | | |
| 4.1 Playlist Management UI | M | 🔲 Not started |
| 4.2 Keyboard Shortcuts | S | 🔲 Not started |
| 4.3 Playback Signal Pipeline | M | 🔲 Not started |
| 4.4 Position-Aware Navigation History | S | 🔲 Not started |
| 4.5 Synced Lyrics & Provider Pipeline | XL | ✅ Complete |
| 4.6 Lyrics Renderer GPU Optimization (deep pass) | M | ⏸ Deferred — profile first |
| **Phase 5 — Data Layer (Stretch)** | | |
| 5.0 Generic SQLite Cache Foundation | L | ✅ Complete |
| 5.1 SQLite Metadata Cache | XL | 🔲 Not started |
| 5.2 Image Cache | L | ✅ Complete |
| 5.3 Audio Cache | XL | 🔲 Not started |

---

## Phase 1 — Core Playback Polish

### 1.1 Shuffle & Repeat UI `[S]`

Shuffle and repeat controls are available in both the player bar and queue flyout. `PlayerViewModel` mirrors `PlaybackService` state and exposes the toggle/cycle commands; `PlaybackQueue` owns the underlying modes.

**Evidence**: [`docs/implementation/ui/2026-04-08-shuffle-repeat-ui.md`](docs/implementation/ui/2026-04-08-shuffle-repeat-ui.md), `Hypersonic/ViewModels/PlayerViewModel.cs`, `Hypersonic/Views/ShellPage.xaml`.

### 1.2 Persistence — Credentials, Settings & Player State `[M]`

Three distinct persistence categories:

**Credentials** — Passwords stored in Windows Credential Manager via `PasswordVault`, encrypted by the OS. Non-secret connection metadata (server URL, username) stored in settings. Multi-server data model (one active server at a time, quick-switch UI deferred).

**Settings** — Extended `ILocalSettingsService` with `RemoveSettingAsync`. Theme, backdrop, server accounts, and active account stored as key-value pairs.

**State** — `AppStateService` persists the player session (volume, mute, shuffle, repeat, window geometry, last-viewed page) to `AppState.json`. Server-tied state (queue, current track, position) is deferred to a future design.

Serialization migrated from Newtonsoft.Json to System.Text.Json.

**Evidence**: [`docs/implementation/service/2026-04-09-persistence.md`](docs/implementation/service/2026-04-09-persistence.md), `Hypersonic/Services/CredentialService.cs`, `Hypersonic/Services/AppStateService.cs`.

**Depends on**: 1.1

---

## Phase 2 — Experience Essentials

### 2.1 Home Page `[M]`

The Home page ships curated sections for recently played, recently released, recently added, most played, and random albums, with loading/error/empty states and shared album mapping. Random picks are stable across navigation for a bounded cache window; their hero presentation now uses 3.2's coverflow.

**Evidence**: [`docs/implementation/ui/2026-04-21-home-page.md`](docs/implementation/ui/2026-04-21-home-page.md), [`docs/implementation/ui/2026-05-20-stable-random-picks.md`](docs/implementation/ui/2026-05-20-stable-random-picks.md), `Hypersonic/ViewModels/MainViewModel.cs`.

### 2.2 Now Playing / Full-Window Player `[L]`

A dedicated in-window page ships with responsive album art and metadata plus Lyrics and Queue tabs. It reuses the singleton `PlayerViewModel`, supports positional queue jumps and auto-scroll, preserves navigation history when entered from the player bar, and exposes favorite actions. The original plain-text lyrics surface has been replaced by the synced renderer described in 4.5.

**Evidence**: [`docs/implementation/ui/2026-04-30-now-playing-page.md`](docs/implementation/ui/2026-04-30-now-playing-page.md), `Hypersonic/Views/NowPlayingPage.xaml`, `Hypersonic/ViewModels/NowPlayingViewModel.cs`.

**Depends on**: 1.1

### 2.3 All Tracks Page `[M]`

A browsable Tracks page lists server songs through paginated `search3` calls (the Subsonic API has no dedicated "get all songs" endpoint). It includes:

- **Incremental loading** through `IncrementalSongCollection`, with stale-generation guards and retry handling.
- **Client-side sorting** of loaded results by title, artist, album, duration, or year.
- **Debounced server search** through `search3`, replacing the current result generation safely.
- **Track-list integration** for now-playing highlight, favorite, play, and queue actions.

The page is registered in navigation between Artists and Playlists.

**Evidence**: [`docs/implementation/ui/2026-04-26-all-tracks-page.md`](docs/implementation/ui/2026-04-26-all-tracks-page.md), `Hypersonic/ViewModels/TracksViewModel.cs`, `Hypersonic/Collections/IncrementalSongCollection.cs`.

---

## Phase 3 — Ecosystem & Polish

### 3.1 Scrobbling `[S]`

Playback is reported to the Subsonic server for listening-history and server-side scrobble integrations. Settings expose enable/disable, now-playing reports, time and percentage thresholds, and OR/AND threshold semantics; deterministic tests cover timing, seeks, pauses, track changes, and failures.

**Evidence**: [`docs/implementation/service/2026-04-25-scrobbling.md`](docs/implementation/service/2026-04-25-scrobbling.md), `Hypersonic/Services/ScrobblingService.cs`, `Hypersonic.Tests/Services/ScrobblingServiceTests.cs`.

**Depends on**: 1.2

### 3.2 CoverflowControl for Home Page Carousel `[M]`

The reusable `CoverflowControl` now presents Random Picks as the Home page hero. It uses a templated `ItemsRepeater`, projected depth/rotation/scale transforms, centered selection, recycling-safe visual reset, pointer and keyboard activation, and incremental-source observation. Selected album metadata renders below the hero and invocation reuses album-detail navigation.

**Evidence**: [`docs/implementation/ui/2026-05-07-coverflow-control.md`](docs/implementation/ui/2026-05-07-coverflow-control.md), `Hypersonic/Controls/CoverflowControl.cs`, `Hypersonic/Views/MainPage.xaml`.

**Depends on**: 2.1

### 3.3 Carousel Scrollbar Polish `[S]`

The shipped behavior deliberately differs from the original horizontal-wheel proposal. `CarouselPanel` hides the horizontal scrollbar, disables horizontal wheel/trackpad scrolling so the surrounding page keeps its vertical wheel behavior, and provides always-discoverable flanking arrow buttons with edge-aware enable/opacity state. Detail/list pages also add right-side padding so content does not crowd the vertical scrollbar.

**Evidence**: [`docs/implementation/ui/2026-04-23-scrollbar-ux.md`](docs/implementation/ui/2026-04-23-scrollbar-ux.md), `Hypersonic/Controls/CarouselPanel.cs`, `Hypersonic/Themes/Generic.xaml`.

**Depends on**: 2.1

### 3.4 Now-Playing Track Highlight in Track Lists `[S]`

Album, artist, playlist, and all-tracks lists reactively mark every matching song with the shared `NowPlayingTracker` and `SongInfo.IsNowPlaying`; the rows render an animated `EqualizerBars` indicator. The tracker marshals playback changes onto the UI thread and is covered through a synchronous test seam while the WinUI test-host issue remains open.

**Evidence**: [`docs/implementation/ui/2026-04-24-now-playing-track-highlight.md`](docs/implementation/ui/2026-04-24-now-playing-track-highlight.md), `Hypersonic/Helpers/NowPlayingTracker.cs`, `Hypersonic/Controls/EqualizerBars.cs`.

### 3.5 Clean Album Version Display `[S]`

`AlbumID3.version` is deserialized and propagated through the mapping layer. Compact surfaces show a clean album name, while album detail shows the version separately; search retains version text where it is needed to distinguish otherwise-identical results. Client and app tests cover deserialization and suffix splitting.

**Evidence**: [`docs/implementation/ui/2026-04-24-album-version-display.md`](docs/implementation/ui/2026-04-24-album-version-display.md), `Hypersonic.Client/Models/Entities/AlbumID3.cs`, `Hypersonic/Helpers/AlbumMapper.cs`.

### 3.6 Unified TrackListControl `[M]`

Extract a reusable `TrackListControl` from the duplicated track list XAML across AlbumDetailPage, ArtistDetailPage, PlaylistDetailPage, and AllTracksPage. The control should expose configuration properties for column visibility (e.g., `ShowAlbumColumn`, `ShowDiscHeaders`, `ShowMetadataColumns`), bindable commands (`ItemClickCommand`, `FavoriteCommand`), and an `ItemsSource` accepting `SongInfo` items optionally mixed with `DiscHeaderItem` for disc grouping. Consolidate the duplicated artist/album link rendering code-behind into the control. The queue popup stays separate — its compact layout and different data model (`QueueItem`) don't justify the added abstraction.

**Depends on**: 2.3

### 3.7 Cross-Surface Favorites `[M]`

`IFavoritesService` is the single writer for optimistic star/unstar state, reconciles against `getStarred2`, and broadcasts `FavoritesChangedMessage` updates to the player, queue, all-tracks, album, artist, and playlist surfaces. Favorite hearts are available in track lists, queue rows, the player bar, and Now Playing; refreshes reconcile server-side changes without stale observations overwriting newer local intent.

**Evidence**: [`docs/implementation/service/2026-06-07-favorite-sync.md`](docs/implementation/service/2026-06-07-favorite-sync.md), `Hypersonic/Services/FavoritesService.cs`, `Hypersonic.Tests/Services/FavoritesServiceTests.cs`, `Hypersonic.Tests/ViewModels/FavoritesCrossVmSyncTests.cs`.

---

## Phase 4 — Post-MVP Enhancements

### 4.1 Playlist Management UI `[M]`

The API client already supports create/update/delete — add the UI: new playlist dialog, edit/delete on playlist detail, "Add to Playlist" context menus on track lists across the app, and remove-from-playlist on playlist detail.

### 4.2 Keyboard Shortcuts `[S]`

Global keyboard accelerators: Space (play/pause), Ctrl+arrows (next/prev/volume), Ctrl+M (mute), Ctrl+S (shuffle), Ctrl+R (repeat), Ctrl+Q (queue flyout). Ctrl+K for search focus already exists.

**Depends on**: 1.1

### 4.3 Playback Signal Pipeline `[M]`

Refactor playback control into an explicit dispatcher-owned signal pipeline or actor. Every playback input — UI commands, taskbar/SMTC commands, and `MediaPlayer` callbacks — should be represented as a typed signal and processed through one serialized path before it can mutate `PlaybackQueue` or player state. Media callbacks should continue to carry a source generation/token so stale events from old `MediaPlaybackItem` instances are rejected before queue navigation.

**Existing groundwork**: `PlaybackService` already owns the UI dispatcher, source-generation guards reject stale media callbacks, and consumers can depend on `IReadOnlyPlaybackState`; the typed, single-path dispatcher/actor itself has not started (`Hypersonic/Services/PlaybackService.cs`, `Hypersonic/Services/PlaybackMediaEventGuard.cs`, `Hypersonic.Core/Contracts/Services/IReadOnlyPlaybackState.cs`).

### 4.4 Position-Aware Navigation History `[S]`

`PlaybackQueue._navigationHistory` currently stores a `Stack<string>` of track IDs, and `GoBack()` resolves via `FindIndex(t => t.Id == previousTrackId)`. After a position-aware queue jump on a queue containing duplicate track IDs (introduced by 2.2 Now Playing), Previous can land on the wrong occurrence — it always finds the *first* occurrence by ID, not the position the user actually came from.

Change the navigation-history representation to capture both the track ID and the queue position at push time (e.g. `Stack<(int occurrenceIndex, string trackId)>` or an equivalent), so `GoBack()` can return to the exact position. A safe fallback is required for queue mutations that invalidate the stored position — fall back to the existing `FindIndex(byId)` lookup when the recorded position no longer holds the same track.

Add `PlaybackQueue` tests covering the duplicate-Previous scenario and the queue-mutation fallback path.

**Depends on**: 2.2 (introduces the user-visible duplicate-jump path that surfaces this limitation)

### 4.5 Synced Lyrics & Provider Pipeline `[XL]`

The delivered lyrics system substantially exceeds the original line-sync scope:

- `LyricsDocument` represents plain, line-synced, and word-synced content with source/format provenance.
- `LyricsCanvasControl` highlights and auto-scrolls the active line, animates word-level karaoke wipes where timing exists, renders plain lyrics statically, and pauses its Win2D loop when hidden or settled.
- `LyricsService` performs budgeted provider fan-out with durable positive/negative SQLite caching, local-file overrides, refresh, and fallback policy. Registered providers are Subsonic, LrcLib, AMLL TTML DB, QQ Music, Netease, and Kugou.
- Settings allow providers to be enabled, reordered, and assigned automatic/manual request budgets; Now Playing exposes refresh and source provenance.
- A shared `PlaybackClock` supplies smooth progress to both the lyrics renderer and player bar through read-only playback state.

The original click-a-line-to-seek idea was optional and is not part of the shipped canvas.

**Evidence**: `Hypersonic.Core/Models/Lyrics/`, `Hypersonic/Services/LyricsService.cs`, `Hypersonic/Controls/Lyrics/LyricsCanvasControl.xaml.cs`, `Hypersonic/App.xaml.cs`, `Hypersonic.Tests/Services/Lyrics/`, [`docs/implementation/service/2026-07-05-lyrics-provider-settings.md`](docs/implementation/service/2026-07-05-lyrics-provider-settings.md), [`docs/implementation/ui/2026-07-12-lyrics-provider-settings-tokenizing-box.md`](docs/implementation/ui/2026-07-12-lyrics-provider-settings-tokenizing-box.md).

**Depends on**: 2.2

### 4.6 Lyrics Renderer GPU Optimization — deep pass `[M]`

The Win2D lyrics renderer (`LyricsCanvasControl` / `LyricsSceneRenderer`) shipped with two GPU-cost fixes already landed: a **pause controller** that stops the `CanvasAnimatedControl` game loop when the surface is hidden (Queue tab / minimised / no lyrics) or idle (paused playback, scene settled), and a **per-frame cost reduction** (folding line opacity into glyph-colour alpha to drop the per-line `CreateLayer`, plus reusing the wipe's gradient-stop array). Together these took active-render GPU from ~10–20% down to ~2% on an RTX 4090 — already below the BetterLyrics reference (~3.5%).

This item is the **remaining, deeper pass**, only worth doing if profiling on lower-end hardware shows the active-playback frame is still too heavy:

- **Cache the neighbour gaussian blur.** Inactive lines currently allocate a `CanvasCommandList` + `GaussianBlurEffect` and run a multi-pass blur every frame. Bake each line's blurred text into a line-local `CanvasRenderTarget`, rebuilt only when the blur amount/colour changes, and draw it per frame with a translate offset + opacity (`DrawImage`'s opacity overload). Requires plumbing the control DPI into the renderer and careful render-target sizing/positioning — validate visually.
- **Glyph-mask wipe (BetterLyrics-grade).** Render each line's glyphs to a cached `CanvasCommandList` mask once at layout time, then composite colour per frame via an `AlphaMaskEffect` fed by a per-row gradient rectangle — instead of re-running `DrawTextLayout` and pushing a clip layer per row on the active line. Removes the last per-frame text rasterisation and the active-line row layers.

**Status**: Deferred unless profiling on representative lower-end hardware justifies the complexity and visual-risk budget.

Reference techniques live in `../BetterLyrics` (GPL — techniques only, no source). Context: [`docs/design/ui/2026-06-13-lyrics-canvas-control.md`](docs/design/ui/2026-06-13-lyrics-canvas-control.md) §7 and the current hot paths in `Hypersonic/Controls/Lyrics/LyricsSceneRenderer.cs`.

---

## Phase 5 — Data Layer (Stretch)

> Phase 5 is a stretch goal. It's planned here so architecture decisions in earlier phases don't paint us into a corner.

Metadata and audio caching remain **cross-cutting changes** — browsing ViewModels still call the Subsonic API client directly, and playback streams directly from server URIs. The generic storage mechanics needed by future cache types are now shared rather than waiting on 5.1.

### 5.0 Generic SQLite Cache Foundation `[L]`

`SqliteCacheStoreBase<TEntry>` now centralizes SQLite schema lifecycle, self-healing, serialized access, atomic payload writes, size-based eviction, reconciliation, temporary-file cleanup, and payload-folder migration. Image and lyrics caches are concrete subclasses, so metadata and audio caches can add entity-specific schema and policy without rebuilding the storage machinery.

**Evidence**: [`docs/implementation/service/2026-06-02-generic-cache-store.md`](docs/implementation/service/2026-06-02-generic-cache-store.md), [`docs/implementation/service/2026-06-05-cache-folder-migration.md`](docs/implementation/service/2026-06-05-cache-folder-migration.md), `Hypersonic/Services/Cache/SqliteCacheStoreBase.cs`, `Hypersonic.Tests/Services/Cache/SqliteCacheStoreBaseTests.cs`.

### 5.1 SQLite Metadata Cache `[XL]`

Local SQLite database as a read-through cache for artists, albums, songs, and playlists. Configurable TTL-based staleness, background incremental sync on app launch.

No metadata store/service is present yet; browsing ViewModels still consume `ISubsonicClient` directly.

**Depends on**: 5.0

### 5.2 Image Cache `[L]`

Cover art is cached on disk with server/account-scoped keys, TTL and stale fallback, in-flight request deduplication, LRU eviction, reconciliation, and Settings-page stats/clear controls. `CachedImage` consumers resolve through `IImageService` rather than binding directly to remote cover-art URIs.

**Evidence**: `Hypersonic/Services/CachedImageService.cs`, `Hypersonic/Services/SqliteImageCacheStore.cs`, `Hypersonic.Tests/Services/ImageCacheServiceTests.cs`, `Hypersonic.Tests/Services/SqliteImageCacheStoreTests.cs`.

**Depends on**: 5.0

### 5.3 Audio Cache `[XL]`

Two strategies: automatic stream-through caching (played tracks saved to disk transparently) and explicit user-initiated downloads (pinned, excluded from LRU eviction). Pre-caches the next 1–2 tracks in the queue to reduce gaps between tracks. Cache management UI in Settings.

**Depends on**: 5.0

---

## Dependency Graph

```
Phase 1                Phase 2              Phase 3
─────────────────────────────────────────────────────

1.1 Shuffle/Repeat ─┬─▶ 1.2 Persistence ──▶ 3.1 Scrobble
                    │
                    └──▶ 2.2 Now Playing    2.1 Home ──┬──▶ 3.2 Coverflow
                                                       └──▶ 3.3 Scrollbar Polish
                    2.3 All Tracks ──▶ 3.6 Unified TrackListControl
                                            3.4 Track Highlight ── (standalone)
                                            3.5 Album Version ─── (standalone)
                                            3.7 Favorites ─────── (standalone)

                         ── MVP ship line ──

Phase 4                Phase 5
──────────────────────────────────

4.1 Playlists ─── (standalone)    5.0 Cache Foundation ─┬──▶ 5.1 Metadata Cache
                                                        ├──▶ 5.2 Image Cache
                                                        └──▶ 5.3 Audio Cache
1.1 ──▶ 4.2 Shortcuts
4.3 Playback Signal Pipeline ─── (standalone)
2.2 ──▶ 4.4 Position-Aware Nav History
2.2 ──▶ 4.5 Time-Synced Lyrics
4.5 ──▶ 4.6 Lyrics GPU Deep Pass (profiling-gated)
```

## Recommended Remaining Implementation Order

| # | Item | Size | Depends on |
|---|------|------|------------|
| 1 | 3.6 Unified TrackListControl | M | 2.3 |
| | **── MVP ship line ──** | | |
| 2 | 4.1 Playlist Management UI | M | — (benefits from 3.6) |
| 3 | 4.2 Keyboard Shortcuts | S | 1.1 |
| 4 | 4.3 Playback Signal Pipeline | M | — |
| 5 | 4.4 Position-Aware Navigation History | S | 2.2 |
| 6 | 5.1 SQLite Metadata Cache | XL | 5.0 |
| 7 | 5.3 Audio Cache | XL | 5.0 |
| 8 | 4.6 Lyrics Renderer GPU Optimization | M | 4.5 + profiling evidence |

**Size key**: S = a few hours · M = 1–2 days · L = 3–5 days · XL = 1+ weeks

---

## Out of Scope (Post-MVP)

- Segmented clickable artist links in the queue flyout (behind a user setting for track list interactivity level — the compact 40px rows make per-artist click targets impractical by default)
- User-configurable track list columns
- Equalizer / audio effects
- Multi-server support
- Podcast, Sharing, Bookmark support (API stubs exist, no implementation)
- Music folder browsing
- CI/CD pipeline
- Localization beyond en-us

---

## Known Issues

- **WinUI test host crash with `[UITestMethod]`**: The MSTest WinUI test host (`Hypersonic.Tests`) crashes before `[UITestMethod]` tests execute, preventing tests that require a DispatcherQueue or UI thread context. Affected areas use internal test-only code paths as workarounds: `NowPlayingTracker` has a `synchronous: true` constructor, and `IncrementalSongCollection` exposes `LoadBatchCoreAsync()` to bypass `AsyncInfo.Run`. Revisit when the upstream test host issue is resolved.

- **Frozen lyrics in the minimized taskbar thumbnail**: The lyrics canvas pauses its render loop when the window is minimized (the pause controller, see 4.6), so the swap chain stops presenting. The taskbar thumbnail / Aero Peek is composited by DWM from the window's last presented frame, so the lyric wipe appears frozen in that preview while minimized. This is a deliberate trade-off — a static thumbnail for ~0% GPU instead of rendering a window nobody is looking at (BetterLyrics keeps its peek live by never pausing, paying ~3% GPU constantly). Purely cosmetic: the restored window always resumes from the correct position. If a live peek is ever wanted, throttle `CanvasAnimatedControl.TargetElapsedTime` to ~1–2 fps while minimized instead of pausing outright.
