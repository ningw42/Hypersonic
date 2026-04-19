using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;
using Hypersonic.Models;
using Hypersonic.ViewModels;

using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Hypersonic.Tests.ViewModels;

[TestClass]
public class PlaybackTrackPropagationTests
{
    private sealed class NullLogger : ILogger
    {
        public string Category => "Test";
        public bool IsEnabled(LogLevel level) => false;
        public void Log(LogLevel level, string message, object? context = null, Exception? exception = null) { }
        public void Debug(string message, object? context = null) { }
        public void Info(string message, object? context = null) { }
        public void Warning(string message, object? context = null, Exception? exception = null) { }
        public void Error(string message, object? context = null, Exception? exception = null) { }
    }

    private sealed class NullLoggingService : ILoggingService
    {
        public LogLevel MinimumLevel { get; set; }
        public bool IsEnabled(LogLevel level) => false;
        public void AddSink(ILogSink sink, LogLevel minimumLevel = LogLevel.Debug) { }
        public void RemoveSink(ILogSink sink) { }
        public ILogger GetLogger(string category) => new NullLogger();
    }

    private sealed class FakePlaybackService : IPlaybackService
    {
        public PlaybackTrack? NowPlaying => null;
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
        public event EventHandler? TrackChanged;
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
        public IReadOnlyList<PlaybackTrack> GetEffectiveQueue() => Array.Empty<PlaybackTrack>();
    }

    private sealed class FakeSubsonicClientService : ISubsonicClientService
    {
        public Client.ISubsonicClient? Client => null;
        public void CreateClient(ServerCredential credential) { }
        public Task<ConnectionTestResult> TestConnectionAsync(ServerCredential credential) =>
            throw new NotImplementedException();
    }

    private sealed class FakeNavigationService : INavigationService
    {
        public event NavigatedEventHandler? Navigated;
        public bool CanGoBack => false;
        public Frame? Frame { get; set; }
        public bool NavigateTo(string pageKey, object? parameter = null, bool clearNavigation = false) => true;
        public bool GoBack() => false;
    }

    private static SongInfo CreateMultiArtistSong() => new()
    {
        Id = "s1",
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
        Album = "Test Album",
        AlbumId = "alb1",
        Duration = 300,
        TrackNumber = 1,
        DiscNumber = 1,
    };

    [TestMethod]
    public void AlbumDetailViewModel_BuildPlaybackTracks_PropagatesStructuredArtistData()
    {
        var vm = new AlbumDetailViewModel(
            new FakeSubsonicClientService(),
            new FakePlaybackService(),
            new FakeNavigationService(),
            new NullLoggingService());

        vm._allSongs = [CreateMultiArtistSong()];
        vm.AlbumName = "Test Album";

        var tracks = vm.BuildPlaybackTracks();

        Assert.AreEqual(1, tracks.Count);
        var track = tracks[0];
        Assert.AreEqual("Dr. Dre feat. Eminem", track.DisplayArtist);
        Assert.AreEqual(2, track.Artists.Count);
        Assert.AreEqual("Dr. Dre", track.Artists[0].Name);
        Assert.AreEqual("a1", track.Artists[0].Id);
        Assert.AreEqual("Eminem", track.Artists[1].Name);
        Assert.AreEqual("a2", track.Artists[1].Id);
        Assert.IsTrue(track.HasStructuredArtists);
        Assert.AreEqual("Dr. Dre feat. Eminem", track.FlatArtistText);
    }

    [TestMethod]
    public void PlaylistDetailViewModel_BuildPlaybackTracks_PropagatesStructuredArtistData()
    {
        var vm = new PlaylistDetailViewModel(
            new FakeSubsonicClientService(),
            new FakePlaybackService(),
            new NullLoggingService());

        vm._allSongs = [CreateMultiArtistSong()];

        var tracks = vm.BuildPlaybackTracks();

        Assert.AreEqual(1, tracks.Count);
        var track = tracks[0];
        Assert.AreEqual("Dr. Dre feat. Eminem", track.DisplayArtist);
        Assert.AreEqual(2, track.Artists.Count);
        Assert.AreEqual("Dr. Dre", track.Artists[0].Name);
        Assert.AreEqual("Eminem", track.Artists[1].Name);
        Assert.IsTrue(track.HasStructuredArtists);
        Assert.AreEqual("Dr. Dre feat. Eminem", track.FlatArtistText);
    }

    [TestMethod]
    public void AlbumDetailViewModel_BuildPlaybackTracks_SingleArtistFallback()
    {
        var vm = new AlbumDetailViewModel(
            new FakeSubsonicClientService(),
            new FakePlaybackService(),
            new FakeNavigationService(),
            new NullLoggingService());

        var song = new SongInfo
        {
            Id = "s2",
            Title = "Simple Song",
            Artist = "Solo Artist",
            ArtistId = "a3",
            Artists = [new SongArtistInfo { Name = "Solo Artist", Id = "a3" }],
            HasStructuredArtists = false,
            Album = "Solo Album",
            AlbumId = "alb2",
            Duration = 200,
            TrackNumber = 1,
            DiscNumber = 1,
        };

        vm._allSongs = [song];
        vm.AlbumName = "Solo Album";

        var tracks = vm.BuildPlaybackTracks();
        var track = tracks[0];
        Assert.AreEqual("Solo Artist", track.FlatArtistText);
        Assert.AreEqual(1, track.Artists.Count);
        Assert.AreEqual("Solo Artist", track.Artists[0].Name);
    }
}
