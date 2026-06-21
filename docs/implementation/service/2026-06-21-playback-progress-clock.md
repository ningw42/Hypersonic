# Shared Playback-Progress Clock + Read-Only Playback State — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the player bar a smooth, frame-interpolated progress thumb by reusing the lyrics renderer's `PlaybackClock`, and split the playback interface into a read-only `IReadOnlyPlaybackState` (observe) plus the existing `IPlaybackService` (command).

**Architecture:** `PlaybackClock` (a pure, ms-based position estimator) moves to `Hypersonic.Core` as shared infrastructure; each consumer owns its own instance fed by its own "now" source. `IPlaybackService` gains a read-only base interface implemented by the same `PlaybackService` singleton (mapped twice in DI). `PlayerViewModel` anchors a private clock from the playback events it already handles and drives `Position` from a ~30 fps tick, with user interaction re-anchoring the clock and pointer drag suspending the push.

**Tech Stack:** .NET 10, WinUI 3, CommunityToolkit.Mvvm, MSTest. Design spec: `docs/design/service/2026-06-21-playback-progress-clock.md`.

**Build/test commands:**
- Core tests: `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj`
- App tests: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64`
- App build: `dotnet build Hypersonic/Hypersonic.csproj -p:Platform=x64`

---

## File Structure

| File | Responsibility | Change |
|---|---|---|
| `Hypersonic.Core/Services/PlaybackClock.cs` | Pure ms-based position estimator | **Create** (moved from app) |
| `Hypersonic/Controls/Lyrics/PlaybackClock.cs` | (old location) | **Delete** |
| `Hypersonic.Core.Tests/Services/PlaybackClockTests.cs` | Estimator tests | **Create** (moved) |
| `Hypersonic.Tests/Controls/Lyrics/PlaybackClockTests.cs` | (old location) | **Delete** |
| `Hypersonic/Controls/Lyrics/LyricsCanvasControl.xaml.cs` | Lyrics renderer | **Modify** (one `using`) |
| `Hypersonic.Core/Contracts/Services/IReadOnlyPlaybackState.cs` | Read-only observe interface | **Create** |
| `Hypersonic.Core/Contracts/Services/IPlaybackService.cs` | Command interface (extends read) | **Modify** |
| `Hypersonic/App.xaml.cs` | DI registration | **Modify** (line 144) |
| `Hypersonic.Core.Tests/Contracts/PlaybackInterfaceTests.cs` | Interface relationship test | **Create** |
| `Hypersonic.Tests/Services/PlaybackStateRegistrationTests.cs` | DI same-instance test | **Create** |
| `Hypersonic/ViewModels/NowPlayingViewModel.cs` | Lyrics observer | **Modify** (dependency type) |
| `Hypersonic/ViewModels/PlayerViewModel.cs` | Player-bar mediator + smoothing | **Modify** |
| `Hypersonic.Tests/ViewModels/PlayerViewModelPositionTests.cs` | Smoothing tests | **Create** |
| `Hypersonic/Views/ShellPage.xaml` | Seek slider | **Modify** (name) |
| `Hypersonic/Views/ShellPage.xaml.cs` | Scrub wiring | **Modify** |
| `CLAUDE.md` | Architecture docs | **Modify** (note the split + clock) |

`PlaybackService.cs` does **not** change — it already implements every member of both interfaces.

---

### Task 1: Relocate `PlaybackClock` to Core (public), add a hard-snap option, and move its tests

**Files:**
- Create: `Hypersonic.Core/Services/PlaybackClock.cs`
- Delete: `Hypersonic/Controls/Lyrics/PlaybackClock.cs`
- Create: `Hypersonic.Core.Tests/Services/PlaybackClockTests.cs`
- Delete: `Hypersonic.Tests/Controls/Lyrics/PlaybackClockTests.cs`
- Modify: `Hypersonic/Controls/Lyrics/LyricsCanvasControl.xaml.cs:3` (add `using`)

The class is relocated with its logic preserved for existing callers, plus one **additive** capability: a `hardSnap` flag on `SetPosition` that forces an immediate jump regardless of the converge band. It defaults to `false`, so the lyrics control (which calls the one-arg form) is byte-for-byte unchanged. The player bar uses `hardSnap: true` for semantic re-anchors (Task 4).

- [ ] **Step 1: Create the moved test in Core.Tests (will fail to compile until the class moves)**

Create `Hypersonic.Core.Tests/Services/PlaybackClockTests.cs` — identical body to the current test, only the `using`/namespace change:

```csharp
using Hypersonic.Core.Services;

namespace Hypersonic.Core.Tests.Services;

[TestClass]
public class PlaybackClockTests
{
    private double _now;
    private PlaybackClock NewClock() => new(() => _now);

    [TestMethod]
    public void WhilePlaying_EstimateAdvancesWithWallClock()
    {
        _now = 0;
        var clock = NewClock();
        clock.SetPlaying(true);
        clock.SetPosition(1000);

        _now = 500;

        Assert.AreEqual(1500, clock.Estimate(), 1e-6, "playing clock advances 1:1 with wall time");
    }

    [TestMethod]
    public void WhilePaused_EstimateIsFrozen()
    {
        _now = 0;
        var clock = NewClock();
        clock.SetPlaying(false);
        clock.SetPosition(1000);

        _now = 500;

        Assert.AreEqual(1000, clock.Estimate(), 1e-6, "paused clock does not advance");
    }

    [TestMethod]
    public void LargeDelta_IsTreatedAsSeek_SnapsImmediately()
    {
        _now = 0;
        var clock = NewClock();
        clock.SetPlaying(true);
        clock.SetPosition(1000);
        _now = 500; // estimate ~1500

        clock.SetPosition(5000); // |5000 - 1500| = 3500 > threshold → seek

        Assert.AreEqual(5000, clock.Estimate(), 1e-6, "a seek snaps the anchor with no smoothing");
    }

    [TestMethod]
    public void SmallDelta_Converges_WithoutAVisibleJump()
    {
        _now = 0;
        var clock = NewClock();
        clock.SetPlaying(true);
        clock.SetPosition(1000);
        _now = 500;
        var predicted = clock.Estimate(); // ~1500

        clock.SetPosition(1480); // delta -20, within threshold → smooth converge

        Assert.AreEqual(predicted, clock.Estimate(), 1e-6, "a small correction must not jump the displayed position");
    }

    [TestMethod]
    public void SmallDelta_EventuallyConvergesTowardAuthoritativeTimeline()
    {
        _now = 0;
        var clock = NewClock();
        clock.SetPlaying(true);
        clock.SetPosition(1000);
        _now = 500;
        clock.Estimate(); // predicted ~1500
        clock.SetPosition(1480); // residual correction of +20 to be absorbed

        _now = 2000; // 1.5s later — plenty of time for the residual to decay
        var authoritativeRaw = 1480 + (2000 - 500); // anchor 1480 at wall 500, playing

        Assert.AreEqual(authoritativeRaw, clock.Estimate(), 1.0, "the residual decays and the estimate rejoins the authoritative timeline");
    }

    [TestMethod]
    public void ResumeAfterPause_ContinuesWithoutJumping()
    {
        _now = 0;
        var clock = NewClock();
        clock.SetPlaying(true);
        clock.SetPosition(1000);
        _now = 500; // estimate 1500

        clock.SetPlaying(false);
        _now = 3000; // long pause — frozen
        Assert.AreEqual(1500, clock.Estimate(), 1e-6, "frozen at the pause point");

        clock.SetPlaying(true);
        _now = 3200; // 200ms after resume
        Assert.AreEqual(1700, clock.Estimate(), 1e-6, "resumes from where it paused, no jump");
    }

    [TestMethod]
    public void HardSnap_IgnoresConvergeBand_JumpsImmediately()
    {
        _now = 0;
        var clock = NewClock();
        clock.SetPlaying(true);
        clock.SetPosition(1000);
        _now = 500;
        var predicted = clock.Estimate(); // ~1500

        clock.SetPosition(1480, hardSnap: true); // delta -20 is inside the converge band, but snap anyway

        Assert.AreEqual(1480, clock.Estimate(), 1e-6, "hard snap lands exactly, ignoring the converge band");
        Assert.AreNotEqual(predicted, clock.Estimate(), "a hard snap is not a smooth converge");
    }
}
```

Then delete the old test file `Hypersonic.Tests/Controls/Lyrics/PlaybackClockTests.cs`.

- [ ] **Step 2: Run the moved test to confirm it fails to compile**

Run: `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj`
Expected: BUILD FAILURE — `PlaybackClock` not found in `Hypersonic.Core.Services`.

- [ ] **Step 3: Create the class in Core (public) and delete the old file**

Create `Hypersonic.Core/Services/PlaybackClock.cs` (identical logic; `internal`→`public`, new namespace):

```csharp
namespace Hypersonic.Core.Services;

/// <summary>
/// An anchored, self-interpolating playback-position estimator. The authoritative position
/// (e.g. <see cref="Contracts.Services.IReadOnlyPlaybackState.Position"/>) only refreshes every
/// ~500 ms, so forwarding it raw makes a progress UI step in coarse jumps. This clock free-runs
/// against a monotonic wall clock between updates so motion stays fluid, and folds each
/// authoritative update back in either as a hard <i>seek</i> (large delta → snap) or a smooth
/// <i>converge</i> (small delta → absorb the residual over the next frames, no visible jump).
/// Paused → frozen. Operates in <b>milliseconds</b> end to end; consumers convert at the boundary.
/// </summary>
/// <remarks>
/// "Now" is injected as a millisecond source (production: a monotonic timestamp such as a
/// <see cref="System.Diagnostics.Stopwatch"/>, or a render-loop frame clock; tests: a controllable
/// lambda) so the estimator is deterministic under test.
/// </remarks>
public sealed class PlaybackClock
{
    private readonly Func<double> _nowMs;

    private double _anchorMs;       // authoritative position at the anchor
    private double _anchorWallMs;   // wall time at the anchor
    private double _correctionMs;   // residual visual offset that decays to 0 (smooth converge)
    private double _lastWallMs;     // wall time of the last correction decay
    private bool _isPlaying;

    /// <summary>Above this |delta| an update is a seek (snap); at or below it converges smoothly.</summary>
    public double SeekThresholdMs { get; set; } = 250;

    /// <summary>Time constant (ms) over which a converge residual decays. ~120 ms ≈ a few frames.</summary>
    public double ConvergenceTimeConstantMs { get; set; } = 120;

    public PlaybackClock(Func<double> nowMs)
    {
        _nowMs = nowMs;
        _anchorWallMs = _nowMs();
        _lastWallMs = _anchorWallMs;
    }

    /// <summary>Sets play state, re-anchoring at the current estimate so the transition never jumps.</summary>
    public void SetPlaying(bool playing)
    {
        if (playing == _isPlaying)
        {
            return;
        }

        ReanchorToCurrent();
        _isPlaying = playing;
    }

    /// <summary>
    /// Folds in an authoritative position. A delta beyond <see cref="SeekThresholdMs"/> snaps (seek);
    /// otherwise the difference becomes a decaying correction so the displayed value does not jump.
    /// Pass <paramref name="hardSnap"/> = true to force an immediate jump regardless of the delta —
    /// for semantic discontinuities (track change, user seek, stop) where converging would briefly
    /// show the old value or drift back toward it.
    /// </summary>
    public void SetPosition(double authoritativeMs, bool hardSnap = false)
    {
        var now = _nowMs();
        var predicted = Estimate(); // advances decay up to `now`

        _anchorMs = authoritativeMs;
        _anchorWallMs = now;
        _lastWallMs = now;

        var jump = authoritativeMs - predicted;
        _correctionMs = (hardSnap || Math.Abs(jump) > SeekThresholdMs)
            ? 0                          // seek / hard snap: no smoothing
            : predicted - authoritativeMs; // converge: keep showing `predicted`, then decay to authoritative
    }

    /// <summary>The estimated position now, smoothed between authoritative updates.</summary>
    public double Estimate()
    {
        var now = _nowMs();

        var dt = now - _lastWallMs;
        if (dt > 0 && _correctionMs != 0)
        {
            _correctionMs *= Math.Exp(-dt / ConvergenceTimeConstantMs);
        }
        _lastWallMs = now;

        var raw = _anchorMs + (_isPlaying ? now - _anchorWallMs : 0);
        return raw + _correctionMs;
    }

    private void ReanchorToCurrent()
    {
        var now = _nowMs();
        var current = Estimate();
        _anchorMs = current;       // fold the correction into the anchor so raw == current here
        _anchorWallMs = now;
        _correctionMs = 0;
        _lastWallMs = now;
    }
}
```

Then delete `Hypersonic/Controls/Lyrics/PlaybackClock.cs`.

- [ ] **Step 4: Fix the lyrics control's namespace reference**

In `Hypersonic/Controls/Lyrics/LyricsCanvasControl.xaml.cs`, add to the `using` block (after the existing `using Hypersonic.Core.Models.Lyrics;`):

```csharp
using Hypersonic.Core.Services;
```

- [ ] **Step 5: Run Core tests (green) and build the app**

Run: `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj`
Expected: PASS (7 PlaybackClock tests).
Run: `dotnet build Hypersonic/Hypersonic.csproj -p:Platform=x64`
Expected: BUILD SUCCEEDED (lyrics control resolves `PlaybackClock` from Core).

- [ ] **Step 6: Commit**

```bash
git add Hypersonic.Core/Services/PlaybackClock.cs Hypersonic.Core.Tests/Services/PlaybackClockTests.cs Hypersonic/Controls/Lyrics/LyricsCanvasControl.xaml.cs
git add -u Hypersonic/Controls/Lyrics/PlaybackClock.cs Hypersonic.Tests/Controls/Lyrics/PlaybackClockTests.cs
git commit -m "refactor(playback): relocate PlaybackClock to Core as shared public estimator with hard-snap"
```

---

### Task 2: Add `IReadOnlyPlaybackState`, make `IPlaybackService` extend it, map both in DI

**Files:**
- Create: `Hypersonic.Core/Contracts/Services/IReadOnlyPlaybackState.cs`
- Modify: `Hypersonic.Core/Contracts/Services/IPlaybackService.cs`
- Modify: `Hypersonic/App.xaml.cs:144`
- Create: `Hypersonic.Core.Tests/Contracts/PlaybackInterfaceTests.cs`
- Create: `Hypersonic.Tests/Services/PlaybackStateRegistrationTests.cs`

- [ ] **Step 1: Write the failing interface-relationship test**

Create `Hypersonic.Core.Tests/Contracts/PlaybackInterfaceTests.cs`:

```csharp
using Hypersonic.Core.Contracts.Services;

namespace Hypersonic.Core.Tests.Contracts;

[TestClass]
public class PlaybackInterfaceTests
{
    [TestMethod]
    public void IPlaybackService_Extends_IReadOnlyPlaybackState()
    {
        Assert.IsTrue(
            typeof(IReadOnlyPlaybackState).IsAssignableFrom(typeof(IPlaybackService)),
            "IPlaybackService must extend the read-only observation surface");
    }
}
```

- [ ] **Step 2: Run it to confirm it fails**

Run: `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj`
Expected: BUILD FAILURE — `IReadOnlyPlaybackState` does not exist.

- [ ] **Step 3: Create `IReadOnlyPlaybackState`**

Create `Hypersonic.Core/Contracts/Services/IReadOnlyPlaybackState.cs`:

```csharp
using Hypersonic.Core.Models;

namespace Hypersonic.Core.Contracts.Services;

/// <summary>
/// Read-only observation surface for the playback engine — the query half of the command/query
/// split with <see cref="IPlaybackService"/>. Pure observers depend on this so they physically
/// cannot mutate playback. Grown incrementally as more pure-observer consumers migrate; today it
/// carries the progress-sync slice. Implemented by the same singleton as <see cref="IPlaybackService"/>.
/// </summary>
public interface IReadOnlyPlaybackState
{
    PlaybackTrack? NowPlaying { get; }
    double Position { get; }            // seconds; the coarse ~500ms anchor
    double Duration { get; }            // seconds
    bool IsPlaying { get; }

    event EventHandler? PlaybackStateChanged;
    event EventHandler<TrackChangedEventArgs>? TrackChanged;
    event EventHandler? PositionChanged;
}
```

- [ ] **Step 4: Make `IPlaybackService` extend it and move the read members up**

Replace the full contents of `Hypersonic.Core/Contracts/Services/IPlaybackService.cs` with (the two `EventArgs` types stay; the four read props + three events move to the base):

```csharp
using Hypersonic.Core.Models;

namespace Hypersonic.Core.Contracts.Services;

public class TrackEndedEventArgs : EventArgs
{
    public required string TrackId { get; init; }
    public double Duration { get; init; }
}

/// <summary>
/// Event payload for <see cref="IReadOnlyPlaybackState.TrackChanged"/>. Carries the
/// direction of the change so consumers (e.g. cover-art animations) can pick
/// the right transition without having to read mutable state on the source.
/// </summary>
public sealed class TrackChangedEventArgs : EventArgs
{
    public TrackChangeDirection Direction { get; }

    public TrackChangedEventArgs(TrackChangeDirection direction)
    {
        Direction = direction;
    }
}

public interface IPlaybackService : IReadOnlyPlaybackState
{
    IReadOnlyList<PlaybackTrack> Queue { get; }
    int QueueIndex { get; }
    int EffectiveQueueIndex { get; }
    double Volume { get; set; }
    bool IsMuted { get; set; }
    bool HasNext { get; }
    bool HasPrevious { get; }
    QueueSource? Source { get; }
    bool ShuffleEnabled { get; set; }
    RepeatMode RepeatMode { get; set; }
    IReadOnlyList<PlaybackTrack> History { get; }

    event EventHandler<TrackEndedEventArgs>? TrackEnded;
    event EventHandler? QueueChanged;

    void PlayQueue(IList<PlaybackTrack> tracks, int startIndex = 0, QueueSource? source = null);
    void Resume();
    void Pause();
    void Stop();
    void NextTrack();
    void PreviousTrack();
    void Seek(double positionSeconds);
    void AddNext(IList<PlaybackTrack> tracks);
    void AddToEnd(IList<PlaybackTrack> tracks);
    void RemoveAt(int index);
    void MoveTrack(int fromIndex, int toIndex);
    void ClearUpcoming();
    void ClearAll();
    void JumpToTrack(string trackId);
    void JumpToQueueIndex(int effectiveIndex);
    IReadOnlyList<PlaybackTrack> GetEffectiveQueue();
}
```

- [ ] **Step 5: Update DI registration**

In `Hypersonic/App.xaml.cs`, replace line 144:

```csharp
            services.AddSingleton<IPlaybackService, PlaybackService>();
```

with:

```csharp
            services.AddSingleton<PlaybackService>();
            services.AddSingleton<IPlaybackService>(sp => sp.GetRequiredService<PlaybackService>());
            services.AddSingleton<IReadOnlyPlaybackState>(sp => sp.GetRequiredService<PlaybackService>());
```

(`GetRequiredService` is in `Microsoft.Extensions.DependencyInjection`, already imported by `App.xaml.cs`.)

- [ ] **Step 6: Write the DI same-instance test**

Create `Hypersonic.Tests/Services/PlaybackStateRegistrationTests.cs` (validates the exact registration pattern App uses, with the shared fake so no `MediaPlayer` is constructed):

```csharp
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Tests.Fakes;

using Microsoft.Extensions.DependencyInjection;

namespace Hypersonic.Tests.Services;

[TestClass]
public class PlaybackStateRegistrationTests
{
    [TestMethod]
    public void BothInterfaces_ResolveToTheSameSingleton()
    {
        var services = new ServiceCollection();
        services.AddSingleton<FakePlaybackService>();
        services.AddSingleton<IPlaybackService>(sp => sp.GetRequiredService<FakePlaybackService>());
        services.AddSingleton<IReadOnlyPlaybackState>(sp => sp.GetRequiredService<FakePlaybackService>());
        using var provider = services.BuildServiceProvider();

        var command = provider.GetRequiredService<IPlaybackService>();
        var read = provider.GetRequiredService<IReadOnlyPlaybackState>();

        Assert.AreSame(command, read, "observe and command handles must be one instance");
    }
}
```

If `Microsoft.Extensions.DependencyInjection` is not resolvable in `Hypersonic.Tests`, add the package (it ships with `Microsoft.Extensions.Hosting`, already referenced transitively via the app project):
`dotnet add Hypersonic.Tests/Hypersonic.Tests.csproj package Microsoft.Extensions.DependencyInjection`

- [ ] **Step 7: Run both test projects (green) and build the app**

Run: `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj`
Expected: PASS (interface relationship test green).
Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64`
Expected: PASS (DI same-instance test green; all existing tests still compile — `FakePlaybackService` already implements every member).
Run: `dotnet build Hypersonic/Hypersonic.csproj -p:Platform=x64`
Expected: BUILD SUCCEEDED.

- [ ] **Step 8: Commit**

```bash
git add Hypersonic.Core/Contracts/Services/IReadOnlyPlaybackState.cs Hypersonic.Core/Contracts/Services/IPlaybackService.cs Hypersonic/App.xaml.cs Hypersonic.Core.Tests/Contracts/PlaybackInterfaceTests.cs Hypersonic.Tests/Services/PlaybackStateRegistrationTests.cs
git commit -m "feat(playback): add IReadOnlyPlaybackState observe interface and map both to one singleton"
```

---

### Task 3: Migrate `NowPlayingViewModel` to the read-only interface

**Files:**
- Modify: `Hypersonic/ViewModels/NowPlayingViewModel.cs` (field + both constructors)

This is a pure-observer migration. `NowPlayingViewModel` only reads `NowPlaying`/`Position`/`IsPlaying`/`Duration` and the three observation events — all on the base interface.

- [ ] **Step 1: Change the field and both constructor parameter types**

In `Hypersonic/ViewModels/NowPlayingViewModel.cs`:

Change the field (line 26):
```csharp
    private readonly IPlaybackService _playbackService;
```
to:
```csharp
    private readonly IReadOnlyPlaybackState _playbackService;
```

Change the first constructor parameter (line 70):
```csharp
        IPlaybackService playbackService,
```
to:
```csharp
        IReadOnlyPlaybackState playbackService,
```

Change the internal constructor parameter (line 85):
```csharp
        IPlaybackService playbackService,
```
to:
```csharp
        IReadOnlyPlaybackState playbackService,
```

(No other changes — every member used is on `IReadOnlyPlaybackState`. The DI container resolves `IReadOnlyPlaybackState` to the same singleton.)

- [ ] **Step 2: Build the app and run app tests**

Run: `dotnet build Hypersonic/Hypersonic.csproj -p:Platform=x64`
Expected: BUILD SUCCEEDED.
Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64`
Expected: PASS — existing `NowPlayingViewModel` tests are unchanged; their `FakePlaybackService` is-a `IReadOnlyPlaybackState` (via `IPlaybackService`), so it still satisfies the narrowed constructor.

- [ ] **Step 3: Commit**

```bash
git add Hypersonic/ViewModels/NowPlayingViewModel.cs
git commit -m "refactor(lyrics): consume IReadOnlyPlaybackState in NowPlayingViewModel (observe-only)"
```

---

### Task 4a: Player-bar clock plumbing, anchoring, tick, and PositionText

**Files:**
- Modify: `Hypersonic/ViewModels/PlayerViewModel.cs`
- Create: `Hypersonic.Tests/ViewModels/PlayerViewModelPositionTests.cs`

Adds the private clock, a ~30 fps tick (test-pumpable), event-driven anchoring (`PositionChanged`/`PlaybackStateChanged`/`TrackChanged`), and second-granular `PositionText`. Scrub gating comes in Task 4b — for 4a, `_isScrubbing` exists and is always `false`.

- [ ] **Step 1: Write the failing position tests**

Create `Hypersonic.Tests/ViewModels/PlayerViewModelPositionTests.cs`:

```csharp
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;
using Hypersonic.ViewModels;

using CommunityToolkit.Mvvm.Messaging;

namespace Hypersonic.Tests.ViewModels;

[TestClass]
public class PlayerViewModelPositionTests
{
    // Self-contained fake (matches the pattern in PlayerViewModelMetadataTests): all members
    // non-throwing, Seek recorded, plus Raise* helpers to drive the VM's event handlers.
    private sealed class FakePlaybackService : IPlaybackService
    {
        public PlaybackTrack? NowPlaying { get; set; }
        public IReadOnlyList<PlaybackTrack> Queue => Array.Empty<PlaybackTrack>();
        public int QueueIndex => -1;
        public int EffectiveQueueIndex => -1;
        public bool IsPlaying { get; set; }
        public double Position { get; set; }
        public double Duration { get; set; }
        public double Volume { get; set; } = 1.0;
        public bool IsMuted { get; set; }
        public bool HasNext { get; set; }
        public bool HasPrevious { get; set; }
        public QueueSource? Source => null;
        public bool ShuffleEnabled { get; set; }
        public RepeatMode RepeatMode { get; set; }
        public IReadOnlyList<PlaybackTrack> History => Array.Empty<PlaybackTrack>();

        public List<double> Seeks { get; } = new();

        public event EventHandler? PlaybackStateChanged;
        public event EventHandler<TrackChangedEventArgs>? TrackChanged;
        public event EventHandler<TrackEndedEventArgs>? TrackEnded;
        public event EventHandler? PositionChanged;
        public event EventHandler? QueueChanged;

        public void RaisePlaybackStateChanged() => PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        public void RaisePositionChanged() => PositionChanged?.Invoke(this, EventArgs.Empty);
        public void RaiseTrackChanged(TrackChangeDirection d) => TrackChanged?.Invoke(this, new TrackChangedEventArgs(d));

        public void PlayQueue(IList<PlaybackTrack> tracks, int startIndex = 0, QueueSource? source = null) { }
        public void Resume() { }
        public void Pause() { }
        public void Stop() { }
        public void NextTrack() { }
        public void PreviousTrack() { }
        public void Seek(double positionSeconds) { Seeks.Add(positionSeconds); Position = positionSeconds; }
        public void AddNext(IList<PlaybackTrack> tracks) { }
        public void AddToEnd(IList<PlaybackTrack> tracks) { }
        public void RemoveAt(int index) { }
        public void MoveTrack(int fromIndex, int toIndex) { }
        public void ClearUpcoming() { }
        public void ClearAll() { }
        public void JumpToTrack(string trackId) { }
        public void JumpToQueueIndex(int effectiveIndex) { }
        public IReadOnlyList<PlaybackTrack> GetEffectiveQueue() => Array.Empty<PlaybackTrack>();
    }

    private sealed class FakeAppStateService : IAppStateService
    {
        public AppState State { get; } = new();
        public Task InitializeAsync() => Task.CompletedTask;
        public Task SaveAsync() => Task.CompletedTask;
        public Task SavePlaybackAsync() => Task.CompletedTask;
        public Task SaveWindowAsync() => Task.CompletedTask;
        public Task SaveNavigationAsync() => Task.CompletedTask;
        public Task SaveGeneralAsync() => Task.CompletedTask;
        public void SaveSync() { }
    }

    private static PlayerViewModel CreatePlayer(FakePlaybackService fake, Func<double> nowMs) =>
        new(fake, new FakeAppStateService(),
            new Hypersonic.Tests.Fakes.FakeFavoritesService(),
            new WeakReferenceMessenger(),
            new Hypersonic.Tests.Fakes.NullLoggingService(),
            createTimer: false, nowMs: nowMs);

    private static void StartPlaying(FakePlaybackService fake)
    {
        fake.IsPlaying = true;
        fake.RaisePlaybackStateChanged();
    }

    [TestMethod]
    public void Tick_AdvancesPositionBetweenAnchors_InSeconds_NoSeek()
    {
        double now = 0;
        var fake = new FakePlaybackService();
        var vm = CreatePlayer(fake, () => now);
        StartPlaying(fake);

        fake.Position = 10;          // 10s
        fake.RaisePositionChanged(); // anchor 10_000ms at now=0

        now = 250;                   // 250ms later
        vm.PumpPositionTickForTest();

        Assert.AreEqual(10.25, vm.Position, 1e-3, "estimate advances 250ms in seconds, not 1000x");
        Assert.AreEqual(0, fake.Seeks.Count, "the smoothing tick must never Seek");
    }

    [TestMethod]
    public void TrackChangeWhilePlaying_ReAnchorsToZero()
    {
        double now = 0;
        var fake = new FakePlaybackService();
        var vm = CreatePlayer(fake, () => now);
        StartPlaying(fake);

        fake.Position = 200;
        fake.RaisePositionChanged(); // anchor far along
        now = 1000;

        // Next/Previous: service resets Position to 0 and fires TrackChanged WITHOUT PositionChanged.
        fake.NowPlaying = new PlaybackTrack { Id = "t2", Title = "Next", Duration = 300 };
        fake.Position = 0;
        fake.RaiseTrackChanged(TrackChangeDirection.Next);

        now = 1010;
        vm.PumpPositionTickForTest();

        Assert.IsTrue(vm.Position < 0.1, "new track starts near 0, not the previous timeline");
    }

    [TestMethod]
    public void StopOrdering_PositionEventBeforeStateEvent_StillLandsAtZero()
    {
        double now = 0;
        var fake = new FakePlaybackService();
        var vm = CreatePlayer(fake, () => now);
        StartPlaying(fake);
        fake.Position = 10;
        fake.RaisePositionChanged();
        now = 2000;

        // Real Stop() order: IsPlaying=false, then PositionChanged(0), then PlaybackStateChanged.
        fake.IsPlaying = false;
        fake.Position = 0;
        fake.RaisePositionChanged();      // per-event write must land even though tick "ran" last
        fake.RaisePlaybackStateChanged();

        Assert.AreEqual(0, vm.Position, 1e-3, "stop renders 0 regardless of event order");
    }

    [TestMethod]
    public void SeekWhilePaused_WritesPositionOnce_WithNoTick()
    {
        double now = 0;
        var fake = new FakePlaybackService();
        var vm = CreatePlayer(fake, () => now);
        // Not playing.
        fake.IsPlaying = false;
        fake.RaisePlaybackStateChanged();

        fake.Position = 42;
        fake.RaisePositionChanged();

        Assert.AreEqual(42, vm.Position, 1e-3, "a paused seek updates the thumb without a running tick");
    }

    [TestMethod]
    public void PositionText_OnlyChangesOnWholeSecondBoundary()
    {
        double now = 0;
        var fake = new FakePlaybackService();
        var vm = CreatePlayer(fake, () => now);
        StartPlaying(fake);
        fake.Position = 5;
        fake.RaisePositionChanged();      // "0:05"
        Assert.AreEqual("0:05", vm.PositionText);

        now = 400;                        // still second 5
        vm.PumpPositionTickForTest();
        Assert.AreEqual("0:05", vm.PositionText, "sub-second advance must not reformat the label");

        now = 1100;                       // now second 6
        vm.PumpPositionTickForTest();
        Assert.AreEqual("0:06", vm.PositionText);
    }

    [TestMethod]
    public void StopNearStart_SnapsToZero_NotConverge()
    {
        double now = 0;
        var fake = new FakePlaybackService();
        var vm = CreatePlayer(fake, () => now);
        StartPlaying(fake);
        fake.Position = 0.2;              // 200ms in — inside the 250ms converge band
        fake.RaisePositionChanged();

        // Real Stop(): IsPlaying=false BEFORE PositionChanged(0), then PlaybackStateChanged.
        fake.IsPlaying = false;
        fake.Position = 0;
        fake.RaisePositionChanged();      // not playing → hard snap to 0 (NOT a converge from 0.2)
        fake.RaisePlaybackStateChanged(); // SetPlaying(false) freezes the clock

        Assert.AreEqual(0, vm.Position, 1e-3, "a near-start stop snaps to 0, it does not converge from 0.2");

        now = 50;
        vm.PumpPositionTickForTest();
        Assert.AreEqual(0, vm.Position, 1e-3, "frozen at 0; no converge rebound pulls the thumb back up");
    }

    [TestMethod]
    public void TrackChangeNearStart_SnapsToZero()
    {
        double now = 0;
        var fake = new FakePlaybackService();
        var vm = CreatePlayer(fake, () => now);
        StartPlaying(fake);
        fake.Position = 0.15;            // 150ms — inside the converge band
        fake.RaisePositionChanged();

        fake.NowPlaying = new PlaybackTrack { Id = "t2", Title = "Next", Duration = 300 };
        fake.Position = 0;
        fake.RaiseTrackChanged(TrackChangeDirection.Next);

        now = 30;
        vm.PumpPositionTickForTest();
        Assert.IsTrue(vm.Position < 0.05, "new track snaps to ~0 even though the prior estimate was within the band");
    }
}
```

- [ ] **Step 2: Run to confirm failure**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~PlayerViewModelPositionTests"`
Expected: BUILD FAILURE — `PumpPositionTickForTest` and the `nowMs` constructor parameter don't exist.

- [ ] **Step 3: Add the clock fields**

In `Hypersonic/ViewModels/PlayerViewModel.cs`, add `using System.Diagnostics;` and `using Hypersonic.Core.Services;` to the using block, and add these fields next to the existing private fields (after line 24, `private readonly DispatcherQueueTimer? _volumeSaveTimer;`):

```csharp
    private readonly PlaybackClock _clock;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly DispatcherQueueTimer? _positionTimer;
    private bool _isScrubbing;
    private int _lastWholeSecond = -1;
```

- [ ] **Step 4: Add the `nowMs` parameter and initialize the clock + tick**

Change the internal constructor signature (line 149-155) to add an optional now-source:

```csharp
    internal PlayerViewModel(
        IPlaybackService playbackService,
        IAppStateService appStateService,
        IFavoritesService favoritesService,
        IMessenger messenger,
        ILoggingService logService,
        bool createTimer,
        Func<double>? nowMs = null)
```

Inside the constructor, immediately after the field assignments (`_log = logService.GetLogger(...)`, around line 161) add:

```csharp
        _clock = new PlaybackClock(nowMs ?? (() => _stopwatch.Elapsed.TotalMilliseconds));
```

Then inside the existing `if (createTimer)` block, after the volume-timer setup (after line 177's closing `};`), add the position tick:

```csharp
            _positionTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            _positionTimer.Interval = TimeSpan.FromMilliseconds(33); // ~30 fps
            _positionTimer.IsRepeating = true;
            _positionTimer.Tick += OnPositionTick;
```

(The public constructor passes `createTimer: true` and `nowMs` defaults to null → the production `Stopwatch` source. Existing test calls with `createTimer: false` keep `nowMs` null → harmless real `Stopwatch`, never read because they don't pump or anchor.)

- [ ] **Step 5: Rewrite the position/state/track handlers and add the helpers**

Replace the existing `OnPositionChanged` event handler (lines 530-536):

```csharp
    private void OnPositionChanged(object? sender, EventArgs e)
    {
        _isExternalUpdate = true;
        Position = _playbackService.Position;
        _isExternalUpdate = false;
        PositionText = FormatTime(_playbackService.Position);
    }
```

with (anchor always; write the one-shot estimate unless the user is dragging):

```csharp
    private void OnPositionChanged(object? sender, EventArgs e)
    {
        // Routine 500ms position ticks fire only WHILE PLAYING → smooth converge. Any PositionChanged
        // that arrives while NOT playing is a semantic jump (stop-to-0, paused seek, clear) → hard snap,
        // so the thumb lands exactly even when the delta sits inside the converge band.
        _clock.SetPosition(_playbackService.Position * 1000.0, hardSnap: !_playbackService.IsPlaying);
        if (!_isScrubbing)
        {
            WriteEstimatedPosition();
        }
    }

    // Fired ~30x/s while playing (and once per anchor). Writes the interpolated position into the
    // bound Position; the _isExternalUpdate guard stops the OnPositionChanged(double) setter from
    // treating our own write as a user Seek.
    private void OnPositionTick(DispatcherQueueTimer sender, object args)
    {
        if (_isScrubbing)
        {
            return;
        }
        WriteEstimatedPosition();
    }

    private void WriteEstimatedPosition()
    {
        var seconds = _clock.Estimate() / 1000.0;
        if (seconds < 0)
        {
            seconds = 0;
        }
        if (Duration > 0 && seconds > Duration)
        {
            seconds = Duration; // don't overshoot the end before the next track anchors
        }

        _isExternalUpdate = true;
        Position = seconds;
        _isExternalUpdate = false;

        var whole = (int)seconds;
        if (whole != _lastWholeSecond)
        {
            _lastWholeSecond = whole;
            PositionText = FormatTime(seconds);
        }
    }

    // Runs the tick only while it is useful: playing and not being dragged.
    private void UpdatePositionTimer()
    {
        if (_positionTimer is null)
        {
            return;
        }
        if (_playbackService.IsPlaying && !_isScrubbing)
        {
            _positionTimer.Start();
        }
        else
        {
            _positionTimer.Stop();
        }
    }

    // Test seam: pump one tick deterministically (the real DispatcherQueueTimer isn't created in tests).
    internal void PumpPositionTickForTest()
    {
        if (!_isScrubbing)
        {
            WriteEstimatedPosition();
        }
    }
```

In `OnPlaybackStateChanged` (lines 471-484), add the clock + timer sync just before the closing `_isSavingFromExternal = false;`:

```csharp
        _clock.SetPlaying(_playbackService.IsPlaying);
        UpdatePositionTimer();
```

In `OnTrackChanged` (lines 503-508), replace:

```csharp
        Duration = track?.Duration ?? 0;
        DurationText = FormatTime(track?.Duration ?? 0);
        _isExternalUpdate = true;
        Position = 0;
        _isExternalUpdate = false;
        PositionText = "0:00";
```

with (re-anchor the clock to the new track's start with a hard snap, and self-heal any orphaned scrub):

```csharp
        Duration = track?.Duration ?? 0;
        DurationText = FormatTime(track?.Duration ?? 0);
        _isScrubbing = false;                  // self-heal: a track change ends any orphaned drag
        _clock.SetPosition(0, hardSnap: true); // semantic reset → snap, never converge from the old timeline
        _lastWholeSecond = 0;
        _isExternalUpdate = true;
        Position = 0;
        _isExternalUpdate = false;
        PositionText = "0:00";
        UpdatePositionTimer();                 // resume the tick if it was suspended by the orphaned scrub
```

- [ ] **Step 6: Run the position tests (green)**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~PlayerViewModelPositionTests"`
Expected: PASS (7 tests).

- [ ] **Step 7: Run the full app test project (no regressions) and build**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64`
Expected: PASS (all existing PlayerViewModel tests still green).
Run: `dotnet build Hypersonic/Hypersonic.csproj -p:Platform=x64`
Expected: BUILD SUCCEEDED.

- [ ] **Step 8: Commit**

```bash
git add Hypersonic/ViewModels/PlayerViewModel.cs Hypersonic.Tests/ViewModels/PlayerViewModelPositionTests.cs
git commit -m "feat(player): interpolate the seek bar between 500ms anchors with a per-instance PlaybackClock"
```

---

### Task 4b: Scrub gating and device-agnostic re-anchor

**Files:**
- Modify: `Hypersonic/ViewModels/PlayerViewModel.cs`
- Modify: `Hypersonic.Tests/ViewModels/PlayerViewModelPositionTests.cs`

Adds `BeginScrub`/`EndScrub` (suspend the push during a pointer drag, re-anchor + resume on end), and makes any *user-originated* `Position` write re-anchor the clock (covers keyboard/gamepad/UIA/track-click uniformly).

- [ ] **Step 1: Add the failing scrub + re-anchor tests**

Append these methods to `PlayerViewModelPositionTests` (inside the class, before the closing brace):

```csharp
    [TestMethod]
    public void UserWrite_ReAnchorsClock_AndSeeks()
    {
        double now = 0;
        var fake = new FakePlaybackService();
        var vm = CreatePlayer(fake, () => now);
        StartPlaying(fake);
        fake.Position = 10;
        fake.RaisePositionChanged();
        now = 1000;

        // Simulate a keyboard/track-click change: the bound setter runs with _isExternalUpdate == false.
        vm.Position = 90;

        Assert.AreEqual(1, fake.Seeks.Count, "a user write seeks");
        Assert.AreEqual(90, fake.Seeks[0], 1e-6);

        now = 1100; // 100ms after the user write
        vm.PumpPositionTickForTest();
        Assert.IsTrue(vm.Position is > 89.9 and < 90.5, "tick continues from the user's value, not the old timeline");
    }

    [TestMethod]
    public void Drag_SuspendsTickPush_AndResumesOnEnd()
    {
        double now = 0;
        var fake = new FakePlaybackService();
        var vm = CreatePlayer(fake, () => now);
        StartPlaying(fake);
        fake.Position = 10;
        fake.RaisePositionChanged();

        vm.BeginScrub();
        vm.Position = 120;                 // the drag owns the thumb
        now = 5000;
        vm.PumpPositionTickForTest();      // must NOT move the thumb away from the drag value
        Assert.AreEqual(120, vm.Position, 1e-3, "tick is suspended during a drag");

        vm.EndScrub();
        now = 5100;
        vm.PumpPositionTickForTest();
        Assert.IsTrue(vm.Position is > 119.9 and < 120.5, "after release the clock resumes from the drop point");
    }

    [TestMethod]
    public void EndScrub_IsIdempotent_FailSafe()
    {
        double now = 0;
        var fake = new FakePlaybackService();
        var vm = CreatePlayer(fake, () => now);
        StartPlaying(fake);

        // A missed/duplicated end must never leave the push stuck suspended.
        vm.EndScrub();
        vm.EndScrub();
        fake.Position = 30;
        fake.RaisePositionChanged();
        now = 30; // tiny advance
        vm.PumpPositionTickForTest();

        Assert.AreEqual(30, vm.Position, 0.1, "push is live after redundant EndScrub");
    }

    [TestMethod]
    public void SmallUserNudge_SnapsImmediately_NotConverge()
    {
        double now = 0;
        var fake = new FakePlaybackService();
        var vm = CreatePlayer(fake, () => now);
        StartPlaying(fake);
        fake.Position = 10;
        fake.RaisePositionChanged();      // anchor at 10s

        vm.Position = 10.1;               // a 100ms keyboard/UIA nudge — inside the converge band

        Assert.AreEqual(1, fake.Seeks.Count);
        Assert.AreEqual(10.1, fake.Seeks[0], 1e-6);

        vm.PumpPositionTickForTest();     // now still 0 → estimate == the snapped anchor
        Assert.AreEqual(10.1, vm.Position, 1e-3, "a small user nudge lands exactly, not a 120ms converge slide");
    }

    [TestMethod]
    public void OrphanedScrub_RecoversOnTrackChange()
    {
        double now = 0;
        var fake = new FakePlaybackService();
        var vm = CreatePlayer(fake, () => now);
        StartPlaying(fake);

        vm.BeginScrub();                  // press — pretend the end (capture-lost) never arrives
        vm.Position = 120;
        now = 5000;
        vm.PumpPositionTickForTest();
        Assert.AreEqual(120, vm.Position, 1e-3, "suspended while the (orphaned) scrub is active");

        // A track change is an unambiguous 'drag is over' signal → self-heal clears the stuck scrub.
        fake.NowPlaying = new PlaybackTrack { Id = "t2", Title = "Next", Duration = 300 };
        fake.Position = 0;
        fake.RaiseTrackChanged(TrackChangeDirection.Next);

        now = 5100;
        vm.PumpPositionTickForTest();
        Assert.IsTrue(vm.Position < 0.2, "push resumed after the self-heal; thumb follows the new track, not stuck at 120");
    }
```

- [ ] **Step 2: Run to confirm failure**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~PlayerViewModelPositionTests"`
Expected: BUILD FAILURE — `BeginScrub`/`EndScrub` don't exist.

- [ ] **Step 3: Add `BeginScrub`/`EndScrub` and re-anchor on user write**

In `Hypersonic/ViewModels/PlayerViewModel.cs`, add the two public methods next to the position helpers:

```csharp
    /// <summary>
    /// The user started interacting with the seek bar (pointer drag). Suspends the estimator's
    /// per-frame push so the thumb follows the pointer exactly. Paired with <see cref="EndScrub"/>;
    /// the pairing is fail-safe — any end (release / capture-lost / cancel) resumes the push.
    /// </summary>
    public void BeginScrub()
    {
        _isScrubbing = true;
        UpdatePositionTimer(); // stop the tick while dragging
    }

    /// <summary>
    /// The user finished interacting with the seek bar. Re-anchors the clock to the final position
    /// and resumes the push. Idempotent so a missed or duplicated end can never get stuck suspended.
    /// </summary>
    public void EndScrub()
    {
        _isScrubbing = false;
        _clock.SetPosition(Position * 1000.0, hardSnap: true); // land exactly on the drop point
        UpdatePositionTimer(); // resume if still playing
    }
```

Then update the generated property callback `OnPositionChanged(double value)` (lines 434-440) from:

```csharp
    partial void OnPositionChanged(double value)
    {
        if (!_isExternalUpdate)
        {
            _playbackService.Seek(value);
        }
    }
```

to (a user-originated write hard-snaps the clock — so even a small keyboard/UIA nudge lands exactly — then seeks):

```csharp
    partial void OnPositionChanged(double value)
    {
        if (!_isExternalUpdate)
        {
            _clock.SetPosition(value * 1000.0, hardSnap: true);
            _playbackService.Seek(value);
        }
    }
```

- [ ] **Step 4: Run the scrub tests (green)**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~PlayerViewModelPositionTests"`
Expected: PASS (12 tests total).

- [ ] **Step 5: Commit**

```bash
git add Hypersonic/ViewModels/PlayerViewModel.cs Hypersonic.Tests/ViewModels/PlayerViewModelPositionTests.cs
git commit -m "feat(player): re-anchor on user seek and suspend the push during a drag (fail-safe)"
```

---

### Task 5: Wire the seek slider to scrub gating (View)

**Files:**
- Modify: `Hypersonic/Views/ShellPage.xaml:574-578` (name the slider)
- Modify: `Hypersonic/Views/ShellPage.xaml.cs` (pointer handlers)

No unit test — this is View glue (the testable logic lives in `PlayerViewModel`, Task 4b). WinUI's `UIElement` has **no `GotPointerCapture` event** (only `PointerCaptureLost`), so we bracket the scrub with a begin on `PointerPressed` and a resume on **any** interaction-end event — `PointerCaptureLost`, `PointerReleased`, `PointerCanceled` — all routed to one idempotent `EndScrub`. The `Slider` captures the pointer on press, so a real drag always ends with `PointerCaptureLost` (which fires on release, cancel, capture-steal, disable, **and window deactivation**); a press that never captures still ends with `PointerReleased`/`PointerCanceled`. Multiple independent ends plus the Task 4a `TrackChanged` self-heal make an orphaned suspend unreachable in practice, with no timeout and no VM/pointer coupling. None of the end events fire mid-drag (we deliberately do *not* end on `PointerExited`, which fires when a captured drag leaves the slider bounds). `AddHandler(..., handledEventsToo: true)` is used because `Slider` may mark these events handled internally.

- [ ] **Step 1: Name the seek slider**

In `Hypersonic/Views/ShellPage.xaml`, change the seek `Slider` (lines 574-578) to add `x:Name="SeekSlider"`:

```xml
                    <Slider x:Name="SeekSlider" Grid.Column="6" Minimum="0"
                            Maximum="{x:Bind Player.Duration, Mode=OneWay}"
                            Value="{x:Bind Player.Position, Mode=TwoWay}"
                            ThumbToolTipValueConverter="{StaticResource SecondsToTimeConverter}"
                            VerticalAlignment="Center"/>
```

- [ ] **Step 2: Register the pointer handlers in the constructor**

In `Hypersonic/Views/ShellPage.xaml.cs`, at the end of the `ShellPage` constructor (after line 125, `UpdatePlayerCoverArtFallback();`), add:

```csharp
        SeekSlider.AddHandler(PointerPressedEvent,
            new PointerEventHandler(SeekSlider_PointerPressed), handledEventsToo: true);
        SeekSlider.AddHandler(PointerReleasedEvent,
            new PointerEventHandler(SeekSlider_EndScrub), handledEventsToo: true);
        SeekSlider.AddHandler(PointerCanceledEvent,
            new PointerEventHandler(SeekSlider_EndScrub), handledEventsToo: true);
        SeekSlider.AddHandler(PointerCaptureLostEvent,
            new PointerEventHandler(SeekSlider_EndScrub), handledEventsToo: true);
```

- [ ] **Step 3: Add the handler methods**

Add to the `ShellPage` class (e.g. after `MainWindow_Activated`, around line 361):

```csharp
    // Begin suspending the interpolation push when the user starts interacting with the seek bar.
    private void SeekSlider_PointerPressed(object sender, PointerRoutedEventArgs e) => Player.BeginScrub();

    // Resume on ANY end of the interaction. The Slider captures the pointer on press, so a captured drag
    // ends with PointerCaptureLost (release / cancel / steal / disable / window-deactivation); a press that
    // never captures still ends with PointerReleased / PointerCanceled. EndScrub is idempotent, so
    // overlapping ends are harmless. (WinUI has no GotPointerCapture event, hence this multi-signal bracket.)
    private void SeekSlider_EndScrub(object sender, PointerRoutedEventArgs e) => Player.EndScrub();
```

(`PointerEventHandler` and `PointerRoutedEventArgs` are in `Microsoft.UI.Xaml.Input`, already imported by `ShellPage.xaml.cs`.)

- [ ] **Step 4: Build the app**

Run: `dotnet build Hypersonic/Hypersonic.csproj -p:Platform=x64`
Expected: BUILD SUCCEEDED.

- [ ] **Step 5: Manual smoke test (deploy via Visual Studio F5)**

Verify, with a track playing:
1. The seek thumb glides smoothly (not 2 Hz steps) and the time label ticks once per second.
2. Dragging the thumb tracks the pointer; on release, playback resumes from the dropped position and the thumb continues smoothly.
3. Pressing Next/Previous while playing snaps the thumb to the new track's start (no forward jump-then-snap).
4. Pause freezes the thumb; seeking while paused moves it immediately.
5. Keyboard focus + arrow keys on the slider move the thumb and seek, and the thumb continues smoothly afterward.

- [ ] **Step 6: Commit**

```bash
git add Hypersonic/Views/ShellPage.xaml Hypersonic/Views/ShellPage.xaml.cs
git commit -m "feat(player): wire seek-slider drag to scrub gating with fail-safe capture-lost end"
```

---

### Task 6: Update CLAUDE.md and final verification

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Document the split + shared clock**

In `CLAUDE.md`, under the **Playback architecture** section, add a bullet:

```markdown
- **Read-only observation**: `IReadOnlyPlaybackState` (Core) is the observe half of a command/query split with `IPlaybackService`, which extends it. The same `PlaybackService` singleton is mapped to both interfaces in DI; pure observers (e.g. `NowPlayingViewModel`) depend on the read interface. `PlaybackClock` (`Hypersonic.Core.Services`) is a shared, ms-based position estimator: each surface (lyrics canvas on the render thread, player bar on the UI thread) owns its own instance fed by its own "now" source, interpolating the coarse 500 ms `Position` anchor into smooth motion. See `docs/design/service/2026-06-21-playback-progress-clock.md`.
```

- [ ] **Step 2: Full verification sweep**

Run: `dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj`
Expected: PASS.
Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64`
Expected: PASS.
Run: `dotnet build Hypersonic.sln -p:Platform=x64`
Expected: BUILD SUCCEEDED.

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md
git commit -m "docs(playback): note IReadOnlyPlaybackState split and shared PlaybackClock"
```

---

## Self-Review Notes

- **Spec coverage:** Same-object role interface (Task 2), per-consumer estimator / Core relocation as `public` (Task 1), millisecond unit boundary (Task 4a, `*1000` / `/1000`), `TrackChanged` re-anchor (Task 4a), order-independent stop write (Task 4a), device-agnostic re-anchor + fail-safe drag suspend (Task 4b, Task 5), zero-impact lyrics wiring (Task 3). All present.
- **Review-fix coverage (plan v2):**
  - *Semantic re-anchors snap, not converge.* `PlaybackClock.SetPosition` gains an additive `hardSnap` flag (Task 1; default off → lyrics frozen). The player bar passes `hardSnap: true` for the three semantic re-anchors it controls — `TrackChanged`→0, user write, `EndScrub` — and on the service `PositionChanged` event it snaps whenever `!IsPlaying` (routine 500 ms ticks only fire while playing, so a not-playing position event is always a stop/paused-seek). Small-delta tests (`StopNearStart`, `TrackChangeNearStart`, `SmallUserNudge`) fail under the old converge path and pass under hard-snap.
  - *Stuck-scrub is unreachable, plus a backstop.* The View begins the suspend on `PointerPressed` and resumes on **any** interaction-end event — `PointerCaptureLost`, `PointerReleased`, `PointerCanceled` — all routed to one idempotent `EndScrub` (Task 5). WinUI has no `GotPointerCapture` event, so this multi-signal bracket replaces a single capture-gained trigger; the `Slider` captures on press so capture-loss always fires, and the redundant release/cancel ends plus the `TrackChanged` self-heal close the remainder. `OrphanedScrub_RecoversOnTrackChange` drives `BeginScrub` with no matching end and asserts the self-heal backstop resumes the push.
- **Deferred items honored:** No read-model growth beyond the four members; no seek-on-release change; no shared progress control.
- **Type consistency:** `PlaybackClock` ms in/out, `SetPosition(authoritativeMs, hardSnap = false)` used identically in clock tests and the VM; VM converts seconds↔ms at every boundary; `BeginScrub`/`EndScrub`/`PumpPositionTickForTest`/`UpdatePositionTimer`/`WriteEstimatedPosition` names are used identically across impl and tests; `_isScrubbing`/`_lastWholeSecond`/`_clock`/`_positionTimer` declared in Task 4a before use in 4b.
- **Test seam:** `nowMs` constructor param defaults to null (production `Stopwatch`); `createTimer: false` existing test calls remain valid; ticks pumped via `PumpPositionTickForTest`. Test counts: 7 `PlaybackClockTests`, 12 `PlayerViewModelPositionTests` (7 after Task 4a, +5 after Task 4b).
