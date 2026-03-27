# SMTC CommandManager Integration — Design Doc

## Context

`PlaybackService` disables `MediaPlaybackCommandManager` (`CommandManager.IsEnabled = false`) and manually manages all SMTC interactions: button handling, playback status sync, and display metadata. The code comment states this prevents SMTC from adding its own taskbar thumbnail buttons, since Hypersonic uses `ITaskbarList3` COM interop for those. However, `CommandManager` manages the **SMTC media session** (system overlay, media keys, global session registration), while taskbar thumbnail buttons are a separate Win32 shell feature managed by `ITaskbarList3`. These two systems do not conflict.

### Problems with the current approach

1. **Suspected missing global session visibility.** With `CommandManager` disabled, Hypersonic may not register a media session with `GlobalSystemMediaTransportControlsSessionManager`. An external app using `GetSessions()` was unable to discover Hypersonic during testing. The current manual SMTC integration (setting `SystemMediaTransportControls` properties directly) handles media keys and in-process SMTC state, but it is unconfirmed whether manual integration alone is sufficient for global session registration. **This is a hypothesis to verify during implementation** — the first step should be confirming that re-enabling `CommandManager` resolves the visibility issue before committing to the full refactor.

2. **Duplicate metadata code.** Display properties are set on the `MediaPlaybackItem` (lines 326-335) AND identically on the SMTC `DisplayUpdater` (lines 346-355). With `CommandManager` enabled, the latter is unnecessary — the `CommandManager` automatically propagates `MediaPlaybackItem` display properties to SMTC.

3. **Redundant boilerplate.** The manual `OnSmtcButtonPressed` handler, `PlaybackStatus` sync in `OnCurrentStateChanged`, and SMTC `IsEnabled`/`IsPlayEnabled`/`IsPauseEnabled` setup are all behaviors that `CommandManager` provides automatically.

---

## Goal

Re-enable `CommandManager` so that:

- Hypersonic registers as a global media session, visible to external apps and the system media overlay.
- SMTC metadata (title, artist, album, thumbnail) is automatically synced from `MediaPlaybackItem` display properties.
- Play/pause and playback status are automatically managed.
- Next/previous commands are routed through the custom `PlaybackQueue` via `CommandManager` events.
- `ITaskbarList3` taskbar thumbnail buttons continue to work independently (no conflict).

---

## Design

### Re-enable CommandManager

Remove `_mediaPlayer.CommandManager.IsEnabled = false`. The `CommandManager` is enabled by default, so simply deleting this line restores automatic SMTC integration.

### Hook NextReceived / PreviousReceived for custom queue

The `CommandManager` does not know about `PlaybackQueue`. Without a `MediaPlaybackList`, it has no built-in next/previous behavior. Hook the `CommandManager` events to route these commands through our queue:

```csharp
var cmd = _mediaPlayer.CommandManager;
cmd.NextReceived += OnCommandManagerNextReceived;
cmd.PreviousReceived += OnCommandManagerPreviousReceived;
```

Handlers use a deferral to hold the `CommandManager` until the dispatched work completes, then set `args.Handled = true` to prevent the `CommandManager` from attempting its own (no-op) default behavior. The deferral ensures the command is not considered "handled" until the queue operation has actually executed — if `TryEnqueue` fails, the deferral completes without setting `Handled`, allowing the `CommandManager` to fall through to its default behavior (which is a no-op, but keeps the contract honest):

```csharp
private void OnCommandManagerNextReceived(MediaPlaybackCommandManager sender,
    MediaPlaybackCommandManagerNextReceivedEventArgs args)
{
    var deferral = args.GetDeferral();
    var enqueued = _dispatcherQueue.TryEnqueue(() =>
    {
        args.Handled = true;
        NextTrack();
        deferral.Complete();
    });

    if (!enqueued)
    {
        deferral.Complete();
    }
}

private void OnCommandManagerPreviousReceived(MediaPlaybackCommandManager sender,
    MediaPlaybackCommandManagerPreviousReceivedEventArgs args)
{
    var deferral = args.GetDeferral();
    var enqueued = _dispatcherQueue.TryEnqueue(() =>
    {
        args.Handled = true;
        PreviousTrack();
        deferral.Complete();
    });

    if (!enqueued)
    {
        deferral.Complete();
    }
}
```

### Control next/previous button enabled state

Without a `MediaPlaybackList`, the `CommandManager` defaults next/previous to disabled. Use `EnablingRule` to control visibility based on queue state:

```csharp
private void UpdateSmtcState()
{
    var cmd = _mediaPlayer.CommandManager;
    cmd.NextBehavior.EnablingRule = HasNext
        ? MediaCommandEnablingRule.Always
        : MediaCommandEnablingRule.Never;
    cmd.PreviousBehavior.EnablingRule = HasPrevious
        ? MediaCommandEnablingRule.Always
        : MediaCommandEnablingRule.Never;
}
```

All existing call sites of `UpdateSmtcState()` remain unchanged.

### Clear SMTC display on empty queue

`ClearSmtcDisplay()` still needs manual intervention when the queue is emptied — the `CommandManager` has no concept of "no media":

```csharp
private void ClearSmtcDisplay()
{
    var updater = _mediaPlayer.SystemMediaTransportControls.DisplayUpdater;
    updater.ClearAll();
    updater.Update();
}
```

The explicit `PlaybackStatus = Closed` line may be removable — setting `_mediaPlayer.Source = null` (which already happens before `ClearSmtcDisplay` is called) is expected to cause the `CommandManager` to update the status automatically. **Verify during implementation**: if the SMTC overlay does not clear correctly after queue teardown without the explicit `PlaybackStatus = Closed`, retain that line.

---

## What gets removed

| Code | Lines | Reason |
|---|---|---|
| `_mediaPlayer.CommandManager.IsEnabled = false` | 98 | Suspected cause of missing global session; disables automatic SMTC integration |
| `var smtc = _mediaPlayer.SystemMediaTransportControls;` + `smtc.IsEnabled/IsPlayEnabled/IsPauseEnabled` setup | 101-106 | `CommandManager` enables these automatically |
| `smtc.ButtonPressed += OnSmtcButtonPressed` | 107 | Replaced by `CommandManager.NextReceived`/`PreviousReceived` |
| `OnSmtcButtonPressed` handler | 385-405 | Replaced by `CommandManager` event handlers |
| Manual `PlaybackStatus` sync in `OnCurrentStateChanged` | 473-479 | `CommandManager` syncs status automatically |
| Duplicate `DisplayUpdater` block in `PlayCurrent()` | 343-355 | `CommandManager` propagates `MediaPlaybackItem` display properties automatically |
| `smtc.IsNextEnabled`/`IsPreviousEnabled` in `UpdateSmtcState()` | 362-364 | Replaced by `NextBehavior.EnablingRule`/`PreviousBehavior.EnablingRule` |
| `smtc.PlaybackStatus = Closed` in `ClearSmtcDisplay()` | 370 | Expected to be handled by `CommandManager` when source is set to null — verify during implementation; retain if SMTC overlay does not clear correctly without it |
| `smtc.ButtonPressed -=` in `Dispose()` | 501 | Event no longer subscribed |

## What stays

| Code | Reason |
|---|---|
| `MediaPlaybackItem` display properties (lines 326-335) | `CommandManager` reads these to populate SMTC — this is the input |
| `ClearSmtcDisplay()` (modified) | Still needed for explicit queue teardown |
| `UpdateSmtcState()` (modified) | Still needed to control next/previous `EnablingRule` |
| All `PlaybackQueue` logic | Unrelated to SMTC transport — queue management is independent |

## What gets added

| Code | Purpose |
|---|---|
| `CommandManager.NextReceived` handler | Routes next command to `PlaybackQueue` |
| `CommandManager.PreviousReceived` handler | Routes previous command to `PlaybackQueue` |
| `NextBehavior.EnablingRule` / `PreviousBehavior.EnablingRule` in `UpdateSmtcState()` | Controls button visibility based on queue state |
| Unsubscribe new handlers in `Dispose()` | Cleanup |

---

## Benefits observed after implementation

### Improved global session accuracy

With `CommandManager` disabled, playback status was reported to the global SMTC session via the manual sync in `OnCurrentStateChanged`, which ran inside a `DispatcherQueue.TryEnqueue()` callback. This added a dispatcher round-trip of latency to every SMTC status update. External apps consuming the session (e.g., lyrics apps using `GlobalSystemMediaTransportControlsSessionManager`) would see the "Playing" state slightly after audio actually started, causing a small but consistent sync offset.

With `CommandManager` enabled, SMTC status is reported synchronously from within the media pipeline, eliminating that delay.

### Timeline properties now reported to global session

The manual SMTC implementation never synchronized `TimelineProperties` with the global session — neither the current position, playback rate, nor position changes from seek were reported. External apps relying on the global session's timeline had no accurate position information, causing them to drift or lose sync entirely.

`CommandManager` automatically maintains `TimelineProperties` from `MediaPlayer` state, so external apps see accurate position and seek events immediately.

---

## ITaskbarList3 interaction

No conflict. `ITaskbarList3` thumbnail toolbar buttons are a Win32 shell feature that operates on the window's HWND via COM interop. `CommandManager` operates on the UWP/WinRT SMTC layer. They are completely independent systems:

- `ITaskbarList3` buttons → rendered in the taskbar preview, handled via `WM_COMMAND` window messages → `TaskbarService`
- `CommandManager` → manages the SMTC session (media keys, system overlay, global session registry) → `PlaybackService`

The original comment ("so SMTC doesn't add its own taskbar thumbnail buttons") was incorrect — SMTC never adds taskbar thumbnail buttons.

---

## Files to modify

| File | Changes |
|---|---|
| `Hypersonic/Services/PlaybackService.cs` | Remove manual SMTC code, add `CommandManager` event hooks |

No new files. No interface changes. No changes to `PlaybackQueue`, `TaskbarService`, `PlayerViewModel`, or any other consumer.

---

## Verification

**Build:**
- `dotnet build Hypersonic.sln -p:Platform=x64` — must compile cleanly.

**Existing tests (no regressions):**
- `dotnet test Hypersonic.Tests.MSTest`
- `dotnet test Hypersonic.Client.UnitTest`
- `dotnet test Hypersonic.UnitTest` — `PlaybackQueueTests` cover `HasNext`/`HasPrevious` semantics that drive `UpdateSmtcState()`

**Manual testing — phase 1 (validate hypothesis):**
1. Re-enable `CommandManager` only (minimal change). Play a track. Verify an external app using `GlobalSystemMediaTransportControlsSessionManager.GetSessions()` can now discover Hypersonic's session. If this does not resolve the visibility issue, investigate further before proceeding with the full refactor.

**Manual testing — phase 2 (full refactor):**
1. Play a track. Verify the Windows notification center / quick settings shows the Hypersonic mini player with correct title, artist, album art.
2. Use media keys (play/pause/next/previous). Verify they control playback correctly.
3. Verify taskbar thumbnail buttons (prev/play-pause/next) still work independently.
4. Play through a queue. Verify next/previous buttons in the SMTC overlay enable/disable correctly based on queue position.
5. Clear the queue. Verify the SMTC overlay clears / disappears.
6. Test with shuffle and repeat modes — verify SMTC next/previous state updates correctly.
