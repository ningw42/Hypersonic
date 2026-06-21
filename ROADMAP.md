# Hypersonic MVP Roadmap

> **Goal**: Ship a polished Subsonic desktop music player with complete playback controls, session persistence, and a rich listening experience. Offline caching is a stretch goal — the MVP is fully functional online-only; the data layer is planned here so the architecture accounts for it from the start.

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
| 3.2 CoverflowControl for Home Carousel | M | 🔲 Not started |
| 3.3 Carousel Scrollbar Polish | S | ✅ Complete |
| 3.4 Now-Playing Track Highlight | S | ✅ Complete |
| 3.5 Clean Album Version Display | S | ✅ Complete |
| 3.6 Unified TrackListControl | M | 🔲 Not started |
| **── MVP ship line ──** | | |
| **Phase 4 — Post-MVP Enhancements** | | |
| 4.1 Playlist Management UI | M | 🔲 Not started |
| 4.2 Keyboard Shortcuts | S | 🔲 Not started |
| 4.3 Playback Signal Pipeline | M | 🔲 Not started |
| 4.4 Position-Aware Navigation History | S | 🔲 Not started |
| 4.5 Time-Synced Lyrics | M | 🔲 Not started |
| 4.6 Lyrics Renderer GPU Optimization (deep pass) | M | 🔲 Not started |
| **Phase 5 — Data Layer (Stretch)** | | |
| 5.1 SQLite Metadata Cache | XL | 🔲 Not started |
| 5.2 Image Cache | L | ✅ Complete |
| 5.3 Audio Cache | XL | 🔲 Not started |

---

## Phase 1 — Core Playback Polish

### 1.1 Shuffle & Repeat UI `[S]`

The shuffle and repeat logic is fully implemented in the service layer (`PlaybackQueue`, `PlaybackService`). The gap is purely UI — add shuffle and repeat toggle buttons to the player bar, backed by ViewModel properties and commands.

### 1.2 Persistence — Credentials, Settings & Player State `[M]`

Three distinct persistence categories:

**Credentials** — Passwords stored in Windows Credential Manager via `PasswordVault`, encrypted by the OS. Non-secret connection metadata (server URL, username) stored in settings. Multi-server data model (one active server at a time, quick-switch UI deferred).

**Settings** — Extended `ILocalSettingsService` with `RemoveSettingAsync`. Theme, backdrop, server accounts, and active account stored as key-value pairs.

**State** — `AppStateService` persists the player session (volume, mute, shuffle, repeat, window geometry, last-viewed page) to `AppState.json`. Server-tied state (queue, current track, position) is deferred to a future design.

Serialization migrated from Newtonsoft.Json to System.Text.Json.

**Depends on**: 1.1

---

## Phase 2 — Experience Essentials

### 2.1 Home Page `[M]`

The home page is currently an empty stub. Fill it with curated horizontal carousels: recently played, recently added, most played, and random picks. Reuse existing album card patterns.

### 2.2 Now Playing / Full-Window Player `[L]`

A dedicated full-content page (not a separate window) with three regions: large album art + metadata, lyrics, and an embedded queue list. Accessible by clicking the album art or track title in the player bar.

The lyrics engine fetches structured lyrics from the OpenSubsonic API (`GetLyricsBySongIdAsync`) and falls back to the legacy `getLyrics` endpoint for servers that don't support the structured format. **v1 renders lyrics as plain text**, ignoring per-line timestamps; time-synced active-line highlighting (and any click-to-seek interaction it would enable) is a separate follow-up — see 4.5 below.

The page should share playback state with the existing singleton `PlayerViewModel` rather than duplicating transport/queue ownership — it only needs to own lyrics and presentation state.

**Depends on**: 1.1

### 2.3 All Tracks Page `[M]`

A browsable page listing all tracks from the server. Uses the `search3` API with pagination (the Subsonic API has no dedicated "get all songs" endpoint). The page should include:

- **Incremental loading** — paginated fetching similar to `IncrementalAlbumCollection`, loading tracks in batches as the user scrolls.
- **Sortable columns** — sort by title, artist, album, duration, and year. Client-side sorting within loaded data, with server-side sort parameter when supported.
- **On-page search** — a filter text box that narrows the displayed track list in real time (client-side filtering of loaded results, plus a server `search3` query for broader matches).
- **Track list integration** — reuse the existing track list patterns (now-playing highlight, context menus, play/queue actions).

Add the page to the sidebar navigation between Artists and Playlists. Register the View, ViewModel, and page mapping per the standard navigation pattern.

---

## Phase 3 — Ecosystem & Polish

### 3.1 Scrobbling `[S]`

Report playback to the Subsonic server for listening history tracking (recently played, most played, Last.fm integration). Scrobble when a track has played ≥ 30 seconds or ≥ 50% of its duration, whichever comes first. Respects a user-configurable toggle from persistence (1.2).

**Depends on**: 1.2

### 3.2 CoverflowControl for Home Page Carousel `[M]`

The Home Page's top carousel (e.g., recently played, featured albums) should use a custom CoverflowControl — a 3D perspective carousel where the centered item is enlarged and flanked by tilted, receding neighbors. Built as a reusable WinUI control using Composition API transforms (rotation, translation, scale) driven by a `ScrollViewer` or pointer/touch input. The control should support item templates, keyboard/gamepad navigation, and smooth spring animations between positions. This replaces a flat horizontal list with a visually rich hero element that draws attention to the featured content.

**Depends on**: 2.1

### 3.3 Carousel Scrollbar Polish `[S]`

Three usability issues with horizontal carousels across the app (Home Page, artist detail, etc.):

1. **Auto-hide scrollbar** — The horizontal scrollbar should only appear on hover or during active scroll, then fade out after a short delay. Use `ScrollViewer` visual states or Composition API opacity animations to achieve this, keeping the layout clean when idle.
2. **Mouse horizontal scroll vs. vertical scroll conflict** — When the mouse wheel is used over a carousel, the event should scroll the carousel horizontally rather than bubbling up to the page's vertical `ScrollViewer`. Handle `PointerWheelChanged` on the carousel's `ScrollViewer` to translate vertical wheel delta into horizontal offset and mark the event as handled.
3. **Scrollbar proximity to content** — The scrollbar renders too close to the carousel items, making it visually cramped. Add a margin or padding between the last row of content and the scrollbar track so the two don't collide visually.

**Depends on**: 2.1

### 3.4 Now-Playing Track Highlight in Track Lists `[S]`

Whenever the user is browsing a page that contains a track list (album detail, artist top songs, playlist detail, search results), the currently playing track should be visually distinguished. Compare each rendered track's ID against `PlayerViewModel.CurrentTrack.Id` — if they match, apply a highlight: a left accent bar, a subtle background tint, or a small "now playing" animated icon (e.g., equalizer bars). The highlight must update reactively: when the track changes or when the user navigates to a page whose list contains the playing track. Implement this as a shared behavior or converter so every track list picks it up without per-page duplication.

### 3.5 Clean Album Version Display `[S]`

Navidrome (via OpenSubsonic) returns album names that include the version suffix — e.g., "OK Computer (Deluxe Edition)". This makes album cards, the player bar, queue items, and search results unnecessarily verbose. Strip the version suffix and show only the main album name everywhere in the app, except on the album detail page where the version is displayed as a subtitle row between the artist name and the release date/genre line.

The OpenSubsonic `AlbumID3` entity has a dedicated `version` field (e.g., `"Deluxe Edition"`). Add deserialization support for this field, propagate it through the mapping layer, and use it to split the display: short name for compact contexts, version as subtitle on the detail page.

### 3.6 Unified TrackListControl `[M]`

Extract a reusable `TrackListControl` from the duplicated track list XAML across AlbumDetailPage, ArtistDetailPage, PlaylistDetailPage, and AllTracksPage. The control should expose configuration properties for column visibility (e.g., `ShowAlbumColumn`, `ShowDiscHeaders`, `ShowMetadataColumns`), bindable commands (`ItemClickCommand`, `FavoriteCommand`), and an `ItemsSource` accepting `SongInfo` items optionally mixed with `DiscHeaderItem` for disc grouping. Consolidate the duplicated artist/album link rendering code-behind into the control. The queue popup stays separate — its compact layout and different data model (`QueueItem`) don't justify the added abstraction.

**Depends on**: 2.3

---

## Phase 4 — Post-MVP Enhancements

### 4.1 Playlist Management UI `[M]`

The API client already supports create/update/delete — add the UI: new playlist dialog, edit/delete on playlist detail, "Add to Playlist" context menus on track lists across the app, and remove-from-playlist on playlist detail.

### 4.2 Keyboard Shortcuts `[S]`

Global keyboard accelerators: Space (play/pause), Ctrl+arrows (next/prev/volume), Ctrl+M (mute), Ctrl+S (shuffle), Ctrl+R (repeat), Ctrl+Q (queue flyout). Ctrl+K for search focus already exists.

**Depends on**: 1.1

### 4.3 Playback Signal Pipeline `[M]`

Refactor playback control into an explicit dispatcher-owned signal pipeline or actor. Every playback input — UI commands, taskbar/SMTC commands, and `MediaPlayer` callbacks — should be represented as a typed signal and processed through one serialized path before it can mutate `PlaybackQueue` or player state. Media callbacks should continue to carry a source generation/token so stale events from old `MediaPlaybackItem` instances are rejected before queue navigation.

### 4.4 Position-Aware Navigation History `[S]`

`PlaybackQueue._navigationHistory` currently stores a `Stack<string>` of track IDs, and `GoBack()` resolves via `FindIndex(t => t.Id == previousTrackId)`. After a position-aware queue jump on a queue containing duplicate track IDs (introduced by 2.2 Now Playing), Previous can land on the wrong occurrence — it always finds the *first* occurrence by ID, not the position the user actually came from.

Change the navigation-history representation to capture both the track ID and the queue position at push time (e.g. `Stack<(int occurrenceIndex, string trackId)>` or an equivalent), so `GoBack()` can return to the exact position. A safe fallback is required for queue mutations that invalidate the stored position — fall back to the existing `FindIndex(byId)` lookup when the recorded position no longer holds the same track.

Add `PlaybackQueue` tests covering the duplicate-Previous scenario and the queue-mutation fallback path.

**Depends on**: 2.2 (introduces the user-visible duplicate-jump path that surfaces this limitation)

### 4.5 Time-Synced Lyrics `[M]`

2.2 ships lyrics as plain text — the OpenSubsonic `StructuredLyrics.Line[]` already exposes per-line `Start` offsets, but v1 ignores them. This item turns those offsets into a synced lyrics view: highlight the active line based on playback position, auto-scroll it into view, and (optionally) seek to a tapped line's offset on click.

Scope:

- Subscribe to `IPlaybackService.PositionChanged` while the lyrics tab is active; map the current position to the active `LyricLine` index.
- Highlight the active line; dim the others. Smooth scroll to keep the active line near the vertical center.
- Click-to-seek on a synced line (`IPlaybackService.Seek(start)`).
- Respect `StructuredLyrics.Synced == false` — if the chosen entry is unsynced, fall through to v1's plain rendering (no highlight, no click-to-seek).
- The legacy `getLyrics` fallback path stays unsynced (legacy `Lyrics` has no per-line timestamps).

**Depends on**: 2.2

### 4.6 Lyrics Renderer GPU Optimization — deep pass `[M]`

The Win2D lyrics renderer (`LyricsCanvasControl` / `LyricsSceneRenderer`) shipped with two GPU-cost fixes already landed: a **pause controller** that stops the `CanvasAnimatedControl` game loop when the surface is hidden (Queue tab / minimised / no lyrics) or idle (paused playback, scene settled), and a **per-frame cost reduction** (folding line opacity into glyph-colour alpha to drop the per-line `CreateLayer`, plus reusing the wipe's gradient-stop array). Together these took active-render GPU from ~10–20% down to ~2% on an RTX 4090 — already below the BetterLyrics reference (~3.5%).

This item is the **remaining, deeper pass**, only worth doing if profiling on lower-end hardware shows the active-playback frame is still too heavy:

- **Cache the neighbour gaussian blur.** Inactive lines currently allocate a `CanvasCommandList` + `GaussianBlurEffect` and run a multi-pass blur every frame. Bake each line's blurred text into a line-local `CanvasRenderTarget`, rebuilt only when the blur amount/colour changes, and draw it per frame with a translate offset + opacity (`DrawImage`'s opacity overload). Requires plumbing the control DPI into the renderer and careful render-target sizing/positioning — validate visually.
- **Glyph-mask wipe (BetterLyrics-grade).** Render each line's glyphs to a cached `CanvasCommandList` mask once at layout time, then composite colour per frame via an `AlphaMaskEffect` fed by a per-row gradient rectangle — instead of re-running `DrawTextLayout` and pushing a clip layer per row on the active line. Removes the last per-frame text rasterisation and the active-line row layers.

Reference techniques live in `../BetterLyrics` (GPL — techniques only, no source). Context: the original perf risk was flagged in `LYRICS.md` §3 and `docs/design/ui/2026-06-13-lyrics-canvas-control.md` §7.

---

## Phase 5 — Data Layer (Stretch)

> Phase 5 is a stretch goal. It's planned here so architecture decisions in earlier phases don't paint us into a corner.

This is a **cross-cutting change** — all browsing ViewModels currently call the Subsonic API client directly, and playback streams directly from server URIs. Introducing a cache layer means inserting a new service between every ViewModel and the API client, and reworking media source creation to support local files.

### 5.1 SQLite Metadata Cache `[XL]`

Local SQLite database as a read-through cache for artists, albums, songs, and playlists. Configurable TTL-based staleness, background incremental sync on app launch.

### 5.2 Image Cache `[L]`

Disk-backed cache for cover art with LRU eviction. Replaces direct server URI loading with a cache service that serves local files when available.

**Depends on**: 5.1

### 5.3 Audio Cache `[XL]`

Two strategies: automatic stream-through caching (played tracks saved to disk transparently) and explicit user-initiated downloads (pinned, excluded from LRU eviction). Pre-caches the next 1–2 tracks in the queue to reduce gaps between tracks. Cache management UI in Settings.

**Depends on**: 5.1

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

                         ── MVP ship line ──

Phase 4                Phase 5
──────────────────────────────────

4.1 Playlists ─── (standalone)    5.1 SQLite ─┬──▶ 5.2 Image Cache
                                               └──▶ 5.3 Audio Cache
1.1 ──▶ 4.2 Shortcuts
4.3 Playback Signal Pipeline ─── (standalone)
2.2 ──▶ 4.4 Position-Aware Nav History
2.2 ──▶ 4.5 Time-Synced Lyrics
```

## Recommended Implementation Order

| # | Item | Size | Depends on |
|---|------|------|------------|
| 1 | 1.1 Shuffle & Repeat UI | S | — |
| 2 | 1.2 Persistence (Settings & State) | M | 1.1 |
| 3 | 2.1 Home Page | M | — |
| 4 | 3.1 Scrobbling | S | 1.2 |
| 5 | 3.3 Carousel Scrollbar Polish | S | 2.1 |
| 6 | 3.4 Now-Playing Track Highlight | S | — |
| 7 | 3.5 Clean Album Version Display | S | — |
| 8 | 2.2 Now Playing / Full-Window Player | L | 1.1 |
| 9 | 2.3 All Tracks Page | M | — |
| 10 | 3.2 CoverflowControl for Home Carousel | M | 2.1 |
| 11 | 3.6 Unified TrackListControl | M | 2.3 |
| | **── MVP ship line ──** | | |
| 12 | 4.1 Playlist Management UI | M | — |
| 13 | 4.2 Keyboard Shortcuts | S | 1.1 |
| 14 | 4.3 Playback Signal Pipeline | M | — |
| 15 | 4.4 Position-Aware Navigation History | S | 2.2 |
| 16 | 4.5 Time-Synced Lyrics | M | 2.2 |
| 17 | 5.1 SQLite Metadata Cache | XL | — |
| 18 | 5.2 Image Cache | L | 5.1 |
| 19 | 5.3 Audio Cache | XL | 5.1 |

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
