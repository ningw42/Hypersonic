using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;
using Hypersonic.ViewModels;

using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Hypersonic.Tests.ViewModels;

[TestClass]
public class ShellViewModelSearchPlayTests
{
    private sealed class CapturingPlaybackService : IPlaybackService
    {
        public List<PlaybackTrack> LastPlayedTracks { get; } = [];

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
        public event EventHandler<TrackChangedEventArgs>? TrackChanged;
        public event EventHandler<TrackEndedEventArgs>? TrackEnded;
        public event EventHandler? PositionChanged;
        public event EventHandler? QueueChanged;

        public void PlayQueue(IList<PlaybackTrack> tracks, int startIndex = 0, QueueSource? source = null)
        {
            LastPlayedTracks.Clear();
            LastPlayedTracks.AddRange(tracks);
        }

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
    }

    private sealed class FakeNavigationService : INavigationService
    {
        public event NavigatedEventHandler? Navigated;
        public bool CanGoBack => false;
        public Frame? Frame { get; set; }
        public bool NavigateTo(string pageKey, object? parameter = null, bool clearNavigation = false) => true;
        public bool GoBack() => false;
    }

    private sealed class FakeNavigationViewService : INavigationViewService
    {
        public IList<object>? MenuItems => null;
        public object? SettingsItem => null;
        public void Initialize(NavigationView navigationView) { }
        public void UnregisterEvents() { }
        public NavigationViewItem? GetSelectedItem(Type pageType) => null;
    }

    private sealed class FakeSubsonicClientService : ISubsonicClientService
    {
        public Client.ISubsonicClient? Client => null;
        public string? ActiveUserIdentity => null;
        public void CreateClient(ServerCredential credential) { }
        public Task<ConnectionTestResult> TestConnectionAsync(ServerCredential credential) =>
            throw new NotImplementedException();
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

    [TestMethod]
    public void HandleSuggestionChosen_Song_PropagatesStructuredArtistDataToPlaybackTrack()
    {
        var playback = new CapturingPlaybackService();
        var vm = new ShellViewModel(
            new FakeNavigationService(),
            new FakeNavigationViewService(),
            new FakeSubsonicClientService(),
            playback,
            new FakeAppStateService(),
            new Hypersonic.Tests.Fakes.FakeFavoritesService(),
            new NullLoggingService());

        var item = new SearchSuggestionItem
        {
            Type = SearchResultType.Song,
            Id = "s1",
            PrimaryText = "Test Song",
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
        };

        vm.HandleSuggestionChosen(item);

        Assert.AreEqual(1, playback.LastPlayedTracks.Count);
        var track = playback.LastPlayedTracks[0];
        Assert.AreEqual("Dr. Dre feat. Eminem", track.DisplayArtist);
        Assert.AreEqual(2, track.Artists.Count);
        Assert.AreEqual("Dr. Dre", track.Artists[0].Name);
        Assert.AreEqual("a1", track.Artists[0].Id);
        Assert.AreEqual("Eminem", track.Artists[1].Name);
        Assert.AreEqual("a2", track.Artists[1].Id);
        Assert.IsTrue(track.HasStructuredArtists);
        Assert.AreEqual("Dr. Dre feat. Eminem", track.FlatArtistText);
        Assert.AreEqual("Test Album", track.Album);
        Assert.AreEqual("alb1", track.AlbumId);
    }

    [TestMethod]
    public void HandleSuggestionChosen_Song_PropagatesTechnicalFieldsToPlaybackTrack()
    {
        var playback = new CapturingPlaybackService();
        var vm = new ShellViewModel(
            new FakeNavigationService(),
            new FakeNavigationViewService(),
            new FakeSubsonicClientService(),
            playback,
            new FakeAppStateService(),
            new Hypersonic.Tests.Fakes.FakeFavoritesService(),
            new NullLoggingService());

        var item = new SearchSuggestionItem
        {
            Type = SearchResultType.Song,
            Id = "s9",
            PrimaryText = "FLAC Track",
            Artist = "Artist",
            Album = "Album",
            Suffix = "flac",
            BitRate = 4608,
            BitDepth = 24,
            SamplingRate = 96000,
        };

        vm.HandleSuggestionChosen(item);

        Assert.AreEqual(1, playback.LastPlayedTracks.Count);
        var track = playback.LastPlayedTracks[0];
        Assert.AreEqual("flac", track.Suffix);
        Assert.AreEqual(4608, track.BitRate);
        Assert.AreEqual(24, track.BitDepth);
        Assert.AreEqual(96000, track.SamplingRate);
    }

    [TestMethod]
    public void HandleSuggestionChosen_Song_HydratesIsStarredFromStore()
    {
        // Search suggestions carry no star field. If the store already knows the song is
        // starred, the search-played track must show favorited even though no poll delta fires.
        var messenger = new CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger();
        var favorites = new Hypersonic.Services.FavoritesService(
            new FakeSubsonicClientService(), new NullLoggingService(), messenger);
        favorites.Reconcile(new[] { ("s1", true) }, favorites.BeginObservation());   // seed store

        var playback = new CapturingPlaybackService();
        var vm = new ShellViewModel(
            new FakeNavigationService(),
            new FakeNavigationViewService(),
            new FakeSubsonicClientService(),
            playback,
            new FakeAppStateService(),
            favorites,
            new NullLoggingService());

        vm.HandleSuggestionChosen(new SearchSuggestionItem
        {
            Type = SearchResultType.Song,
            Id = "s1",
            PrimaryText = "Starred Song",
            Artist = "Artist",
            Album = "Album",
        });

        Assert.AreEqual(1, playback.LastPlayedTracks.Count);
        Assert.IsTrue(playback.LastPlayedTracks[0].IsStarred,
            "PlaybackTrack.IsStarred hydrates from the store at creation");
    }
}
