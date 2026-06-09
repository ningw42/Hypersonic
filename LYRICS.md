# Time-Synced Lyrics — Tracking

Tracking doc for the Time-Synced Lyrics feature. Captures the major design decision and the phased task split.

---

## 1. Major design decision: XAML-wrapped Win2D control

The lyrics surface is a **Win2D `CanvasAnimatedControl` embedded inside the existing XAML `NowPlayingPage`**, not a full Win2D-rendered page.

### Why Win2D for the lyrics region

- The target UX ceiling is **Apple Music-style word-synced karaoke** — gradient fill *within* a syllable, per-character glow/scale effects. None of this is achievable with `RichTextBlock` + `Run` opacity flips, which can only do discrete per-word transitions (Spotify tier).
- BetterLyrics (`../BetterLyrics`) is the reference; its `NowPlayingCanvas` is exactly this pattern and the rendering techniques are directly portable.
- The same renderer handles both line-only and word-level data — a line-only track is just a word-synced track with one segment per line. **No runtime renderer swap; no XAML→Win2D fallback path.**

### Why only the lyrics region, not the whole page

- `CanvasAnimatedControl` is a regular XAML `FrameworkElement`. It participates in `Grid`/`StackPanel` layout, respects theming around it, and raises pointer events normally.
- Album art, transport, queue, settings affordances stay as XAML — keeps accessibility, focus, theming, and existing styles free.
- The complexity tax is scoped to *what is drawn inside the canvas*: text layout, scroll/auto-center, active-line resolution, gradient fill. Outside the canvas, nothing changes.

### Out of scope for this decision

- Translation/romanization rendering (multi-lane). The data model accommodates it; the renderer ships single-lane first.
- Custom backdrop effects (blur, glow halos behind the page). Lyrics canvas effects only.

---

## 2. Task split

Four phases, each independently shippable to `master`. Order is mandatory: the data model defines the contracts both ends depend on; providers/service land before the renderer so the renderer can be exercised against real fetched data; integration is last.

### Phase 0 — Data model (`Hypersonic.Core`) — ✅ shipped

> See implementation: [`docs/implementation/service/2026-05-22-lyrics-data-model.md`](docs/implementation/service/2026-05-22-lyrics-data-model.md). Container renamed `Lyrics` → `LyricsDocument` to avoid colliding with `Hypersonic.Client.Models.Entities.Lyrics`.

Define platform-independent lyric types in `Hypersonic.Core/Models/Lyrics/`. Both the renderer's input and the providers' output bind to these.

- `LyricsLine` — start ms, optional end ms, text, optional `IReadOnlyList<LyricsWord>`.
- `LyricsWord` — start ms, end ms, text.
- `Lyrics` (container) — list of lines, language code, source provider, sync kind enum (`Word | Line | Plain | None`), optional global offset ms.
- Shape mirrors BetterLyrics' `BaseLyrics`/`LyricsLine` — line-only tracks have an empty word list; the renderer treats one line as one segment.

No behavior, no parsing — just types and validation invariants (monotonic timestamps, word timestamps within line bounds when present).

### Phase 1 — Lyrics provider + service (`Hypersonic.Core` + `Hypersonic`)

- `ILyricsProvider` — one source. Fetches a track's lyrics, surfacing both the parsed `Lyrics?` (to rank providers) and the raw payload + its format (what the cache persists and later re-parses). Stateless.
- `LyricsService` — orchestrator and cache **policy layer**, the analog of `CachedImageService`. Holds the ordered provider list (DI) and the fallback policy; delegates all persistence — index row and payload file alike — to a `SqliteLyricsCacheStore`.
- Providers, in priority order:
  1. `SubsonicLyricsProvider` — wraps existing `getLyricsBySongId` call, adapts `StructuredLyrics` → `Lyrics`.
  2. `LrcLibLyricsProvider` — `https://lrclib.net/api/get` line-synced LRC.
  3. *(Deferred)* `NeteaseLyricsProvider`, `AmllTtmlDbLyricsProvider` — word-synced sources.
- Fallback policy: **quality-tiered with time budget**. Try providers concurrently up to a timeout; prefer `Word > Line > Plain`; first-result wins within a tier. Bias toward word-sync when available.
- Cache: **mirror the cover-art cache, end to end.** Lyrics get the same two-part shape as images — a policy layer (`LyricsService`, mirroring `CachedImageService`) over a SQLite-backed store (`SqliteLyricsCacheStore : SqliteCacheStoreBase<LyricsCacheEntry>`, payload subfolder `"Lyrics"`, mirroring `SqliteImageCacheStore`). The shared base owns both halves: the SQLite **category index** (schema/migration, the write gate, LRU eviction, reconcile, negative-cache rows for no-lyrics-found, self-heal) and the payload-file lifecycle (atomic temp-file write + rename via `WritePayloadAsync`, URI lookup via `TryGetPayloadUriAsync`). The subclass supplies only the schema/column mapping.
  - **Payload is the raw provider response, parsed on read — not a serialized `Lyrics`.** Store exactly what the provider returned (lrclib LRC, AMLL TTML, NetEase YRC, the Subsonic `StructuredLyrics` body) byte-for-byte, the way the image cache stores raw image bytes; the payload file's extension reflects that format and the store's filename predicate recognizes it, mirroring how `SqliteImageCacheStore` recognizes image extensions. The `Lyrics` container is rebuilt by running the format's parser (chosen from the stored format, independent of the network provider) over the raw bytes on each cache read — once per track load — and is never itself persisted, so the cached source of truth stays lossless and the `Lyrics` model can gain fields without touching the cache.
  - **Index row:** keyed by Subsonic song ID; carries the metadata needed to pick the read-time parser and make cache decisions without touching disk — source provider, payload format, sync kind (`Word | Line | Plain | None`), language, offset ms — plus the standard `CreatedAt / LastAccess / ExpiresAt` lifecycle columns and the payload file name.
- Migrate existing `NowPlayingViewModel` fetch/cache logic into `LyricsService`. **Open call:** singleton service feeding singleton `PlayerViewModel` (preferred), vs leaving lyrics scoped to `NowPlayingViewModel`. Decide before Phase 3.

### Phase 2 — Win2D lyrics control (`Hypersonic`)

Standalone `LyricsCanvasControl` (Win2D) that takes `IReadOnlyList<LyricsLine>` + `currentPositionMs` as inputs.

Renderer responsibilities:

- Resolve active line via cached linear scan (port BetterLyrics' `LyricsSynchronizer`, ~50 LOC).
- Vertical layout with active-line centering; smooth scroll between lines.
- Active-line emphasis: scale/opacity transition.
- Word-level highlight when word data is present: gradient brush across the active line, edge advancing through played-width fraction.
- No data fetching, no caching, no `PlayerViewModel` binding — pure presentation, fed inputs from the outside.

With Phase 1 already shipped, the control can be exercised against real provider output during development (e.g., a temporary scaffolding page consuming `LyricsService` directly) without requiring full `PlayerViewModel` integration.

### Phase 3 — Integration on `NowPlayingPage`

- Expose `CurrentLyrics` and `CurrentPositionMs` on `PlayerViewModel` (or whichever ViewModel owns the binding after the Phase 1 call).
- Replace the existing text-only lyrics area on `NowPlayingPage` with the `LyricsCanvasControl` from Phase 2.
- Wire `LyricsService` into the track-change pipeline; surface load/error states via the existing `LyricsLoadState` enum.

---

## 3. Risks and open questions

- **`CanvasAnimatedControl` text layout perf** — validated by BetterLyrics on similar hardware, but worth a Phase 2 smoke test before building the full effect stack.
- **Lyrics state ownership** — service-owned (singleton, feeds `PlayerViewModel`) vs page-owned (scoped to `NowPlayingViewModel`). Decide before Phase 3 starts.
- **Provider-result schema for word-sync** — `Lyrics` model is fixed in Phase 0, so word-sync providers in Phase 1 must conform. Confirm AMLL TTML and NetEase YRC both map cleanly to `LyricsLine`/`LyricsWord` during the Phase 0 design.
- **Legal posture of external providers** — lrclib and AMLL (CC0/MIT) ship by default. NetEase/QQ Music gated behind a settings toggle, off by default.
- **`HttpClient` fan-out** — each `SubsonicClient` currently owns its own `HttpClient`. Introduce `IHttpClientFactory` registration before Phase 1 to avoid socket exhaustion as provider count grows.

---

## 4. References

- Reference app: `../BetterLyrics` — WinUI 3, same platform. Key files surveyed:
  - Lyrics model: `BetterLyrics.WinUI3/Models/Lyrics/`
  - Renderer: `BetterLyrics.WinUI3/Renderer/LyricsLineRenderer.cs`
  - Sync resolver: `BetterLyrics.WinUI3/Helper/Lyrics/LyricsSynchronizer.cs`
  - Parsers: `BetterLyrics.WinUI3/Helper/Lyrics/LyricsContentParser/`
- OpenSubsonic `getLyricsBySongId` spec: https://opensubsonic.netlify.app/docs/endpoints/getlyricsbysongid/
- lrclib API: https://lrclib.net/docs
- AMLL TTML DB: https://github.com/Steve-xmh/amll-ttml-db
- Existing Hypersonic surface (verified): `Hypersonic.Client/SubsonicClient.cs:455-458` (`GetLyricsBySongIdAsync`), `Hypersonic.Client/Models/Entities/StructuredLyrics.cs`, `Hypersonic/ViewModels/NowPlayingViewModel.cs` (existing fetch/cache).
- Cache pattern to mirror (verified): `Hypersonic/Services/CachedImageService.cs` (per-entity policy layer), `Hypersonic/Services/SqliteImageCacheStore.cs` (image subclass), `Hypersonic/Services/Cache/SqliteCacheStoreBase.cs` (generic SQLite category store — gate, eviction, reconcile, negative-cache, self-heal). Lyrics add a parallel `SqliteLyricsCacheStore` subclass.
