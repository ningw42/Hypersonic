# Cross-Surface Favorite (Star) Synchronization — Implementation Notes

Companion to the design at [`../../design/service/2026-06-07-favorite-sync.md`](../../design/service/2026-06-07-favorite-sync.md). This document records only what the design does **not** already say: the concrete shape the code took, the places it deviates from the design, and the test scaffolding that made it provable. The design's semantics (one writer, the epoch/seq/lease write gate, optimistic-via-broadcast, the coalescing worker) were implemented faithfully — those are not re-explained here.

What shipped, in dependency order:

- `PlaybackTrack` gains the hand-rolled `INotifyPropertyChanged` (only `IsStarred` notifies).
- `FavoritesService` + `IFavoritesService` + `FavoritesChangedMessage`, registered in DI.
- `PlayerViewModel` delegates toggling to the service, gains the `Receive` handler, and drops the bespoke `RevertStar`.
- The four track-list VMs delegate + reconcile + overlay; `ArtistDetailViewModel`'s `SyncStarInBothCaches` is deleted.
- Seed on connect (`ActivationService` / `ConnectViewModel`); re-poll on window activation (`MainWindow`).
- Queue-row + now-playing hearts (XAML).
- CLAUDE.md documentation.

---

## Deviations from the design

### `RefreshAsync` is split into a synchronous arbiter + an async body

The design's contract lists `Task RefreshAsync()` (deliberately not `async`) and says the coalesced caller should get "its `Task`" back. Realizing *task identity* — the same `Task` object handed to every caller that arrives while a pass is in flight — forced a structural split the design did not spell out:

- `RefreshAsync()` (`Hypersonic/Services/FavoritesService.cs`) is a **plain, non-async** method. It performs the lease check synchronously; on a hit it sets `held.Queued = true` and `return held.Task;` — returning the *existing* task instance, not a fresh state-machine task.
- `RunRefreshAsync(client, lease)` is the `async` body, assigned to `lease.Task` before being returned.

Had `RefreshAsync` been written `async Task`, `return held.Task` would *await-and-wrap* it, producing a new task identity and changing the coalesced caller's completion semantics. The single-flight test pins the difference with `Assert.AreSame(t1, t2)` (`FavoritesServiceTests.RefreshSingleFlight_CoalescesAndRunsExactlyOneTrailingPass`). This split is the load-bearing implementation decision behind the whole "refresh lease" section of the design.

### Queue-row hearts reuse `FavoriteToggleButtonStyle`, not a `Button`+`FontIcon`

The design's "Queue-row hearts" example binds glyph/foreground via `{x:Bind Track.IsStarred, Mode=OneWay, Converter=…}`, implying the player-bar's `Button` + `FontIcon` + `BoolToGlyphConverter`/`BoolToFavoriteBrushConverter` recipe. The implementation instead reuses the **existing track-list** heart — a `ToggleButton` with `Style="{StaticResource FavoriteToggleButtonStyle}"` driven by `IsChecked="{x:Bind Track.IsStarred, Mode=OneWay}"` — in both queue templates (`Hypersonic/Views/ShellPage.xaml` col 3, `Hypersonic/Views/NowPlayingPage.xaml` col 4). No new converters or template are needed and queue rows now look identical to album/playlist rows.

Only the **now-playing page's large heart** follows the player-bar recipe (`Button` + `FontIcon` + the two converters), because the design explicitly wanted it bound to `Player.IsStarred`/`Player.ToggleStarCommand` so it stays in lockstep with the player bar.

The single design-mandated subtlety — that `x:Bind` defaults to `OneTime`, so the binding to the notifying flag must be explicit `Mode=OneWay` — survives intact: only `IsChecked` carries `Mode=OneWay` (it is the live-updating one); the sibling `Track.Title` etc. stay one-time.

### Queue-row command reaches the VM by `ElementName`, with named page roots

Not anticipated in the design: inside the queue `DataTemplate` the `DataContext` is the row's `QueueItem`, so the row's command cannot `x:Bind` to the page's `Player`. The fix is a classic binding to a **named page root**:

```xml
Command="{Binding Player.ToggleQueueItemStarCommand, ElementName=ShellPageRoot}"
CommandParameter="{x:Bind}"
```

This required adding `x:Name="ShellPageRoot"` / `x:Name="NowPlayingPageRoot"` to the two pages. Each row therefore mixes binding modes deliberately — compiled `x:Bind` for the item-scoped `IsChecked`/`CommandParameter`, classic `{Binding … ElementName}` for the page-scoped command. The command itself, `ToggleQueueItemStarAsync(QueueItem?)`, lives on `PlayerViewModel` alongside the now-playing `ToggleStarAsync`.

### Both UI-thread guards became test-survivability guards

The design's "UI-thread affinity" rule is `if (_dispatcher.HasThreadAccess) Send else enqueue`. The implementation widened the inline branch to **`if (_dispatcher is null || _dispatcher.HasThreadAccess)`** and wrapped the `Start()` dispatcher capture in `try/catch` (both in `Hypersonic/Services/FavoritesService.cs`). On a unit-test thread `DispatcherQueue.GetForCurrentThread()` yields no queue, so `_dispatcher` stays `null`: the poll timer is silently skipped and broadcasts send **inline**. This is what lets the entire service be exercised synchronously off the UI thread in `FavoritesServiceTests` with no dispatcher pump — a degradation path the design implied (tests isolate the messenger) but never described.

### `ComputeDiff` returns a third value, `Survivors`

The design describes the pure diff as yielding "added/removed". The shipped helper is `(List<string> Added, List<string> Removed, List<string> Survivors)` (`Hypersonic/Services/FavoritesService.cs`). `Survivors` (every non-skipped id of the union) is the concrete vehicle for the design's "stamp on accept, even on a no-diff confirmation" rule: `RunRefreshAsync` walks `survivors` to write `_lastWriteSeq[id] = obsSeq` regardless of whether membership changed, then applies `added`/`removed`. Without surfacing survivors, the confirmation-stamping the write gate depends on would have no list to iterate.

### `Start` resets state before the first-call timer setup; `_seq` comment inline

Minor ordering: `Start()` advances the epoch and clears `_starred`/`_desired`/`_lastWriteSeq`/`_lastFullRefreshSeq` **first**, then (first call only) captures the dispatcher and starts the timer, then seeds via `RefreshAsync()`. The design lists the same effects without committing to this order; nothing observable depends on it. `_seq` is intentionally *not* reset, marked with an inline comment so a future reader doesn't "fix" it.

### Test organization differs from the design's test list

- The design sketches one combined cross-VM test (`SongInfo` across two VMs *plus* queue `PlaybackTrack`s *plus* the player mirror). It shipped **split**: `FavoritesCrossVmSyncTests` covers two `TracksViewModel` instances + the load-site reconcile/overlay; the queue-`PlaybackTrack` and player-mirror halves live in `PlayerViewModelFavoriteTests` (which wires a *real* `FavoritesService` so toggles round-trip back through a broadcast).
- The design's "`Start` … without duplicating the timer" assertion is **not** tested: with no dispatcher the timer branch never runs in tests, so there is nothing to assert. `Start_Idempotent_ResetsStoreAndLeaseAndReseeds` covers the reset/reseed half only.
- The predecessor's bespoke optimistic/revert player tests were rewritten to the service-delegation + broadcast model, as the design directed — the player no longer owns that logic. `NotifyThemeChanged_RaisesIsStarred` is **retained** (the hollow-heart foreground still binds through a theme-dependent converter, so the theme-change re-raise still matters).

---

## Implementation details beyond the design

### `PlaybackTrack` INPC, exactly as scoped

`Hypersonic.Core/Models/PlaybackTrack.cs` hand-rolls `INotifyPropertyChanged` with a single notifying property (`IsStarred`) and an early-out on no-op writes; every other field stays a plain auto-prop. `PlaybackTrackTests` (in `Hypersonic.Core.Tests`) pins all three facets: `IsStarred` raises once, a same-value write is silent, and `Title`/`Album` writes raise nothing. Core gains no package dependency.

### Each subscriber's `Receive` coerces `Ids` to a set once

Every handler (`PlayerViewModel`, the four list VMs) opens with:

```csharp
var ids = message.Ids as IReadOnlySet<string> ?? new HashSet<string>(message.Ids);
```

The service broadcasts single-id changes as `new[] { id }` (an array, not a set), so the `as` fails and the handler builds a one-element `HashSet` — cheap, and it keeps the membership test uniform whether the delta is one id (toggle) or many (poll). `PlayerViewModel.Receive` updates **every** effective-queue `PlaybackTrack` whose id matches (redrawing the bound queue rows) and touches its own mirror only when `NowPlaying.Id` is in the set — the structural id-keyed revert that let `RevertStar` be deleted.

### Worker `serverKnown` is captured unconditionally, used only by the worker

`ToggleAsync` reads `serverKnown = _starred.Contains(songId)` before any mutation, even for calls that turn out to be already-leased (and return early before becoming the worker). For the already-leased caller the captured value reflects the *optimistic* store, not the server — but that caller never uses it, so it is harmless. The value is only meaningful on the path that becomes the worker, where no op is yet in flight and store == server holds, exactly as the design requires.

### `_lastWriteSeq` pruning is implemented, not deferred

The design marks floor-pruning "optional". `PruneWriteSeqAtOrBelowFloor()` (`Hypersonic/Services/FavoritesService.cs`) runs after every successful full refresh, dropping per-id entries at or below `_lastFullRefreshSeq` (redundant, since `EffectiveSeq` folds the floor in). It is guarded to no-op while the floor is still 0.

### Wiring is three one-liners plus a lazy activation hook

- DI (`Hypersonic/App.xaml.cs`): `IMessenger` → `WeakReferenceMessenger.Default` and `IFavoritesService` → `FavoritesService`, both singletons.
- Seed on connect: `App.GetService<IFavoritesService>().Start()` is called on the auto-connect path in `Hypersonic/Services/ActivationService.cs` (immediately before `ShowShell`, so it runs on the UI thread and the timer starts) and on the manual path in `Hypersonic/ViewModels/ConnectViewModel.cs`. `Start` is idempotent, so a connection that takes both paths just advances the epoch and reseeds.
- Re-poll on activation: `MainWindow_Activated` (`Hypersonic/MainWindow.xaml.cs`) gates on `WindowActivationState != Deactivated`, resolves `IFavoritesService` lazily (`App.MainWindow` is built before the DI host), and fires `RefreshAsync()` fire-and-forget. The resolve-plus-kickoff is wrapped in a broad parameterless `catch` that swallows **any** synchronous failure — its purpose is the early-activation case where the DI host isn't built yet, but the catch is deliberately broad. Implemented as the design's "dedicated, separate-from-ShellPage" subscription.
- Search-play hydration (`Hypersonic/ViewModels/ShellViewModel.cs`): the one materialization site lacking a server `starred` field seeds `IsStarred = _favorites.IsStarred(item.Id)`.

### The four list VMs share one load-site shape

Each reconcile site is the same three lines, placed right after the list is mapped: capture `BeginObservation()` *before* the fetch, then `Reconcile(items.Select(s => (s.Id, s.IsStarred)), token)`, then overlay `s.IsStarred = _favorites.IsStarred(s.Id)`. `ArtistDetailViewModel` concatenates its community + personal lists into one `allTopSongs` and reconciles + overlays them in one combined pass, so every matching instance is overlaid — including both the community and personal copies of an id that appears in both lists. This is the replacement for the deleted `SyncStarInBothCaches`, whose job the broadcast `Receive` and the overlay now cover.

---

## Test scaffolding

- **Messenger isolation.** Every test constructs the service/VMs with a **fresh `WeakReferenceMessenger`**, never `.Default`, so weak registrations from a prior test's still-alive recipients can't bleed into broadcast counts. The `FavoritesServiceTests.Recorder` is a small class that registers for `FavoritesChangedMessage` *and* keeps itself strongly referenced (a registered inline lambda would be collected by the weak messenger before it could record anything).
- **No-op double.** `FakeFavoritesService` (`Hypersonic.Tests/Fakes/FakeFavoritesService.cs`) satisfies the new constructor dependency for the many VM tests that don't exercise favorites. Tests that *do* assert star behavior pass a real `FavoritesService`. Either way a fresh messenger is supplied so the VM's `IsActive = true` registration is real and isolated. This is why every previously-existing VM test that builds one of the five VMs grew two constructor arguments (`IFavoritesService` + `IMessenger`).
- **`getStarred2` fake.** `RecordingSubsonicClient` (`Hypersonic.Tests/Fakes/RecordingSubsonicClient.cs`) replaces its old `NotImplementedException` with a configurable `GetStarred2Async`: a settable `StarredSongIds` set, `GetStarred2Status`, `GetStarred2ShouldThrow`, a `GetStarred2CallCount`, and a `GetStarred2Gate` `TaskCompletionSource` for interleaving a toggle or a `Start()` mid-refresh. It emits `Song` rows carrying only an `Id` (and a dummy `Starred` timestamp), matching the "we consume only ids" cost note.
- **Coverage built.** `FavoritesServiceTests` realizes the design's strategy list end-to-end — toggle success/unstar/throw/failed-status/null-client, coalescing + superseded-failed settle, the stale-snapshot and server-vs-server and no-diff-confirmation and absence-floor gate cases, refresh single-flight + trailing pass, both epoch-guard cases (discard replaced work; new epoch seeds while old `finally` stays inert), refresh diff/failure, reconcile gate, `Start` reset, and the pure `ComputeDiff`. Cross-VM, load-site, player-mirror, search-play hydration, and `PlaybackTrack` INPC are covered in their respective test files.

---

## Files touched

| Area | Files |
|---|---|
| Core model | `Hypersonic.Core/Models/PlaybackTrack.cs` (+ `Hypersonic.Core.Tests/Models/PlaybackTrackTests.cs`) |
| Service | `Hypersonic/Services/FavoritesService.cs`, `Hypersonic/Contracts/Services/IFavoritesService.cs`, `Hypersonic/Messages/FavoritesChangedMessage.cs` |
| Subscribers | `Hypersonic/ViewModels/{Player,Tracks,AlbumDetail,PlaylistDetail,ArtistDetail}ViewModel.cs` |
| Hydration | `Hypersonic/ViewModels/ShellViewModel.cs` |
| Wiring | `Hypersonic/App.xaml.cs`, `Hypersonic/Services/ActivationService.cs`, `Hypersonic/ViewModels/ConnectViewModel.cs`, `Hypersonic/MainWindow.xaml.cs` |
| XAML | `Hypersonic/Views/ShellPage.xaml`, `Hypersonic/Views/NowPlayingPage.xaml` |
| Docs | `CLAUDE.md` |
| Tests | `Hypersonic.Tests/Services/FavoritesServiceTests.cs`, `Hypersonic.Tests/ViewModels/FavoritesCrossVmSyncTests.cs`, `Hypersonic.Tests/Fakes/{FakeFavoritesService,RecordingSubsonicClient}.cs`, plus constructor updates across the existing Player/Artist/PlaybackTrack-propagation/Shell-search-play test files |
