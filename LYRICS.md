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

A phased split, each phase independently shippable to `master`. Order is mandatory: the data model defines the contracts both ends depend on; providers/service land before the renderer so the renderer can be exercised against real fetched data; integration is last. (Phase 1 carries a 1.5 fast-follow for external providers + concurrency.)

### Phase 0 — Data model (`Hypersonic.Core`) — ✅ shipped

> See implementation: [`docs/implementation/service/2026-05-22-lyrics-data-model.md`](docs/implementation/service/2026-05-22-lyrics-data-model.md). Container renamed `Lyrics` → `LyricsDocument` to avoid colliding with `Hypersonic.Client.Models.Entities.Lyrics`.

Define platform-independent lyric types in `Hypersonic.Core/Models/Lyrics/`. Both the renderer's input and the providers' output bind to these.

- `LyricsLine` — start ms, optional end ms, text, optional `IReadOnlyList<LyricsWord>`.
- `LyricsWord` — start ms, end ms, text.
- `Lyrics` (container) — list of lines, language code, source provider, sync kind enum (`Word | Line | Plain | None`), optional global offset ms.
- Shape mirrors BetterLyrics' `BaseLyrics`/`LyricsLine` — line-only tracks have an empty word list; the renderer treats one line as one segment.

No behavior, no parsing — just types and validation invariants (monotonic timestamps, word timestamps within line bounds when present).

### Phase 1 — Lyrics provider + service (`Hypersonic.Core` + `Hypersonic`) — ✅ shipped

> **Source of truth:** [`docs/design/service/2026-06-09-lyrics-provider-service.md`](docs/design/service/2026-06-09-lyrics-provider-service.md). This slice builds the full provider/service/durable-cache architecture but ships **only the Subsonic source, fetched sequentially**; external providers + concurrency move to Phase 1.5. Where the bullets below and the design doc diverge, the design doc wins.
>
> See implementation: [`docs/implementation/service/2026-06-09-lyrics-provider-service.md`](docs/implementation/service/2026-06-09-lyrics-provider-service.md) — records the deviations from the plan and the details surfaced during implementation and review.

- `ILyricsProvider` — **one real source, self-contained.** A provider owns its internal endpoint fallback and format selection and returns its single best result: the parsed + validated `LyricsDocument`, the raw payload + format tag, and a `definitive` bit. Stateless.
- `SubsonicLyricsProvider` — the one source this phase. Hosts the structured (`getLyricsBySongId`) → legacy (`getLyrics`) fallback **internally**; today's `LyricsFetchPolicy.Combine` relocates here (the semantics are Subsonic-specific), not into the cross-provider policy.
- `LyricsService` (singleton) — orchestrator and cache **policy layer**, the analog of `CachedImageService`. Holds the ordered provider list (DI) and the cross-provider fallback policy, with in-flight dedup by key; delegates all persistence to `SqliteLyricsCacheStore`.
- Fallback policy: **sequential** this phase — walk providers in priority order, first usable Content wins; positive-cache gated by the `definitive` bit; negative-cache only when every provider was definitive (any transient blocks it). Generalizes `Combine` to N providers. The concurrent quality-tiered variant (time budget, cross-provider `Word > Line > Plain` ranking) is **Phase 1.5**.
- Parsing: a format → parser registry rebuilds `LyricsDocument` from raw bytes, shared by the provider (fetch time) and the cache (read time). Interface + registry live in Core; the two Subsonic parsers live in `Hypersonic` (they reuse `Hypersonic.Client` DTOs), keeping Core dependency-free.
- Cache: **mirror the cover-art cache, end to end.** A policy layer (`LyricsService`, mirroring `CachedImageService`) over a SQLite-backed store (`SqliteLyricsCacheStore : SqliteCacheStoreBase<LyricsCacheEntry>`, payload subfolder `"Lyrics"`, mirroring `SqliteImageCacheStore`). The shared base owns both halves: the SQLite **category index** (schema/migration, write gate, LRU eviction, reconcile, negative-cache rows for no-lyrics-found, self-heal) and the payload-file lifecycle. The subclass supplies schema/column mapping plus a `ReadPayloadAsync` (entry + bytes) for in-process parse-on-read — lyrics need the bytes, not the file URI that images hand to XAML.
  - **Payload is the raw provider response, parsed on read — not a serialized `LyricsDocument`.** Store exactly what the provider returned (the Subsonic `StructuredLyrics` body now; lrclib LRC, AMLL TTML, NetEase YRC later) byte-for-byte; the payload file's extension reflects that format and the store's filename predicate recognizes it. The `LyricsDocument` is rebuilt by the format's parser over the raw bytes on each cache read and is never itself persisted, so the cached source of truth stays lossless and the model can gain fields without touching the cache.
  - **Index row:** keyed by the image cache's Subsonic scope — **server hash + active-user identity hash + song-id hash** (not the bare song id, which is unique only within one server + account). Carries source provider, payload format, sync kind (`Word | Line | Plain | None`), language, offset ms — plus `CreatedAt / LastAccess / ExpiresAt` lifecycle columns and the payload file name. Positive TTL long; negative TTL shorter so a track that later gains lyrics gets re-checked.
- Migrate existing `NowPlayingViewModel` fetch/cache logic into `LyricsService`; the VM becomes a pure consumer of the singleton service and gains a `CurrentLyrics` (`LyricsDocument?`) for the Phase 2 renderer. Whether `PlayerViewModel` also consumes the service is the **Phase 3** call.

### Phase 1.5 — External providers + concurrency (`Hypersonic`) — ✅ shipped

> **Source of truth:** [`docs/design/service/2026-06-10-lyrics-lrclib-provider.md`](docs/design/service/2026-06-10-lyrics-lrclib-provider.md). Same `ILyricsProvider` contract and parser registry; only the service's orchestration strategy + public surface change, so this is purely additive. Where these bullets and the design doc diverge, the design doc wins.
>
> See implementation: [`docs/implementation/service/2026-06-10-lyrics-lrclib-provider.md`](docs/implementation/service/2026-06-10-lyrics-lrclib-provider.md) — records the LRC `[offset:]` sign decision, the fan-out budget-timer choice, the disambiguation heuristic, and the other details surfaced during implementation and review.

- `LrcLibLyricsProvider` — self-contained external source: `https://lrclib.net/api/get` (track + artist required; album/duration sent as optional filters when available) with an internal `/api/search` fallback (track+artist query, album/duration used only in client-side ranking) for metadata-variant resilience; picks its single best result (synced → `Line`, plain → `Plain`, empty/instrumental → definitively empty). Two new **Core** parsers: `lrclib-synced` (LRC, honors `[offset:]`) and `lrclib-plain`.
- *(Deferred further)* `NeteaseLyricsProvider`, `AmllTtmlDbLyricsProvider` — word-synced sources, with their legal-posture settings toggle.
- Fallback policy upgrades to **concurrent quality-tiered with a per-call time budget**: fan out, prefer `Word > Line > Plain`, early-exit on all-complete or top-tier; Subsonic wins an equal-tier tie (registration order).
- Caching for the concurrent outcome: **eager-pin the positive winner at full TTL**, suppress the negative write when a peer was truncated; a **manual force-refresh** (large budget, cache-overwrite) is the on-demand recovery path. The `GetLyricsAsync` surface gains optional `budget` + `forceRefresh`; the refresh **button UI is Phase 3**. (Background cache-upgrade noted as a future enhancement.)
- External lookup ships **always on, no settings toggle** (lrclib is CC0/openly accessible); the privacy/legal gate arrives with NetEase/QQ.
- `IHttpClientFactory` registration — needed once providers make their own outbound HTTP.

### Phase 2 — Win2D lyrics control (`Hypersonic`) — ✅ shipped

> **Source of truth:** [`docs/design/ui/2026-06-13-lyrics-canvas-control.md`](docs/design/ui/2026-06-13-lyrics-canvas-control.md). Where these bullets and the design doc diverge, the design doc wins.
>
> See implementation: [`docs/implementation/ui/2026-06-13-lyrics-canvas-control.md`](docs/implementation/ui/2026-06-13-lyrics-canvas-control.md) — code complete, all automated tests green, and exercised in the `Lyrics Lab` harness against real word- and line-synced songs (intra-word wipe, bloom, alignment confirmed); formal 60 fps profiling on lower-end hardware is still advisable. Records the AMLL TTML dialect pick, the staged bloom/glow/edge-fade deferrals, the behaviours pinned in code, and the Phase 3 input contract.

Standalone `LyricsCanvasControl` (Win2D) — **pure presentation**, fed `LyricsDocument?` + `PositionMs` + `IsPlaying` + `TrackDurationMs?` + colors from outside (no fetch, no cache, no VM/`MediaPlayer` reach-in). Targets the **Apple-faithful** effect set; clean-room reimplementation from BetterLyrics' *techniques* (it is GPL-3.0, so no source is copied).

Renderer responsibilities:

- Resolve active line via cached linear scan (port BetterLyrics' `LyricsSynchronizer`, single-lane).
- Vertical layout with active-line centering (~40% top-offset); critically-damped **spring** scroll between lines. **Wrapping enabled** — a long line becomes multiple visual rows.
- Active-line **gradient wipe**: a played-head walks the line's words; rendered as a 4-stop gradient **per visual row** so wrapped lines flow correctly (word-level when word data is present; one synthetic word per line otherwise → graceful line-level fill). `Plain` → static.
- Per-word **bloom** (word timing + enabled): scale pop + upward float + glow, fixed short onset independent of word length. Bloom unit is the **word** (no sub-word synthesis).
- Inactive-line **falloff**: opacity down, gaussian blur up, slight scale-down with distance.
- **Line-end resolution**: line end = `EndMs ?? nextLine.StartMs`; final line = `EndMs ?? TrackDurationMs ?? (StartMs + ~4s tail)` so the last line still wipes (guards the Line/Plain fallback).

Key refinements over the original sketch (see design doc): a self-interpolating **`PlaybackClock`** so the wipe stays fluid between the 500ms position updates; **Approach C** animation (analytic wipe + a ~60-LOC spring/tween primitive, *not* a ported keyframe engine); colors are **inputs** (theme accent now, album-art palette a future enhancement); display-only but the scroll model is built to accept a user-scroll offset + pointer hit-testing so Phase 3 interaction is wiring, not a rewrite.

**Verification vehicle** (folded into this phase): a Core **`TtmlLyricsParser`** (registered as format `ttml`, reused later by the deferred AMLL provider) + a **`LocalFileLyricsSource`** reading a sideloaded `%LOCALAPPDATA%\Hypersonic\LyricsOverride\<songId>.ttml`. It is an **override source, not a fan-out provider**: `LyricsService` consults it **before the cache read** (one small isolated hook), and a present file wins outright — bypassing cache + fan-out + write, and reversible (delete the file to revert). A real song's word-timed TTML thus plays back live in a throwaway dev harness page. Win2D (`Microsoft.Graphics.Win2D`) is already referenced — no new dependency.

### Phase 3 — Integration on `NowPlayingPage` — ✅ shipped

> **Source of truth:** [`docs/design/ui/2026-06-21-lyrics-now-playing-integration.md`](docs/design/ui/2026-06-21-lyrics-now-playing-integration.md). Two refinements emerged: all sync kinds (including Plain) route through the canvas, and the manual refresh button's feedback lives entirely in the button. Position/play-state/duration are sourced from `IPlaybackService` (the true origin) rather than `PlayerViewModel`.

- Expose `CurrentLyrics` and `CurrentPositionMs` on `PlayerViewModel` (or whichever ViewModel owns the binding after the Phase 1 call).
- Replace the existing text-only lyrics area on `NowPlayingPage` with the `LyricsCanvasControl` from Phase 2.
- Wire `LyricsService` into the track-change pipeline; surface load/error states via the existing `LyricsLoadState` enum.

---

## 3. Risks and open questions

- **`CanvasAnimatedControl` text layout perf** — validated by BetterLyrics on similar hardware, but worth a Phase 2 smoke test before building the full effect stack.
- **Lyrics state ownership** — resolved for Phase 1: a singleton `LyricsService` is consumed by `NowPlayingViewModel` (a pure subscriber). Whether `PlayerViewModel` also consumes it remains the **Phase 3** call.
- **Provider-result schema for word-sync** — `LyricsDocument` is fixed in Phase 0, so the word-sync providers (Phase 1.5) must conform. AMLL TTML and NetEase YRC were confirmed to map cleanly to `LyricsLine`/`LyricsWord` during the Phase 0 design; Subsonic structured is `Line`/`Plain` only.
- **Legal posture of external providers** — lrclib and AMLL (CC0/MIT) ship by default. NetEase/QQ Music gated behind a settings toggle, off by default.
- **`HttpClient` fan-out** — each `SubsonicClient` currently owns its own `HttpClient`. Subsonic-only Phase 1 makes no new outbound HTTP; introduce `IHttpClientFactory` before **Phase 1.5**, when external providers start making their own requests.

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
