using CommunityToolkit.Mvvm.Messaging;

using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;
using Hypersonic.Messages;
using Hypersonic.Models;
using Hypersonic.Services;
using Hypersonic.Tests.Fakes;
using Hypersonic.ViewModels;

namespace Hypersonic.Tests.ViewModels;

[TestClass]
public class PlayerViewModelFavoriteTests
{
    // Local fake: RaiseTrackChanged() triggers OnTrackChanged -> RefreshQueue, which calls
    // GetEffectiveQueue()/EffectiveQueueIndex. This fake returns safe values (the shared
    // FakePlaybackService throws for those). EffectiveQueue is settable so a broadcast can be
    // observed updating the queued PlaybackTrack instances.
    private sealed class FakePlaybackService : IPlaybackService
    {
        public PlaybackTrack? NowPlaying { get; set; }
        public List<PlaybackTrack> EffectiveQueue { get; set; } = new();
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
        public IReadOnlyList<PlaybackTrack> GetEffectiveQueue() => EffectiveQueue;

        public void RaiseTrackChanged() =>
            TrackChanged?.Invoke(this, new TrackChangedEventArgs(TrackChangeDirection.Jump));
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

    private static (PlayerViewModel vm, FakePlaybackService playback, RecordingSubsonicClient client, WeakReferenceMessenger messenger)
        CreateViewModel(PlaybackTrack? nowPlaying = null)
    {
        var playback = new FakePlaybackService { NowPlaying = nowPlaying };
        if (nowPlaying is not null)
        {
            playback.EffectiveQueue.Add(nowPlaying);
        }

        var client = new RecordingSubsonicClient();
        var clientService = new FakeSubsonicClientService { Client = client };
        var messenger = new WeakReferenceMessenger();
        // A real FavoritesService wired to the same messenger so toggles round-trip through
        // the service and the optimistic flip arrives back at the VM via a broadcast.
        var favorites = new FavoritesService(clientService, new NullLoggingService(), messenger);
        var vm = new PlayerViewModel(playback, new FakeAppStateService(), favorites, messenger,
            new NullLoggingService(), createTimer: false);
        return (vm, playback, client, messenger);
    }

    // ---- Hydration ----

    [TestMethod]
    public void OnTrackChanged_HydratesIsStarred()
    {
        var (vm, playback, _, _) = CreateViewModel();

        playback.NowPlaying = new PlaybackTrack { Id = "s1", Title = "T", IsStarred = true };
        playback.RaiseTrackChanged();
        Assert.IsTrue(vm.IsStarred, "IsStarred must hydrate from the starred track");

        playback.NowPlaying = new PlaybackTrack { Id = "s2", Title = "T2", IsStarred = false };
        playback.RaiseTrackChanged();
        Assert.IsFalse(vm.IsStarred, "IsStarred must update to the new unstarred track");
    }

    [TestMethod]
    public void OnTrackChanged_NoTrack_IsStarredFalse()
    {
        var (vm, playback, _, _) = CreateViewModel(new PlaybackTrack { Id = "s1", IsStarred = true });
        playback.RaiseTrackChanged(); // hydrate true

        playback.NowPlaying = null;
        playback.RaiseTrackChanged();

        Assert.IsFalse(vm.IsStarred, "No track => IsStarred false");
    }

    // ---- Toggle delegation + broadcast round-trip ----

    [TestMethod]
    public async Task ToggleStar_NotStarred_DelegatesToServiceAndFlipsViaBroadcast()
    {
        var track = new PlaybackTrack { Id = "s1", Title = "T", IsStarred = false };
        var (vm, _, client, _) = CreateViewModel(track);

        await vm.ToggleStarCommand.ExecuteAsync(null);

        Assert.AreEqual(1, client.StarCallCount, "Toggle delegates to the service, which calls Star");
        Assert.AreEqual("s1", client.LastStarredId);
        Assert.IsTrue(vm.IsStarred, "The optimistic broadcast flips the mirror reentrantly");
        Assert.IsTrue(track.IsStarred, "The broadcast flips the queued PlaybackTrack instance");
    }

    [TestMethod]
    public async Task ToggleStar_StarThenUnstar_CallsBoth()
    {
        var track = new PlaybackTrack { Id = "s1", IsStarred = false };
        var (vm, _, client, _) = CreateViewModel(track);

        await vm.ToggleStarCommand.ExecuteAsync(null);    // star (store now knows s1)
        Assert.IsTrue(vm.IsStarred);
        Assert.AreEqual(1, client.StarCallCount);

        await vm.ToggleStarCommand.ExecuteAsync(null);    // unstar
        Assert.IsFalse(vm.IsStarred);
        Assert.AreEqual(1, client.UnstarCallCount);
    }

    [TestMethod]
    public async Task ToggleStar_Failure_RevertsMirrorAndTrackViaBroadcast()
    {
        var track = new PlaybackTrack { Id = "s1", IsStarred = false };
        var (vm, _, client, _) = CreateViewModel(track);
        client.StarShouldThrow = true;

        await vm.ToggleStarCommand.ExecuteAsync(null);

        Assert.IsFalse(vm.IsStarred, "Service revert broadcast rolls the mirror back");
        Assert.IsFalse(track.IsStarred, "And rolls the queued track back");
    }

    [TestMethod]
    public async Task ToggleStar_NoTrack_NoOp()
    {
        var (vm, _, client, _) = CreateViewModel(nowPlaying: null);

        await vm.ToggleStarCommand.ExecuteAsync(null);

        Assert.AreEqual(0, client.StarCallCount);
        Assert.AreEqual(0, client.UnstarCallCount);
    }

    [TestMethod]
    public async Task ToggleQueueItemStar_DelegatesToService()
    {
        var (vm, _, client, _) = CreateViewModel();
        var item = new QueueItem { Track = new PlaybackTrack { Id = "q1", IsStarred = false } };

        await vm.ToggleQueueItemStarCommand.ExecuteAsync(item);

        Assert.AreEqual(1, client.StarCallCount);
        Assert.AreEqual("q1", client.LastStarredId);
    }

    // ---- Broadcast handler (the mid-flight race) ----

    [TestMethod]
    public void Receive_MessageForNowPlaying_UpdatesMirror()
    {
        var track = new PlaybackTrack { Id = "s1", IsStarred = false };
        var (vm, _, _, messenger) = CreateViewModel(track);

        messenger.Send(new FavoritesChangedMessage(true, new[] { "s1" }));

        Assert.IsTrue(vm.IsStarred, "A broadcast containing the now-playing id updates the mirror");
    }

    [TestMethod]
    public void Receive_MessageForDifferentId_LeavesMirrorUnchanged()
    {
        var track = new PlaybackTrack { Id = "s1", IsStarred = false };
        var (vm, _, _, messenger) = CreateViewModel(track);

        messenger.Send(new FavoritesChangedMessage(true, new[] { "OTHER" }));

        Assert.IsFalse(vm.IsStarred, "A revert for a different id must not clobber the now-playing heart");
    }

    [TestMethod]
    public void Receive_MessageForQueuedTrack_UpdatesThatInstanceOnly()
    {
        var nowPlaying = new PlaybackTrack { Id = "s1", IsStarred = false };
        var other = new PlaybackTrack { Id = "s2", IsStarred = false };
        var (vm, playback, _, messenger) = CreateViewModel(nowPlaying);
        playback.EffectiveQueue.Add(other);

        messenger.Send(new FavoritesChangedMessage(true, new[] { "s2" }));

        Assert.IsTrue(other.IsStarred, "The matching queued track updates");
        Assert.IsFalse(nowPlaying.IsStarred, "A non-matching queued track is left alone");
        Assert.IsFalse(vm.IsStarred, "The mirror only follows the now-playing id");
    }

    [TestMethod]
    public void NotifyThemeChanged_RaisesIsStarred()
    {
        // The hollow-heart Foreground binds IsStarred through a theme-dependent converter, so a
        // theme change must re-raise IsStarred (same mechanism as ShuffleEnabled/RepeatMode).
        var (vm, _, _, _) = CreateViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.NotifyThemeChanged();

        CollectionAssert.Contains(raised, nameof(PlayerViewModel.IsStarred));
    }
}
