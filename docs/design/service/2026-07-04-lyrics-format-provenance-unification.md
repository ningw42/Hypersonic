# Lyrics Format & Provenance Unification + Source Badge

Status: **Design** · Area: service (Phase 1) + ui (Phase 2) · Date: 2026-07-04

## Motivation

The user-facing goal is small: show **which source served the current lyrics** on the Now Playing page, as an info glyph with a hover tooltip ("Lyrics from LrcLib").

Surfacing that turned out to require straightening the lyric-provenance model first. Today a `LyricsDocument` carries a single `SourceProvider` string, but its meaning is muddled:

- It is stamped by the **parser**, not the provider. For five of six sources the parser's stamp happens to equal the provider id; for AMLL it does not — `TtmlLyricsParser` stamps the *format* `"ttml"` into the identity slot (`Hypersonic.Core/Services/Lyrics/TtmlLyricsParser.cs:77`), so AMLL lyrics reach the UI (and the cache row) as `SourceProvider == "ttml"`, not `"amll-ttml-db"`.
- The existing `PayloadFormat` tag (`lrclib-synced`, `netease-lrc`, `subsonic-legacy`, …) conflates **format** with **provider**: several tags exist only so a parser could stamp a distinct provider id. `NeteaseLrcLyricsParser` is documented as *"identical in shape to `LrcSyncedLyricsParser` but stamps the `netease` source … so it's attributed distinctly."*

This feature is the first real consumer of provenance, so it is the right moment to fix the model rather than paper over it.

## The end-state model: three orthogonal axes

A `LyricsDocument` gains a clean, three-axis provenance model. Each axis answers one question and has exactly one owner:

| Axis | Question | Owner (stamps it) | Type |
|---|---|---|---|
| `SourceProvider` | **who** served it | the **provider** | `string?` (provider id) |
| `Format` | **what wire format** it is | the **parser** | `LyricsFormat` enum (new) |
| `SyncKind` | **granularity** of timing | the **parser** (exists) | `LyricsSyncKind` |

```
enum LyricsFormat { Unknown, PlainText, Lrc, Ttml, Qrc, Krc, Yrc, SubsonicStructured }
```

### Orthogonality (the load-bearing property)

`SourceProvider` and `Format` are **conceptually orthogonal**: the type system, parser registry, and cache place no constraint linking them — any `(SourceProvider, Format)` pair is representable. They are only **empirically correlated** by which real services happen to emit which formats:

| Provider | Format(s) emitted |
|---|---|
| subsonic | `SubsonicStructured`, `PlainText` |
| lrclib | `Lrc`, `PlainText` |
| netease | `Yrc`, `Lrc` |
| amll-ttml-db | `Ttml` |
| qq | `Qrc` |
| kugou | `Krc` |

The relationship is many-to-many: `Lrc` comes from both lrclib and netease; `PlainText` from both subsonic and lrclib; subsonic/lrclib/netease each emit two formats. Neither axis is a function of the other — which is precisely why they must be separate fields and why collapsing them into one would be wrong.

Non-goals of the model: no validation enforces "plausible" pairs (a `(subsonic, Yrc)` document is representable; we do not want a provider↔format whitelist — YAGNI). `Format` *bounds* the possible `SyncKind` (a `PlainText` cannot be `Word`) but does not *determine* the actual value (a `Ttml` may carry only line timing), so `SyncKind` stays a distinct fact.

`SubsonicStructured` is the one format member named after a provider. This is honest: it is OpenSubsonic's proprietary structured-lyrics JSON, a genuine wire format with no portable name — not a provider identity. (`Json` is a viable alternative name; deferred to the plan.)

---

## Phase 1 — model unification (no user-visible change)

An invisible refactor that makes `PayloadFormat` and the new `Format` **one and the same typed thing**, decouples identity from format, and fixes the AMLL bug as a side effect. Independently shippable and verifiable.

### 1.1 Merge `PayloadFormat` into a typed `LyricsFormat`

`PayloadFormat` is today a stringly-typed **parser-dispatch key** (`LyricsProviderResult`, `LyricsCacheEntry`, the registry, `ExtensionForFormat`). There are 9 tags but only 5 real parsing algorithms behind them; the extra tags exist solely to encode provider identity. Once identity moves off the parser (§1.3), those tags collapse and the dispatch key *becomes* the format family. So `PayloadFormat` is promoted to the `LyricsFormat` enum everywhere:

- `ILyricsPayloadParser.FormatTag: string` → `Format: LyricsFormat`.
- `ILyricsParserRegistry` keys by `LyricsFormat`; `Parse(LyricsFormat, bytes)`.
- `LyricsProviderResult.PayloadFormat` and `LyricsCacheEntry.PayloadFormat` → `Format: LyricsFormat`; `LyricsProviderResult.Content(document, bytes, LyricsFormat, definitive)`.
- `LyricsService.ExtensionForFormat` keys on the enum.
- `ILyricifyLyricsClient` / `LyricifyLyricsResult.FormatTag` → `LyricsFormat`.

Consequence: the `PayloadFormat → SourceType` mapping function proposed in earlier drafts is **not needed** — the merge eliminates the second representation entirely.

### 1.2 Parser dedup

The two pairs of byte-identical parsers (differing only by the provider stamp) collapse to one each:

- `NeteaseLrcLyricsParser` → **deleted**; the single LRC parser (`Format = Lrc`) serves both lrclib and netease line lyrics.
- `SubsonicLegacyLyricsParser` → **deleted**; the single plain-text parser (`Format = PlainText`) serves both subsonic-legacy and lrclib-plain.

After dedup, the generic text parsers (LRC, PlainText, TTML) live in `Hypersonic.Core` (format-generic, provider-agnostic). `SubsonicStructured` (needs `Hypersonic.Client` DTOs) and `LyricifyWord` (needs the Lyricify lib) stay in `Hypersonic`. `LyricifyWordLyricsParser` remains one class registered three times, parameterized by `LyricsFormat` (`Qrc`/`Krc`/`Yrc`) instead of a string tag, and no longer carries a `sourceId`.

### 1.3 Ownership shift

The rule becomes: **the parser owns `Format` + `SyncKind` + lines (everything derivable from the payload); the provider owns `SourceProvider` (identity).**

- Every parser **stops stamping `SourceProvider`** and stamps `Format` (its own value) alongside `SyncKind` on the document it builds.
- Every provider stamps `SourceProvider = Id` on the document it returns — a small `LyricsDocument` copy helper carries the parser's `Format`/`SyncKind`/lines through the init-only rebuild. Providers: `SubsonicLyricsProvider`, `LrcLibLyricsProvider`, `AmllTtmlLyricsProvider`, and the three via `LyricifyLyricsProviderBase`.
- On **cache read**: the re-parse restores `Format` + `SyncKind` + lines; `LyricsService.OverlayRowMetadata` (`Hypersonic/Services/LyricsService.cs:453`) restores `SourceProvider` from the index row (it already does this) — the sole source of identity on that path, since the re-parse leaves it null. The row's stored `Format` is used **only to select the parser** (dispatch); it is *not* overlaid, because the parser re-derives the same value by construction (the registry maps a `Format` to the parser whose `.Format` is that value, so they cannot diverge).

Why the split falls this way — `Format` with the parser, `SourceProvider` with the provider — comes down to what each layer can know:
- **`Format` is knowable by the parser**: it *is* the parser's identity / registry key, so the parser can always stamp it correctly, on both fetch and re-parse.
- **`SourceProvider` is NOT knowable by the parser**: after the dedup a parser is shared across providers (the one LRC parser serves both lrclib and netease), so it cannot know who fetched the bytes. Only the provider knows its own identity.

They are therefore *not* symmetric — one is derivable from the payload, the other is not — which is exactly why they land in different layers.

This is what fixes AMLL: the TTML parser stamps `Format = Ttml` (never identity); `AmllTtmlLyricsProvider` stamps `SourceProvider = "amll-ttml-db"`. The dev-only local override (`LocalFileLyricsSource`, not a provider) yields `SourceProvider == null` → attributed to nothing.

### 1.4 Cache persistence & migration

Both provenance fields are persisted in the SQLite index row and must survive the change: `SourceProvider` (identity — the sole source of provider on a cache read) and `Format` (the parser-dispatch key, needed before a document exists). `SyncKind`, `LanguageCode`, `OffsetMs` remain stored for service decisions as today. The `LyricsCacheEntry.PayloadFormat` property → `Format` (`LyricsFormat`), and the SQLite column is renamed `PayloadFormat` → `Format` to match.

Migration mechanism (verified against `SqliteCacheSchemaDriver.EnsureSchemaAsync`, `Hypersonic/Services/Cache/SqliteCacheSchemaDriver.cs:25`): on an *upgrade* (stored `v1` < current), the driver runs the `MigrationSteps` whose `TargetVersion` is in `(1, current]` — it does **not** auto-rebuild (the drop-and-recreate branch fires only when the DB is at a *future* version). So bumping the version with an empty `MigrationSteps` would leave the old `v1` table in place — a latent bug, not a rebuild. We must author an explicit step.

Plan — **discard**: bump `SqliteLyricsCacheStore.CurrentSchemaVersion` `1 → 2` (`Hypersonic/Services/SqliteLyricsCacheStore.cs:36`) **and** add a `v2` `MigrationStep` that drops and recreates the table (`DROP TABLE Entries;` + the store's current `CreateCurrentSchemaSql`, referenced not duplicated). Existing rows carry retired string tags (`netease-lrc`, `subsonic-legacy`, …) and the pre-fix AMLL `"ttml"` identity — none map cleanly forward — so a clean discard is both simplest and correct: every previously-cached song becomes a lazy cache miss and re-fetches on next play (lyrics are a disposable, cheap-to-rebuild convenience), shedding the old mis-attribution for free. Orphaned payload files are pruned by the store's reconcile/self-heal. (A data-preserving alternative — rename the column, value-remap old tags, `UPDATE` the AMLL rows — is possible but adds a mapping to maintain for no user-visible benefit.)

### 1.5 Pivotal spots

- Parsers to edit/delete: `Hypersonic.Core/Services/Lyrics/{LrcSyncedLyricsParser, LrcPlainLyricsParser, NeteaseLrcLyricsParser, TtmlLyricsParser}.cs`; `Hypersonic/Services/Lyrics/{SubsonicStructuredLyricsParser, SubsonicLegacyLyricsParser, LyricifyWordLyricsParser, LyricifySyllableMapper}.cs`.
- Contracts/models: `Hypersonic.Core/Contracts/Lyrics/{ILyricsPayloadParser, ILyricsParserRegistry, ILyricsProvider}.cs`; `Hypersonic.Core/Models/Lyrics/{LyricsDocument, LyricsProviderResult}.cs`; new `Hypersonic.Core/Models/Lyrics/LyricsFormat.cs`.
- Service/cache: `Hypersonic/Services/{LyricsService, SqliteLyricsCacheStore}.cs`; `Hypersonic/Models/LyricsCacheEntry.cs`.
- Providers: `Hypersonic/Services/Lyrics/{SubsonicLyricsProvider, LrcLibLyricsProvider, AmllTtmlLyricsProvider, LyricifyLyricsProviderBase, ILyricifyLyricsClient}.cs`.
- Registrations: `Hypersonic/App.xaml.cs` (parser list; the three `LyricifyWordLyricsParser` factories drop `sourceId`, pass `LyricsFormat`).

### 1.6 Test implications

Notable churn: `SourceProvider` moves off the parser, so parser tests assert `Format` + `SyncKind` + lines and drop `SourceProvider`. Specifically — `LrcSyncedLyricsParserTests`/`LrcPlainLyricsParserTests`/`SubsonicLyricsParsersTests` assert `Format` (e.g. `Lrc` / `PlainText`) instead of `SourceProvider`; `TtmlLyricsParserTests.SourceProvider_IsTtml` becomes a `Format == Ttml` assertion; `NeteaseLrcLyricsParserTests` and the Subsonic-legacy-specific tests are deleted with their parsers; provider tests gain `SourceProvider` stamping coverage (especially AMLL → `amll-ttml-db`); `LyricsServiceTests` and `SqliteLyricsCacheStoreTests` cover the typed `Format` round-trip, `OverlayRowMetadata`'s restore of `SourceProvider` on read, and the `v2` discard migration; `LyricsProviderRegistrationTests` gains provider `DisplayName` coverage.

---

## Phase 2 — source badge on Now Playing

Small, rides on Phase 1's reliable `SourceProvider`. Model-only for `Format` — the tooltip shows the provider name; `Format` is not surfaced in this phase.

### 2.1 Display names + catalog

- `ILyricsProvider.DisplayName` returns each provider's brand name: **Subsonic, LrcLib, AMLL, QQ Music, Netease, Kugou**. (Brand names, not localized — matching the `TechnicalDetail` precedent, which is code-formatted, not in `.resw`.)
- New singleton `ILyricsSourceCatalog` / `LyricsSourceCatalog`, built from the injected `IEnumerable<ILyricsProvider>`, exposing `string? DisplayNameFor(string? id)` — a one-time id→name dictionary; unknown or null id → `null`. Registered in `App.xaml.cs`. This is the natural seam if a future "providers" settings screen wants the same list, and keeps id→name knowledge out of the VM.

### 2.2 ViewModel (`NowPlayingViewModel`)

Inject the catalog. Add derived state:

- `LyricsSourceName` — `Catalog.DisplayNameFor(CurrentLyrics?.SourceProvider)`.
- `HasLyricsSource` — `LyricsLoadState == Loaded` **and** `LyricsSourceName is not null`. Drives glyph visibility; false during Loading / NoLyrics / NoTrack and for an unknown id (e.g. the local-override `null`).
- `LyricsSourceTooltip` — `"Lyrics from {name}"`, code-formatted like `TechnicalDetail`.

Wire via `[NotifyPropertyChangedFor]` on both `CurrentLyrics` and `LyricsLoadState`.

### 2.3 XAML (`NowPlayingPage.xaml`)

An info `FontIcon` (Segoe MDL2 `&#xE946;`) grouped with the existing refresh button in a top-right horizontal `StackPanel`, hosted in a focusable `TransparentButtonStyle` button (no command) so the tooltip is reachable by keyboard/touch, not only hover, and it visually matches the adjacent refresh button. `Visibility` ← `HasLyricsSource` (`BoolToVisibilityConverter`); `ToolTipService.ToolTip` ← `LyricsSourceTooltip`.

### 2.4 Tests

`LyricsSourceCatalog` (known id → name, unknown → null, null → null); `NowPlayingViewModel` (`HasLyricsSource`/`LyricsSourceTooltip` across Loaded / Loading / NoLyrics / unknown-id).

---

## Out of scope / non-goals

- Surfacing `Format` or `SyncKind` in the UI (model-only this round).
- Any provider↔format validation or whitelist.
- Localizing provider brand names.
- A "providers" settings screen (the catalog is built to enable it later, not to deliver it now).
