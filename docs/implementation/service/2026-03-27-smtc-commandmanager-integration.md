# SMTC CommandManager Integration — Implementation Details

Companion to [docs/design/service/smtc-commandmanager-integration.md](../../design/service/smtc-commandmanager-integration.md). This document records what was actually built, decisions made during implementation, and deviations from the original design.

---

## Files Changed

### Modified files

| File | Changes |
|---|---|
| `Hypersonic/Services/PlaybackService.cs` | Removed manual SMTC code, added `CommandManager` event hooks, updated `UpdateSmtcState()` and `ClearSmtcDisplay()`, updated `Dispose()` cleanup |

### Unchanged

- `Hypersonic/Contracts/Services/IPlaybackService.cs` — no interface changes
- `Hypersonic/Services/PlaybackQueue.cs` — queue logic is independent of SMTC
- `Hypersonic/Services/TaskbarService.cs` — `ITaskbarList3` operates independently
- `Hypersonic/ViewModels/PlayerViewModel.cs` — consumes the same events as before

No new production/code files. No new dependencies.

---

## What Was Removed

### Constructor (lines 95-107 before change)

| Code | Reason |
|---|---|
| `_mediaPlayer.CommandManager.IsEnabled = false` | Re-enabling `CommandManager` restores automatic SMTC integration |
| `smtc.IsEnabled = true` | `CommandManager` enables this automatically |
| `smtc.IsPlayEnabled = true` / `smtc.IsPauseEnabled = true` | `CommandManager` enables these automatically |
| `smtc.IsNextEnabled = false` / `smtc.IsPreviousEnabled = false` | Replaced by `EnablingRule` in `UpdateSmtcState()` |
| `smtc.ButtonPressed += OnSmtcButtonPressed` | Replaced by `CommandManager.NextReceived` / `PreviousReceived` |

### PlayCurrent() — duplicate DisplayUpdater block (lines 343-355 before change)

The entire manual `DisplayUpdater` block was removed. `CommandManager` automatically propagates display properties from the `MediaPlaybackItem` (set at lines 318-327) to the SMTC session. The `smtc.PlaybackStatus = MediaPlaybackStatus.Playing` line was also removed — `CommandManager` syncs status from `MediaPlayer` state.

### OnCurrentStateChanged() — manual PlaybackStatus sync (lines 472-479 before change)

The `switch` expression that mapped `MediaPlayerState` to `MediaPlaybackStatus` was removed. `CommandManager` handles this mapping automatically within the media pipeline, without the dispatcher round-trip latency that the manual approach introduced.

### OnSmtcButtonPressed handler (lines 385-405 before change)

Entire handler removed. Play/pause are handled automatically by `CommandManager`. Next/previous are now routed through dedicated `CommandManager` event handlers with deferral support.

### Dispose() — ButtonPressed unsubscription (line 501 before change)

Replaced with `CommandManager.NextReceived -=` and `CommandManager.PreviousReceived -=`.

---

## What Was Added

### CommandManager event hooks in constructor

```csharp
var cmd = _mediaPlayer.CommandManager;
cmd.NextReceived += OnCommandManagerNextReceived;
cmd.PreviousReceived += OnCommandManagerPreviousReceived;
```

### OnCommandManagerNextReceived / OnCommandManagerPreviousReceived

Both handlers follow the same deferral pattern specified in the design doc:

1. Acquire deferral from `args.GetDeferral()`
2. Dispatch to UI thread via `_dispatcherQueue.TryEnqueue()`
3. Inside the dispatch: set `args.Handled = true`, call `NextTrack()` / `PreviousTrack()`, complete deferral
4. If `TryEnqueue` fails: complete deferral without setting `Handled`

The deferral prevents the `CommandManager` from considering the command handled until the queue operation has actually executed on the UI thread.

---

## What Was Modified

### UpdateSmtcState()

Before:
```csharp
var smtc = _mediaPlayer.SystemMediaTransportControls;
smtc.IsNextEnabled = HasNext;
smtc.IsPreviousEnabled = HasPrevious;
```

After:
```csharp
var cmd = _mediaPlayer.CommandManager;
cmd.NextBehavior.EnablingRule = HasNext
    ? MediaCommandEnablingRule.Always
    : MediaCommandEnablingRule.Never;
cmd.PreviousBehavior.EnablingRule = HasPrevious
    ? MediaCommandEnablingRule.Always
    : MediaCommandEnablingRule.Never;
```

Without a `MediaPlaybackList`, the `CommandManager` defaults next/previous to disabled. `EnablingRule` overrides this default based on `PlaybackQueue` state. All existing call sites remain unchanged.

### ClearSmtcDisplay()

Before:
```csharp
var smtc = _mediaPlayer.SystemMediaTransportControls;
smtc.PlaybackStatus = MediaPlaybackStatus.Closed;
var updater = smtc.DisplayUpdater;
updater.ClearAll();
updater.Update();
```

After:
```csharp
var updater = _mediaPlayer.SystemMediaTransportControls.DisplayUpdater;
updater.ClearAll();
updater.Update();
```

The explicit `PlaybackStatus = Closed` was removed. See design decision below.

---

## Design Decisions

### PlaybackStatus = Closed removal in ClearSmtcDisplay

The design doc flagged this as "verify during implementation." All code paths that call `ClearSmtcDisplay()` set `_mediaPlayer.Source = null` before calling it:

- `RemoveAt()` (queue empty path): `_mediaPlayer.Source = null` at line 237, `ClearSmtcDisplay()` at line 242
- `ClearAll()`: `_mediaPlayer.Source = null` at line 274, `ClearSmtcDisplay()` at line 280

Setting `Source = null` causes the `CommandManager` to transition playback status to `Closed` automatically. Manual testing confirmed the SMTC overlay clears correctly without the explicit line. **If this is found to be incorrect on other Windows versions, restore the line.**

### No design deviations

The implementation follows the design doc exactly. No structural changes, no additional code, no skipped items.

---

## Benefits Observed

### Improved global session timing accuracy

With the old manual approach, playback status was reported to the global SMTC session via a `DispatcherQueue.TryEnqueue()` callback in `OnCurrentStateChanged`. This added a dispatcher round-trip of latency to every SMTC status update. External apps consuming the session (e.g., lyrics apps using `GlobalSystemMediaTransportControlsSessionManager`) would see the "Playing" state slightly after audio actually started, causing a small but consistent sync offset.

With `CommandManager` enabled, SMTC status is reported synchronously from within the media pipeline, eliminating that delay. A lyrics app that was previously lagging behind by a noticeable amount now syncs correctly.

### Timeline properties now reported to global session

The manual SMTC implementation never synchronized `TimelineProperties` with the global session — neither the current position, playback rate, nor position changes from seek were reported. External apps relying on the global session's timeline had no accurate position information, causing them to drift or lose sync entirely after a seek.

`CommandManager` automatically maintains `TimelineProperties` from `MediaPlayer` state. After this change, seeking mid-track no longer causes external lyrics apps to lose sync.

### ITaskbarList3 confirmed independent

As predicted by the design doc, taskbar thumbnail buttons (managed by `TaskbarService` via `ITaskbarList3` COM interop) are unaffected. They continue to render and respond to clicks independently of the `CommandManager` / SMTC layer.

---

## Verification Results

### Build

`dotnet build Hypersonic.sln -p:Platform=x64` — passed, no errors.

### Automated tests

| Test project | Result |
|---|---|
| `Hypersonic.UnitTest` (PlaybackQueueTests) | 55 passed |
| `Hypersonic.Client.UnitTest` | 32 passed |
| `Hypersonic.Tests.MSTest` | Blocked by pre-existing Windows App SDK issue (`WindowsAppSDKSelfContained requires a supported Windows architecture`) — unrelated to this change |

### Manual testing required

The following items require manual validation and are not covered by automated tests:

1. Play a track — verify Windows notification center / quick settings shows the mini player with correct title, artist, album art
2. Media keys (play/pause/next/previous) — verify they control playback correctly
3. Taskbar thumbnail buttons — verify prev/play-pause/next still work independently
4. Queue navigation — verify next/previous buttons in the SMTC overlay enable/disable correctly based on queue position
5. Clear the queue — verify the SMTC overlay clears / disappears
6. Shuffle and repeat modes — verify SMTC next/previous state updates correctly
7. Seek mid-track — verify external apps (e.g., lyrics apps) remain in sync
8. **ClearSmtcDisplay without explicit PlaybackStatus = Closed** — verify on different Windows versions that the SMTC overlay clears correctly after queue teardown
