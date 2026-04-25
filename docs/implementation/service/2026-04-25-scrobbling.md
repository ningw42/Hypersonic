# Scrobbling — Implementation Details

Companion to [docs/design/service/2026-04-25-scrobbling.md](../../design/service/2026-04-25-scrobbling.md). This document records what was actually built, decisions made during implementation, and deviations from the original design.

---

## Files Changed

### New files

| File | Purpose |
|---|---|
| `Hypersonic.Core/Models/ScrobbleConditionMode.cs` | OR/AND condition mode enum |
| `Hypersonic/Contracts/Services/IScrobblingService.cs` | Scrobbling service interface |
| `Hypersonic/Services/ScrobblingService.cs` | Scrobbling logic: wall-clock tracking, threshold evaluation, settings management, API calls |
| `Hypersonic.Tests/Services/ScrobblingServiceTests.cs` | 37 tests covering threshold evaluation, guards, toggle behavior, settings validation, Now Playing |

### Modified files

| File | Changes |
|---|---|
| `Hypersonic.Core/Contracts/Services/IPlaybackService.cs` | Added `TrackEndedEventArgs` class (with `TrackId` and `Duration`) and `event EventHandler<TrackEndedEventArgs>? TrackEnded` |
| `Hypersonic/Services/PlaybackService.cs` | Fire `TrackEnded` with typed args at top of `OnMediaEnded` before state mutation or auto-advance |
| `Hypersonic/ViewModels/SettingsViewModel.cs` | Added `IScrobblingService` dependency, scrobbling properties with init guard, `PlaytimePreset` record, `SwitchConditionModeCommand` |
| `Hypersonic/Views/SettingsPage.xaml` | Added Scrobbling section between Personalization and About: master toggle, Now Playing toggle, playtime ComboBox, percentage Slider, condition mode RadioButtons |
| `Hypersonic/Strings/en-us/Resources.resw` | Added 13 localization strings for scrobbling UI |
| `Hypersonic/App.xaml.cs` | Registered `TimeProvider.System` and `IScrobblingService` as singletons |
| `Hypersonic/Services/ActivationService.cs` | Added `App.GetService<IScrobblingService>().InitializeAsync()` call in `ActivateAsync` |
| `Hypersonic.Tests/Hypersonic.Tests.csproj` | Added `Microsoft.Extensions.TimeProvider.Testing` v8.6.0 package reference |
| `Hypersonic.Tests/Fakes/FakePlaybackService.cs` | Added `event EventHandler<TrackEndedEventArgs>? TrackEnded` and `RaiseTrackEnded(string trackId, double duration)` helper |

---

## Design Deviations

### `_trackedTitle` added to tracking state

The design spec defines `_trackedTrackId` and `_trackedDuration` as internal tracking state. The implementation adds `_trackedTitle` (set from `NowPlaying.Title` alongside `_trackedTrackId`).

**Why:** All logging calls in `FireScrobbleAsync` and `EnqueueNowPlayingSendAsync` include `Title` in the context object alongside `TrackId`. Without capturing the title at session start, the log entry for a scrobble would only show an opaque ID. This makes debug-level log output immediately useful when diagnosing threshold behavior ("Scrobble submitted: TrackId=abc, Title=Song Name") without requiring a separate API lookup.

### `_lastScrobbleTask` and `PendingScrobbleTask` / `PendingNowPlayingTask` internal properties

The design spec describes `_nowPlayingSendTask` for serializing Now Playing sends but does not define an equivalent for scrobble submissions. The implementation adds `_lastScrobbleTask` and exposes both as `internal` properties.

**Why:** Tests need to await async API calls that are fire-and-forgotten at call sites (`_ = FireScrobbleAsync(...)`). Without `PendingScrobbleTask` and `PendingNowPlayingTask`, the test's `FlushTasks()` helper would have no way to know when the fire-and-forget tasks complete. The `internal` visibility keeps these out of the public interface while allowing the test project to access them (test project references the app project directly, not through the interface).

### `InvalidCastException` added to `ReadValidatedSettingAsync` catch filter

The design spec's `ReadValidatedSettingAsync` catches `JsonException`, `InvalidOperationException`, and `FormatException`. The implementation also catches `InvalidCastException`.

**Why:** `FakeLocalSettingsService` in tests stores values as `object` in a `Dictionary<string, object?>` and casts on read. When a test stores an `int` and the service reads it as `ScrobbleConditionMode`, the cast throws `InvalidCastException` rather than `JsonException`. This also guards against production scenarios where `ILocalSettingsService` implementations use different internal storage representations. The defense-in-depth cost is negligible.

### `IsEnabled` guard added inside `FireScrobbleAsync`

The design spec shows `FireScrobbleAsync` only guarding against a null `SubsonicClient`. The implementation adds an `if (!IsEnabled) return;` check at the top.

**Why:** `FireScrobbleAsync` is fire-and-forgotten (`_ = FireScrobbleAsync(...)`) — it runs asynchronously after the synchronous call site. If the user disables scrobbling between the moment the scrobble is enqueued and the moment `FireScrobbleAsync` actually executes, the `IsEnabled` check prevents a scrobble from leaking through after the user opted out. This is a timing-window defense that the design spec's synchronous pseudocode didn't need to account for.

### `IsEnabled` and `NowPlayingEnabled` guards inside `EnqueueNowPlayingSendAsync` continuation

The design spec's `EnqueueNowPlayingSendAsync` only checks `_pendingNowPlayingTrackId != trackId` inside the continuation. The implementation also checks `if (!IsEnabled || !NowPlayingEnabled) return;`.

**Why:** Same timing-window reasoning as `FireScrobbleAsync`. The `ContinueWith` continuation runs after the previous send completes, which may be after the user toggled scrobbling or Now Playing off. Without this check, a queued Now Playing could send after the user disabled the feature.

### Scrobbling initialization moved from `InitializeAsync` to `ActivateAsync`

The design spec (Section 2.6) originally placed `App.GetService<IScrobblingService>().InitializeAsync()` inside `ActivationService.InitializeAsync()`. The implementation places it in `ActivateAsync()` — after `InitializeAsync()` returns but before `RestoreWindowState()` — and the design spec has been updated to match.

**Why:** `InitializeAsync()` uses `.ConfigureAwait(false)` on its first two awaits (`_themeSelectorService.InitializeAsync()` and `_backdropSelectorService.InitializeAsync()`), which means subsequent code may resume on a thread pool thread rather than the UI thread. `ScrobblingService.InitializeAsync()` subscribes to `IPlaybackService` events that fire on the UI dispatcher thread. Initializing it from a thread pool thread is safe for the subscription itself, but placing it in `ActivateAsync()` (which always runs on the UI thread) makes the threading contract explicit and avoids any future breakage if `InitializeCoreAsync` grows code that touches UI-thread-affinitied objects. The dependency ordering is preserved: `InitializeAsync()` loads app state before `ActivateAsync()` runs.

### `SwitchConditionModeCommand` takes `int` instead of `ScrobbleConditionMode`

The design spec shows `RelayCommand<ScrobbleConditionMode>`. The implementation uses `RelayCommand<int>` and casts: `var mode = (ScrobbleConditionMode)param`.

**Why:** WinUI XAML's `CommandParameter` sends enum values as their underlying integer type through the command infrastructure. Using `RelayCommand<ScrobbleConditionMode>` would cause a runtime `InvalidCastException` when WinUI tries to pass the boxed enum value. The `int`-and-cast pattern matches the existing `SwitchBackdropCommand` which handles `BackdropType` the same way.

### `ContentControl` wrapper instead of `StackPanel` for disabled sub-controls

The design spec shows `<StackPanel IsEnabled="...">` wrapping the sub-controls. The implementation uses `<ContentControl IsEnabled="..."><StackPanel>...</StackPanel></ContentControl>`.

**Why:** `StackPanel` inherits from `Panel`, not `Control`, and does not have an `IsEnabled` property in WinUI 3. Only `Control`-derived elements expose `IsEnabled`. Wrapping in a `ContentControl` propagates the disabled state to all child controls through the visual tree. This is the standard WinUI pattern for disabling a group of controls that live inside a non-control container.

### Percentage debounce uses `CancellationTokenSource` + `Task.Delay` instead of a timer

The design spec describes a "trailing-edge debounce" conceptually. The implementation uses `CancellationTokenSource` cancellation with `Task.Delay(500, token)` rather than a `DispatcherQueueTimer`.

**Why:** `ScrobblingService` has no dependency on `DispatcherQueue` — it receives events dispatched by `PlaybackService` but doesn't create its own dispatcher infrastructure. Using `CancellationTokenSource` + `Task.Delay` provides the same trailing-edge behavior (each new call cancels the pending delay, only the last one persists) without introducing a WinUI dispatcher dependency into the service. This also makes the debounce testable without a UI thread.

---

## Implementation Notes

### Test infrastructure

The test file defines four test-local fakes alongside `FakePlaybackService` (which lives in `Hypersonic.Tests/Fakes/`):

| Fake | Purpose |
|---|---|
| `NullLogger` / `NullLoggingService` | Silent logging — tests don't assert log output |
| `FakeLocalSettingsService` | In-memory `Dictionary<string, object?>` with a `Set()` helper for pre-seeding values |
| `RecordingSubsonicClient` | Records all `ScrobbleAsync` calls in a `List<(string Id, ulong? Time, bool Submission)>` |
| `FakeSubsonicClientService` | Simple client holder with settable `Client` property |

Tests use `Microsoft.Extensions.Time.Testing.FakeTimeProvider` for deterministic time control. `FakeTimeProvider.Advance()` moves the clock forward, and `GetTimestamp()` / `GetElapsedTime()` reflect the advanced time.

### Test helpers

Four helpers reduce boilerplate across the 37 tests:

- **`InitEnabled()`**: Seeds settings in `FakeLocalSettingsService` and calls `InitializeAsync()`. Parameters default to the standard thresholds (30s/50%/OR) but are overridable.
- **`SimulateTrackStart()`**: Sets up `FakePlaybackService` with a track, marks `IsPlaying = true`, fires `TrackChanged` then `PlaybackStateChanged`. This mirrors the real auto-advance sequence.
- **`AdvanceAndTick(seconds)`**: Advances `FakeTimeProvider` by the given duration, then fires `PositionChanged`. This simulates the 500ms position timer ticks compressed into a single call.
- **`FlushTasks()`**: Awaits both `PendingScrobbleTask` and `PendingNowPlayingTask` to ensure all fire-and-forget API calls have completed before assertions run.

### Wall-clock tracking mechanics in tests

Tests manipulate time exclusively through `FakeTimeProvider.Advance()` — they never set fake position values. This validates that the service truly uses wall-clock time (via `TimeProvider.GetTimestamp()` / `GetElapsedTime()`) rather than the playback position reported by `IPlaybackService.Position`. The `Position` property on `FakePlaybackService` is never set in any scrobbling test.

### Pause simulation

To simulate a pause, tests set `_playback.IsPlaying = false` then fire `RaisePlaybackStateChanged()`. This triggers `OnPlaybackStateChanged` which banks the in-flight wall-clock segment. The `FakeTimeProvider` time continues to advance during the "pause" (simulating real wall-clock passage), but the service correctly does not count it because `_isSegmentActive` is `false`. See `Scrobble_PauseResume_AccumulatesCorrectly` for the canonical example.

### TrackEnded simulation

To simulate a natural track completion, tests:
1. Set `_playback.IsPlaying = false` and fire `PlaybackStateChanged` (banks the segment)
2. Call `_playback.RaiseTrackEnded(trackId, duration)` (fires the failsafe)

In production, the event ordering is different: `PlaybackService.OnMediaEnded` fires `TrackEnded` *first* (before any state mutation), then advances the queue or resets `IsPlaying`. The native `MediaPlayer.CurrentStateChanged` event (Playing→Stopped) may arrive before or after `MediaEnded` depending on dispatcher queue ordering, but `OnMediaEnded`'s handler fires `TrackEnded` before it touches `IsPlaying`.

The test ordering (step 1 before step 2) is a simplification that pre-banks the segment before the failsafe runs. This is safe because `OnTrackEnded` handles both cases: if `_isSegmentActive` is `true` it banks the segment itself, if `false` it proceeds with whatever was already banked. The two orderings produce identical results.

### Now Playing serialization testing

`NowPlaying_RapidSkip_OnlyLatestSent` validates the `ContinueWith` serialization chain. Three tracks are started in rapid succession without awaiting between them. The test asserts that the final Now Playing call sent to the server is for `t3` (the last track). Earlier sends may or may not complete depending on task scheduling, but the serialization ensures `t3` is always last.

### Settings validation test pattern

Settings validation tests (`Settings_Validation_*`) seed an invalid value via `FakeLocalSettingsService.Set()`, then call `InitializeAsync()`, and assert the property fell back to its default. This tests the `ReadValidatedSettingAsync` path. The `Setter_InvalidPlaytime_Rejected` test validates the opposite direction — calling `SetPlaytimeThresholdAsync(99)` after initialization and asserting the in-memory property did not change.

### `FakePlaybackService.RaiseTrackEnded` helper

Added as a public method that constructs `TrackEndedEventArgs` from the given `trackId` and `duration`, then invokes the `TrackEnded` event. This is the only way to simulate `MediaPlayer.MediaEnded` in tests, since the real event originates from the native media pipeline.

```csharp
public void RaiseTrackEnded(string trackId, double duration)
{
    TrackEnded?.Invoke(this, new TrackEndedEventArgs
    {
        TrackId = trackId,
        Duration = duration,
    });
}
```

---

## Verification Results

### Build

`dotnet build Hypersonic.sln -p:Platform=x64` — 0 errors, 0 warnings in new/modified files.

### Automated tests

| Test project | Result |
|---|---|
| `Hypersonic.Tests` | All passing, including 37 new scrobbling tests |
| `Hypersonic.Core.Tests` | All passing (not affected by this change) |
| `Hypersonic.Client.UnitTest` | All passing (not affected by this change) |

### New test coverage

| Test | Scenario |
|---|---|
| `Scrobble_OrMode_PlaytimeFirst` | 240s track, 30s/50% OR — scrobbles at 31s (playtime met first) |
| `Scrobble_OrMode_PercentageFirst` | 20s track, 30s/50% OR — scrobbles at 11s (percentage met first) |
| `Scrobble_AndMode_BothRequired` | 240s track, 30s/50% AND — no scrobble at 31s (only playtime), scrobbles at 121s (both met) |
| `Scrobble_AndMode_CompletionFailsafe` | 20s track, 30s/50% AND, full play — failsafe relaxes playtime to 19, snaps percentage to 100% |
| `Scrobble_AndMode_SeekAbuse_NoScrobble` | 240s track, listen 31s then end — playtime OK but 12.9% < 50%, AND blocks |
| `Scrobble_LongTrack_NoToleranceOnPlaytime` | 240s track, 30s/50% OR, 29s played — no tolerance, 29 < 30 |
| `Scrobble_SeekNearEnd_NoFalseScrobble` | 240s track, 2s listening then end — 2s and 0.8%, both fail |
| `Scrobble_AtMostOnce` | Continue past threshold — exactly 1 scrobble, guard prevents duplicate |
| `Scrobble_SkipNoScrobble` | TrackChanged after 5s without TrackEnded — no scrobble |
| `Scrobble_Disabled_NoScrobble` | IsEnabled=false — no scrobble or Now Playing |
| `Scrobble_DisabledMidTrack_NoScrobble` | Disable after 20s — session reset, no scrobble |
| `Scrobble_DisableCancelsInFlightNowPlaying` | Disable blocks new Now Playing sends for subsequent tracks |
| `Scrobble_DisableReEnable_SessionReset` | Scrobble fires, disable, re-enable — must re-earn threshold |
| `Scrobble_DisableReEnable_NewSessionCanScrobbleAgain` | Re-enabled session counts as new session, can scrobble again |
| `Scrobble_DisabledPlayback_NoSegmentStart` | Playback while disabled — no accumulation, re-enable starts fresh |
| `Scrobble_DurationUnknown_FailsafeUsesFullThreshold` | Duration=0 at TrackEnded — full playtime threshold, no relaxation |
| `Scrobble_CompletionPercentage100_TimingTolerance` | 20s track, 100%/30s AND, 19.8s banked — failsafe: playtime=19 OK, percentage snaps to 100% |
| `Scrobble_DurationZero_PercentageSkipped` | Duration=0 — percentage never met, playtime still works |
| `Scrobble_DurationUpdatedLater` | Duration starts at 0, updated to 60s mid-playback — percentage works after update |
| `Scrobble_RepeatOne_MultipleScrobbles` | TrackEnded + TrackChanged cycle — one scrobble per loop |
| `NowPlaying_SentOnPlaybackConfirmed` | TrackChanged + PlaybackStateChanged — Now Playing sent |
| `NowPlaying_NotSentOnFailedStream` | TrackChanged but never Playing — no Now Playing |
| `NowPlaying_NotSentWhenDisabled` | NowPlayingEnabled=false — no Now Playing |
| `NowPlaying_SentOncePerSession` | Pause/resume — Now Playing sent only on first Playing |
| `NowPlaying_SentOnEnableMidTrack` | Enable while playing — Now Playing sent immediately |
| `NowPlaying_SentViaSafetyNet` | PositionChanged safety net starts segment and sends Now Playing |
| `Scrobble_PauseResume_AccumulatesCorrectly` | 15s + pause + 16s = 31s, pause time excluded |
| `Scrobble_SeekDoesNotInflate` | Wall-clock only — seek doesn't inflate accumulated time |
| `Scrobble_AutoAdvance_SegmentSafetyNet` | TrackChanged while Playing, no PlaybackStateChanged — PositionChanged starts segment |
| `Scrobble_NullClient_NoException` | Null SubsonicClient — no exception, no API calls |
| `Settings_Validation_InvalidPlaytime` | Stored 99 — falls back to 30 |
| `Settings_Validation_OutOfRangePercentage` | Stored 200 — falls back to 50 |
| `Settings_Validation_NonStepPercentage` | Stored 37 — falls back to 50 (not divisible by 5) |
| `Settings_Validation_InvalidConditionMode` | Stored "invalid" — falls back to Or |
| `Initialize_Idempotent` | Concurrent `InitializeAsync()` — events subscribed once, no duplicates |
| `NowPlaying_RapidSkip_OnlyLatestSent` | 3 rapid skips — final Now Playing is for the last track |
| `Setter_InvalidPlaytime_Rejected` | `SetPlaytimeThresholdAsync(99)` — in-memory value unchanged |
