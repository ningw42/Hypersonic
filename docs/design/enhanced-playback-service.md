# Enhanced Playback Service — Feature Spec & Design Spec

## Context

Hypersonic's current `PlaybackService` holds a flat `List<PlaybackTrack>` with an index. It supports play/pause/stop/seek and auto-advance, but lacks queue manipulation, source context, shuffle, repeat, and history. This design upgrades the service to a rich, playlist-aware queue system — backend only, no UI changes.

**Gapless playback is deferred** to a future enhancement. The current per-track `MediaPlayer.Source` approach is retained.

---

## Part 1: Feature Spec

### 1.1 Queue Source Context

Every queue session carries metadata about where tracks originated, enabling future "Playing from: Album X" UI.

- A `QueueSource` record captures the origin kind (Album, Playlist, Artist, Search, etc.) plus optional ID and display name.
- Set when `PlayQueue` is called. Readable via `IPlaybackService.Source`.
- Individual tracks do NOT carry source — the queue as a whole has one source. Tracks added via "Play Next" from a different context are simply injected; the source doesn't change.

### 1.2 Queue Manipulation

| Operation | Behavior |
|---|---|
| `PlayQueue(tracks, startIndex, source)` | Replace entire queue and start playing. Existing behavior, plus optional source context. Clears navigation history. |
| `AddNext(tracks)` | Insert tracks immediately after the current track. If nothing is playing, sets the queue but does NOT auto-play. |
| `AddToEnd(tracks)` | Append tracks to end of queue. If nothing is playing, sets the queue but does NOT auto-play. |
| `RemoveAt(index)` | Remove track by queue index. If it's the current track: advance to next (or stop if none). If before current: adjust index down. |
| `MoveTrack(fromIndex, toIndex)` | Reorder a track within the queue. No-op while shuffled (shuffle order determines playback). |
| `ClearUpcoming()` | Remove all tracks after current. Does not stop playback. |
| `ClearAll()` | Stop playback, empty the queue, and clear navigation history. |

All mutations raise the `QueueChanged` event. Any mutation that changes `HasNext` or `HasPrevious` also raises `PlaybackStateChanged` (see Section 2.8).

**Stop() vs ClearAll():** `Stop()` preserves the queue and cursor — it pauses and resets position to 0. The user can `Resume()` to replay the current track. `ClearAll()` is the explicit full teardown that empties the queue and clears all state.

### 1.3 Shuffle

- `ShuffleEnabled` (bool) property toggleable at any time.
- **On toggle ON**: Fisher-Yates shuffle of all upcoming track indices. Current track stays in position.
- **On toggle OFF**: Resume from current track's original queue position in original order.
- `Queue` always returns tracks in original insertion order. A `GetEffectiveQueue()` method returns the playback-order view.
- `HasNext`/`HasPrevious`, `NextTrack()`/`PreviousTrack()` respect the active order.

### 1.4 Repeat

- `RepeatMode` enum: `Off`, `All`, `One`.
- **Off**: Stop at end of queue (current behavior).
- **All**: Wrap to first track at end. `HasNext` returns true when there is at least one track.
- **One**: Auto-replay current track on end. Manual `NextTrack()` still advances. `HasNext`/`HasPrevious` behave as if repeat is off.
- Mode changes raise `PlaybackStateChanged`.

### 1.5 History

Two distinct history concepts:

- **Play history** (`IReadOnlyList<PlaybackTrack> History`, public): Bounded in-memory ring buffer (last 100 tracks) populated on every track transition. Read-only log for display (e.g., future "Recently Played" UI). Session-only, not persisted.

- **Navigation history** (`Stack<int> _navigationHistory`, internal): Records the sequence of track indices actually visited. Drives `PreviousTrack()` behavior — going back always reverses the actual path taken, including through shuffled sequences. See Section 2.7 for full semantics.

### 1.6 Not In Scope

- Gapless / crossfade playback (deferred).
- UI for queue view, shuffle/repeat buttons.
- Cross-session queue persistence.
- Radio / auto-generated follow-on tracks.
- Scrobble integration (can be added as a separate feature alongside or after this).

---

## Part 2: Design Spec

### 2.1 New Models

**`QueueSource`** — new file: `Hypersonic/Models/QueueSource.cs`

```csharp
namespace Hypersonic.Models;

public enum QueueSourceKind
{
    None,
    Album,
    Playlist,
    Artist,
    Search,
}

public record QueueSource(QueueSourceKind Kind, string? Id = null, string? Name = null);
```

**`RepeatMode`** — new file: `Hypersonic/Models/RepeatMode.cs`

```csharp
namespace Hypersonic.Models;

public enum RepeatMode
{
    Off,
    All,
    One,
}
```

**`PlaybackTrack`** — unchanged.

### 2.2 Enhanced IPlaybackService Interface

File: `Hypersonic/Contracts/Services/IPlaybackService.cs`

```csharp
public interface IPlaybackService
{
    // --- Existing (unchanged) ---
    PlaybackTrack? NowPlaying { get; }
    IReadOnlyList<PlaybackTrack> Queue { get; }
    int QueueIndex { get; }
    bool IsPlaying { get; }
    double Position { get; }
    double Duration { get; }
    double Volume { get; set; }
    bool HasNext { get; }
    bool HasPrevious { get; }

    event EventHandler? PlaybackStateChanged;
    event EventHandler? TrackChanged;
    event EventHandler? PositionChanged;

    void Resume();
    void Pause();
    void Stop();
    void NextTrack();
    void PreviousTrack();
    void Seek(double positionSeconds);

    // --- New / modified ---
    QueueSource? Source { get; }
    bool ShuffleEnabled { get; set; }
    RepeatMode RepeatMode { get; set; }
    int EffectiveQueueIndex { get; }
    IReadOnlyList<PlaybackTrack> History { get; }

    event EventHandler? QueueChanged;

    void PlayQueue(IList<PlaybackTrack> tracks, int startIndex = 0, QueueSource? source = null);
    void AddNext(IList<PlaybackTrack> tracks);
    void AddToEnd(IList<PlaybackTrack> tracks);
    void RemoveAt(int index);
    void MoveTrack(int fromIndex, int toIndex);
    void ClearUpcoming();
    void ClearAll();
}
```

**`QueueIndex`** returns the index into the original insertion-order `Queue`.
**`EffectiveQueueIndex`** returns the current position within the shuffle permutation when shuffle is on, or equals `QueueIndex` when shuffle is off. Useful for a future queue UI to highlight the "current" row in shuffle view.

**Backward compatibility**: `PlayQueue` adds an optional `source` parameter (default `null`). All existing callers compile without changes.

### 2.3 Internal Architecture — PlaybackQueue

Extract queue logic into a testable pure class: `Hypersonic/Services/PlaybackQueue.cs`

This class owns:
- `List<PlaybackTrack> _tracks` — the queue in insertion order
- `int _currentIndex` — position in original order
- `List<int> _shuffleOrder` — shuffled index permutation (empty when shuffle off)
- `int _shufflePosition` — position within shuffle order
- `bool _shuffleEnabled`
- `RepeatMode _repeatMode`
- `List<PlaybackTrack> _history` (capped at 100)
- `Stack<int> _navigationHistory` — track indices visited, for PreviousTrack()

Public methods mirror the queue manipulation API:
- `SetQueue(tracks, startIndex)` — replace queue, clear navigation history
- `AddNext(tracks)` — insert after current (see Section 2.6 for shuffle behavior)
- `AddToEnd(tracks)` — append (see Section 2.6 for shuffle behavior)
- `RemoveAt(index)` — remove with index adjustment
- `MoveTrack(from, to)` — reorder (no-op while shuffled)
- `ClearUpcoming()`, `ClearAll()`
- `Advance()` → returns next `PlaybackTrack?` (respects shuffle + repeat), pushes outgoing to history
- `GoBack()` → pops navigation history, returns `PlaybackTrack?`
- `CurrentTrack` → current `PlaybackTrack?`
- `HasNext`, `HasPrevious` → booleans
- `SetShuffle(bool)` — toggles shuffle, rebuilds shuffle order
- `EffectiveQueue` → `IReadOnlyList<PlaybackTrack>` in playback order
- `EffectiveIndex` → int, current position in effective order

This class has **zero** dependency on `MediaPlayer`, `DispatcherQueue`, or WinUI. Fully unit-testable.

### 2.4 PlaybackService Changes

`PlaybackService` delegates all queue/index logic to `PlaybackQueue` and retains ownership of:
- `MediaPlayer` lifecycle
- SMTC manual management
- Position timer
- Event dispatch

Key changes:
- Replace `_queue`/`_queueIndex` fields with a single `PlaybackQueue _playbackQueue` field.
- `PlayQueue` → calls `_playbackQueue.SetQueue(...)`, then `PlayCurrent()`.
- `NextTrack` → calls `_playbackQueue.Advance()`, then `PlayCurrent()`.
- `PreviousTrack` → delegates to `PlaybackQueue.GoBack()` with position context (see Section 2.7).
- `OnMediaEnded` → if `RepeatMode.One`, replay current via `Seek(0)` + `Resume()`. Otherwise calls `_playbackQueue.Advance()`. If null (end of queue, repeat off), stop.
- `HasNext`/`HasPrevious` → delegate to `_playbackQueue`.
- `NowPlaying` → delegate to `_playbackQueue.CurrentTrack`.
- All new manipulation methods → delegate to `_playbackQueue` + raise `QueueChanged` + conditionally raise `PlaybackStateChanged` (see Section 2.8).
- Shuffle/Repeat setters → delegate to `_playbackQueue` + raise `PlaybackStateChanged`.

**Event firing pattern for mutations:** Before each mutation, snapshot `HasNext` and `HasPrevious`. After the mutation, compare. Fire `QueueChanged` always. Fire `PlaybackStateChanged` if either `HasNext` or `HasPrevious` changed.

`PlayCurrent()` remains the same (create `MediaSource`, set `_mediaPlayer.Source`, update SMTC). No gapless changes.

### 2.5 HasNext / HasPrevious Logic

```
HasNext:
  if RepeatMode == All && queue.Count > 0 → true
  if RepeatMode == One → same as Off (manual Next still advances)
  if ShuffleEnabled → _shufflePosition < _shuffleOrder.Count - 1
  else → _currentIndex < _tracks.Count - 1

HasPrevious:
  true if _navigationHistory.Count > 0
  (note: the 3-second restart rule is applied in PlaybackService,
   not in PlaybackQueue — see Section 2.7)
```

### 2.6 Shuffle Detail

**Central invariant:** `_shuffleOrder` is a `List<int>` containing indices into `_tracks`. Every entry must be a valid index into `_tracks`, and no index may appear twice. When `_tracks` is mutated, `_shuffleOrder` must be updated atomically to reflect new, removed, or shifted indices.

**Toggle ON:**
1. Collect all indices except `_currentIndex`.
2. Fisher-Yates shuffle them.
3. `_shuffleOrder = [_currentIndex, ...shuffled]`.
4. `_shufflePosition = 0`.

**Toggle OFF:**
1. Clear `_shuffleOrder`.
2. `_currentIndex` already points to the correct original position.

**Advance (shuffle on):**
1. `_shufflePosition++`.
2. If past end: if RepeatAll, reshuffle and wrap to 0; if RepeatOff, return null.
3. `_currentIndex = _shuffleOrder[_shufflePosition]`.

**Queue mutations while shuffled:**

Each mutation follows the central invariant. Precise steps:

**`AddNext(tracks)`:**
1. Let `N = _tracks.Count` (before insertion).
2. Append new tracks to end of `_tracks` → new indices are `N, N+1, ..., N+k-1`.
3. Insert those indices into `_shuffleOrder` at `_shufflePosition + 1`, preserving their order.
4. No existing indices in `_shuffleOrder` need adjustment (we only appended to `_tracks`).

**`AddToEnd(tracks)`:**
1. Let `N = _tracks.Count` (before insertion).
2. Append new tracks to end of `_tracks` → new indices are `N, N+1, ..., N+k-1`.
3. Insert those new indices at random positions within `_shuffleOrder` after `_shufflePosition` (shuffle them into the remaining unplayed portion).

**`RemoveAt(queueIndex)`:**
1. Remove `_tracks[queueIndex]`.
2. Find and remove `queueIndex` from `_shuffleOrder`. If the removed entry is before `_shufflePosition`, decrement `_shufflePosition`.
3. Decrement all entries in `_shuffleOrder` that are > `queueIndex` (to account for the index shift in `_tracks`).
4. If `queueIndex < _currentIndex`, decrement `_currentIndex`.
5. If `queueIndex == _currentIndex` (removing current track): advance to next or stop.

**`MoveTrack(from, to)`:**
No-op while shuffled. Shuffle order determines playback; moving tracks in `_tracks` would require a complex index remap with no user-visible benefit. The UI should disable reorder drag handles when shuffle is on.

**`ClearUpcoming()`:**
1. Collect all indices in `_shuffleOrder` after `_shufflePosition` — these are the unplayed tracks.
2. Remove those tracks from `_tracks` (in reverse index order to avoid shifting issues).
3. Truncate `_shuffleOrder` to `[0.._shufflePosition]`.
4. Reindex remaining `_shuffleOrder` entries to account for removed `_tracks` entries.

### 2.7 Navigation History and PreviousTrack()

**Problem solved:** Under shuffle, the user might visit tracks A→D→B. Pressing "previous" should reverse the actual path: B→D→A. A simple `_shufflePosition - 1` would not guarantee this.

**Data structure:** `PlaybackQueue` maintains `Stack<int> _navigationHistory` storing indices into `_tracks`. This is separate from the public `History` ring buffer.

**When entries are pushed:**
- On every track transition that changes `CurrentTrack` — whether via `Advance()`, `GoBack()`, or `RemoveAt()` on the current track — the outgoing track's `_currentIndex` is pushed onto `_navigationHistory`.
- Also appended to the public `_history` ring buffer (capped at 100, oldest evicted).

**PreviousTrack() in PlaybackService:**
1. If `Position > 3` seconds → restart current track (`Seek(0)`). No history interaction.
2. Else → call `_playbackQueue.GoBack()`:
   a. If `_navigationHistory` is non-empty → pop the top entry, push current `_currentIndex` onto navigation history? No — **pop only, do not push**. The current track was already pushed when we arrived at it. Set `_currentIndex` to the popped value. Return the track.
   b. If `_navigationHistory` is empty → return null. `PlaybackService` responds by restarting current track (`Seek(0)`).

**HasPrevious:** `_navigationHistory.Count > 0`. Note: the 3-second restart is always available as a user affordance, but `HasPrevious` reflects whether there is a distinct previous track to go back to. Since the current `PlaybackService` already always allows PreviousTrack (it falls through to Seek(0)), this is not a behavioral regression — the button is always usable.

**Stale entries:** If a track is removed from `_tracks` via `RemoveAt`, its index in `_navigationHistory` becomes stale. `GoBack()` pops entries until it finds one that still exists in `_tracks`, or the stack is exhausted.

**Cleared by:** `PlayQueue()` and `ClearAll()` both clear `_navigationHistory`.

### 2.8 Event Contract

| Event | Fires When | Existing Consumers |
|---|---|---|
| `PlaybackStateChanged` | IsPlaying, Volume, HasNext/HasPrevious, Shuffle, Repeat changes | PlayerViewModel, TaskbarService |
| `TrackChanged` | NowPlaying changes | PlayerViewModel, TaskbarService |
| `PositionChanged` | Every 500ms while playing, or on Seek | PlayerViewModel |
| **`QueueChanged`** (new) | Queue contents/order modified | Future queue UI |

**Key rule:** Any operation that may change `HasNext` or `HasPrevious` MUST fire `PlaybackStateChanged`, in addition to `QueueChanged` if the queue was modified. This ensures existing consumers (`PlayerViewModel` at line 170-178, `TaskbarService` at lines 188-196) refresh button/SMTC states without needing to subscribe to `QueueChanged`.

**Which operations fire which events:**

| Operation | `QueueChanged` | `PlaybackStateChanged` (if Has* changes) | `TrackChanged` |
|---|---|---|---|
| `PlayQueue` | Yes | Yes | Yes |
| `AddNext` | Yes | Yes (HasNext may become true) | No |
| `AddToEnd` | Yes | Yes (HasNext may become true) | No |
| `RemoveAt` | Yes | Yes (Has* may change) | If current removed |
| `MoveTrack` | Yes | No (count unchanged) | No |
| `ClearUpcoming` | Yes | Yes (HasNext becomes false) | No |
| `ClearAll` | Yes | Yes | Yes (NowPlaying → null) |
| `SetShuffle` | Yes | Yes (Has* may change) | No |
| `SetRepeatMode` | No | Yes (Has* changes) | No |

**Implementation pattern in `PlaybackService`:** Before each mutation, capture `var prevHasNext = HasNext; var prevHasPrev = HasPrevious;`. After, fire `QueueChanged`. Then if `HasNext != prevHasNext || HasPrevious != prevHasPrev`, fire `PlaybackStateChanged`.

### 2.9 Thread Safety

Same approach as today: all state mutation on UI thread via `DispatcherQueue.TryEnqueue`. The `PlaybackQueue` object is only accessed from the UI thread. No locks needed.

### 2.10 Backward Compatibility

#### Compile compatibility

All existing callers compile without changes:
- `PlayQueue`'s new `source` parameter has default value `null`.
- New interface members are additive — existing code does not reference them.

#### Behavioral compatibility

**Default behavior is identical.** `RepeatMode` defaults to `Off`, `ShuffleEnabled` defaults to `false`, `Source` defaults to `null`. No new features activate unless explicitly enabled. When all new properties are at their defaults, the service behaves exactly as the current implementation:
- `HasNext` / `HasPrevious` use the same original-order index logic.
- Track advancement is sequential.
- `PreviousTrack()` with empty `_navigationHistory` falls back to Seek(0), matching existing behavior.

**When new features are activated**, behavior diverges intentionally:
- `RepeatMode.All` → `HasNext`/`HasPrevious` become true at queue boundaries → `TaskbarService` enables Prev/Next buttons that were previously disabled → **intentional and correct**.
- `ShuffleEnabled` → track advancement order changes → `PlayerViewModel` and `TaskbarService` see different `NowPlaying` values → **intentional**.
- `PreviousTrack()` navigates by history stack instead of `_queueIndex - 1` → in sequential non-repeat mode, these produce the same result. In shuffle mode, history-based navigation is the correct behavior.

**No consumer changes required for correct default behavior.** Changes to consume new features (QueueSource, QueueChanged, shuffle/repeat UI) are additive and optional.

---

## Files to Create

| File | Purpose |
|---|---|
| `Hypersonic/Models/QueueSource.cs` | Queue origin context record |
| `Hypersonic/Models/RepeatMode.cs` | Repeat mode enum |
| `Hypersonic/Services/PlaybackQueue.cs` | Pure queue logic (testable, no MediaPlayer dependency) |

## Files to Modify

| File | Changes |
|---|---|
| `Hypersonic/Contracts/Services/IPlaybackService.cs` | Add new properties, events, methods |
| `Hypersonic/Services/PlaybackService.cs` | Delegate to PlaybackQueue, wire up new features |

## Files Unchanged but Referenced

| File | Role |
|---|---|
| `Hypersonic/Models/PlaybackTrack.cs` | Track model — no changes needed |
| `Hypersonic/ViewModels/PlayerViewModel.cs` | Primary consumer — fully backward compatible at defaults |
| `Hypersonic/Services/TaskbarService.cs` | Subscriber — fully backward compatible at defaults |
| `Hypersonic/App.xaml.cs` | DI registration — no changes needed (no new dependencies) |

---

## Implementation Sequence

1. **New models**: `QueueSource`, `RepeatMode`.
2. **PlaybackQueue class**: All queue logic, shuffle, repeat, navigation history — fully unit-testable.
3. **Unit tests for PlaybackQueue**: See verification section below.
4. **Extend IPlaybackService interface**: New members.
5. **Refactor PlaybackService**: Replace `_queue`/`_queueIndex` with `PlaybackQueue`, implement all new methods, fire events per Section 2.8.
6. **Verify**: Build solution, run all tests, manual smoke test.
7. **Optional**: Update callers to pass `QueueSource`.

## Verification

**Build:**
- `dotnet build Hypersonic.sln -p:Platform=x64` — must compile cleanly.

**Existing tests (no regressions):**
- `dotnet test Hypersonic.Tests.MSTest`
- `dotnet test Hypersonic.Client.UnitTest`

**New PlaybackQueue unit tests** — location: `Hypersonic.Tests.MSTest` project.

Queue manipulation:
- `AddNext_InsertsAfterCurrent` — verify insertion position and index integrity
- `AddToEnd_AppendsToQueue` — verify queue length and order
- `RemoveAt_CurrentTrack_AdvancesToNext` — verify auto-advance and NowPlaying update
- `RemoveAt_BeforeCurrent_AdjustsIndex` — verify `_currentIndex` shifts down
- `RemoveAt_LastTrack_Stops` — verify null return when removing only remaining track
- `MoveTrack_ReordersUpcoming` — verify upcoming order changes, current unaffected
- `ClearUpcoming_PreservesCurrent` — verify current track still plays, HasNext is false
- `ClearAll_ResetsEverything` — verify empty queue, null CurrentTrack, empty histories

Shuffle:
- `ShuffleOn_CurrentTrackUnchanged` — verify NowPlaying doesn't change on toggle
- `ShuffleOn_UpcomingRandomized` — verify remaining tracks are shuffled (statistical: run N times, not always same order)
- `ShuffleOff_ResumesOriginalOrder` — verify `_currentIndex` maps back correctly
- `AddNext_WhileShuffled_PlaysNextCorrectly` — verify the added track is the effective next
- `RemoveAt_WhileShuffled_IndicesConsistent` — verify all `_shuffleOrder` entries remain valid indices into `_tracks`
- `MoveTrack_WhileShuffled_IsNoOp` — verify queue and shuffle order unchanged

Repeat:
- `RepeatAll_WrapsAtEnd` — verify `Advance()` wraps to first track
- `RepeatAll_HasNextTrueAtEnd` — verify HasNext at last position
- `RepeatOne_MediaEnd_ReplaysCurrent` — verify same track returned on `Advance()`
- `RepeatOne_ManualNext_Advances` — verify manual NextTrack still moves forward

Navigation history:
- `PreviousTrack_ShuffleMode_ReversesPath` — play A→D→B, `GoBack()` returns D then A
- `PreviousTrack_NavigationHistoryEmpty_ReturnsNull` — verify graceful fallback
- `PreviousTrack_AfterRemove_SkipsStaleEntry` — verify stale history entries skipped
- `PlayQueue_ClearsNavigationHistory` — verify fresh session has empty history
- `ClearAll_ClearsNavigationHistory` — verify teardown clears history

**Manual smoke test:**
- Open app → play album → tracks advance → skip next/prev → works as before (default behavior identical).
