# Player Bar Favorite (Heart) Button

## Overview

The player bar at the bottom of the app exposes shuffle, transport, repeat, seek, queue, and volume controls — but there is no way to favorite (star) the track that is currently playing. Favoriting today only exists in track-list rows (album detail, playlist detail, artist top songs, all-tracks). The user must navigate back to a list containing the playing track to star it, which is a notable gap for a primary, always-visible surface.

**Goal**: Add an always-visible heart button to the player bar that toggles the favorite state of the now-playing track, placed between the seek bar's duration text and the queue flyout toggle.

## Background

### Existing favorite pattern (track lists)

Track-list rows already implement favoriting:

- `SongInfo.IsStarred` (an `[ObservableProperty]`) holds the per-song state, mapped in `SongMapper.MapFromChild` from the Subsonic `starred` field (`IsStarred = !string.IsNullOrEmpty(song.Starred)`).
- Each row binds a `ToggleButton` styled with `FavoriteToggleButtonStyle` and invokes the ViewModel's `ToggleStar` command.
- The command (e.g. `TracksViewModel.ToggleStarAsync`) is **optimistic**: it flips `IsStarred` immediately, calls `client.StarAsync(id)` / `client.UnstarAsync(id)`, and reverts the flag on failure (logged as a warning).
- The Subsonic client comes from `ISubsonicClientService.Client` (nullable), the same singleton every track-list ViewModel injects.

### Glyphs and color

- `&#xEB51;` — outline heart (HeartFill off)
- `&#xEB52;` — filled heart
- Favorited color used in list rows: red `#E74856`

`FavoriteToggleButtonStyle` is deliberately **hover-reveal** — it shows nothing until the row is hovered, then a hollow heart, then a red filled heart when checked. That behavior suits list rows but **not** an always-visible player-bar control, so this design does not reuse that style (see Design §3).

### Player bar layout

The player transport lives in `PlayerControlsGrid` in `ShellPage.xaml` (`ShellPage.xaml:505`), a single-row grid of 9 `Auto`/`*` columns:

| Col | Control |
|----|---------|
| 0 | Shuffle |
| 1 | Previous |
| 2 | Play/Pause |
| 3 | Next |
| 4 | Repeat |
| 5 | Position text |
| 6 | Seek slider (`*`) |
| 7 | Duration text |
| 8 | Queue toggle |

All transport buttons are always-visible 32×32 transparent `Button`s with a `FontIcon`. Shuffle and queue drive their icon `Foreground` through converters bound to VM state (`BoolToAccentBrushConverter`, `RepeatModeToAccentBrushConverter`).

### The data gap

`PlaybackTrack` — the now-playing model the player bar binds through (`PlayerViewModel`) — carries no starred state. It is constructed in five places, four of them `BuildPlaybackTracks()` methods that copy from `SongInfo`:

- `TracksViewModel.BuildPlaybackTracks`
- `AlbumDetailViewModel.BuildPlaybackTracks`
- `PlaylistDetailViewModel.BuildPlaybackTracks`
- `ArtistDetailViewModel.BuildPlaybackTracks`
- `ShellViewModel.HandleSuggestionChosen` (from a search suggestion, which has no star state)

`PlayerViewModel` currently depends only on `IPlaybackService` and `IAppStateService`.

## Design

### 1. Carry `IsStarred` on `PlaybackTrack`

Add a `bool IsStarred` to `PlaybackTrack` and populate it at each build site from the source `SongInfo.IsStarred`. The search-suggestion build site in `ShellViewModel` defaults to `false` (search suggestions don't carry star state — a known, acceptable limitation).

This is the same approach the model already uses for every other piece of display metadata: the state travels with the track, so the player reflects the correct heart the instant a track becomes current — no extra network round-trip and no flicker on track change. (The rejected alternative — fetching starred state per track change — adds latency and a visible flicker, and Subsonic has no cheap per-song "is starred" check.)

### 2. `PlayerViewModel`: state + toggle command

- Inject `ISubsonicClientService` (and an `ILogger` from `ILoggingService`) — matching the track-list ViewModels.
- Add `[ObservableProperty] bool IsStarred`, hydrated from `track.IsStarred`:
  - in the constructor hydration block (when a track is already loaded at startup), and
  - in `OnTrackChanged` (set to the new track's value, or `false` when there is no track).
- Add `[RelayCommand] ToggleStarAsync`:
  1. Guard: return if `!HasTrack`, if the current track id is empty, or if `Client` is null.
  2. Optimistically flip `IsStarred` **and** the live `_playbackService.NowPlaying.IsStarred` (so the state persists on the track instance, not just the VM mirror).
  3. Call `UnstarAsync(id)` / `StarAsync(id)` based on prior state.
  4. On failure — either a thrown exception **or** a `SubsonicResponse.Status == "failed"` (Subsonic returns HTTP 200 on logical failures, so the status must be checked) — revert through a `RevertStar` helper and log a warning.
- `RevertStar(track, wasStarred)`: always restore the *captured* track's own `IsStarred`, but set the visible VM `IsStarred` by rehydrating from the *current* `_playbackService.NowPlaying` — if playback advanced during the await, `OnTrackChanged` already hydrated the new track's state and must not be clobbered.

### 3. Player bar XAML

Insert one `Auto` column into `PlayerControlsGrid` between the duration text and the queue toggle. The duration text stays at column 7; the favorite button becomes column 8; the queue toggle shifts to column 9.

The control is a transparent 32×32 `Button` (**not** a `ToggleButton`, since the icon appearance is driven entirely by the `IsStarred`-bound converters, and the optimistic command owns the state change), styled with `TransparentButtonStyle` (Design §4a):

- `Command="{x:Bind Player.ToggleStarCommand}"`
- `IsEnabled="{x:Bind Player.HasTrack, Mode=OneWay}"` — non-interactive when nothing is playing.
- `Style="{StaticResource TransparentButtonStyle}"`.
- Inner `FontIcon`:
  - **Glyph** via the existing `BoolToGlyphConverter`, parameter `&#xEB52;|&#xEB51;` (filled when starred, outline when not).
  - **Foreground** via a new `BoolToFavoriteBrushConverter` (Design §4).

### 4. `BoolToFavoriteBrushConverter`

A small `IValueConverter` returning a red `SolidColorBrush(#E74856)` when `true`. When `false` it returns the secondary text brush resolved via `BoolToAccentBrushConverter.GetThemedBrush("TextFillColorSecondaryBrush")` — i.e. the brush from the element's *actual* theme dictionary, not a one-time `Application.Current.Resources` snapshot, so it stays correct across light/dark. Registered globally in `Themes/Generic.xaml` alongside the other converters.

Because the binding only re-evaluates when `IsStarred` changes, `PlayerViewModel.NotifyThemeChanged()` (invoked from `ShellPage.ActualThemeChanged`) re-raises `IsStarred` so the hollow heart re-resolves its brush on theme toggle — the same mechanism used for the shuffle/repeat/queue icons.

### 4a. `TransparentButtonStyle`

A `Button` style mirroring the existing `TransparentToggleButtonStyle`: transparent background normally and when disabled (no background box), `ControlFillColorSecondaryBrush`/`Tertiary` fill on hover/press (the same brushes the default button template and the neighbouring transport/queue buttons use), and a Disabled visual state that dims the content to `0.4` opacity. Opacity is used because it is the only template-level lever that overrides the `FontIcon`'s explicit `Foreground`.

### No-track appearance

When nothing is playing, `IsStarred` is `false` and the button is disabled, so `TransparentButtonStyle`'s Disabled state renders a dimmed (0.4-opacity) secondary-color outline heart with a transparent background — no fill box, consistent with the other player-bar buttons (which only show a fill on hover). This supersedes an earlier iteration that left the disabled button at full opacity with no dedicated dim.

## States

| State | Glyph | Color | Interactive |
|-------|-------|-------|-------------|
| No track playing | `&#xEB51;` outline | `TextFillColorSecondary` @ 0.4 opacity (dimmed) | No (disabled) |
| Track, not favorited | `&#xEB51;` outline | `TextFillColorSecondary` | Yes |
| Track, favorited | `&#xEB52;` filled | red `#E74856` | Yes |

## Edge cases

| Scenario | Behavior |
|----------|----------|
| Track changes while playing | `OnTrackChanged` re-hydrates `IsStarred` from the new track → heart reflects new track immediately |
| Toggle, then request fails (exception **or** `Status == "failed"`) | `RevertStar` restores the captured track's own `IsStarred` and re-reads the visible heart from current `NowPlaying`; warning logged |
| Track changes *during* an in-flight toggle that then fails | Revert does not clobber the new track's heart (it rehydrates from current `NowPlaying`) |
| Track started from a search suggestion | `PlaybackTrack.IsStarred` defaults `false`; toggling still works (calls Star/Unstar by id) |
| No track / queue cleared (`NowPlaying == null`) | `IsStarred = false`, button disabled (dimmed outline heart, no background box) |
| Theme toggled while idle/unfavorited | `NotifyThemeChanged()` re-raises `IsStarred` → hollow heart re-resolves its theme-correct brush |
| Toggle from player bar while a list shows the same song | List row does **not** live-update (separate `SongInfo` instance) — see Scope exclusions |

## Test coverage

`PlayerViewModel` tests (`Hypersonic.Tests`, fake `IPlaybackService` + fake `ISubsonicClientService`):

| Test | Scenario |
|------|----------|
| `OnTrackChanged_HydratesIsStarred` | Track change with `IsStarred = true` → VM `IsStarred` true; a subsequent unstarred track → false |
| `OnTrackChanged_NoTrack_IsStarredFalse` | `NowPlaying` null → `IsStarred` false |
| `ToggleStar_NotStarred_CallsStarAndFlips` | From false → optimistic true, `StarAsync` called with the track id, `NowPlaying.IsStarred` true |
| `ToggleStar_Starred_CallsUnstar` | From true → optimistic false, `UnstarAsync` called |
| `ToggleStar_Failure_Reverts` | Client throws → `IsStarred` and `NowPlaying.IsStarred` revert to prior value |
| `ToggleStar_ApiReturnsFailedStatus_Reverts` | Response `Status == "failed"` (no exception) → both flips revert |
| `ToggleStar_TrackChangesDuringFailedRequest_DoesNotClobberNewTrack` | Track advances to B mid-request, then the request fails → heart still reflects B; A's own state reverts |
| `ToggleStar_NoTrack_NoOp` | `HasTrack` false → no client call |
| `NotifyThemeChanged_RaisesIsStarred` | `NotifyThemeChanged()` re-raises `IsStarred` so the hollow heart re-resolves its theme brush |

(The race/failed-status tests use a `RecordingSubsonicClient` with a `Gate` `TaskCompletionSource` and a settable `ResponseStatus`.)

`PlaybackTrack` propagation tests carry `IsStarred` through from `SongInfo` at every build site: `TracksViewModelTests` (all-tracks), `PlaybackTrackPropagationTests` (album + playlist `BuildPlaybackTracks`), and `ArtistDetailViewModelTopSongsTests` (`PlayFromTopSong` → `BuildTopSongsPlaybackTracks`).

## Files to modify

| File | Change |
|------|--------|
| `Hypersonic.Core/Models/PlaybackTrack.cs` | Add `bool IsStarred` |
| `Hypersonic/Helpers/SongMapper.cs` | (no change — `SongInfo.IsStarred` already mapped) |
| `Hypersonic/ViewModels/TracksViewModel.cs` | Copy `IsStarred` in `BuildPlaybackTracks` |
| `Hypersonic/ViewModels/AlbumDetailViewModel.cs` | Copy `IsStarred` in `BuildPlaybackTracks` |
| `Hypersonic/ViewModels/PlaylistDetailViewModel.cs` | Copy `IsStarred` in `BuildPlaybackTracks` |
| `Hypersonic/ViewModels/ArtistDetailViewModel.cs` | Copy `IsStarred` in `BuildPlaybackTracks` |
| `Hypersonic/ViewModels/PlayerViewModel.cs` | Inject `ISubsonicClientService` + logger; add `IsStarred` + `ToggleStarCommand` (+ `RevertStar`); hydrate on track change; re-raise `IsStarred` in `NotifyThemeChanged` |
| `Hypersonic/Converters/BoolToFavoriteBrushConverter.cs` | **New** — red when starred, theme-resolved secondary (via `GetThemedBrush`) when not |
| `Hypersonic/Themes/Generic.xaml` | Register `BoolToFavoriteBrushConverter`; add `TransparentButtonStyle` (transparent disabled state, 0.4 content dim) |
| `Hypersonic/Views/ShellPage.xaml` | Add column + favorite `Button` (`TransparentButtonStyle`) between duration text and queue toggle; shift queue toggle to col 9 |
| `App.xaml.cs` | (verify) `PlayerViewModel` resolves its new dependencies via DI (already-registered singletons) |
| `Hypersonic.Tests/ViewModels/PlayerViewModel*Tests.cs` | Toggle + hydration tests; fake `ISubsonicClientService` |
| `Hypersonic.Tests/ViewModels/PlaybackTrackPropagationTests.cs` | Assert `IsStarred` propagates |

## Scope exclusions

- **Cross-page star sync** — Toggling from the player bar will not live-update a track-list row showing the same song (separate `SongInfo` instance), and vice versa. This is a pre-existing limitation across all pages (starring in album detail doesn't update artist detail either) and is out of scope.
- **NowPlayingPage (full-window player)** — Could reuse `PlayerViewModel.IsStarred` / `ToggleStarCommand` later, but is not part of this change.
- **Search-suggestion star state** — Tracks started directly from a search suggestion default to unfavorited until toggled; wiring star state into `SearchSuggestionItem` is out of scope.
