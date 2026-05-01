using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;
using Hypersonic.Models;
using Hypersonic.ViewModels;

namespace Hypersonic.Tests.ViewModels;

[TestClass]
public class PlayerViewModelMetadataTests
{
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

        public void RaiseTrackChanged() => TrackChanged?.Invoke(this, new TrackChangedEventArgs(TrackChangeDirection.Jump));
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

    private static PlaybackTrack CreateMultiArtistTrack() => new()
    {
        Id = "t1",
        Title = "Test Song",
        Artist = "Dr. Dre",
        ArtistId = "a1",
        DisplayArtist = "Dr. Dre feat. Eminem",
        Artists =
        [
            new SongArtistInfo { Name = "Dr. Dre", Id = "a1" },
            new SongArtistInfo { Name = "Eminem", Id = "a2" },
        ],
        HasStructuredArtists = true,
        FlatArtistText = "Dr. Dre feat. Eminem",
        Album = "Test Album",
        AlbumId = "alb1",
        Duration = 300,
    };

    [TestMethod]
    public void NowPlayingMetadataChanged_FiresAfterTrackChanged_WithCorrectMetadata()
    {
        var playback = new FakePlaybackService();
        var appState = new FakeAppStateService();
        var vm = new PlayerViewModel(playback, appState, createTimer: false);

        var track = CreateMultiArtistTrack();
        playback.NowPlaying = track;

        var eventFired = false;
        vm.NowPlayingMetadataChanged += (_, _) =>
        {
            eventFired = true;

            Assert.AreEqual("Dr. Dre feat. Eminem", vm.DisplayArtist);
            Assert.AreEqual(2, vm.Artists.Count);
            Assert.AreEqual("Dr. Dre", vm.Artists[0].Name);
            Assert.AreEqual("Eminem", vm.Artists[1].Name);
            Assert.IsTrue(vm.HasStructuredArtists);
            Assert.AreEqual("Test Album", vm.AlbumName);
            Assert.AreEqual("alb1", vm.AlbumId);
            Assert.AreEqual("Test Song", vm.TrackTitle);
            Assert.IsTrue(vm.HasTrack);
        };

        playback.RaiseTrackChanged();

        Assert.IsTrue(eventFired, "NowPlayingMetadataChanged must fire");
    }

    [TestMethod]
    public void NowPlayingMetadataChanged_NullTrack_ClearsMetadata()
    {
        var playback = new FakePlaybackService { NowPlaying = CreateMultiArtistTrack() };
        var appState = new FakeAppStateService();
        var vm = new PlayerViewModel(playback, appState, createTimer: false);

        playback.RaiseTrackChanged();
        Assert.IsTrue(vm.HasTrack);

        playback.NowPlaying = null;
        var eventFired = false;
        vm.NowPlayingMetadataChanged += (_, _) => eventFired = true;
        playback.RaiseTrackChanged();

        Assert.IsTrue(eventFired);
        Assert.IsFalse(vm.HasTrack);
        Assert.IsNull(vm.DisplayArtist);
        Assert.AreEqual(0, vm.Artists.Count);
        Assert.IsFalse(vm.HasStructuredArtists);
        Assert.AreEqual(string.Empty, vm.AlbumName);
        Assert.IsNull(vm.AlbumId);
    }

    [TestMethod]
    public void Constructor_HydratesMetadataFromExistingTrack()
    {
        var track = CreateMultiArtistTrack();
        var playback = new FakePlaybackService { NowPlaying = track, IsPlaying = true };
        var appState = new FakeAppStateService();

        var vm = new PlayerViewModel(playback, appState, createTimer: false);

        Assert.IsTrue(vm.HasTrack);
        Assert.AreEqual("Test Song", vm.TrackTitle);
        Assert.AreEqual("Dr. Dre feat. Eminem", vm.DisplayArtist);
        Assert.AreEqual(2, vm.Artists.Count);
        Assert.IsTrue(vm.HasStructuredArtists);
        Assert.AreEqual("Test Album", vm.AlbumName);
        Assert.AreEqual("alb1", vm.AlbumId);
        Assert.IsTrue(vm.IsPlaying);
    }
}
