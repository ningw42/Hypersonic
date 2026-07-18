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

    [TestMethod]
    public void PressWithoutDrag_DoesNotRewindToStalePosition()
    {
        double now = 0;
        var fake = new FakePlaybackService();
        var vm = CreatePlayer(fake, () => now);
        StartPlaying(fake);
        fake.Position = 10;
        fake.RaisePositionChanged();      // displayed ~10s
        Assert.AreEqual(10, vm.Position, 1e-3);

        vm.BeginScrub();                  // press on the thumb, no slider value change
        // Playback keeps advancing; a service tick arrives DURING the hold and re-anchors the clock,
        // but does not write Position (scrubbing) — so the displayed value is frozen at the press point.
        now = 600;
        fake.Position = 10.6;
        fake.RaisePositionChanged();
        Assert.AreEqual(10, vm.Position, 1e-3, "display is frozen during the hold");

        vm.EndScrub();                    // release with no slider change

        // EndScrub must resume at LIVE playback (~10.6), not rewind the thumb to the stale 10.
        Assert.IsTrue(vm.Position > 10.5, "release lands on live playback, not the stale pre-press value");

        now = 650;
        vm.PumpPositionTickForTest();
        Assert.IsTrue(vm.Position > 10.5, "and the resumed tick continues from live playback, not the stale value");
    }

    [TestMethod]
    public void StopThenResumeAttempt_ClockStaysAtZero_NoCreep()
    {
        double now = 0;
        var fake = new FakePlaybackService();
        var vm = CreatePlayer(fake, () => now);
        StartPlaying(fake);
        fake.Position = 30;
        fake.RaisePositionChanged();
        now = 5000;

        // Stop(): IsPlaying=false, then PositionChanged(0), then PlaybackStateChanged — with a wall-time
        // gap between the two stop events (a real-app frame gap). The clock must freeze AT the zero snap,
        // not keep "playing" into the gap and creep a few ms past zero.
        fake.IsPlaying = false;
        fake.Position = 0;
        fake.RaisePositionChanged();      // hard-snap to 0; the clock must also stop advancing
        now = 5050;                       // time passes before the state event arrives
        fake.RaisePlaybackStateChanged(); // SetPlaying(false) must not reanchor to a crept >0 value

        now = 9000;
        vm.PumpPositionTickForTest();     // the pump proves the frozen estimate is exactly 0
        Assert.AreEqual(0, vm.Position, 1e-9, "clock frozen exactly at 0 after stop; no playing-creep");
    }

    [TestMethod]
    public void QuickReleaseWithNoServiceTick_DoesNotRewindToCoarseAnchor()
    {
        double now = 0;
        var fake = new FakePlaybackService();
        var vm = CreatePlayer(fake, () => now);
        StartPlaying(fake);
        fake.Position = 10;
        fake.RaisePositionChanged();      // coarse service anchor at 10s, now=0

        now = 300;                        // 300ms later: the thumb interpolates to ~10.3 (no new service tick)
        vm.PumpPositionTickForTest();
        Assert.IsTrue(vm.Position is > 10.25 and < 10.35, "thumb interpolated ahead of the coarse 10s anchor");

        vm.BeginScrub();                  // press the thumb, no slider move
        vm.EndScrub();                    // quick release — _playbackService.Position is still the stale 10s

        // The release must keep the thumb at the live ~10.3, NOT rewind it to the 500ms coarse anchor (10s).
        Assert.IsTrue(vm.Position > 10.25, "a quick release does not rewind to the 500ms coarse anchor");
    }

    [TestMethod]
    public void StrayEndScrubWhileNotScrubbing_IsNoOp_NoRewind()
    {
        double now = 0;
        var fake = new FakePlaybackService();
        var vm = CreatePlayer(fake, () => now);
        StartPlaying(fake);
        fake.Position = 10;
        fake.RaisePositionChanged();
        now = 300;
        vm.PumpPositionTickForTest();
        Assert.IsTrue(vm.Position is > 10.25 and < 10.35);

        // The view routes several end signals (PointerReleased / Canceled / CaptureLost) into EndScrub;
        // an end that arrives when not actually scrubbing must be a true no-op, not a coarse re-anchor.
        vm.EndScrub();
        vm.PumpPositionTickForTest();     // now still 300

        Assert.IsTrue(vm.Position > 10.25, "a stray end while not scrubbing must not rewind the thumb");
    }
}
