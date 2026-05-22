# Lyrics Data Model (Phase 0) — Design

Companion to `LYRICS.md` §Phase 0. Captures the decisions, the pivotal spots, and the guidance that Phase 1 (providers + service) and Phase 2 (Win2D renderer) bind to. No code — see the implementation plan at `docs/implementation/service/2026-05-22-lyrics-data-model.md` for that.

---

## What we're building

A small set of platform-independent lyric types in `Hypersonic.Core/Models/Lyrics/`:

- A **container** holding an ordered list of lines plus metadata (sync granularity, language, source, global offset).
- A **line** type with a start timestamp, optional end timestamp, text, and an optional list of words.
- A **word** type with start, end, and text.
- A **sync-kind** enum: `Word`, `Line`, `Plain`, `None`.
- A **validator** that returns the list of invariants a given document violates.

Mirrors BetterLyrics' `BaseLyrics` / `LyricsLine` / `LyricsData` shape, trimmed to the single-lane subset Hypersonic ships first. Translation and romanization lanes are deliberately out of scope here; they can be added later without breaking this shape.

---

## Pivotal decisions

### Container name: `LyricsDocument`, not `Lyrics`

`LYRICS.md` calls the container `Lyrics`. We're overriding that:

- `Hypersonic.Client.Models.Entities.Lyrics` already exists — the legacy non-structured Subsonic DTO. Phase 1's `SubsonicLyricsProvider` references both types in one file; one needs disambiguation. Renaming the new core type is cheaper than touching the wire-serialized client DTO.
- `LyricsDocument` reads as "the complete lyrics artifact for one track," which is what it is. `Lyrics` is ambiguous.

Every other naming choice in `LYRICS.md` is preserved.

### Validator returns issues; it never throws

Constructors stay simple — no validation side-effects, consistent with the existing Core model style (`AlbumInfo`, `PlaybackTrack`).

Real-world LRC/TTML data is noisy. Throwing in a constructor would turn one bad line into a totally-dropped lyric. A validator that returns the list of violations lets providers log them and decide whether to keep, repair, or discard. Phase 1 invokes it once per provider response.

### Sync kind is a first-class field, not a derived property

BetterLyrics computes `IsWordByWord` lazily by scanning every line. We pin the granularity at the document level instead, so the renderer can branch on it without re-scanning, and so providers state their intent explicitly. The validator enforces consistency between the field and the line/word contents.

Semantics:
- `Word` — every line has at least one word. Karaoke-grade rendering.
- `Line` — lines carry timestamps; word lists are empty.
- `Plain` — lines carry text only; all timestamps are zero.
- `None` — empty document. Used to cache "definitively no lyrics" results so we don't re-fetch.

### Documents are effectively immutable after construction

Properties are init-only. Line and word collections are exposed as `IReadOnlyList<T>` and snapshotted from whatever enumerable the caller passes in. The renderer can trust that the document doesn't mutate underneath it; cache files round-trip through `System.Text.Json` cleanly via the existing `Hypersonic.Core.Helpers.Json` options.

### Validator is the only "behavior"

No parsing, no fetching, no fallback policy, no synchronization helpers in Phase 0. Those live in Phase 1 (`LyricsService`) and Phase 2 (renderer). Keeping Phase 0 to data + invariants means it ships independently and the contract is small enough to review in one sitting.

---

## Validation invariants (the contract the validator enforces)

These are what providers can assume after a clean validation, and what the renderer relies on:

1. Line `StartMs` is non-negative.
2. Line `EndMs`, when present, is at least `StartMs`.
3. Lines are monotonic by `StartMs` (equality allowed — chorus lines may share a timestamp; backwards motion is rejected).
4. Word `EndMs` is at least `StartMs`.
5. Words within a line are monotonic by `StartMs`.
6. When a line has `EndMs`, every word's `[StartMs, EndMs]` falls within the line's bounds.
7. `SyncKind=Word` ⟹ every line has at least one word.
8. `SyncKind=Line` ⟹ every line has zero words.
9. `SyncKind=Plain` ⟹ every line has zero words **and** `StartMs == 0`.
10. `SyncKind=None` ⟹ zero lines.

---

## Mapping the future providers

LYRICS.md flags as an open question whether AMLL TTML and NetEase YRC can map cleanly. Confirmed during this design pass:

- **AMLL TTML** — `<p>` elements map to lines, `<span begin/end>` children inside them map to words. Translation/romanization siblings (`ttm:role`) are dropped in Phase 0; the shape can carry them later via additional optional fields without breaking existing consumers.
- **NetEase YRC** — line preamble `[startMs,durationMs]` and per-word `(wordStartMs,durationMs,0)wordText` both translate directly. `wordStartMs` is absolute (not relative to the line), so no rebasing is needed; durations become `EndMs` via addition.

No model changes are required to bring either provider online later.

---

## Boundaries with adjacent phases

- **Phase 1** (`ILyricsProvider`, `LyricsService`) consumes this contract. Convention: providers return `LyricsDocument?` — `null` means "no answer / try the next provider," a document with `SyncKind=None` means "definitively no lyrics, cache the negative and stop." Providers should run the validator on their own output and discard documents that come back with issues (logging them once).
- **Phase 2** (Win2D renderer) consumes lines + the current playback position. It assumes invariants 1–10 hold; it should not re-validate at render time.
- **Phase 3** (NowPlayingPage integration) doesn't touch these types directly — it binds to whatever the owning ViewModel exposes after the Phase 1 ownership decision.

---

## Deferred / out of scope

- **Multi-lane lyrics** (translation, romanization). The shape can be extended later by adding optional secondary/tertiary collections to `LyricsLine`, mirroring BetterLyrics' approach. Not added preemptively.
- **Per-character timing.** BetterLyrics derives this at render time from word durations; Phase 2 can do the same. No need to bake it into the persisted shape.
- **Language detection.** We store a language code if the provider supplies one. Auto-detection (BetterLyrics' `LanguageHelper`) is a renderer concern, not a model concern.
- **Editing.** No mutation API. If editing arrives later, it can produce a new document rather than mutating an existing one.

---

## References

- `LYRICS.md` — feature tracking doc; Phase 0 task split.
- BetterLyrics shape: `../BetterLyrics/BetterLyrics.WinUI3/Models/Lyrics/` (`BaseLyrics`, `LyricsLine`, `LyricsData`).
- OpenSubsonic `getLyricsBySongId`: https://opensubsonic.netlify.app/docs/endpoints/getlyricsbysongid/
- Existing legacy client DTO (the naming-collision peer): `Hypersonic.Client/Models/Entities/Lyrics.cs`.
