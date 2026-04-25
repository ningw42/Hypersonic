# Scrobbling — Feature Spec & Design Spec

## Context

Scrobbling reports listened tracks to the Subsonic server, updating play count and last-played timestamp. Navidrome and other OpenSubsonic servers can forward scrobbles to Last.fm, ListenBrainz, or other external services based on server-side configuration. This design covers client-side scrobble triggering only — the server handles any downstream forwarding.

The Subsonic API provides a single endpoint (`scrobble.view`, since API v1.5.0) that serves two purposes:

| Action | `submission` param | Purpose |
|---|---|---|
| **Scrobble submission** | `true` (default) | Records a completed listen — updates play count, last-played timestamp, and triggers server-side forwarding to external services |
| **Now Playing notification** | `false` | Reports the currently playing track without recording a listen — visible via server dashboard and `getNowPlaying` endpoint |

Full parameter set:

| Parameter | Required | Type | Description |
|---|---|---|---|
| `id` | Yes | string | Song ID to scrobble |
| `time` | No | long (ms since epoch) | When the song was listened to (v1.8.0+) |
| `submission` | No | bool | `true` = scrobble, `false` = now playing |

The `Hypersonic.Client` already implements this endpoint as `SubsonicClient.ScrobbleAsync(string id, ulong? time, bool submission)`. The constant `Constants.Action.Scrobble` is defined. No new client-side API work is needed.

---

## Part 1: Feature Spec

### 1.1 Scrobble Actions

Two independently toggleable actions, both targeting `scrobble.view`:

| Action | When Sent | Default |
|---|---|---|
| **Scrobble submission** | When configured threshold conditions are met during playback | Disabled |
| **Now Playing notification** | When a new track starts playing | Enabled (when scrobbling is on) |

Both require the master scrobbling toggle to be on. Now Playing is a sub-toggle — it cannot be enabled independently of scrobbling.

**Default-off rationale**: Scrobbling records listening data and the server may forward it to third-party services (Last.fm, ListenBrainz). Users should opt in explicitly. The master toggle defaults to **disabled**; Now Playing defaults to enabled *within* the scrobbling feature, so enabling the master toggle activates both without extra clicks.

### 1.2 Threshold Configuration

Scrobbling uses two independently configured thresholds evaluated against **accumulated playtime** (see Section 1.3):

**Playtime threshold** — minimum accumulated listening time in seconds. Presets: 15s, 30s, 60s, 120s, 240s. Default: **30s**.

**Percentage threshold** — minimum percentage of track duration that must be listened to. Range: 10–100% in 5% increments. Default: **50%**.

**Condition mode** — how the two thresholds combine:

- **Either (OR)** (default): Scrobble fires when the **first** threshold is met — whichever comes first. Note: with the default 30s playtime, long tracks will scrobble after just 30 seconds of listening. This matches Last.fm's 30-second minimum but is more aggressive than their 50%/4-minute rule. The Settings UI should make the thresholds visible so users can adjust.
- **Both (AND)**: Scrobble fires only when **both** thresholds are met simultaneously.

### 1.3 Accumulated Playtime

Both thresholds are evaluated against *accumulated playtime*, not raw playback position. This measures actual seconds of listening using wall-clock time deltas:

- When playback enters the **playing** state, the service records `_segmentStart = _timeProvider.GetTimestamp()`.
- When playback **leaves** the playing state (pause, stop, track end), the service banks the elapsed wall-clock time: `_accumulatedPlaytime += _timeProvider.GetElapsedTime(_segmentStart).TotalSeconds`.
- Threshold evaluation happens on each position timer tick. The evaluator computes `currentPlaytime = _accumulatedPlaytime + _timeProvider.GetElapsedTime(_segmentStart).TotalSeconds` to include the in-flight segment without banking it yet.

This approach:
- **Pauses** do not contribute — the segment ends and is banked, no new segment starts.
- **Seeks** do not inflate — seek does not change `IsPlaying`, so the wall-clock segment continues unaffected. The seek itself takes near-zero real time.
- **Non-timer `PositionChanged` events** are irrelevant — accumulated playtime is based on wall-clock time, not event counting.

The percentage check formula: `(currentPlaytime / trackDuration) × 100 ≥ percentageThreshold`.

### 1.4 Play Session Definition

A **play session** is the unit of scrobble tracking. One play session = at most one scrobble.

A new play session starts when:
- `TrackChanged` fires (new track, skip, auto-advance, repeat-one loop, queue jump)

In repeat-one mode, the sequence is: `MediaEnded` → `TrackEnded` (failsafe evaluates for current session) → `Advance()` returns same track → `PlayCurrent()` → `TrackChanged` (resets state, starts new session). The `TrackEnded` event closes the old session; the `TrackChanged` event opens the new one.

A play session is **not** reset by:
- Pause / Resume (same track, same session)
- Seek (same track, same session)
- Stop then Resume on the same track (same session, accumulated time preserved)

When the user stops playback (`Stop()`) and later resumes the same track, the accumulated time from before the stop carries over. The guard flag also carries over — if the track was already scrobbled before the stop, it will not scrobble again on resume. Similarly, `PreviousTrack()` when position is past 3 seconds calls `Seek(0)` rather than changing tracks, so accumulated playtime from before the restart is preserved. This means multiple partial listens of the same track within one session can combine to meet the threshold. This is intentional: the user listened to enough of the track in aggregate to justify a scrobble.

### 1.5 Scrobble Rules

1. **At most once per play session.** A guard flag prevents duplicate submissions. The flag is set when a scrobble fires and resets when a new play session starts.

2. **Track completion failsafe.** If a track finishes naturally (the `TrackEnded` event fires, see Section 2.2) and no scrobble has been sent yet, evaluate thresholds with two completion-specific adjustments:

   1. **Playtime relaxation for short tracks**: When the track duration is known and shorter than `PlaytimeThreshold`, the effective playtime threshold is lowered to `_trackedDuration - 1.0` (with a 1s tolerance for timing imprecision). When the track is longer than the threshold, the exact `PlaytimeThreshold` is used with no tolerance. When duration is unknown, the full threshold is used.
   2. **Percentage snap-to-100%**: When accumulated playtime is within 2s of the known track duration, percentage is treated as 100% instead of computing the raw ratio. This prevents 99% failures from wall-clock imprecision on naturally completed tracks.

   Both adjustments only apply in the `OnTrackEnded` failsafe — normal `OnPositionChanged` evaluation uses exact thresholds with no tolerance. The configured condition mode (OR or AND) is always preserved.

   Concretely:
   ```
   // Playtime: relax only when track is shorter than threshold (impossible to reach)
   if (_trackedDuration > 0 && _trackedDuration < PlaytimeThreshold)
       effectivePlaytime = max(_trackedDuration - 1.0, 0)       // 1s timing tolerance
   else
       effectivePlaytime = PlaytimeThreshold                     // exact, no tolerance
   playtimeMet       = _accumulatedPlaytime >= effectivePlaytime

   // Percentage: snap to 100% when close to duration (timing imprecision)
   actualPercentage  = (_trackedDuration > 0)
       ? ((_accumulatedPlaytime >= _trackedDuration - 2.0) ? 100.0
           : (_accumulatedPlaytime / _trackedDuration * 100))
       : 0
   percentageMet     = actualPercentage >= PercentageThreshold

   shouldScrobble    = (ConditionMode == Or)
                       ? (playtimeMet || percentageMet)
                       : (playtimeMet && percentageMet)
   ```

   The 1-second playtime tolerance only activates for short tracks where the threshold was relaxed to the track duration. For long tracks, the exact `PlaytimeThreshold` is used — so a 240s track with a 30s threshold requires exactly 30s of accumulated playtime, even in the failsafe. The 2-second percentage snap-to-100% prevents a fully played track from computing 99% due to `TimeProvider` granularity. Both adjustments only apply in `OnTrackEnded`.

   This prevents:
   - **Missed scrobbles on short tracks**: A 20s track with 30s/50% AND → `effectivePlaytime = max(20 - 1, 0) = 19`. After full play (~19.8s banked): `playtimeMet = 19.8 >= 19 ✓`, `percentageMet = 100% ✓`, AND passes.
   - **False scrobbles from seek-to-end abuse**: A 240s track, listen 30s, seek near end → `effectivePlaytime = 30` (no relaxation, track is longer). `playtimeMet = 30 >= 30 ✓`, but `percentageMet = 30/240 = 12.5% < 50% ✗`. AND correctly blocks.
   - **Weakened threshold on long tracks**: A 240s track with 30s OR, listen ~29s, seek to end → `effectivePlaytime = 30` (exact). `playtimeMet = 29 >= 30 ✗`, `percentageMet = 29/240 = 12% < 50% ✗`. No scrobble.
   - **Seek-near-end with minimal listening**: 2s on a 240s track → `playtimeMet = 2 >= 30 ✗`, `percentageMet = 0.8% ✗`. Both modes block.
   - **Over-scrobbling with unknown duration**: If duration is still 0, `effectivePlaytime = PlaytimeThreshold` (no relaxation), and percentage evaluates to 0%. Only the full playtime threshold can trigger.

   The failsafe **only** runs on the explicit `TrackEnded` event — not on skip, seek-to-end, stop, or any other track change reason.

3. **Repeat-one support.** In repeat-one mode, `OnMediaEnded` fires `TrackEnded` (closing the current session with a failsafe evaluation), then calls `PlaybackQueue.Advance()` which returns the same track, then `PlayCurrent()` fires `TrackChanged` (opening a new session). Each loop gets its own play session — allowing one scrobble per loop.

4. **IsEnabled guard.** All scrobble paths (threshold evaluation, completion failsafe, now-playing) check `IsEnabled` before firing. Disabling scrobbling **detaches from the current track entirely**: clears `_trackedTrackId`, zeroes accumulated playtime, and stops the active segment. No scrobble or failsafe can fire for the detached track. Re-enabling **re-attaches to whatever is currently playing** as a completely fresh session (zero accumulated time, guard flag cleared). This prevents duplicate scrobbles after disable/re-enable on the same track and ensures the toggle is an immediate privacy opt-out.

### 1.6 Settings

All scrobbling configuration is persisted via `ILocalSettingsService` (settings, not state). Changes are saved immediately for discrete controls (toggles, radio buttons, combo box). The percentage slider uses a **trailing-edge debounce** — the in-memory value updates immediately, but the disk write is delayed by 500ms and resets on each new change, so only the final value in a drag gesture hits disk.

| Key | Type | Default | Validation |
|---|---|---|---|
| `"ScrobblingEnabled"` | `bool` | `false` | — |
| `"ScrobbleNowPlaying"` | `bool` | `true` | — |
| `"ScrobblePlaytimeThreshold"` | `int` | `30` | Must be one of [15, 30, 60, 120, 240]; invalid → 30 |
| `"ScrobblePercentageThreshold"` | `int` | `50` | Must be in [10, 100] and divisible by 5; invalid → 50 |
| `"ScrobbleConditionMode"` | `string` | `"Or"` | Must parse to enum; invalid → `Or` |

**Validation**: On `InitializeAsync`, each key is read inside a try/catch that catches `JsonException`, `InvalidOperationException`, and `FormatException` (the exceptions `ILocalSettingsService.ReadSettingAsync<T>` can throw on corrupt or wrong-type data). On any such exception, the key falls back to its documented default and a Warning-level log is emitted (with the exception attached). Validation failures (in-range but invalid values) log at Debug level. No error is surfaced to the user. Example pattern:

```csharp
private async Task<T> ReadValidatedSettingAsync<T>(string key, T defaultValue, Func<T, bool>? validate = null)
{
    try
    {
        var value = await _localSettingsService.ReadSettingAsync<T>(key);
        if (value is null) return defaultValue;
        if (validate != null && !validate(value))
        {
            _logger.Debug($"Setting '{key}' failed validation, using default",
                context: new { key, value, defaultValue });
            return defaultValue;
        }
        return value;
    }
    catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
    {
        _logger.Warning($"Setting '{key}' unreadable, using default",
            context: new { key, defaultValue }, exception: ex);
        return defaultValue;
    }
}
```

Used during initialization:

```csharp
IsEnabled = await ReadValidatedSettingAsync(EnabledKey, false);
NowPlayingEnabled = await ReadValidatedSettingAsync(NowPlayingKey, true);
PlaytimeThreshold = await ReadValidatedSettingAsync(PlaytimeKey, 30,
    v => ValidPlaytimePresets.Contains(v));
PercentageThreshold = await ReadValidatedSettingAsync(PercentageKey, 50,
    v => v >= 10 && v <= 100 && v % 5 == 0);
ConditionMode = await ReadValidatedSettingAsync(ConditionModeKey, ScrobbleConditionMode.Or,
    v => Enum.IsDefined(v));
```

### 1.7 Configuration UI

New **Scrobbling** section on SettingsPage, inserted between Personalization and About:

```
Scrobbling
────────────────────────────────────────────────
[ToggleSwitch] Enable scrobbling

  [ToggleSwitch] Send "Now Playing" notifications

  Playtime threshold
  [ 30 seconds           ▾]     ← ComboBox

  Percentage threshold
  [────────●─────────] 50%      ← Slider + label

  Condition mode
  (●) Either threshold (OR)     ← RadioButton, default
  ( ) Both thresholds (AND)     ← RadioButton
────────────────────────────────────────────────
```

All sub-controls (everything below the master toggle) are disabled when scrobbling is off, using `IsEnabled` binding to the master toggle.

### 1.8 Not In Scope

- Direct third-party scrobbling (Last.fm API, ListenBrainz, Maloja). Server handles forwarding.
- Offline scrobble queue or retry on network failure.
- Per-track scrobble history or statistics UI.
- Scrobble on app close (if thresholds not yet met when the process exits, that scrobble is lost).
- Minimum track duration filter. Very short tracks (interludes, transitions) will scrobble if thresholds are met. Server-side filtering (e.g., Navidrome's Last.fm plugin minimum duration) is the intended guard.
- Batch scrobbling (the API supports multiple `id` params since v1.8.0, but we scrobble one at a time).

---

## Part 2: Design Spec

### 2.1 New Model

**`ScrobbleConditionMode`** — new file: `Hypersonic.Core/Models/ScrobbleConditionMode.cs`

```csharp
namespace Hypersonic.Core.Models;

public enum ScrobbleConditionMode
{
    Or,
    And
}
```

Serialized as `"Or"` / `"And"` by the existing `JsonStringEnumConverter` in `Json.Options`.

### 2.2 PlaybackService: Add `TrackEnded` Event

The current `PlaybackService.OnMediaEnded` handler either auto-advances to the next track (fires `TrackChanged`) or resets position/duration to 0 and fires `PlaybackStateChanged`. In the end-of-queue case, position and duration are already 0 by the time any subscriber sees `PlaybackStateChanged`, making it impossible to detect natural completion from that event alone.

**Solution**: Add a `TrackEnded` event to `IPlaybackService` that fires **inside `OnMediaEnded`, before any state mutation or auto-advance**. This gives subscribers an unambiguous "the media player reached the end of this track" signal with position and duration still intact.

**`IPlaybackService`** — add event and args type:

```csharp
public class TrackEndedEventArgs : EventArgs
{
    public required string TrackId { get; init; }
    public double Duration { get; init; }
}

event EventHandler<TrackEndedEventArgs>? TrackEnded;
```

Using typed args captures the track identity and duration at the moment `MediaEnded` fires, before any state mutation. This makes the completion signal independent of mutable `IPlaybackService` properties and future event-ordering changes.

**`PlaybackService.OnMediaEnded`** — modified:

```csharp
private void OnMediaEnded(MediaPlayer sender, object args)
{
    _dispatcherQueue.TryEnqueue(() =>
    {
        // Fire TrackEnded BEFORE any state changes.
        // Capture track identity and duration now, while they are still valid.
        if (NowPlaying != null)
        {
            TrackEnded?.Invoke(this, new TrackEndedEventArgs
            {
                TrackId = NowPlaying.Id,
                Duration = Duration
            });
        }

        var prevHasNext = HasNext;
        var prevHasPrevious = HasPrevious;
        var next = _playbackQueue.Advance();

        if (next != null)
        {
            PlayCurrent();
            FirePlaybackStateChangedIfHasChanged(prevHasNext, prevHasPrevious);
        }
        else
        {
            _log.Info("Queue ended");
            IsPlaying = false;
            Position = 0;
            Duration = 0;
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
            PositionChanged?.Invoke(this, EventArgs.Empty);
            UpdateSmtcState();
        }
    });
}
```

`TrackEnded` fires **only** when `MediaPlayer.MediaEnded` fires — i.e., the media player reached the end of the stream. It does **not** fire on skip, stop, seek, queue removal, or any other track change. This is the explicit completion signal the scrobbling service needs to distinguish natural completion from all other transitions.

### 2.3 Scrobbling Service

**`IScrobblingService`** — new file: `Hypersonic/Contracts/Services/IScrobblingService.cs`

```csharp
namespace Hypersonic.Contracts.Services;

public interface IScrobblingService
{
    Task InitializeAsync();

    bool IsEnabled { get; }
    bool NowPlayingEnabled { get; }
    int PlaytimeThreshold { get; }
    int PercentageThreshold { get; }
    ScrobbleConditionMode ConditionMode { get; }

    Task SetEnabledAsync(bool enabled);
    Task SetNowPlayingEnabledAsync(bool enabled);
    Task SetPlaytimeThresholdAsync(int seconds);
    Task SetPercentageThresholdAsync(int percentage);
    Task SetConditionModeAsync(ScrobbleConditionMode mode);
}
```

The interface exposes settings as read-only properties (for ViewModel binding) and setter methods (for persistence). Runtime tracking state is internal to the implementation.

**`SetEnabledAsync` segment management**: Beyond persisting the setting, this method manages the wall-clock segment to prevent disabled listening time from being counted:

```
SetEnabledAsync(false):
    IsEnabled = false
    // Detach from current track — disabling is an immediate privacy opt-out.
    // Clearing _trackedTrackId means no scrobble or failsafe can fire for this track.
    // The next TrackChanged will re-attach to whatever is playing at that point.
    _trackedTrackId = null
    _trackedDuration = 0
    _accumulatedPlaytime = 0
    _hasScrobbled = false
    _nowPlayingSent = false
    _isSegmentActive = false
    persist setting

SetEnabledAsync(true):
    IsEnabled = true
    // Re-attach to the current track (if any) as a fresh session.
    _trackedTrackId = playbackService.NowPlaying?.Id
    _trackedDuration = playbackService.Duration
    _accumulatedPlaytime = 0
    _hasScrobbled = false
    _nowPlayingSent = false
    _isSegmentActive = false
    if (playbackService.IsPlaying && _trackedTrackId != null)
    {
        _segmentStart = _timeProvider.GetTimestamp()
        _isSegmentActive = true
        TrySendNowPlaying()
    }
    persist setting
```

**Setter runtime validation**: All setter methods validate their input before updating in-memory state or persisting. Invalid values are rejected — the in-memory property is not changed and the save is skipped. This mirrors the read-time validation in `ReadValidatedSettingAsync` and prevents the UI from writing garbage values via binding glitches or programmatic errors.

```csharp
SetPlaytimeThresholdAsync(int seconds):
    if (!ValidPlaytimePresets.Contains(seconds)) return   // reject, no-op
    PlaytimeThreshold = seconds
    persist setting

SetPercentageThresholdAsync(int percentage):
    if (percentage < 10 || percentage > 100 || percentage % 5 != 0) return
    PercentageThreshold = percentage
    persist setting (debounced)

SetConditionModeAsync(ScrobbleConditionMode mode):
    if (!Enum.IsDefined(mode)) return
    ConditionMode = mode
    persist setting
```

All setter persist calls are wrapped in try/catch — on `SaveSettingAsync` failure, log at Warning level and continue. The in-memory value is already updated, so the service operates correctly for the rest of the session; only crash-recovery is affected.

**Dependencies** (constructor-injected):

| Dependency | Purpose |
|---|---|
| `IPlaybackService` | Subscribe to `TrackChanged`, `TrackEnded`, `PositionChanged`, `PlaybackStateChanged` |
| `ISubsonicClientService` | Access `SubsonicClient` for API calls |
| `ILocalSettingsService` | Read/write settings |
| `ILoggingService` | Error logging |
| `TimeProvider` | Injectable clock for `GetTimestamp()` / `GetElapsedTime()`. Production uses `TimeProvider.System`; tests inject `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing` for deterministic time advancement. |

**Internal tracking state** (reset per play session):

```csharp
private string? _trackedTrackId;          // ID of the track being monitored
private double _trackedDuration;           // Duration of the tracked track (seconds)
private double _accumulatedPlaytime;       // Banked seconds of actual listening
private long _segmentStart;                // TimeProvider timestamp when current playing segment began
private bool _isSegmentActive;             // True while a playing segment is in progress
private bool _hasScrobbled;                // Guard flag — true after scrobble fires
private bool _nowPlayingSent;              // True after Now Playing sent for current track
private string? _pendingNowPlayingTrackId; // Latest track ID requested for Now Playing
private Task _nowPlayingSendTask = Task.CompletedTask; // Serializes Now Playing HTTP sends
```

**Initialization** (`InitializeAsync`):

Guarded by `_initializeTask` with a lock to coalesce concurrent or repeated calls. If `InitializeCoreAsync` faults, the cached task is reset so the next call retries:

```csharp
private readonly object _initLock = new();
private Task? _initializeTask;

public Task InitializeAsync()
{
    lock (_initLock)
    {
        if (_initializeTask is { IsFaulted: true })
            _initializeTask = null;
        return _initializeTask ??= InitializeCoreAsync();
    }
}
```

1. Read all five settings keys from `ILocalSettingsService`, validate each (see Section 1.6), fall back to defaults for missing/invalid keys.
2. Subscribe to `IPlaybackService` events: `TrackChanged`, `TrackEnded`, `PositionChanged`, `PlaybackStateChanged`.
3. Snapshot current playback state: if a track is already playing when the service initializes, set `_trackedTrackId` and `_trackedDuration` from current service properties. Only start a wall-clock segment if `IsEnabled` is true — otherwise the segment starts later when the user enables scrobbling via `SetEnabledAsync(true)`.

**Event handler: `OnTrackEnded(TrackEndedEventArgs e)`**

Fires when `MediaPlayer.MediaEnded` fires — the track played to completion. Uses `e.TrackId` and `e.Duration` from the event args rather than reading mutable service state.

```
1. Guard: if (!IsEnabled || _trackedTrackId == null || e.TrackId != _trackedTrackId) return

2. Bank any in-flight playing segment:
   if (_isSegmentActive)
       _accumulatedPlaytime += _timeProvider.GetElapsedTime(_segmentStart).TotalSeconds
       _isSegmentActive = false

3. Re-read duration from event args if still unknown:
   if (_trackedDuration <= 0)
       _trackedDuration = e.Duration

4. Completion failsafe (relaxed playtime for short tracks, configured condition mode):
   if (!_hasScrobbled && _accumulatedPlaytime > 0)
       // Relax playtime only when track is shorter than threshold (impossible to reach).
       // For long tracks, use exact threshold — no tolerance.
       if (_trackedDuration > 0 && _trackedDuration < PlaytimeThreshold)
           effectivePlaytime = max(_trackedDuration - 1.0, 0)
       else
           effectivePlaytime = PlaytimeThreshold
       playtimeMet = _accumulatedPlaytime >= effectivePlaytime

       // Snap to 100% when close to duration (timing imprecision on natural completion).
       actualPercentage = (_trackedDuration > 0)
           ? ((_accumulatedPlaytime >= _trackedDuration - 2.0)
               ? 100.0
               : (_accumulatedPlaytime / _trackedDuration * 100))
           : 0
       percentageMet = actualPercentage >= PercentageThreshold

       shouldScrobble = (ConditionMode == Or)
           ? (playtimeMet || percentageMet)
           : (playtimeMet && percentageMet)

       if (shouldScrobble)
           _hasScrobbled = true
           fire scrobble for _trackedTrackId
```

The only differences from normal threshold evaluation (`OnPositionChanged`) are: (1) `effectivePlaytime` is relaxed to `_trackedDuration - 1.0` only when the track is shorter than the configured threshold (impossible to reach normally), with no tolerance applied for long tracks, (2) percentage snaps to 100% when accumulated time is within 2s of duration, and (3) duration is re-read one final time in step 3 to capture any late `MediaOpened` update.

**Event handler: `OnTrackChanged`**

Fires after `OnTrackEnded` (if auto-advancing) or independently (skip, queue jump, etc.).

```
1. Reset tracking state:
   _trackedTrackId = playbackService.NowPlaying?.Id
   _trackedDuration = playbackService.Duration
   _accumulatedPlaytime = 0
   _hasScrobbled = false
   _nowPlayingSent = false
   _isSegmentActive = false

2. Do NOT start a wall-clock segment here.
   During auto-advance, IsPlaying may still reflect the previous track's
   state — the media player has not yet entered Playing for the new source.
   The segment will start reliably when OnPlaybackStateChanged detects
   the IsPlaying → true transition (or OnCurrentStateChanged fires Playing
   for the new media item).

3. Now Playing is NOT sent here — deferred to the segment-start paths
   (OnPlaybackStateChanged or OnPositionChanged safety net) when
   playback is confirmed active for the new source.
```

Note: The failsafe for the *previous* track is handled by `OnTrackEnded`, not here. `OnTrackChanged` only resets state for the *new* track. This means skips (which fire `TrackChanged` without `TrackEnded`) correctly do **not** trigger a failsafe scrobble for the skipped track. The segment is deliberately not started here because `IsPlaying` may still reflect the previous track's state during auto-advance; the segment-start paths (`OnPlaybackStateChanged` or `OnPositionChanged` safety net) will start the segment and send Now Playing once playback is confirmed for the new source.

**Event handler: `OnPositionChanged`**

Fires every ~500ms from the position timer while playing, **and also** from non-timer sources (seek, stop, clear, media-end reset). The wall-clock approach makes this handler's role simple: it only evaluates thresholds, it does not accumulate time.

```
1. Early exit:
   if (!IsEnabled || _hasScrobbled || _trackedTrackId == null) return

2. Safety net: start segment if playing but no active segment.
   During auto-advance, OnPlaybackStateChanged may not fire if the media
   player stays in Playing state across source changes. This ensures
   accumulation begins no later than the first position tick for the new track.
   IsEnabled is checked to avoid accumulating disabled time.
   if (playbackService.IsPlaying && !_isSegmentActive && IsEnabled)
       _segmentStart = _timeProvider.GetTimestamp()
       _isSegmentActive = true
       TrySendNowPlaying()     // Same guarded helper used by OnPlaybackStateChanged
       return   // Don't evaluate on the same tick we start — no time has elapsed yet

3. if (!_isSegmentActive) return   // Not playing, nothing to evaluate

4. Compute current playtime (without banking):
   currentPlaytime = _accumulatedPlaytime
       + _timeProvider.GetElapsedTime(_segmentStart).TotalSeconds

5. Re-read duration if stale:
   if (_trackedDuration <= 0)
       _trackedDuration = playbackService.Duration

6. Evaluate thresholds:
   playtimeMet = currentPlaytime >= PlaytimeThreshold
   percentageMet = _trackedDuration > 0
       && (currentPlaytime / _trackedDuration * 100) >= PercentageThreshold

   shouldScrobble = (ConditionMode == Or)
       ? (playtimeMet || percentageMet)
       : (playtimeMet && percentageMet)

7. Fire:
   if (shouldScrobble)
       _hasScrobbled = true
       fire scrobble for _trackedTrackId
```

Step 2 is the safety net for continuous auto-advance: if the media player transitions directly from Playing (old source) to Playing (new source) without an intermediate non-Playing state, `OnPlaybackStateChanged` never fires for the new track. The first `PositionChanged` tick after `OnTrackChanged` detects this and starts the segment. The `return` prevents counting the startup tick itself as listening time.

Step 3 handles the case where `Duration` was 0 or unavailable at `TrackChanged` time (metadata not yet loaded) but has since been updated by `MediaOpened`.

**Event handler: `OnPlaybackStateChanged`**

Manages wall-clock segments. No longer attempts completion detection (that's handled by `OnTrackEnded`).

```
1. Detect state transitions:
   isPlaying = playbackService.IsPlaying

   if (isPlaying && !_isSegmentActive && _trackedTrackId != null && IsEnabled)
       // Playback started or resumed while enabled — begin new segment
       _segmentStart = _timeProvider.GetTimestamp()
       _isSegmentActive = true
       TrySendNowPlaying()

   else if (!isPlaying && _isSegmentActive)
       // Playback paused or stopped — bank segment
       _accumulatedPlaytime += _timeProvider.GetElapsedTime(_segmentStart).TotalSeconds
       _isSegmentActive = false
```

This handler fires for many reasons (volume, mute, shuffle, repeat). Only the `IsPlaying` transitions matter, guarded by `_isSegmentActive` to avoid double-banking. The `IsEnabled` check on segment start prevents accumulating disabled listening time. `TrySendNowPlaying()` is called on every segment start; the `_nowPlayingSent` guard inside it ensures the notification fires at most once per play session.

**Now Playing helper** — shared by `OnPlaybackStateChanged`, `OnPositionChanged` safety net, and `SetEnabledAsync(true)`. All callers run on the UI dispatcher thread (playback events are dispatcher-queued, `SetEnabledAsync` is called from SettingsViewModel on the UI thread), so no synchronization is needed for `_nowPlayingSent`, `_pendingNowPlayingTrackId`, or `_nowPlayingSendTask`:

```csharp
private void TrySendNowPlaying()
{
    if (!_nowPlayingSent && NowPlayingEnabled && _trackedTrackId != null)
    {
        _nowPlayingSent = true;
        _ = EnqueueNowPlayingSendAsync(_trackedTrackId);
    }
}
```

**API call methods:**

```csharp
private async Task FireScrobbleAsync(string trackId)
{
    try
    {
        var client = _subsonicClientService.Client;
        if (client == null)
        {
            _logger.Debug("Scrobble skipped — no active connection", context: new { trackId });
            return;
        }

        var time = (ulong)_timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        await client.ScrobbleAsync(trackId, time, submission: true);
        _logger.Debug("Scrobble submitted", context: new { trackId });
    }
    catch (Exception ex)
    {
        _logger.Error("Scrobble submission failed", context: new { trackId }, ex);
    }
}

private string? _pendingNowPlayingTrackId;
private Task _nowPlayingSendTask = Task.CompletedTask;

private Task EnqueueNowPlayingSendAsync(string trackId)
{
    // Record latest intent.
    _pendingNowPlayingTrackId = trackId;

    // Serialize: chain onto any in-flight send so the newest track
    // is always sent last. Before sending, check whether this is
    // still the latest requested track — skip if superseded.
    _nowPlayingSendTask = _nowPlayingSendTask.ContinueWith(async _ =>
    {
        var current = _pendingNowPlayingTrackId;
        if (current != trackId) return;

        try
        {
            var client = _subsonicClientService.Client;
            if (client == null)
            {
                _logger.Debug("Now Playing skipped — no active connection", context: new { trackId });
                return;
            }

            await client.ScrobbleAsync(current, submission: false);
            _logger.Debug("Now Playing sent", context: new { trackId = current });
        }
        catch (Exception ex)
        {
            _logger.Error("Now Playing notification failed", context: new { trackId = current }, ex);
        }
    }, TaskScheduler.Default).Unwrap();

    return _nowPlayingSendTask;
}
```

Both are fire-and-forget (`_ = FireScrobbleAsync(...)` at call sites). Failures are logged but do not affect playback or UI. A null `SubsonicClient` (not yet connected) logs at Debug level for diagnostics.

**Now Playing ordering**: `ISubsonicClient.ScrobbleAsync` has no `CancellationToken` parameter, so in-flight HTTP requests cannot be cancelled. To ensure the latest track always wins on the server, sends are **serialized**: each `EnqueueNowPlayingSendAsync` call chains onto `_nowPlayingSendTask`, and before sending it checks whether `_pendingNowPlayingTrackId` still matches the requested track. If a newer track has been requested while the previous send was in flight, the stale send is skipped.

**Threading contract**: All calls to `TrySendNowPlaying()` and all mutations of `_pendingNowPlayingTrackId` / `_nowPlayingSendTask` occur on the UI dispatcher thread (playback events are dispatcher-queued via `DispatcherQueue.TryEnqueue`, and `SetEnabledAsync` is called from `SettingsViewModel` on the UI thread). No additional synchronization is needed. Tests that call these methods directly must do so sequentially or on a single thread.

### 2.4 Settings UI Integration

**SettingsViewModel** — add `IScrobblingService` as a constructor dependency and new properties:

```csharp
private readonly IScrobblingService _scrobblingService;
private bool _isInitializingScrobbling;

[ObservableProperty]
public partial bool IsScrobblingEnabled { get; set; }

[ObservableProperty]
public partial bool IsNowPlayingEnabled { get; set; }

[ObservableProperty]
public partial int SelectedPlaytimeThreshold { get; set; }

[ObservableProperty]
[NotifyPropertyChangedFor(nameof(PercentageThresholdText))]
public partial double ScrobblePercentageThreshold { get; set; }

[ObservableProperty]
public partial ScrobbleConditionMode ScrobbleConditionMode { get; set; }

public string PercentageThresholdText => $"{(int)ScrobblePercentageThreshold}%";

public List<PlaytimePreset> PlaytimePresets { get; } =
[
    new(15, "Settings_PlaytimePreset_15Seconds".GetLocalized()),
    new(30, "Settings_PlaytimePreset_30Seconds".GetLocalized()),
    new(60, "Settings_PlaytimePreset_1Minute".GetLocalized()),
    new(120, "Settings_PlaytimePreset_2Minutes".GetLocalized()),
    new(240, "Settings_PlaytimePreset_4Minutes".GetLocalized()),
];

public record PlaytimePreset(int Seconds, string Display);
```

**Property change handlers** — guarded by `_isInitializingScrobbling` to prevent constructor hydration from triggering persistence writes. Save failures are handled inside the service setter methods (they catch and log exceptions), so fire-and-forget is safe — the in-memory value is always updated first, and a failed persist only means the setting may not survive a crash:

```csharp
partial void OnIsScrobblingEnabledChanged(bool value)
{
    if (!_isInitializingScrobbling)
        _ = _scrobblingService.SetEnabledAsync(value);
}

partial void OnIsNowPlayingEnabledChanged(bool value)
{
    if (!_isInitializingScrobbling)
        _ = _scrobblingService.SetNowPlayingEnabledAsync(value);
}

partial void OnSelectedPlaytimeThresholdChanged(int value)
{
    if (!_isInitializingScrobbling)
        _ = _scrobblingService.SetPlaytimeThresholdAsync(value);
}

partial void OnScrobblePercentageThresholdChanged(double value)
{
    if (!_isInitializingScrobbling)
        _ = _scrobblingService.SetPercentageThresholdAsync((int)value);
}
```

**Condition mode command** — follows the existing RadioButton + Command pattern used by backdrop selection:

```csharp
public ICommand SwitchConditionModeCommand { get; }

// In constructor:
SwitchConditionModeCommand = new RelayCommand<ScrobbleConditionMode>(
    async (param) =>
    {
        if (ScrobbleConditionMode != param)
        {
            ScrobbleConditionMode = param;
            await _scrobblingService.SetConditionModeAsync(param);
        }
    });
```

**Constructor initialization** — hydrate properties from service under guard:

```csharp
_isInitializingScrobbling = true;
IsScrobblingEnabled = _scrobblingService.IsEnabled;
IsNowPlayingEnabled = _scrobblingService.NowPlayingEnabled;
SelectedPlaytimeThreshold = _scrobblingService.PlaytimeThreshold;
ScrobblePercentageThreshold = _scrobblingService.PercentageThreshold;
ScrobbleConditionMode = _scrobblingService.ConditionMode;
_isInitializingScrobbling = false;
```

**Percentage slider debounce**: The slider generates continuous change events during drag. `OnScrobblePercentageThresholdChanged` updates `PercentageThresholdText` immediately (via `NotifyPropertyChangedFor`) but the persistence call passes through `SetPercentageThresholdAsync`, which the `ScrobblingService` implements as a **trailing-edge debounce**: it updates the in-memory `PercentageThreshold` property immediately (so threshold evaluation always uses the latest value) but delays the `ILocalSettingsService.SaveSettingAsync` call by 500ms, resetting the timer on each new call. Only the last value in a drag gesture hits disk.

If the user navigates away from SettingsPage before the debounce timer fires, the in-memory value is still correct (the service is a singleton), and the next `SetPercentageThresholdAsync` call from any source will persist the current value. There is no explicit flush on page unload — the worst case is that a crash within the 500ms window loses the slider change, which is acceptable for a preference setting.

**SettingsPage.xaml** — new section inserted between the Backdrop `StackPanel` and the About `TextBlock`:

```xml
<!-- Scrobbling -->
<TextBlock x:Uid="Settings_Scrobbling"
           Style="{ThemeResource SubtitleTextBlockStyle}"
           Margin="{StaticResource MediumTopMargin}" />
<StackPanel Margin="{StaticResource SmallTopBottomMargin}">
    <ToggleSwitch
        x:Uid="Settings_ScrobblingEnabled"
        IsOn="{x:Bind ViewModel.IsScrobblingEnabled, Mode=TwoWay}" />

    <StackPanel Margin="{StaticResource XSmallTopMargin}"
                IsEnabled="{x:Bind ViewModel.IsScrobblingEnabled, Mode=OneWay}">

        <ToggleSwitch
            x:Uid="Settings_NowPlaying"
            IsOn="{x:Bind ViewModel.IsNowPlayingEnabled, Mode=TwoWay}" />

        <TextBlock x:Uid="Settings_PlaytimeThreshold"
                   Margin="{StaticResource SmallTopMargin}" />
        <ComboBox
            ItemsSource="{x:Bind ViewModel.PlaytimePresets}"
            SelectedValue="{x:Bind ViewModel.SelectedPlaytimeThreshold, Mode=TwoWay}"
            SelectedValuePath="Seconds"
            DisplayMemberPath="Display"
            Margin="{StaticResource XSmallTopMargin}" />

        <TextBlock x:Uid="Settings_PercentageThreshold"
                   Margin="{StaticResource SmallTopMargin}" />
        <StackPanel Orientation="Horizontal" Spacing="8">
            <Slider
                Minimum="10" Maximum="100" StepFrequency="5"
                Value="{x:Bind ViewModel.ScrobblePercentageThreshold, Mode=TwoWay}"
                Width="200" />
            <TextBlock
                Text="{x:Bind ViewModel.PercentageThresholdText, Mode=OneWay}"
                VerticalAlignment="Center" />
        </StackPanel>

        <TextBlock x:Uid="Settings_ConditionMode"
                   Margin="{StaticResource SmallTopMargin}" />
        <StackPanel Margin="{StaticResource XSmallTopMargin}">
            <RadioButton
                x:Uid="Settings_ConditionMode_Or"
                Command="{x:Bind ViewModel.SwitchConditionModeCommand}"
                IsChecked="{x:Bind ViewModel.ScrobbleConditionMode, Converter={StaticResource EnumToBooleanConverter}, ConverterParameter=Or, Mode=OneWay}"
                FontSize="15"
                GroupName="ConditionMode">
                <RadioButton.CommandParameter>
                    <models:ScrobbleConditionMode>Or</models:ScrobbleConditionMode>
                </RadioButton.CommandParameter>
            </RadioButton>
            <RadioButton
                x:Uid="Settings_ConditionMode_And"
                Command="{x:Bind ViewModel.SwitchConditionModeCommand}"
                IsChecked="{x:Bind ViewModel.ScrobbleConditionMode, Converter={StaticResource EnumToBooleanConverter}, ConverterParameter=And, Mode=OneWay}"
                FontSize="15"
                GroupName="ConditionMode">
                <RadioButton.CommandParameter>
                    <models:ScrobbleConditionMode>And</models:ScrobbleConditionMode>
                </RadioButton.CommandParameter>
            </RadioButton>
        </StackPanel>
    </StackPanel>
</StackPanel>
```

**Localization strings** — add to `Strings/en-us/Resources.resw`:

| Name | Value |
|---|---|
| `Settings_Scrobbling.Text` | `Scrobbling` |
| `Settings_ScrobblingEnabled.Header` | `Enable scrobbling` |
| `Settings_NowPlaying.Header` | `Send "Now Playing" notifications` |
| `Settings_PlaytimeThreshold.Text` | `Playtime threshold` |
| `Settings_PercentageThreshold.Text` | `Percentage threshold` |
| `Settings_ConditionMode.Text` | `Condition mode` |
| `Settings_ConditionMode_Or.Content` | `Either threshold (OR)` |
| `Settings_ConditionMode_And.Content` | `Both thresholds (AND)` |
| `Settings_PlaytimePreset_15Seconds` | `15 seconds` |
| `Settings_PlaytimePreset_30Seconds` | `30 seconds` |
| `Settings_PlaytimePreset_1Minute` | `1 minute` |
| `Settings_PlaytimePreset_2Minutes` | `2 minutes` |
| `Settings_PlaytimePreset_4Minutes` | `4 minutes` |

### 2.5 DI Registration

In `App.xaml.cs`, add to the singleton services section:

```csharp
services.AddSingleton<TimeProvider>(TimeProvider.System);
services.AddSingleton<IScrobblingService, ScrobblingService>();
```

### 2.6 Activation Integration

`IScrobblingService` is **not** constructor-injected into `ActivationService`. Reason: `ScrobblingService` depends on `IPlaybackService`, whose constructor reads `IAppStateService.State.Playback` to restore persisted volume/mute/shuffle/repeat. `ActivationService` is resolved in `App.OnLaunched()` before `InitializeAsync()` calls `_appStateService.InitializeAsync()`, so constructor-injecting `IScrobblingService` would force `PlaybackService` to construct with default (unloaded) state.

Instead, resolve lazily in `ActivationService.ActivateAsync()` **after** `InitializeAsync()` returns (which loads app state) but before `RestoreWindowState()`. This ensures the call runs on the UI thread, since `InitializeAsync()` uses `.ConfigureAwait(false)` and may resume on a thread pool thread. Follows the same `App.GetService<T>()` pattern used for `ITaskbarService` later in `ActivateAsync()`:

```csharp
public async Task ActivateAsync(object activationArgs)
{
    // Execute tasks before activation.
    await InitializeAsync();

    // Initialize scrobbling after app state is loaded.
    // Must run on the UI thread (not inside InitializeAsync which
    // uses ConfigureAwait(false) and may resume on a thread pool thread).
    await App.GetService<IScrobblingService>().InitializeAsync();

    // Restore window geometry from saved state.
    RestoreWindowState();
    // ... rest of activation
}
```

This reads persisted settings and subscribes to playback events. No playback events fire until the user connects and starts playing, so there is no race condition.

### 2.7 Error Handling

| Scenario | Behavior |
|---|---|
| `SubsonicClient` is null (not connected) | Log at Debug level, skip |
| HTTP error on scrobble submission | Log at Error level, continue |
| HTTP error on Now Playing | Log at Error level, continue |
| Settings key missing | Use default value (see Section 1.6) |
| Settings key wrong type or corrupt | Caught by `ReadValidatedSettingAsync` (filters `JsonException`, `InvalidOperationException`, `FormatException`); use default, log at Warning (exception attached) |
| Settings value out of range or wrong step | Caught by validation predicate in `ReadValidatedSettingAsync`; reset to default, log at Debug (see Section 1.6) |
| Track duration is 0 or negative | Percentage threshold evaluates to `false`; re-read on next tick (Section 2.3, step 3) |
| `_timeProvider.GetElapsedTime` returns negative | Impossible — `_timeProvider.GetTimestamp()` is monotonic |

No retry logic. No offline queue. Failed scrobbles are lost — acceptable for MVP.

---

## Files to Create

| File | Purpose |
|---|---|
| `Hypersonic.Core/Models/ScrobbleConditionMode.cs` | OR/AND condition mode enum |
| `Hypersonic/Contracts/Services/IScrobblingService.cs` | Scrobbling service interface |
| `Hypersonic/Services/ScrobblingService.cs` | Scrobbling logic, wall-clock tracking, settings management |

## Files to Modify

| File | Changes |
|---|---|
| `Hypersonic.Core/Contracts/Services/IPlaybackService.cs` | Add `TrackEndedEventArgs` class and `event EventHandler<TrackEndedEventArgs>? TrackEnded` |
| `Hypersonic/Services/PlaybackService.cs` | Fire `TrackEnded` with typed args at top of `OnMediaEnded` before state mutation |
| `Hypersonic/ViewModels/SettingsViewModel.cs` | Add scrobbling properties, commands, init guard, PlaytimePreset |
| `Hypersonic/Views/SettingsPage.xaml` | Add Scrobbling section with ToggleSwitches, ComboBox, Slider, RadioButtons |
| `Hypersonic/Strings/en-us/Resources.resw` | Add 13 localization strings |
| `Hypersonic/App.xaml.cs` | Register `TimeProvider.System` and `IScrobblingService` singletons |
| `Hypersonic/Services/ActivationService.cs` | Add `App.GetService<IScrobblingService>().InitializeAsync()` call in `ActivateAsync` after `InitializeAsync` returns |
| `Hypersonic.Tests/Hypersonic.Tests.csproj` | Add `Microsoft.Extensions.TimeProvider.Testing` Version="8.6.0" package reference |
| `Hypersonic.Tests/Fakes/FakePlaybackService.cs` | Add `event EventHandler<TrackEndedEventArgs>? TrackEnded` and `RaiseTrackEnded(string trackId, double duration)` helper |

## Implementation Sequence

1. **PlaybackService event**: Add `TrackEnded` to `IPlaybackService` and fire it in `PlaybackService.OnMediaEnded`.
2. **Model**: Create `ScrobbleConditionMode` enum in `Hypersonic.Core/Models/`.
3. **Service interface**: Create `IScrobblingService` in `Hypersonic/Contracts/Services/`.
4. **Service implementation**: Create `ScrobblingService` in `Hypersonic/Services/` — settings read/write/validation, wall-clock playback tracking, threshold evaluation, API calls, slider debounce.
5. **DI + activation**: Register in `App.xaml.cs`, add lazy `InitializeAsync` call in `ActivationService.ActivateAsync` after `InitializeAsync` returns.
6. **ViewModel**: Add properties, init guard, commands, and initialization to `SettingsViewModel`.
7. **XAML**: Add scrobbling section to `SettingsPage.xaml`.
8. **Localization**: Add strings to `Resources.resw`.
9. **Tests**: Add automated tests for `ScrobblingService` (see Verification).

## Verification

**Build:** `dotnet build Hypersonic.sln -p:Platform=x64` — must compile cleanly.

**Automated tests** (`Hypersonic.Tests`):

Test `ScrobblingService` with fake implementations of `IPlaybackService`, `ISubsonicClientService`, `ILocalSettingsService`, and `ILoggingService`. The fakes raise events programmatically and record API calls.

| Test | Scenario |
|---|---|
| `Scrobble_OrMode_PlaytimeFirst` | Playtime threshold met before percentage → single scrobble |
| `Scrobble_OrMode_PercentageFirst` | Short track, percentage met first → single scrobble |
| `Scrobble_AndMode_BothRequired` | Both thresholds met → scrobble at later threshold |
| `Scrobble_AndMode_CompletionFailsafe` | 20s track, 30s/50% AND, played fully → failsafe relaxes playtime to max(20-1,0)=19, snaps percentage to 100% → scrobble |
| `Scrobble_AndMode_SeekAbuse_NoScrobble` | 240s track, 30s/50% AND, listen 30s then seek to end → failsafe: playtime ✓ but percentage 12.5% ✗ → AND blocks |
| `Scrobble_LongTrack_NoToleranceOnPlaytime` | 240s track, 30s OR, listen 29s then seek to end → failsafe: effectivePlaytime=30 (no tolerance), 29 < 30 ✗ → no scrobble |
| `Scrobble_SeekNearEnd_NoFalseScrobble` | Seek to last 2s of a 240s track, let it end → TrackEnded: playtime 2s < 30s ✗, percentage 0.8% ✗ → no scrobble |
| `Scrobble_AtMostOnce` | Continue playing past both thresholds → exactly one scrobble |
| `Scrobble_SkipNoScrobble` | TrackChanged without TrackEnded, low playtime → no scrobble |
| `Scrobble_Disabled_NoScrobble` | IsEnabled=false → no scrobble or now-playing |
| `Scrobble_DisabledMidTrack_NoScrobble` | Disable after accumulating time → session reset, no scrobble fires |
| `Scrobble_DisableCancelsInFlightNowPlaying` | Disable blocks new Now Playing sends for subsequent tracks |
| `Scrobble_DisableReEnable_SessionReset` | Accumulate 20s, scrobble fires, disable, re-enable same track → _trackedTrackId re-attached, accumulated=0, must re-earn threshold |
| `Scrobble_DisableReEnable_NewSessionCanScrobbleAgain` | Scrobble fires, disable, re-enable, accumulate again → second scrobble fires (new session) |
| `Scrobble_DisabledPlayback_NoSegmentStart` | Start playback while disabled → no segment starts, re-enable starts fresh segment |
| `Scrobble_DurationUnknown_FailsafeUsesFullThreshold` | Duration=0 at TrackEnded → effectivePlaytime = PlaytimeThreshold (no relaxation) |
| `Scrobble_CompletionPercentage100_TimingTolerance` | 20s track, 100% threshold AND, banks 19.8s → TrackEnded treats as 100% → scrobble |
| `Scrobble_RepeatOne_MultipleScrobbles` | TrackEnded + TrackChanged cycle → one scrobble per loop |
| `NowPlaying_SentOnPlaybackConfirmed` | Track changes, then IsPlaying → true → now-playing sent |
| `NowPlaying_NotSentOnFailedStream` | Track changes but never enters Playing → no now-playing |
| `NowPlaying_NotSentWhenDisabled` | NowPlayingEnabled=false → no now-playing |
| `NowPlaying_SentOncePerSession` | Pause/resume same track → Now Playing sent only on first Playing |
| `NowPlaying_SentOnEnableMidTrack` | Enable scrobbling while track is playing → Now Playing sent immediately |
| `NowPlaying_SentViaSafetyNet` | Auto-advance without PlaybackStateChanged → Now Playing sent from PositionChanged safety net |
| `Scrobble_SeekDoesNotInflate` | Seek forward during playback → accumulated time reflects wall-clock only |
| `Scrobble_PauseResume_AccumulatesCorrectly` | Pause for 5s, resume → only playing time counted |
| `Scrobble_DurationZero_PercentageSkipped` | Duration=0 → percentage never met, playtime still works |
| `Scrobble_DurationUpdatedLater` | Duration starts at 0, updated mid-playback → percentage works after update |
| `Scrobble_AutoAdvance_SegmentSafetyNet` | TrackChanged while IsPlaying, no PlaybackStateChanged → segment starts on first PositionChanged tick |
| `Scrobble_NullClient_NoException` | SubsonicClient null → no error, debug log |
| `Settings_Validation_InvalidPlaytime` | Read invalid playtime → falls back to 30 |
| `Settings_Validation_OutOfRangePercentage` | Read percentage=200 → falls back to 50 (default) |
| `Settings_Validation_NonStepPercentage` | Read percentage=37 → falls back to 50 (not divisible by 5) |
| `Settings_Validation_InvalidConditionMode` | Read "invalid" → falls back to Or |
| `Initialize_Idempotent` | Call InitializeAsync twice concurrently → events subscribed only once, no duplicate scrobbles |
| `NowPlaying_RapidSkip_OnlyLatestSent` | Skip 3 tracks quickly → sends serialize, only the final track's Now Playing reaches the server |
| `Setter_InvalidPlaytime_Rejected` | SetPlaytimeThresholdAsync(99) → in-memory value unchanged, no persist |

**Manual smoke tests:**

1. **Basic scrobble**: Enable scrobbling → play a track past threshold → verify play count increments on Navidrome dashboard.
2. **OR mode**: Set playtime=15s, percentage=50%, mode=Either → play a 60s track → scrobble fires at 15s (playtime met first).
3. **AND mode**: Same thresholds, mode=Both → scrobble fires at 30s (both 15s playtime AND 50% of 60s met).
4. **Completion failsafe**: Set playtime=240s, percentage=75%, mode=Both → play a 30s track to completion → scrobble fires at track end (effective playtime relaxed to max(30-1,0)=29, percentage snapped to 100%, both met).
5. **Skip without scrobble**: Set thresholds to 30s/50% Either → skip a track after 5s → no scrobble.
6. **Now Playing**: Enable Now Playing → start a track → wait for playback to begin → Navidrome shows "currently playing" entry.
7. **Disabled**: Disable scrobbling → play a track → no scrobble or now-playing sent.
8. **Repeat-one**: Enable repeat-one → play a short track → each loop generates exactly one scrobble.
9. **End-of-queue**: Play a single track to completion (no next track) → scrobble fires via `TrackEnded` failsafe.
10. **Settings persistence**: Toggle settings → close and reopen Settings page → values preserved.
11. **Seek near end**: Seek to last 2 seconds of a 4-minute track → let it play out → `TrackEnded` fires but accumulated playtime is ~2s → neither 30s playtime nor 50% percentage met → no scrobble.

## Threshold Evaluation Examples

| Track Duration | Playtime Threshold | Percentage Threshold | Mode | Scrobble Fires At |
|---|---|---|---|---|
| 240s (4 min) | 30s | 50% | OR | 30s (playtime met first) |
| 240s (4 min) | 30s | 50% | AND | 120s (both met: 30s ✓ AND 120/240=50% ✓) |
| 60s (1 min) | 30s | 50% | OR | 30s (both met simultaneously) |
| 20s | 30s | 50% | OR | 10s (50% met; playtime never reaches 30s) |
| 20s | 30s | 50% | AND | Track end (failsafe: effective=max(20-1,0)=19 ✓, 100% ✓, AND passes) |
| 300s (5 min) | 240s | 25% | OR | 75s (25% met first) |
| 300s (5 min) | 240s | 25% | AND | 240s (playtime met last) |

## Event Flow Diagrams

**Normal playback (auto-advance to next track):**
```
MediaEnded
  → TrackEnded (scrobbling service: banks segment, failsafe fires if needed)
  → Advance() returns next track
  → PlayCurrent() sets new source, calls Play()
  → TrackChanged (scrobbling service: reset state for new track)
  → OnCurrentStateChanged: Opening → Buffering → Playing
  → PlaybackStateChanged (scrobbling service: starts segment, sends Now Playing)
```

**End of queue (no next track):**
```
MediaEnded
  → TrackEnded (scrobbling service: banks segment, failsafe fires if needed)
  → Advance() returns null
  → IsPlaying=false, Position=0, Duration=0
  → PlaybackStateChanged (scrobbling service: _isSegmentActive already false, no-op)
```

**User skip (NextTrack):**
```
NextTrack()
  → AdvanceManual()
  → PlayCurrent() sets new source, calls Play()
  → TrackChanged (scrobbling service: reset state — no failsafe, TrackEnded never fired)
  → OnCurrentStateChanged: Playing → Opening → Playing
  → PlaybackStateChanged (scrobbling service: starts segment, sends Now Playing)
```

**Pause / Resume:**
```
Pause()
  → IsPlaying=false
  → PlaybackStateChanged (scrobbling service: banks wall-clock segment)

Resume()
  → IsPlaying=true
  → PlaybackStateChanged (scrobbling service: starts new segment; Now Playing already sent)
```
