using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;
using Hypersonic.Tests.Fakes;
using Hypersonic.ViewModels;

namespace Hypersonic.Tests.ViewModels;

[TestClass]
public class PlayerViewModelFavoriteTests
{
    // Local fake: RaiseTrackChanged() triggers OnTrackChanged -> RefreshQueue,
    // which calls GetEffectiveQueue()/EffectiveQueueIndex. This fake returns
    // safe empties (the shared Hypersonic.Tests.Fakes.FakePlaybackService throws
    // for those). The nested type takes precedence over the imported one here.
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
        public IReadOnlyList<PlaybackTrack> GetEffectiveQueue() => Array.Empty<PlaybackTrack>();

        public void RaiseTrackChanged() =>
            TrackChanged?.Invoke(this, new TrackChangedEventArgs(TrackChangeDirection.Jump));
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

    private static (PlayerViewModel vm, FakePlaybackService playback, RecordingSubsonicClient client)
        CreateViewModel(PlaybackTrack? nowPlaying = null)
    {
        var playback = new FakePlaybackService { NowPlaying = nowPlaying };
        var client = new RecordingSubsonicClient();
        var clientService = new FakeSubsonicClientService { Client = client };
        var vm = new PlayerViewModel(playback, new FakeAppStateService(), clientService,
            new NullLoggingService(), createTimer: false);
        return (vm, playback, client);
    }

    [TestMethod]
    public void OnTrackChanged_HydratesIsStarred()
    {
        var (vm, playback, _) = CreateViewModel();

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
        var (vm, playback, _) = CreateViewModel(new PlaybackTrack { Id = "s1", IsStarred = true });
        playback.RaiseTrackChanged(); // hydrate true

        playback.NowPlaying = null;
        playback.RaiseTrackChanged();

        Assert.IsFalse(vm.IsStarred, "No track => IsStarred false");
    }

    [TestMethod]
    public async Task ToggleStar_NotStarred_CallsStarAndFlips()
    {
        var track = new PlaybackTrack { Id = "s1", Title = "T", IsStarred = false };
        var (vm, _, client) = CreateViewModel(track);
        vm.IsStarred = false;

        await vm.ToggleStarCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.IsStarred, "VM flag flips to true");
        Assert.IsTrue(track.IsStarred, "Live PlaybackTrack flag flips to true");
        Assert.AreEqual(1, client.StarCallCount);
        Assert.AreEqual("s1", client.LastStarredId);
    }

    [TestMethod]
    public async Task ToggleStar_Starred_CallsUnstar()
    {
        var track = new PlaybackTrack { Id = "s1", Title = "T", IsStarred = true };
        var (vm, _, client) = CreateViewModel(track);
        vm.IsStarred = true;

        await vm.ToggleStarCommand.ExecuteAsync(null);

        Assert.IsFalse(vm.IsStarred);
        Assert.IsFalse(track.IsStarred);
        Assert.AreEqual(1, client.UnstarCallCount);
    }

    [TestMethod]
    public async Task ToggleStar_Failure_Reverts()
    {
        var track = new PlaybackTrack { Id = "s1", Title = "T", IsStarred = false };
        var (vm, _, client) = CreateViewModel(track);
        vm.IsStarred = false;
        client.StarShouldThrow = true;

        await vm.ToggleStarCommand.ExecuteAsync(null);

        Assert.IsFalse(vm.IsStarred, "VM flag reverts on failure");
        Assert.IsFalse(track.IsStarred, "Live track flag reverts on failure");
    }

    [TestMethod]
    public async Task ToggleStar_NoTrack_NoOp()
    {
        var (vm, _, client) = CreateViewModel(nowPlaying: null);

        await vm.ToggleStarCommand.ExecuteAsync(null);

        Assert.AreEqual(0, client.StarCallCount);
        Assert.AreEqual(0, client.UnstarCallCount);
    }

    [TestMethod]
    public async Task ToggleStar_ApiReturnsFailedStatus_Reverts()
    {
        // Subsonic returns HTTP 200 with status="failed"; the client does not throw,
        // so the optimistic flip must be reverted based on the response status.
        var track = new PlaybackTrack { Id = "s1", Title = "T", IsStarred = false };
        var (vm, _, client) = CreateViewModel(track);
        vm.IsStarred = false;
        client.ResponseStatus = "failed";

        await vm.ToggleStarCommand.ExecuteAsync(null);

        Assert.IsFalse(vm.IsStarred, "Failed status must revert the VM flag");
        Assert.IsFalse(track.IsStarred, "Failed status must revert the track flag");
    }

    [TestMethod]
    public async Task ToggleStar_TrackChangesDuringFailedRequest_DoesNotClobberNewTrack()
    {
        // Toggle track A, then have playback advance to B (starred) while the request
        // is in flight, then fail the request. The revert must not overwrite B's heart.
        var trackA = new PlaybackTrack { Id = "A", Title = "A", IsStarred = false };
        var (vm, playback, client) = CreateViewModel(trackA);
        playback.RaiseTrackChanged(); // hydrate A => IsStarred false

        client.Gate = new TaskCompletionSource<bool>();
        client.StarShouldThrow = true;

        var toggle = vm.ToggleStarCommand.ExecuteAsync(null); // optimistic A => true, awaits gate

        // Playback advances to a different, starred track mid-request.
        var trackB = new PlaybackTrack { Id = "B", Title = "B", IsStarred = true };
        playback.NowPlaying = trackB;
        playback.RaiseTrackChanged(); // OnTrackChanged hydrates IsStarred = true (B)
        Assert.IsTrue(vm.IsStarred, "Heart should reflect B (starred) before the request resolves");

        client.Gate.SetResult(true); // release -> StarAsync throws -> revert path runs
        await toggle;

        Assert.IsTrue(vm.IsStarred, "Revert must not clobber B's starred heart");
        Assert.IsFalse(trackA.IsStarred, "A's own state must still be reverted");
    }

    [TestMethod]
    public void NotifyThemeChanged_RaisesIsStarred()
    {
        // The hollow-heart Foreground binds IsStarred through a theme-dependent
        // converter, so a theme change must re-raise IsStarred to re-resolve the
        // brush (same mechanism as ShuffleEnabled/RepeatMode/IsQueueOpen).
        var (vm, _, _) = CreateViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.NotifyThemeChanged();

        CollectionAssert.Contains(raised, nameof(PlayerViewModel.IsStarred));
    }
}
