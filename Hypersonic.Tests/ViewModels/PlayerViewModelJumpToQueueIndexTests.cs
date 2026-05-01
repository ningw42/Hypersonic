using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;
using Hypersonic.Models;
using Hypersonic.ViewModels;

namespace Hypersonic.Tests.ViewModels;

[TestClass]
public class PlayerViewModelJumpToQueueIndexTests
{
    private sealed class FakePlaybackService : IPlaybackService
    {
        public PlaybackTrack? NowPlaying { get; set; }
        public IReadOnlyList<PlaybackTrack> Queue => Array.Empty<PlaybackTrack>();
        public int QueueIndex => -1;
        public int EffectiveQueueIndex { get; set; } = -1;
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
        public IReadOnlyList<PlaybackTrack> Effective { get; set; } = Array.Empty<PlaybackTrack>();

        public event EventHandler? PlaybackStateChanged;
        public event EventHandler? TrackChanged;
        public event EventHandler<TrackEndedEventArgs>? TrackEnded;
        public event EventHandler? PositionChanged;
        public event EventHandler? QueueChanged;

        public List<int> JumpToQueueIndexCalls { get; } = new();

        public void PlayQueue(IList<PlaybackTrack> tracks, int startIndex = 0, QueueSource? source = null) { }
        public void Resume() { }
        public void Pause() { }
        public void Stop() { }
        public void NextTrack() { }
        public void PreviousTrack() { }
        public void Seek(double positionSeconds) { }
        public void AddNext(IList<PlaybackTrack> tracks) { }
        public void AddToEnd(IList<PlaybackTrack> tracks) { }
        public void RemoveAt(int index) { }
        public void MoveTrack(int fromIndex, int toIndex) { }
        public void ClearUpcoming() { }
        public void ClearAll() { }
        public void JumpToTrack(string trackId) { }
        public void JumpToQueueIndex(int effectiveIndex) => JumpToQueueIndexCalls.Add(effectiveIndex);
        public IReadOnlyList<PlaybackTrack> GetEffectiveQueue() => Effective;

        public void RaiseQueueChanged() => QueueChanged?.Invoke(this, EventArgs.Empty);

        // Suppress unused-event warnings.
        public void Touch()
        {
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
            TrackChanged?.Invoke(this, EventArgs.Empty);
            TrackEnded?.Invoke(this, new TrackEndedEventArgs { TrackId = "" });
            PositionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class FakeAppStateService : IAppStateService
    {
        public AppState State { get; } = new();
        public Task InitializeAsync() => Task.CompletedTask;
        public Task SaveAsync() => Task.CompletedTask;
        public Task SavePlaybackAsync() => Task.CompletedTask;
        public Task SaveWindowAsync() => Task.CompletedTask;
        public Task SaveNavigationAsync() => Task.CompletedTask;
        public void SaveSync() { }
    }

    private static PlaybackTrack T(string id) => new() { Id = id, Title = id };

    [TestMethod]
    public void JumpToQueueIndex_ResolvesPosition_AndForwards()
    {
        var playback = new FakePlaybackService();
        var t0 = T("a"); var t1 = T("b"); var t2 = T("c");
        playback.Effective = new[] { t0, t1, t2 };
        playback.EffectiveQueueIndex = 0;
        var appState = new FakeAppStateService();
        var vm = new PlayerViewModel(playback, appState, createTimer: false);
        vm.RefreshQueue();

        vm.JumpToQueueIndex(vm.QueueItems[1]);

        CollectionAssert.AreEqual(new[] { 1 }, playback.JumpToQueueIndexCalls);
    }

    [TestMethod]
    public void JumpToQueueIndex_DuplicateTrackId_DistinguishesByPosition()
    {
        var playback = new FakePlaybackService();
        // Two distinct PlaybackTrack instances with same Id.
        var dup1 = new PlaybackTrack { Id = "d", Title = "d1" };
        var dup2 = new PlaybackTrack { Id = "d", Title = "d2" };
        playback.Effective = new[] { dup1, T("x"), dup2 };
        playback.EffectiveQueueIndex = 0;
        var appState = new FakeAppStateService();
        var vm = new PlayerViewModel(playback, appState, createTimer: false);
        vm.RefreshQueue();

        Assert.AreEqual(3, vm.QueueItems.Count);
        vm.JumpToQueueIndex(vm.QueueItems[2]);

        CollectionAssert.AreEqual(new[] { 2 }, playback.JumpToQueueIndexCalls);
    }

    [TestMethod]
    public void JumpToQueueIndex_NotInQueue_NoOp()
    {
        var playback = new FakePlaybackService();
        playback.Effective = new[] { T("a") };
        playback.EffectiveQueueIndex = 0;
        var appState = new FakeAppStateService();
        var vm = new PlayerViewModel(playback, appState, createTimer: false);
        vm.RefreshQueue();

        var stranger = new QueueItem { Track = T("z") };
        vm.JumpToQueueIndex(stranger);

        Assert.AreEqual(0, playback.JumpToQueueIndexCalls.Count);
    }
}
