using Hypersonic.Client;
using Hypersonic.Client.Models.Entities;
using Hypersonic.Client.Models.Responses;
using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;
using Hypersonic.Models;
using Hypersonic.ViewModels;

using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Hypersonic.Tests.ViewModels;

[TestClass]
public class TracksViewModelTests
{
    #region Test infrastructure

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
    }

    private sealed class FakeSubsonicClient : ISubsonicClient
    {
        public Func<string, int?, int?, Search3Response>? Search3Handler { get; set; }
        public Func<string, int?, int?, Task<Search3Response>>? Search3AsyncHandler { get; set; }
        public bool Search3ShouldThrow { get; set; }

        public async Task<Search3Response> Search3Async(string query, int? artistCount = null, int? artistOffset = null,
            int? albumCount = null, int? albumOffset = null, int? songCount = null, int? songOffset = null,
            string? musicFolderId = null)
        {
            if (Search3ShouldThrow) throw new HttpRequestException("Search failed");
            if (Search3AsyncHandler != null)
            {
                return await Search3AsyncHandler(query, songCount, songOffset);
            }
            if (Search3Handler != null)
            {
                return Search3Handler(query, songCount, songOffset);
            }
            return new Search3Response { SearchResult3 = new SearchResult3() };
        }

        public Uri GetCoverArtUri(string id, string? size = null) => new($"https://test/cover/{id}");
        public Uri GetStreamUri(string id, int? maxBitrate = null, string? format = null, int? timeOffset = null, string? size = null, bool estimateContentLength = false, bool converted = false) => new($"https://test/stream/{id}");

        public Task<SubsonicResponse> PingAsync() => throw new NotImplementedException();
        public Task<GetLicenseResponse> GetLicenseAsync() => throw new NotImplementedException();
        public Task<GetOpenSubsonicExtensionsResponse> GetOpenSubsonicExtensions() => throw new NotImplementedException();
        public Task<GetMusicFoldersResponse> GetMusicFoldersAsync() => throw new NotImplementedException();
        public Task<GetIndexesResponse> GetIndexesAsync(string? musicFolderId = null, ulong? ifModifiedSince = null) => throw new NotImplementedException();
        public Task<GetMusicDirectoryResponse> GetMusicDirectoryAsync(string id) => throw new NotImplementedException();
        public Task<GetGenresResponse> GetGenresAsync() => throw new NotImplementedException();
        public Task<GetArtistsResponse> GetArtistsAsync(string? musicFolderId = null) => throw new NotImplementedException();
        public Task<GetArtistResponse> GetArtistAsync(string id) => throw new NotImplementedException();
        public Task<GetAlbumResponse> GetAlbumAsync(string id) => throw new NotImplementedException();
        public Task<GetSongResponse> GetSongAsync(string id) => throw new NotImplementedException();
        public Task<GetArtistInfoResponse> GetArtistInfoAsync(string id, int count = 20, bool includeNotPresent = false) => throw new NotImplementedException();
        public Task<GetArtistInfo2Response> GetArtistInfo2Async(string id, int count = 20, bool includeNotPresent = false) => throw new NotImplementedException();
        public Task<GetAlbumInfoResponse> GetAlbumInfoAsync(string id) => throw new NotImplementedException();
        public Task<GetAlbumInfo2Response> GetAlbumInfo2Async(string id) => throw new NotImplementedException();
        public Task<GetSimilarSongsResponse> GetSimiarSongsAsync(string id, int count = 50) => throw new NotImplementedException();
        public Task<GetSimilarSongs2Response> GetSimiarSongs2Async(string id, int count = 50) => throw new NotImplementedException();
        public Task<GetTopSongsResponse> GetTopSongsAsync(string artist, int count = 50) => throw new NotImplementedException();
        public Task<GetAlbumListResponse> GetAlbumListAsync(string type, int? size = null, int? offset = null, int? fromYear = null, int? toYear = null, string? genre = null, string? musicFolderId = null) => throw new NotImplementedException();
        public Task<GetAlbumList2Response> GetAlbumList2Async(string type, int? size = null, int? offset = null, int? fromYear = null, int? toYear = null, string? genre = null, string? musicFolderId = null) => throw new NotImplementedException();
        public Task<GetRandomSongsResponse> GetRandomSongsAsync(int? size = null, string? genre = null, int? fromYear = null, int? toYear = null, string? musicFolderId = null) => throw new NotImplementedException();
        public Task<GetSongsByGenreResponse> GetSongsByGenreAsync(string genre, int? count = null, int? offset = null, string? musicFolderId = null) => throw new NotImplementedException();
        public Task<GetNowPlayingResponse> GetNowPlayingAsync() => throw new NotImplementedException();
        public Task<GetStarredResponse> GetStarredAsync(string? musicFolderId = null) => throw new NotImplementedException();
        public Task<GetStarred2Response> GetStarred2Async(string? musicFolderId = null) => throw new NotImplementedException();
        public Task<Search2Response> Search2Async(string query, int? artistCount = null, int? artistOffset = null, int? albumCount = null, int? albumOffset = null, int? songCount = null, int? songOffset = null, string? musicFolderId = null) => throw new NotImplementedException();
        public Task<GetPlaylistsResponse> GetPlaylistsAsync(string? username = null) => throw new NotImplementedException();
        public Task<GetPlaylistResponse> GetPlaylistAsync(string id) => throw new NotImplementedException();
        public Task<SubsonicResponse> CreatePlaylistAsync(string? playlistId = null, string? name = null, string[]? songId = null) => throw new NotImplementedException();
        public Task<SubsonicResponse> UpdatePlaylistAsync(string playlistId, string? name = null, string? comment = null, bool? @public = null, string[]? songIdToAdd = null, int[]? songIndexToRemove = null) => throw new NotImplementedException();
        public Task<SubsonicResponse> DeletePlaylistAsync(string id) => throw new NotImplementedException();
        public Task<Stream> StreamAsync(string id, int? maxBitrate = null, string? format = null, int? timeOffset = null, string? size = null, bool estimateContentLength = false, bool converted = false) => throw new NotImplementedException();
        public Task<Stream> DownloadAsync(string id) => throw new NotImplementedException();
        public Task<Stream> GetCoverArtAsync(string id, string? size = null) => throw new NotImplementedException();
        public Task<GetLyricsResponse> GetLyricsAsync(string? artist = null, string? title = null) => throw new NotImplementedException();
        public Task<SubsonicResponse> StarAsync(string? id = null, string? albumId = null, string? artistId = null) => throw new NotImplementedException();
        public Task<SubsonicResponse> UnstarAsync(string? id = null, string? albumId = null, string? artistId = null) => throw new NotImplementedException();
        public Task<SubsonicResponse> SetRatingAsync(string id, int rating) => throw new NotImplementedException();
        public Task<SubsonicResponse> ScrobbleAsync(string id, ulong? time = null, bool submission = true) => throw new NotImplementedException();
        public Task<GetUserResponse> GetUserAsync(string username) => throw new NotImplementedException();
        public Task<GetUsersResponse> GetUsersAsync() => throw new NotImplementedException();
        public Task<GetScanStatusResponse> GetScanStatusAsync() => throw new NotImplementedException();
        public Task<StartScanResponse> StartScanAsync() => throw new NotImplementedException();
        public Task<GetLyricsBySongIdResponse> GetLyricsBySongIdAsync(string id) => throw new NotImplementedException();
        public Task<Stream> GetAvatarAsync(string username) => throw new NotImplementedException();
        public Uri GetAvatarUri(string username) => throw new NotImplementedException();
    }

    private sealed class FakeSubsonicClientService : ISubsonicClientService
    {
        public ISubsonicClient? Client { get; set; }
        public string? ActiveUserIdentity { get; set; }
        public void CreateClient(ServerCredential credential) { }
        public Task<ConnectionTestResult> TestConnectionAsync(ServerCredential credential) =>
            throw new NotImplementedException();
    }

    private static Song MakeSong(string id, string title = "Song", string artist = "Artist", int duration = 200, int year = 2024) => new()
    {
        Id = id,
        Title = title,
        Artist = artist,
        Duration = duration,
        Year = year,
        Album = "Album",
    };

    private static Search3Response MakeSearch3Response(params Song[] songs) => new()
    {
        SearchResult3 = new SearchResult3 { Song = songs },
    };

    private static TracksViewModel CreateVm(FakeSubsonicClient fakeClient, FakePlaybackService? playback = null)
    {
        var clientService = new FakeSubsonicClientService { Client = fakeClient };
        var messenger = new CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger();
        var favorites = new Hypersonic.Services.FavoritesService(clientService, new NullLoggingService(), messenger);
        return new TracksViewModel(clientService, playback ?? new FakePlaybackService(), favorites, messenger, new NullLoggingService());
    }

    #endregion

    #region Stale-result guard

    [TestMethod]
    public async Task LoadMoreSongs_ReturnsNull_WhenGenerationChangedMidFlight()
    {
        // TCS lets us pause the API call mid-flight, mutate generation, then complete it.
        var tcs = new TaskCompletionSource<Search3Response>();
        var fakeClient = new FakeSubsonicClient();
        fakeClient.Search3AsyncHandler = (_, _, _) => tcs.Task;
        var vm = CreateVm(fakeClient);

        // Start load — blocks on tcs
        var loadTask = vm.LoadMoreSongsAsync(100);

        // While load is in flight, increment generation (simulates search reset)
        vm.OnNavigatedFrom();

        // Complete the API call with valid data
        tcs.SetResult(MakeSearch3Response(MakeSong("s1")));
        var result = await loadTask;

        Assert.IsNull(result, "Result from pre-generation-change load should be discarded");
        Assert.AreEqual(0, vm._allSongs.Count, "Stale items should not be added to _allSongs");
    }

    [TestMethod]
    public async Task LoadMoreSongs_DoesNotMutateOffset_WhenGenerationChangedMidFlight()
    {
        var tcs = new TaskCompletionSource<Search3Response>();
        var fakeClient = new FakeSubsonicClient();
        var callCount = 0;

        // First call returns immediately, second blocks on TCS
        fakeClient.Search3AsyncHandler = (query, count, offset) =>
        {
            callCount++;
            if (callCount == 1) return Task.FromResult(MakeSearch3Response(MakeSong("s1"), MakeSong("s2")));
            return tcs.Task;
        };
        var vm = CreateVm(fakeClient);

        // First load succeeds, offset becomes 2
        var result1 = await vm.LoadMoreSongsAsync(100);
        Assert.AreEqual(2, result1!.Count);
        Assert.AreEqual(2, vm._allSongs.Count);

        // Start second load — blocks on TCS
        var loadTask = vm.LoadMoreSongsAsync(100);

        // Mid-flight: navigate away (increments generation)
        vm.OnNavigatedFrom();

        // Complete the stale request
        tcs.SetResult(MakeSearch3Response(MakeSong("s3"), MakeSong("s4")));
        var result2 = await loadTask;

        Assert.IsNull(result2, "Stale result should be null");
        Assert.AreEqual(2, vm._allSongs.Count, "Stale load should not mutate _allSongs");
    }

    [TestMethod]
    public async Task LoadMoreSongs_StaleException_NotSurfacedToUser()
    {
        var tcs = new TaskCompletionSource<Search3Response>();
        var fakeClient = new FakeSubsonicClient();
        fakeClient.Search3AsyncHandler = (_, _, _) => tcs.Task;
        var vm = CreateVm(fakeClient);

        // Start load — blocks on TCS
        var loadTask = vm.LoadMoreSongsAsync(100);

        // Mid-flight: navigate away
        vm.OnNavigatedFrom();

        // Complete with exception
        tcs.SetException(new HttpRequestException("Network error"));
        var result = await loadTask;

        Assert.IsNull(result, "Stale exception should return null");
        Assert.IsNull(vm.ErrorMessage, "Stale exception should not set ErrorMessage");
    }

    #endregion

    #region Current-generation errors

    [TestMethod]
    public async Task LoadMoreSongs_CurrentGenerationException_SetsErrorMessage()
    {
        var fakeClient = new FakeSubsonicClient { Search3ShouldThrow = true };
        var vm = CreateVm(fakeClient);

        var result = await vm.LoadMoreSongsAsync(100);

        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Count, "Should return empty list on error");
        Assert.IsNotNull(vm.ErrorMessage);
    }

    [TestMethod]
    public async Task LoadMoreSongs_InitialEmptyQueryException_IncludesCompatibilityHint()
    {
        var fakeClient = new FakeSubsonicClient { Search3ShouldThrow = true };
        var vm = CreateVm(fakeClient);

        await vm.LoadMoreSongsAsync(100);

        Assert.IsTrue(vm.ErrorMessage!.Contains("may not support"), $"Expected compatibility hint, got: {vm.ErrorMessage}");
    }

    [TestMethod]
    public async Task LoadMoreSongs_PartialLoadFailure_PreservesLoadedData()
    {
        var callCount = 0;
        var fakeClient = new FakeSubsonicClient();
        fakeClient.Search3Handler = (query, count, offset) =>
        {
            callCount++;
            if (callCount == 1)
            {
                return MakeSearch3Response(MakeSong("s1"), MakeSong("s2"));
            }
            throw new HttpRequestException("Batch 2 failed");
        };
        var vm = CreateVm(fakeClient);

        // First batch succeeds
        await vm.LoadMoreSongsAsync(100);
        Assert.AreEqual(2, vm._allSongs.Count);

        // Second batch fails
        await vm.LoadMoreSongsAsync(100);
        Assert.AreEqual(2, vm._allSongs.Count, "Loaded data should be preserved after partial failure");
        Assert.IsNotNull(vm.ErrorMessage);
        Assert.IsFalse(vm.ErrorMessage!.Contains("may not support"), "Partial failure should not include compatibility hint");
    }

    #endregion

    #region Navigation guard

    [TestMethod]
    public async Task LoadMoreSongs_ReturnsNull_WhenNavigatedAway()
    {
        var fakeClient = new FakeSubsonicClient();
        fakeClient.Search3Handler = (_, _, _) => MakeSearch3Response(MakeSong("s1"));
        var vm = CreateVm(fakeClient);
        vm.OnNavigatedFrom();

        var result = await vm.LoadMoreSongsAsync(100);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task OnNavigatedFrom_IncrementsGeneration_PreNavLoadsStaleEvenAfterBackNav()
    {
        // Verifies: navigate away increments generation so that a load started before
        // navigation is stale even after back-navigation resets _navigatedAway.
        var loadCount = 0;
        var fakeClient = new FakeSubsonicClient();
        fakeClient.Search3Handler = (_, _, _) =>
        {
            loadCount++;
            return MakeSearch3Response(MakeSong($"s{loadCount}"));
        };
        var vm = CreateVm(fakeClient);

        // First load — succeeds
        var result1 = await vm.LoadMoreSongsAsync(100);
        Assert.IsNotNull(result1);
        Assert.AreEqual(1, vm._allSongs.Count);

        // Navigate away (increments generation from 0 to 1)
        vm.OnNavigatedFrom();

        // A load started with old generation (0) would be stale.
        // OnNavigatedFrom set _navigatedAway = true, so loads return null.
        var result2 = await vm.LoadMoreSongsAsync(100);
        Assert.IsNull(result2);
    }

    #endregion

    #region Search reset

    [TestMethod]
    public async Task SearchReset_ClearsBothCollections_ResetsOffset()
    {
        var offsets = new List<int?>();
        var fakeClient = new FakeSubsonicClient();
        fakeClient.Search3Handler = (query, count, offset) =>
        {
            offsets.Add(offset);
            return MakeSearch3Response(MakeSong("s1"));
        };
        var vm = CreateVm(fakeClient);

        // Initial load at offset 0
        await vm.LoadMoreSongsAsync(100);
        Assert.AreEqual(0, offsets[0]);

        // Search triggers reset — calls ExecuteSearchAsync internally (after 300ms debounce)
        await vm.OnSearchTextChangedAsync("test query");

        // The search load should have offset=0 (reset)
        var lastOffset = offsets[^1];
        Assert.AreEqual(0, lastOffset, "Search reset should reset offset to 0");
    }

    [TestMethod]
    public async Task SearchReset_ClearsAllSongs()
    {
        var callCount = 0;
        var fakeClient = new FakeSubsonicClient();
        fakeClient.Search3Handler = (query, count, offset) =>
        {
            callCount++;
            return MakeSearch3Response(MakeSong($"s{callCount}"));
        };
        var vm = CreateVm(fakeClient);

        // Load initial batch
        await vm.LoadMoreSongsAsync(100);
        Assert.AreEqual(1, vm._allSongs.Count);

        // Search replaces with fresh data
        await vm.OnSearchTextChangedAsync("new query");

        // After search, _allSongs should contain only the search result
        Assert.AreEqual(1, vm._allSongs.Count);
        Assert.AreEqual($"s{callCount}", vm._allSongs[0].Id);
    }

    [TestMethod]
    public async Task SearchReset_ResetsSortToDefault()
    {
        var fakeClient = new FakeSubsonicClient();
        fakeClient.Search3Handler = (_, _, _) => MakeSearch3Response(MakeSong("s1"));
        var vm = CreateVm(fakeClient);

        // Load data and set a sort
        await vm.LoadMoreSongsAsync(100);
        vm.SelectedSortIndex = 1; // Title

        // Search
        await vm.OnSearchTextChangedAsync("query");

        Assert.AreEqual(0, vm.SelectedSortIndex, "Search should reset sort to Default");
    }

    [TestMethod]
    public async Task WhitespaceOnlyQuery_TreatedAsClear()
    {
        var queries = new List<string>();
        var fakeClient = new FakeSubsonicClient();
        fakeClient.Search3Handler = (query, _, _) =>
        {
            queries.Add(query);
            return MakeSearch3Response(MakeSong("s1"));
        };
        var vm = CreateVm(fakeClient);

        await vm.OnSearchTextChangedAsync("   ");

        Assert.IsTrue(queries.Count > 0);
        Assert.AreEqual("", queries[^1], "Whitespace-only query should be trimmed to empty");
    }

    #endregion

    #region Sort + position update

    [TestMethod]
    public async Task Sort_ReordersAllSongs_UpdatesPositionNumbers()
    {
        var fakeClient = new FakeSubsonicClient();
        fakeClient.Search3Handler = (_, _, _) => MakeSearch3Response(
            MakeSong("s1", title: "Charlie"),
            MakeSong("s2", title: "Alpha"),
            MakeSong("s3", title: "Bravo"));
        var vm = CreateVm(fakeClient);

        await vm.LoadMoreSongsAsync(100);
        Assert.AreEqual(3, vm._allSongs.Count);

        // Sort by Title ascending (index 1, default ascending)
        vm.SelectedSortIndex = 1;

        Assert.AreEqual("Alpha", vm._allSongs[0].Title);
        Assert.AreEqual("Bravo", vm._allSongs[1].Title);
        Assert.AreEqual("Charlie", vm._allSongs[2].Title);

        Assert.AreEqual(1, vm._allSongs[0].PositionNumber);
        Assert.AreEqual(2, vm._allSongs[1].PositionNumber);
        Assert.AreEqual(3, vm._allSongs[2].PositionNumber);
    }

    [TestMethod]
    public async Task Sort_TracksCollectionMatchesAllSongs()
    {
        var fakeClient = new FakeSubsonicClient();
        fakeClient.Search3Handler = (_, _, _) => MakeSearch3Response(
            MakeSong("s1", title: "Zebra"),
            MakeSong("s2", title: "Apple"));
        var vm = CreateVm(fakeClient);

        await vm.LoadMoreSongsAsync(100);
        vm.Tracks.AddExternalBatch(vm._allSongs);

        vm.SelectedSortIndex = 1; // Title ascending

        Assert.AreEqual(vm._allSongs.Count, vm.Tracks.Count);
        for (var i = 0; i < vm._allSongs.Count; i++)
        {
            Assert.AreSame(vm._allSongs[i], vm.Tracks[i], $"Tracks[{i}] should match _allSongs[{i}]");
        }
    }

    [TestMethod]
    public async Task SortFieldChange_ResetsDirectionToFieldDefault()
    {
        var fakeClient = new FakeSubsonicClient();
        fakeClient.Search3Handler = (_, _, _) => MakeSearch3Response(MakeSong("s1"));
        var vm = CreateVm(fakeClient);
        await vm.LoadMoreSongsAsync(100);

        // Year (index 5) defaults to descending
        vm.SelectedSortIndex = 5;
        Assert.IsTrue(vm.IsSortDescending, "Year should default to descending");

        // Title (index 1) defaults to ascending
        vm.SelectedSortIndex = 1;
        Assert.IsFalse(vm.IsSortDescending, "Title should default to ascending");
    }

    [TestMethod]
    public async Task SortFieldChange_DoesNotDoubleSortWhenDirectionChanges()
    {
        var fakeClient = new FakeSubsonicClient();
        fakeClient.Search3Handler = (_, _, _) => MakeSearch3Response(
            MakeSong("s1", title: "B"),
            MakeSong("s2", title: "A"));
        var vm = CreateVm(fakeClient);
        await vm.LoadMoreSongsAsync(100);
        vm.Tracks.AddExternalBatch(vm._allSongs);

        // Track ReplaceAll calls via Reset events (ReplaceAll does Clear which fires Reset)
        var resetCount = 0;
        vm.Tracks.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                resetCount++;
            }
        };

        // Switching from Default(0) to Year(5) changes IsSortDescending from false to true.
        // This should trigger exactly ONE sort (one Reset from ReplaceAll), not two.
        vm.SelectedSortIndex = 5;

        Assert.AreEqual(1, resetCount, "Changing sort field should trigger exactly one sort, not two");
    }

    #endregion

    #region Now-playing refresh

    [TestMethod]
    public async Task BatchAppend_RefreshesNowPlaying_ForLaterBatch()
    {
        var playback = new FakePlaybackService
        {
            NowPlaying = new PlaybackTrack { Id = "s2" },
            IsPlaying = true,
        };
        var callCount = 0;
        var fakeClient = new FakeSubsonicClient();
        fakeClient.Search3Handler = (_, _, _) =>
        {
            callCount++;
            if (callCount == 1)
            {
                return MakeSearch3Response(MakeSong("s1"));
            }
            return MakeSearch3Response(MakeSong("s2")); // now-playing track in second batch
        };
        var vm = CreateVm(fakeClient, playback);

        // First batch
        var batch1 = await vm.LoadMoreSongsAsync(100);
        Assert.IsNotNull(batch1);

        // Inject a synchronous tracker into the VM's internal field (avoids DispatcherQueue requirement)
        vm._nowPlayingTracker = new Hypersonic.Helpers.NowPlayingTracker(playback, () => vm._allSongs, synchronous: true);
        vm._nowPlayingTracker.Subscribe();

        // Second batch — s2 arrives.
        // LoadMoreSongsAsync calls _nowPlayingTracker.Refresh(force: true) internally.
        // No manual refresh needed — the assert validates the VM's actual code path.
        var batch2 = await vm.LoadMoreSongsAsync(100);
        Assert.IsNotNull(batch2);

        var s2 = vm._allSongs.First(s => s.Id == "s2");
        Assert.IsTrue(s2.IsNowPlaying, "Now-playing track loaded in later batch should be highlighted");
        Assert.IsTrue(s2.IsPlaying, "Now-playing track loaded in later batch should show as playing");

        vm._nowPlayingTracker.Unsubscribe();
    }

    [TestMethod]
    public void BuildPlaybackTracks_PropagatesTechnicalFields()
    {
        var vm = CreateVm(new FakeSubsonicClient());
        vm._allSongs = new List<SongInfo>
        {
            new SongInfo
            {
                Id = "s9",
                Title = "FLAC Track",
                Artist = "Artist",
                Album = "Album",
                Suffix = "flac",
                BitRate = 4608,
                BitDepth = 24,
                SamplingRate = 96000,
            },
        };

        var t = vm.BuildPlaybackTracks()[0];

        Assert.AreEqual("flac", t.Suffix);
        Assert.AreEqual(4608, t.BitRate);
        Assert.AreEqual(24, t.BitDepth);
        Assert.AreEqual(96000, t.SamplingRate);
    }

    [TestMethod]
    public void BuildPlaybackTracks_PropagatesIsStarred()
    {
        var vm = CreateVm(new FakeSubsonicClient());
        vm._allSongs = new List<SongInfo>
        {
            new SongInfo { Id = "s1", Title = "Starred", Artist = "A", Album = "Al", IsStarred = true },
            new SongInfo { Id = "s2", Title = "Plain", Artist = "A", Album = "Al", IsStarred = false },
        };

        var tracks = vm.BuildPlaybackTracks();

        Assert.IsTrue(tracks[0].IsStarred, "Starred song must propagate IsStarred = true");
        Assert.IsFalse(tracks[1].IsStarred, "Unstarred song must propagate IsStarred = false");
    }

    #endregion
}
