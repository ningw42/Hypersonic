using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;
using Hypersonic.Models;
using Hypersonic.ViewModels;

namespace Hypersonic.Tests.ViewModels;

[TestClass]
public class PlayerViewModelRefreshQueueTests
{
    private sealed class FakePlaybackService : IPlaybackService
    {
        public PlaybackTrack? NowPlaying { get; set; }
        public IReadOnlyList<PlaybackTrack> Queue => Effective;
        public int QueueIndex => EffectiveQueueIndex;
        public int EffectiveQueueIndex { get; set; }
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
        public event EventHandler<TrackChangedEventArgs>? TrackChanged;
        public event EventHandler<TrackEndedEventArgs>? TrackEnded;
        public event EventHandler? PositionChanged;
        public event EventHandler? QueueChanged;

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
        public void JumpToQueueIndex(int effectiveIndex) { }
        public IReadOnlyList<PlaybackTrack> GetEffectiveQueue() => Effective;

        public void Touch()
        {
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
            TrackChanged?.Invoke(this, new TrackChangedEventArgs(TrackChangeDirection.Jump));
            TrackEnded?.Invoke(this, new TrackEndedEventArgs { TrackId = "" });
            PositionChanged?.Invoke(this, EventArgs.Empty);
            QueueChanged?.Invoke(this, EventArgs.Empty);
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
        public Task SaveGeneralAsync() => Task.CompletedTask;
        public void SaveSync() { }
    }

    private static PlaybackTrack T(string id, string title = "") =>
        new() { Id = id, Title = string.IsNullOrEmpty(title) ? id : title };

    private static (PlayerViewModel vm, FakePlaybackService svc) Setup(IReadOnlyList<PlaybackTrack> effective, int index)
    {
        var svc = new FakePlaybackService { Effective = effective, EffectiveQueueIndex = index };
        var vm = new PlayerViewModel(svc, new FakeAppStateService(), new Hypersonic.Tests.Fakes.FakeFavoritesService(), new CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger(), new Hypersonic.Tests.Fakes.NullLoggingService(), createTimer: false);
        vm.RefreshQueue();
        return (vm, svc);
    }

    [TestMethod]
    public void NoOp_PreservesCollection_AndItemReferences()
    {
        var t0 = T("a"); var t1 = T("b");
        var (vm, svc) = Setup(new[] { t0, t1 }, 0);
        var collectionRef = vm.QueueItems;
        var item0 = vm.QueueItems[0];
        var item1 = vm.QueueItems[1];

        // Same effective list, same instances, same index.
        vm.RefreshQueue();

        Assert.AreSame(collectionRef, vm.QueueItems);
        Assert.AreSame(item0, vm.QueueItems[0]);
        Assert.AreSame(item1, vm.QueueItems[1]);
    }

    [TestMethod]
    public void CurrentIndexAdvance_PreservesCollection_FlipsHighlight()
    {
        var t0 = T("a"); var t1 = T("b");
        var (vm, svc) = Setup(new[] { t0, t1 }, 0);
        var collectionRef = vm.QueueItems;
        Assert.IsTrue(vm.QueueItems[0].IsCurrent);
        Assert.IsFalse(vm.QueueItems[1].IsCurrent);

        svc.EffectiveQueueIndex = 1;
        vm.RefreshQueue();

        Assert.AreSame(collectionRef, vm.QueueItems);
        Assert.IsFalse(vm.QueueItems[0].IsCurrent);
        Assert.IsTrue(vm.QueueItems[1].IsCurrent);
    }

    [TestMethod]
    public void LengthChange_Rebuilds()
    {
        var t0 = T("a"); var t1 = T("b");
        var (vm, svc) = Setup(new[] { t0, t1 }, 0);
        var collectionRef = vm.QueueItems;

        svc.Effective = new[] { t0, t1, T("c") };
        vm.RefreshQueue();

        Assert.AreNotSame(collectionRef, vm.QueueItems);
        Assert.AreEqual(3, vm.QueueItems.Count);
    }

    [TestMethod]
    public void SameIdNewInstance_ForcesRebuild_AndShowsNewMetadata()
    {
        var t0 = T("a", "Old Title");
        var (vm, svc) = Setup(new[] { (PlaybackTrack)t0 }, 0);
        var collectionRef = vm.QueueItems;
        Assert.AreEqual("Old Title", vm.QueueItems[0].Track.Title);

        var t0New = T("a", "New Title");
        svc.Effective = new[] { t0New };
        vm.RefreshQueue();

        Assert.AreNotSame(collectionRef, vm.QueueItems);
        Assert.AreSame(t0New, vm.QueueItems[0].Track);
        Assert.AreEqual("New Title", vm.QueueItems[0].Track.Title);
    }

    [TestMethod]
    public void DuplicateSameIdSwap_ForcesRebuild_PreservesOccurrence()
    {
        var a = new PlaybackTrack { Id = "d", Title = "A" };
        var b = new PlaybackTrack { Id = "d", Title = "B" };
        var (vm, svc) = Setup(new[] { a, b }, 0);
        Assert.AreSame(a, vm.QueueItems[0].Track);
        Assert.AreSame(b, vm.QueueItems[1].Track);

        // Swap the two same-Id occurrences.
        svc.Effective = new[] { b, a };
        vm.RefreshQueue();

        Assert.AreSame(b, vm.QueueItems[0].Track);
        Assert.AreSame(a, vm.QueueItems[1].Track);
    }

    [TestMethod]
    public void EmptyToNonEmpty_AndBack_RebuildsAndUpdatesIsQueueEmpty()
    {
        var (vm, svc) = Setup(Array.Empty<PlaybackTrack>(), -1);
        Assert.IsTrue(vm.IsQueueEmpty);

        svc.Effective = new[] { T("a") };
        svc.EffectiveQueueIndex = 0;
        vm.RefreshQueue();
        Assert.IsFalse(vm.IsQueueEmpty);
        Assert.AreEqual(1, vm.QueueItems.Count);

        svc.Effective = Array.Empty<PlaybackTrack>();
        svc.EffectiveQueueIndex = -1;
        vm.RefreshQueue();
        Assert.IsTrue(vm.IsQueueEmpty);
        Assert.AreEqual(0, vm.QueueItems.Count);
    }
}
