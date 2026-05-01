# Now Playing Page — Implementation Notes

Companion to [`docs/design/ui/2026-04-30-now-playing-page.md`](../../design/ui/2026-04-30-now-playing-page.md). This document covers implementation-specific decisions, deviations from the design, and details that only appear in source. The design doc is preserved as written; everything below is what shifted during implementation.

---

## Queue tab — Album column added between title/artist and duration

The design's "Item template" specifies three columns (cover art, title + artist, duration). Implementation added a fourth column for the album name between title/artist and duration:

| Column | Width | Content |
|--------|-------|---------|
| Cover art | ~48px | Small thumbnail from `Track.CoverArtUri`, with the same placeholder fallback |
| Title + artist | `*` | Two stacked `TextBlock`s: title (bold when `IsCurrent`) and `Track.FlatArtistText` (secondary color) |
| **Album** | 240px (fixed) | `Track.Album` rendered as a single `TextBlock`, **vertically centered** in the row (not aligned to either the title or the artist line) and **left-aligned** within the column. The column is given a fixed width so short and long album names share the same left edge across rows; an `Auto`-width column would let each row's album text snug against the duration column and read as right-aligned. Plain text in v1 — no clickable album link. Adding the album-link affordance is intentionally deferred and tracked together with the equivalent change in the player-bar flyout. Trim with ellipsis; secondary text color. |
| Duration | ~60px | `Track.Duration` formatted via `SecondsToTimeConverter` |

The flyout queue (`ShellPage.xaml`) puts album info on a single line as `artist — album` because vertical space is tight. The now-playing page has more horizontal room, so it gets a dedicated centered column instead of compressing album into the artist line. Both surfaces deferring album-link interactivity keeps that work bundled.

---

## Lyrics fetch logic extracted into `LyricsFetchPolicy`

The "Files to Create" list for v1 implied per-call classification + cache-decision logic would live inline in `NowPlayingViewModel`. Implementation extracted both into [`Hypersonic/Helpers/LyricsFetchPolicy.cs`](../../../Hypersonic/Helpers/LyricsFetchPolicy.cs):

- `LyricsOutcomeKind` enum (`Content` / `DefinitiveEmpty` / `DefinitiveUnsupported` / `Transient`).
- `LyricsOutcome` record (kind + optional text).
- `LyricsFetchPolicy.IsDefinitiveErrorCode(int)` and `IsEmptyClassificationCode(int)` for the Subsonic error-code classification (codes 20, 30, 70 → definitive; code 70 → "empty" sub-classification; others → transient).
- `LyricsFetchPolicy.Combine(structured, legacy)` returning `LyricsFetchResult(Text, ShouldCache)` per the cache-write matrix.
- `LyricsFetchPolicy.FlattenStructured(StructuredLyrics[]?)` for the "first usable entry" rule.
- `LyricsFetchPolicy.ClassifyFailedStatus(int)` mapping a Subsonic `Status="failed"` code to a per-call `LyricsOutcome` (definitive empty for code 70, definitive unsupported for 20/30, transient otherwise).
- Two `internal delegate`s on `NowPlayingViewModel` for test injection — `StructuredLyricsFetcher` (calls `getLyricsBySongId` and classifies) and `LegacyLyricsFetcher` (calls `getLyrics(artist, title)` and classifies). The structured-content short-circuit (do not call legacy when structured returns content) is orchestrated in `LoadLyricsAsync`. Splitting the seam this way is what lets the test suite assert the short-circuit boundary directly via per-leg invocation counters; a single combined delegate (the original shape) couldn't.

This change keeps `NowPlayingViewModel` thin and lets the matrix be unit-tested directly via [`LyricsFetchPolicyTests`](../../../Hypersonic.Tests/Helpers/LyricsFetchPolicyTests.cs) against in-memory `LyricsOutcome` values, without instantiating the VM or its dispatcher seam.

---

## `ScrollIntoViewAlignment.Default`, not `Leading`

The Auto-scroll section calls for `ListView.ScrollIntoView` and references the existing flyout's `Leading` alignment as the model. The new now-playing queue uses `ScrollIntoViewAlignment.Default` ([`NowPlayingPage.xaml.cs:243`](../../../Hypersonic/Views/NowPlayingPage.xaml.cs)) so a track change to a row that is *already visible* in the current scroll window is a no-op, preserving the user's manual scroll position. Only when the new current row is out of view does WinUI scroll the minimum amount needed to bring it in.

This is a deliberate divergence from the flyout (which keeps `Leading` in [`ShellPage.xaml.cs`](../../../Hypersonic/Views/ShellPage.xaml.cs) — a popup that just opened benefits from anchoring the current row to the top). The now-playing page is a long-lived surface where the user often manually inspects upcoming tracks; forcing `Leading` on every track advance would yank the scroll position out from under them. The `QueueAutoScrollCoordinator` and `QueueScrollResolver` test contracts are unchanged — only the in-page glue's alignment choice differs.

---

## `LyricLine.Start` and `StructuredLyrics.Offset` carry `[XmlIgnore]`

Both fields are nullable (`long?`) public-API properties on [`Hypersonic.Client`](../../../Hypersonic.Client/Models/Entities/LyricLine.cs) entities. The `XmlSerializer` reflection rejects `Nullable<T>` on `[XmlAttribute]` (it can't map a nullable value to a single attribute string), so deserializing any `getLyricsBySongId` XML response fails before the Now Playing page can run its fallback logic. Both properties keep their nullable shape (no API-shape change) and are now marked `[XmlIgnore]` instead of `[XmlAttribute]`, with `[JsonPropertyName]` retained for the JSON path.

Consequence for the future synced-lyrics work (roadmap 4.5): a server returning XML responses will deserialize with `Start`/`Offset` always `null`. Navidrome (the primary target — see `reference_subsonic_server`) returns JSON for these endpoints, so this is acceptable for v1. When 4.5 lands and a non-OpenSubsonic XML server actually needs synced offsets, the right fix is to add `StartSpecified`/`OffsetSpecified` companion properties or a custom converter.

The JSON path is regression-guarded by [`GetLyricsBySongIdResponseTest`](../../../Hypersonic.Client.UnitTest/Deserialization/Responses/GetLyricsBySongIdResponseTest.cs), which round-trips a synced-lyrics JSON payload and asserts both `Start` and `Offset` survive intact through `[JsonPropertyName]`. The same test also covers the XML-deserializes-without-crash invariant and asserts the v1 `null` outcome on the XML path.

---

## Page-side helper construction order — gate/coordinator before `InitializeComponent()`

The XAML-wired `SelectorBar.SelectionChanged` handler can fire during `InitializeComponent()` while the framework applies `IsSelected="True"` to the default `LyricsTab`. The handler dereferences `_queueTabGate` and `_scrollCoordinator`; if those are still null (the original construction order assigned them only *after* `InitializeComponent()`), first navigation to `NowPlayingPage` crashes with a `NullReferenceException` on the `else` branch's `_queueTabGate.Detach()` / `_scrollCoordinator.OnTabSelected(NowPlayingTab.Lyrics, -1)` calls.

Two-part fix in [`NowPlayingPage.xaml.cs`](../../../Hypersonic/Views/NowPlayingPage.xaml.cs):

1. Construct `_scrollCoordinator` and `_queueTabGate` **before** `InitializeComponent()`. They have no XAML dependencies of their own, so the reordering is safe; their callbacks are `MarshalAndScroll` (which only touches `_pageDispatcher` at invocation time, not at delegate-creation time) and the `attachAll`/`detachAll` lambdas (which capture singletons resolved earlier in the constructor).
2. Add an `_initialized` bool, set `false` until the very end of the constructor body, and short-circuit `TabSelectorBar_SelectionChanged` when it's still `false`. This is the second line of defense — it removes the dependency on SelectorBar's exact initial-event behavior and guarantees no state mutation from a synthetic `SelectionChanged`-during-init even if the field-construction order is later perturbed.

Both guards together: even a future XAML change that wires a `SelectorBarItem.IsSelected` programmatically before the rest of the constructor runs would no-op cleanly. The `Unloaded` cleanup is unaffected — it always sees the fully-initialized page state.

---

## Lyrics-state visibility driven by a per-state converter, not a fan-out of bool flags

The XAML surface in [`NowPlayingPage.xaml`](../../../Hypersonic/Views/NowPlayingPage.xaml) renders the lyrics surface as three stacked siblings inside a single `Grid` — the `ProgressRing`, the lyrics `ScrollViewer`, and the "No lyrics available" `TextBlock` — with **all three** binding their `Visibility` to `ViewModel.LyricsLoadState` through a new [`LyricsLoadStateToVisibilityConverter`](../../../Hypersonic/Converters/LyricsLoadStateToVisibilityConverter.cs) parameterised by the target state name (`"Loading"` / `"Loaded"` / `"NoLyrics"`). The `NoTrack` case has no XAML element — its visibility is the **absence** of any of the three (all three converters return `Collapsed`), which falls out for free from the per-state matching rule.

The design left the rendering glue open ("plain text in a `TextBlock`… centered subtle text 'No lyrics available'") and a literal reading of the States table would push toward three boolean `[ObservableProperty]`s on the VM (`IsLoading`, `IsLoaded`, `ShowNoLyrics`) plus the existing `BoolToVisibilityConverter`. That shape would force the VM to maintain four mutually-exclusive flags in lockstep with `LyricsLoadState` (one for each enum value), and the cache-hit / load-completion paths in `HandleTrackChange` and `LoadLyricsAsync` would have to update all of them on every transition — adding a fifth state later means touching every transition site. The single-enum + per-state converter keeps the VM's authority over `LyricsLoadState` (already documented as the authoritative property in the design's Owned state list) and lets the XAML be the only place that knows which state corresponds to which visual.

The technical-detail line gets its own [`StringToVisibilityConverter`](../../../Hypersonic/Converters/StringToVisibilityConverter.cs) — collapses the `TextBlock` when `TechnicalDetail` is null/whitespace. The design's "hides the `TextBlock` when null/empty" line implied this converter without naming it. Both converters are registered locally in `NowPlayingPage.xaml`'s `Page.Resources` rather than in `Themes/Generic.xaml`, matching the pattern `EnumToBooleanConverter` follows in `SettingsPage` (per the CLAUDE.md note) — these converters are page-specific and don't justify polluting the global resource dictionary.

---

## Lyrics cache eviction is touch-on-write LRU, not pure FIFO

The design said "LRU eviction is fine but a simple FIFO ring is also acceptable at this scale" and left the choice to the implementation. The shipped policy in [`NowPlayingViewModel`](../../../Hypersonic/ViewModels/NowPlayingViewModel.cs) is **touch-on-write LRU**: writes (cache stores from `LoadLyricsAsync` per the cache-write matrix) move the entry to the tail of the ordering list and evict from the head when capacity is exceeded; **reads do not promote**. Storage is a `Dictionary<string, (LinkedListNode<string> Node, string? Text)>` paired with a `LinkedList<string>` for the ordering, so writes are O(1) and the linked-list node removal/append is O(1) — a plain `Queue<string>` would have been O(N) for the existing-key case (find-and-remove-from-middle).

Why touch-on-write and not pure FIFO: in the shipped VM, `StoreCached` is only ever reached on a cache miss — every cache hit (including a cached `null`) returns out of `HandleTrackChange` before the fetch runs, so the existing-key branch in `StoreCached` (the `_lyricsCache.TryGetValue` + `_lyricsCacheOrder.Remove` step) is **defensive / future-only** under v1's read path. Pure FIFO via `Queue<string>` would be observably identical today. The linked-list shape costs the same per write and keeps the existing-key update O(1), which is the right baseline if a future invalidation path lands (manual "refresh lyrics" action, server-side re-index notification, the upcoming roadmap 4.5 synced-lyrics work) and starts re-writing existing keys with content. Read promotion was omitted because it would burden every cache hit (the common path) with a list mutation for a benefit that only pays off if the working set exceeds 10 unique tracks during the session — for a single page-VM lifetime, that's not the typical pattern. Bound is `LyricsCacheCapacity = 10` per the design.

---

## XAML bindings on `Player.*` direct, no VM mirror — but artist/album/cover-art still go through code-behind

The design's "Always-on metadata render" section established that `RenderNowPlaying()` rebuilds **the artist `RichTextBlock`, the album hyperlink, the title text, and the cover-art binding** — all four — imperatively from the page code-behind on `Player.NowPlayingMetadataChanged` (design line 134). Implementation honors the artist/album/cover-art clauses exactly: the XAML uses `x:Name`-only declarations for `MetaArtistText`, `MetaAlbumText`, and `LargeCoverArt`, and `RenderNowPlaying()` rebuilds the inline graph and assigns the bitmap.

**Deliberate deviation: the track title is bound, not rebuilt.** The XAML declares `<TextBlock Text="{x:Bind Player.TrackTitle, Mode=OneWay}">` and `RenderNowPlaying()` does **not** touch it. The design's wording would have the code-behind set `MetaTitle.Text = Player.TrackTitle` alongside the artist/album/cover work. The deviation is intentional because `TrackTitle` is an `[ObservableProperty]` on `PlayerViewModel` (a plain `string`, not a `RichTextBlock`) — `x:Bind` listens to its `PropertyChanged` and updates the `TextBlock.Text` for free. The boundary that ended up shaping the code-behind is **whether the display element needs an imperative `Inlines` / `Hyperlink` graph** (RichTextBlock → code-behind) or just text content (TextBlock → `x:Bind`). The artist row needs imperative rebuild for hyperlink segments; the album row needs imperative rebuild for the optional hyperlink wrapper around the album name; the title row is plain text and would gain nothing from being driven by code-behind. `TechnicalDetail` is bound the same way (`{x:Bind ViewModel.TechnicalDetail, Mode=OneWay}`) and the design did not mention it in the rebuild list.

Cover art is `Image.Source = new BitmapImage(...)` inside `RenderNowPlaying()` because the `Player.CoverArtUrl` is a `string`, not a `BitmapImage`, and the `BitmapImage` constructor needs an absolute `Uri` — wrapping that conversion in a converter is possible but the existing `ShellPage` pattern is to do it in code-behind, which the page mirrors. (This part is *not* a deviation — it matches the design's "cover-art binding" clause.)

`SecondaryBrushProvider` and `TertiaryBrushProvider` are `Visibility="Collapsed"` `TextBlock`s carrying the theme-resource brushes — same trick `ShellPage.xaml` uses. The brushes are read via `SecondaryBrush` / `TertiaryBrush` instance properties on the page so the theme refresh path (re-running `RenderNowPlaying()` on `ActualThemeChanged`) reads fresh brush values rather than caching a stale snapshot in a field.

---

## Hand cursor on player-bar tap targets — reflection on `ProtectedCursor`

The design's Entry-points section says "both must indicate clickability on hover (cursor change to hand)" but doesn't specify the WinUI mechanism. WinUI 3's `UIElement.ProtectedCursor` is `protected`, so you cannot set it from a sibling class via the public surface. The implementation in [`ShellPage.xaml.cs:329`](../../../Hypersonic/Views/ShellPage.xaml.cs) (`SetHandCursorOnHover`) reaches the property via reflection — the standard WinUI 3 workaround for setting cursors on `Grid`/`TextBlock` (`InputCursor` → `PointerCursor` indirection only became public on `UIElement` for derived classes, not for arbitrary callers).

`SetHandCursorOnHover` does the `GetProperty` lookup and the `InputSystemCursor.Create(Hand)` allocation **once per call site, during `ShellPage` construction** — both happen before the `PointerEntered` handler is attached. The hover-time cost is just `prop.SetValue(element, hand)` per pointer-enter (and yes, the handler can fire on every pointer enter — there is no first-time-only guard). Both the `PropertyInfo` and the `InputSystemCursor` instance are captured in the lambda's closure, so they are reused across all pointer events for that element. Caching `prop` and `hand` in static fields shared across the two call sites is possible but premature — there are exactly two tap targets in the player bar today and the per-construction cost is one reflection lookup and one cursor allocation. The handler does **not** restore the original cursor on `PointerExited`: WinUI's input system already reverts the cursor when the pointer leaves the element, so the explicit set-on-enter is sufficient. If the `ProtectedCursor` property is renamed in a future WinUI version the reflection silently no-ops (the helper checks `prop == null` *before* attaching the handler, so the subscription is skipped entirely rather than firing-and-failing on every hover) — the click still works, only the cursor hint is lost.

---

## `ShellPage` keeps anonymous-lambda subscriptions; only `NowPlayingPage` uses named handlers

The design's lifecycle rules ("`-=` named handler in `Unloaded`") apply to `NowPlayingPage` because it's a transient page — every navigation builds a fresh instance, and a leaked subscription on the singleton `Player` would multiply per visit. `ShellPage` is constructed exactly once per app launch (it owns the navigation `Frame`) and is never re-created, so its `Player.NowPlayingMetadataChanged += (_, _) => RenderNowPlaying();` ([ShellPage.xaml.cs:74](../../../Hypersonic/Views/ShellPage.xaml.cs)) **never needs to be detached** — both objects live for the rest of the process.

This is why the implementation deliberately did not migrate `ShellPage`'s player-bar artist/album rendering to the named-handler form even though `NowPlayingPage`'s implementation is the more disciplined shape. Naming the handler on `ShellPage` would buy nothing (the subscription's lifetime equals the process lifetime) and cost a method declaration that signals "we plan to detach this someday" — false signal. The asymmetry is correct and intentional; it's documented here so a later cleanup pass doesn't conclude `ShellPage` is sloppy and unify the two.

`NowPlayingPage`'s `OnPlayerMetadataChanged` and `OnActualThemeChanged` named handlers exist for the opposite reason: the page is transient, and forgetting to `-=` would leak the page through the singleton `Player` and the long-lived `FrameworkElement` self-reference — manifestly visible after a few navigate-away/back cycles as duplicated render calls per event.

---

## `PlaybackQueue.JumpToQueueIndex` reads effective count without materializing the list

The design's New-API section pseudo-code reads `GetEffectiveQueue().Count` for the bounds check. The shipped implementation in [`PlaybackQueue.cs`](../../../Hypersonic.Core/Services/PlaybackQueue.cs) uses `_shuffleEnabled ? _shuffleOrder.Count : _tracks.Count` instead — same value, but avoids the per-call `List<PlaybackTrack>` allocation that `GetEffectiveQueue()` does (it returns a fresh `List<>` projected through the shuffle map). For a duplicate-track shuffled queue the projection is O(N); the direct count read is O(1) and allocates nothing.

Functionally identical — the translation through `_shuffleOrder[effectiveIndex]` for the underlying-index lookup is the same step `GetEffectiveQueue()` would have performed at position `effectiveIndex`. The shape change is purely an allocation/perf nit, but it's worth recording because the design's pseudo-code, taken literally, would have allocated a queue-sized list on every double-tap.

---

## `Hypersonic.Client` lyrics XML deserialization fix vs. `[XmlIgnore]`-by-design

Beyond the `[XmlIgnore]` swap on `LyricLine.Start` and `StructuredLyrics.Offset` (covered in its own section above), the implementation **does not** add `XmlSerializer` round-trip coverage for the now-XML-ignored fields. That is intentional: under the v1 design, the XML path is explicitly defined to deserialize `Start`/`Offset` as `null`, and adding a "round-trips through XML" test would lock in behavior the roadmap 4.5 work needs to change (synced lyrics will require these fields to survive XML deserialization, at which point the right test is "round-trips through XML *with* the upcoming `Specified` companions"). The JSON-round-trip test in [`GetLyricsBySongIdResponseTest`](../../../Hypersonic.Client.UnitTest/Deserialization/Responses/GetLyricsBySongIdResponseTest.cs) is the regression guard for the v1 invariant; the XML-doesn't-crash assertion in the same test is the boundary condition.

---

## `SearchSuggestionItem` populated unconditionally — no `is-song` short-circuit

The design's "Mapper update" item #2 says "populate them from each `song` in `PerformSearchAsync`'s song-mapping loop." The `Suffix`/`BitRate`/`BitDepth`/`SamplingRate` fields are populated **only inside the song-mapping loop** in [`ShellViewModel.PerformSearchAsync`](../../../Hypersonic/ViewModels/ShellViewModel.cs) — not inside the artist or album loops, since those `SearchSuggestionItem`s never become `PlaybackTrack`s. There is no defensive `if (item.Type == SearchResultType.Song)` guard around the field assignment in `HandleSuggestionChosen`'s `PlaybackTrack` construction, because the construction is already inside `case SearchResultType.Song:` — the surrounding switch is the guard. This keeps the field copy a flat list of assignments rather than a defensive object, mirroring how the rest of the existing song-fields group (`Artist`, `ArtistId`, `Album`, `AlbumId`, etc.) is handled — consistency with the established pattern was preferred over per-field null-coalescing.

---

## `LastPagePersistencePolicy` ships with a fixed allow-list, not reflection over `IPageService`

The design's "No restore as `LastPage` (write side)" test row directed the implementation to lift `ShellViewModel.OnNavigated`'s inline `Replace("Page", "ViewModel")` mapping plus `TopLevelPages.Contains` check into a pure helper. The shipped helper in [`LastPagePersistencePolicy.cs`](../../../Hypersonic/Helpers/LastPagePersistencePolicy.cs) is a hand-maintained `Dictionary<Type, string>` mapping the six allowed page types to their VM full names — **no reflection, no `IPageService` consultation, no string mangling**.

The design's wording ("returns the VM key string when the page should be persisted") admits either shape. The dictionary was chosen over the original string-replace pattern because (a) it removes the implicit `…Page` ↔ `…ViewModel` naming convention dependency — a future page that breaks the convention (e.g., a `SettingsAccountPage` mapping to `AccountSettingsViewModel`) wouldn't silently misbehave, and (b) it makes the allow-list a literal at the top of the file rather than something a reader has to derive from the absence of a `TopLevelPages.Contains` check. The cost is one line per new persistable page — strictly less than what the old `TopLevelPages` `HashSet` cost (since the `HashSet` was the allow-list **and** the `Replace` was the mapping, both of which now collapse into the dictionary entry). `NowPlayingPage` is excluded by simply not appearing in the dictionary, which is the design's required behavior.

The roadmap-deferred read-side consolidation (`DefaultActivationHandler` and `ConnectViewModel`'s separate `TopLevelPages` `HashSet`s) can later be served by exposing a `RestorablePages.IsAllowed(string viewModelName)` method that consults the same dictionary's values — the dictionary's values are exactly the set of strings the read side needs to allow.
