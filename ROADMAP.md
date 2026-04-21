# Hypersonic MVP Roadmap

> **Goal**: Ship a polished Subsonic desktop music player with complete playback controls, session persistence, and a rich listening experience. Offline caching (Phase 3) is a stretch goal — the MVP is fully functional online-only; the data layer is planned here so the architecture accounts for it from the start.

| Item | Size | Status |
|------|------|--------|
| **Phase 1 — Core Playback Polish** | | |
| 1.1 Shuffle & Repeat UI | S | ✅ Complete |
| 1.2 Persistence (Credentials, Settings & State) | M | ✅ Complete |
| **Phase 2 — Experience Essentials** | | |
| 2.1 Home Page | M | 🔲 Not started |
| 2.2 Now Playing / Full-Window Player | L | 🔲 Not started |
| **Phase 4 — Ecosystem & Polish** | | |
| 4.1 Scrobbling | S | 🔲 Not started |
| 4.2 Playlist Management UI | M | 🔲 Not started |
| 4.3 Keyboard Shortcuts | S | 🔲 Not started |
| 4.4 CoverflowControl for Home Carousel | M | 🔲 Not started |
| 4.5 Carousel Scrollbar Polish | S | 🔲 Not started |
| 4.6 Now-Playing Track Highlight | S | 🔲 Not started |
| **── MVP ship line ──** | | |
| **Phase 3 — Data Layer (Stretch)** | | |
| 3.1 SQLite Metadata Cache | XL | 🔲 Not started |
| 3.2 Image Cache | L | 🔲 Not started |
| 3.3 Audio Cache | XL | 🔲 Not started |

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

A dedicated full-content page (not a separate window) with three regions: large album art + metadata, time-synced lyrics, and an embedded queue list. Accessible by clicking the album art or track title in the player bar.

The lyrics engine fetches structured lyrics from the OpenSubsonic API (`GetLyricsBySongIdAsync`), syncs the active line to playback position, and falls back to plain lyrics for servers that don't support the structured format.

The page should share playback state with the existing singleton `PlayerViewModel` rather than duplicating transport/queue ownership — it only needs to own lyrics and presentation state.

**Depends on**: 1.1

---

## Phase 3 — Data Layer (Stretch)

> Phase 3 is a stretch goal, not a gate for MVP ship. It's planned here so architecture decisions in Phases 1–2 don't paint us into a corner.

This is a **cross-cutting change** — all browsing ViewModels currently call the Subsonic API client directly, and playback streams directly from server URIs. Introducing a cache layer means inserting a new service between every ViewModel and the API client, and reworking media source creation to support local files.

### 3.1 SQLite Metadata Cache `[XL]`

Local SQLite database as a read-through cache for artists, albums, songs, and playlists. Configurable TTL-based staleness, background incremental sync on app launch.

### 3.2 Image Cache `[L]`

Disk-backed cache for cover art with LRU eviction. Replaces direct server URI loading with a cache service that serves local files when available.

**Depends on**: 3.1

### 3.3 Audio Cache `[XL]`

Two strategies: automatic stream-through caching (played tracks saved to disk transparently) and explicit user-initiated downloads (pinned, excluded from LRU eviction). Pre-caches the next 1–2 tracks in the queue to reduce gaps between tracks. Cache management UI in Settings.

**Depends on**: 3.1

---

## Phase 4 — Ecosystem & Polish

### 4.1 Scrobbling `[S]`

Report playback to the Subsonic server for listening history tracking (recently played, most played, Last.fm integration). Scrobble when a track has played ≥ 30 seconds or ≥ 50% of its duration, whichever comes first. Respects a user-configurable toggle from persistence (1.2).

**Depends on**: 1.2

### 4.2 Playlist Management UI `[M]`

The API client already supports create/update/delete — add the UI: new playlist dialog, edit/delete on playlist detail, "Add to Playlist" context menus on track lists across the app, and remove-from-playlist on playlist detail.

### 4.3 Keyboard Shortcuts `[S]`

Global keyboard accelerators: Space (play/pause), Ctrl+arrows (next/prev/volume), Ctrl+M (mute), Ctrl+S (shuffle), Ctrl+R (repeat), Ctrl+Q (queue flyout). Ctrl+K for search focus already exists.

**Depends on**: 1.1

### 4.4 CoverflowControl for Home Page Carousel `[M]`

The Home Page's top carousel (e.g., recently played, featured albums) should use a custom CoverflowControl — a 3D perspective carousel where the centered item is enlarged and flanked by tilted, receding neighbors. Built as a reusable WinUI control using Composition API transforms (rotation, translation, scale) driven by a `ScrollViewer` or pointer/touch input. The control should support item templates, keyboard/gamepad navigation, and smooth spring animations between positions. This replaces a flat horizontal list with a visually rich hero element that draws attention to the featured content.

**Depends on**: 2.1

### 4.5 Carousel Scrollbar Polish `[S]`

Three usability issues with horizontal carousels across the app (Home Page, artist detail, etc.):

1. **Auto-hide scrollbar** — The horizontal scrollbar should only appear on hover or during active scroll, then fade out after a short delay. Use `ScrollViewer` visual states or Composition API opacity animations to achieve this, keeping the layout clean when idle.
2. **Mouse horizontal scroll vs. vertical scroll conflict** — When the mouse wheel is used over a carousel, the event should scroll the carousel horizontally rather than bubbling up to the page's vertical `ScrollViewer`. Handle `PointerWheelChanged` on the carousel's `ScrollViewer` to translate vertical wheel delta into horizontal offset and mark the event as handled.
3. **Scrollbar proximity to content** — The scrollbar renders too close to the carousel items, making it visually cramped. Add a margin or padding between the last row of content and the scrollbar track so the two don't collide visually.

**Depends on**: 2.1

### 4.6 Now-Playing Track Highlight in Track Lists `[S]`

Whenever the user is browsing a page that contains a track list (album detail, artist top songs, playlist detail, search results), the currently playing track should be visually distinguished. Compare each rendered track's ID against `PlayerViewModel.CurrentTrack.Id` — if they match, apply a highlight: a left accent bar, a subtle background tint, or a small "now playing" animated icon (e.g., equalizer bars). The highlight must update reactively: when the track changes or when the user navigates to a page whose list contains the playing track. Implement this as a shared behavior or converter so every track list picks it up without per-page duplication.

---

## Dependency Graph

```
Phase 1                Phase 2              Phase 3              Phase 4
─────────────────────────────────────────────────────────────────────────

1.1 Shuffle/Repeat ─┬─▶ 1.2 Persistence    3.1 SQLite ─┬──▶ 3.2 Image Cache
                    │        │                          │
                    │        └──▶ 4.1 Scrobble          └──▶ 3.3 Audio Cache
                    │
                    ├──▶ 2.2 Now Playing    2.1 Home ──┬──▶ 4.4 Coverflow
                    │                                  │
                    └──▶ 4.3 Shortcuts                 └──▶ 4.5 Scrollbar Polish
                                            
                    4.2 Playlists ─── (standalone)      4.6 Track Highlight ── (standalone)
```

## Recommended Implementation Order

| # | Item | Size | Depends on |
|---|------|------|------------|
| 1 | 1.1 Shuffle & Repeat UI | S | — |
| 2 | 1.2 Persistence (Settings & State) | M | 1.1 |
| 3 | 2.1 Home Page | M | — |
| 4 | 2.2 Now Playing / Full-Window Player | L | 1.1 |
| 5 | 4.4 CoverflowControl for Home Carousel | M | 2.1 |
| 6 | 4.5 Carousel Scrollbar Polish | S | 2.1 |
| 7 | 4.6 Now-Playing Track Highlight | S | — |
| 8 | 4.1 Scrobbling | S | 1.2 |
| 9 | 4.3 Keyboard Shortcuts | S | 1.1 |
| 10 | 4.2 Playlist Management UI | M | — |
| | **── MVP ship line ──** | | |
| 11 | 3.1 SQLite Metadata Cache | XL | — |
| 12 | 3.2 Image Cache | L | 3.1 |
| 13 | 3.3 Audio Cache | XL | 3.1 |

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
