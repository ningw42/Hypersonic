# Enhanced Playback Service — Implementation Details

Companion to [docs/design/service/enhanced-playback-service.md](../../design/service/enhanced-playback-service.md). This document records what was actually built, key decisions made during implementation, and deviations from the original design.

---

## Files Changed

### New files added to the `Hypersonic` project

| File | Purpose |
|---|---|
| `Hypersonic/Models/QueueSource.cs` | `QueueSourceKind` enum + `QueueSource` positional record |
| `Hypersonic/Models/RepeatMode.cs` | `RepeatMode` enum (Off, All, One) |
| `Hypersonic/Services/PlaybackQueue.cs` | Pure queue logic — shuffle, repeat, history, all manipulation |

### New test project added to solution

| File | Purpose |
|---|---|
| `Hypersonic.UnitTest/Hypersonic.UnitTest.csproj` | Plain `net10.0` test project (CLI-runnable via `dotnet test`), links source files from `Hypersonic` |
| `Hypersonic.UnitTest/PlaybackQueueTests.cs` | PlaybackQueue unit tests |

### Modified files

| File | Changes |
|---|---|
| `Hypersonic/Contracts/Services/IPlaybackService.cs` | Added: `Source`, `ShuffleEnabled`, `RepeatMode`, `EffectiveQueueIndex`, `History`, `QueueChanged` event, `AddNext`, `AddToEnd`, `RemoveAt`, `MoveTrack`, `ClearUpcoming`, `ClearAll`, `GetEffectiveQueue`. Modified: `PlayQueue` gains optional `source` parameter. |
| `Hypersonic/Services/PlaybackService.cs` | Replaced `_queue`/`_queueIndex` with `PlaybackQueue` delegation. All new methods implemented. Event contract enforced per design. |
| `Hypersonic.sln` | Added `Hypersonic.UnitTest` project |

### Unchanged (backward compatible)

- `Hypersonic/Models/PlaybackTrack.cs`
- `Hypersonic/ViewModels/PlayerViewModel.cs`
- `Hypersonic/Services/TaskbarService.cs`
- `Hypersonic/App.xaml.cs`

---

## Architecture

### Two-layer split

The implementation separates concerns into two classes:

**`PlaybackQueue`** (pure logic, no WinUI dependencies):
- Owns `_tracks`, `_currentIndex`, `_shuffleOrder`, `_shufflePosition`, `_repeatMode`, `_history`, `_navigationHistory`
- Provides all queue manipulation, traversal, shuffle, repeat, and history logic
- Returns values; never fires events (that's the service's job)
- Fully unit-testable via `dotnet test`

**`PlaybackService`** (WinUI integration):
- Owns `MediaPlayer`, `DispatcherQueue`, `DispatcherQueueTimer`, SMTC
- Delegates all queue/index logic to `PlaybackQueue`
- Responsible for: creating `MediaSource`, managing playback, firing events, updating SMTC
- Cannot be unit-tested outside WinUI runtime (see Testing Gaps below)

### Design deviation: navigation history uses track IDs, not indices

The design doc describes navigation history as `Stack<int>` storing indices into `_tracks`. The implementation uses `Stack<string>` storing track IDs instead. **The design doc was not updated to reflect this change** — readers should treat the implementation as authoritative on this point.

**Why:** Queue mutations (RemoveAt, MoveTrack, ClearUpcoming) shift indices in `_tracks`. With index-based history, every mutation would need to reindex the entire history stack to avoid `GoBack()` navigating to the wrong track. With ID-based history, mutations cannot corrupt the stack — `GoBack()` does `_tracks.FindIndex(t => t.Id == id)` and skips entries whose tracks were removed.

**Trade-off:** `FindIndex` is O(n) per history pop instead of O(1) index lookup. For queues of typical size (< 1000 tracks) this is negligible.

### Advance checks before mutating

`AdvanceInternal()` checks whether movement is possible **before** pushing to history or moving the cursor. This prevents phantom history entries when `NextTrack()` is called at end-of-queue with `RepeatMode.Off`:

```
if (!canAdvance) return null;  // no mutation
PushCurrentToHistory();        // only after confirming movement
_currentIndex = ...;           // move cursor
```

### RepeatOne does not push history

`Advance()` (auto-advance on media end) with `RepeatMode.One` returns `CurrentTrack` immediately without calling `AdvanceInternal()`. This means:
- No history entry is created (same track replaying is not a "transition")
- `HasPrevious` is unaffected
- `AdvanceManual()` (user pressing Next) bypasses this and always calls `AdvanceInternal()`, so manual Next still advances even in RepeatOne

---

## Event Contract (as implemented)

### Guaranteed event firings

| Operation | `QueueChanged` | `PlaybackStateChanged` | `TrackChanged` |
|---|---|---|---|
| `PlayQueue` | Always | Always | Always (even if StreamUri is null) |
| `AddNext` | Always | If HasNext/HasPrevious changed | Never |
| `AddToEnd` | Always | If HasNext/HasPrevious changed | Never |
| `RemoveAt` (non-current) | Always | If HasNext/HasPrevious changed | Never |
| `RemoveAt` (current, queue non-empty) | Always | If HasNext/HasPrevious changed | Always (even if new track's StreamUri is null) |
| `RemoveAt` (current, queue now empty) | Always | Always | Always |
| `MoveTrack` (actual move) | Always | Never | Never |
| `MoveTrack` (no-op) | Never | Never | Never |
| `ClearUpcoming` | Always | If HasNext/HasPrevious changed | Never |
| `ClearAll` | Always | Always | Always |
| `ShuffleEnabled` (changed) | Always | Always | Never |
| `ShuffleEnabled` (same value) | Never | Never | Never |
| `RepeatMode` (changed) | Never | Always | Never |
| `RepeatMode` (same value) | Never | Never | Never |
| `Stop` | Never | Always | Never |
| `OnMediaEnded` (has next) | Never | If HasNext/HasPrevious changed | Yes (via PlayCurrent) |
| `OnMediaEnded` (end of queue) | Never | Always | Never |

### Key guarantee for existing consumers

`PlayerViewModel` and `TaskbarService` subscribe to `PlaybackStateChanged` and re-read `HasNext`/`HasPrevious`. The implementation ensures `PlaybackStateChanged` fires whenever those derived properties change, using the snapshot pattern:

```csharp
var prevHasNext = HasNext;
var prevHasPrevious = HasPrevious;
// ... perform mutation ...
if (HasNext != prevHasNext || HasPrevious != prevHasPrevious)
    PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
```

For shuffle and repeat toggles, `PlaybackStateChanged` fires unconditionally (mode changed is itself a state change consumers need to observe).

### TrackChanged always fires when NowPlaying changes

`PlayQueue()` and `RemoveAt()` fire `TrackChanged` even when the new current track has no `StreamUri` and cannot be played. This prevents stale UI metadata when the queue cursor moves to an unplayable track.

---

## End-of-Queue Behavior

When the last track finishes playing (`OnMediaEnded`, no next track), the handler sets service-level state directly:

```csharp
IsPlaying = false;
Position = 0;
Duration = 0;
PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
PositionChanged?.Invoke(this, EventArgs.Empty);
UpdateSmtcState();
```

What this does and does not do:

| Aspect | Behavior |
|---|---|
| `NowPlaying` | Preserved (last track remains as current) |
| `IsPlaying` | Set to `false` directly |
| `Position` / `Duration` | Reset to 0 |
| `HasNext` | `false` |
| `MediaPlayer.Source` | Not touched (still holds last `MediaPlaybackItem`) |
| `MediaPlayer` state | Not explicitly paused — `MediaPlayer` is expected to transition to `MediaPlayerState.Stopped` after `MediaEnded`, which would trigger `OnCurrentStateChanged` asynchronously. The implementation does not assert this; it relies on the callback. |
| SMTC PlaybackStatus | Updated indirectly via `OnCurrentStateChanged` callback (not set in `OnMediaEnded` itself) |
| SMTC Next/Previous | Updated via `UpdateSmtcState()` |
| `TrackChanged` | Not fired (NowPlaying didn't change) |

The user can call `Resume()` to replay the last track, or `PlayQueue()` to start fresh. `ClearAll()` is the explicit full teardown that nulls `NowPlaying`, clears `MediaPlayer.Source`, and wipes SMTC metadata.

---

## Stop() vs ClearAll()

| | `Stop()` | `ClearAll()` |
|---|---|---|
| Queue | Preserved | Emptied |
| NowPlaying | Preserved | null |
| Position | 0 | 0 |
| Duration | Preserved | 0 |
| IsPlaying | false (set directly) | false |
| Navigation history | Preserved | Cleared |
| Play history | Preserved | Cleared |
| MediaPlayer.Source | Preserved | null |
| SMTC display | Preserved (paused) | Cleared |
| Can Resume() | Yes | No |

---

## Shuffle Invariant

`_shuffleOrder` is a `List<int>` of indices into `_tracks`. The central invariant:

> Every entry in `_shuffleOrder` must be a valid, unique index into `_tracks`. When `_tracks` is mutated, `_shuffleOrder` must be updated atomically.

### Mutations while shuffled

- **AddNext:** Appends tracks to `_tracks`, inserts new indices into `_shuffleOrder` at `_shufflePosition + 1` (play-next semantics).
- **AddToEnd:** Appends tracks to `_tracks`, inserts new indices at random positions after `_shufflePosition`.
- **RemoveAt:** Removes from `_tracks`, removes from `_shuffleOrder`, decrements all `_shuffleOrder` entries > removed index.
- **MoveTrack:** No-op while shuffled. Shuffle order determines playback; original-order reordering has no user-visible effect.
- **ClearUpcoming:** Collects indices after `_shufflePosition`, removes those tracks from `_tracks`, truncates and reindexes `_shuffleOrder`.
- **SetShuffle:** Idempotent. Setting to the same value is a no-op (won't reshuffle).

---

## Backward Compatibility

### Compile compatibility

All existing callers compile without changes. `PlayQueue`'s new `source` parameter has a default value of `null`.

### Behavioral compatibility at defaults

With `ShuffleEnabled = false`, `RepeatMode = Off`, `Source = null` (all defaults), existing consumers see behavior consistent with the prior implementation:

- `HasNext`/`HasPrevious` use the same sequential logic as before
- Track advancement is sequential
- `PreviousTrack()` with empty navigation history falls through to `Seek(0)`, matching the original behavior

The overall event model is not literally identical — `QueueChanged` is a new event, `PlaybackStateChanged` now also fires on shuffle/repeat changes, and `TrackChanged` fires on some unplayable-track transitions that the old service never encountered. However, existing consumers (`PlayerViewModel`, `TaskbarService`) only subscribe to `PlaybackStateChanged` and `TrackChanged`, and their handler logic (re-read `HasNext`/`HasPrevious`/`IsPlaying`/`NowPlaying`) produces the same UI state at default settings.

### Intentional behavioral divergence when features are activated

- `RepeatMode.All` → `HasNext` becomes true at queue end → TaskbarService enables Next button
- `ShuffleEnabled` → track advancement order changes → different `NowPlaying` values on advance
- `PreviousTrack()` → uses navigation history stack instead of `_queueIndex - 1`. In sequential mode the result is identical. In shuffle mode, history-based navigation correctly reverses the actual path.

---

## Test Projects

### Test location

PlaybackQueue tests live in `Hypersonic.UnitTest`, a plain `net10.0` project that links the source files directly. This project exists because `PlaybackQueue` has zero WinUI dependencies but the main `Hypersonic` project targets a Windows-specific TFM that can't be referenced from a plain .NET test project.

Run: `dotnet test Hypersonic.UnitTest/Hypersonic.UnitTest.csproj`

```xml
<Compile Include="..\Hypersonic\Models\PlaybackTrack.cs" Link="Models\PlaybackTrack.cs" />
<Compile Include="..\Hypersonic\Models\QueueSource.cs" Link="Models\QueueSource.cs" />
<Compile Include="..\Hypersonic\Models\RepeatMode.cs" Link="Models\RepeatMode.cs" />
<Compile Include="..\Hypersonic\Services\PlaybackQueue.cs" Link="Services\PlaybackQueue.cs" />
```

### Test categories

| Category | Key cases |
|---|---|
| SetQueue | Sets current track, clamps invalid index, empty tracks, clears both histories |
| AddNext | Inserts after current, establishes cursor when nothing playing |
| AddToEnd | Appends to end, establishes cursor when nothing playing |
| RemoveAt | Advances on current removal, records to both histories, adjusts index on before-current removal, empties queue, navigation history skips removed tracks |
| MoveTrack | Reorders upcoming, current-track no-op, shuffle no-op, same-index no-op |
| ClearUpcoming / ClearAll | Preserves current, resets all state including play history |
| Shuffle | Current unchanged on toggle, upcoming randomized, resumes original order on off, idempotent same-value, play-next while shuffled, indices consistent after removal |
| Repeat | Wrap at end (All), HasNext at end (All), replay current (One), no history on RepeatOne, manual next advances past RepeatOne |
| Advance at end | No history push on failed advance, no accumulation on repeated calls |
| Navigation history | Reverses shuffle path, empty returns null, skips stale entries, cleared by SetQueue and ClearAll, GoBack records to play history, immune to index shifts |
| EffectiveIndex | Matches CurrentIndex without shuffle, returns shuffle position, returns -1 on empty queue and shuffled empty queue |
| Play history | Populated on forward transitions |

### Testing gaps

`PlaybackService` depends on `MediaPlayer` and `DispatcherQueue`, which require the WinUI runtime. This means:

- **No automated service-level event contract tests exist.** The event-firing patterns (which events fire for each operation, in what order) are not verified by tests.
- The queue logic has full coverage through `PlaybackQueue` tests. The service layer's event-firing code follows a uniform snapshot/compare/fire pattern, but correctness of that wiring is verified only by manual testing and code review.
- Service-level tests could be added to `Hypersonic.Tests.MSTest` and run in Visual Studio, but this was not done in the current implementation.

---

## Known Limitations

1. **No gapless playback** — deferred per design. Each track transition creates a fresh `MediaSource`. A `MediaPlaybackList` sliding window can be layered on top later.
2. **No service-level event tests** — see Testing Gaps above.
3. **No crossfade** — out of scope.
4. **No cross-session persistence** — queue is session-only. Subsonic `savePlayQueue` API could be integrated later.
5. **No scrobble integration** — can be added independently via `ISubsonicClientService`.
6. **Design doc drift** — `docs/design/enhanced-playback-service.md` still describes navigation history as `Stack<int>` in multiple places. The implementation uses `Stack<string>` (track IDs). The design doc was not updated to reflect this change.
