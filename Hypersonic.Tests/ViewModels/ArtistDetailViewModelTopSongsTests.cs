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
public class ArtistDetailViewModelTopSongsTests
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

    private sealed class FakeNavigationService : INavigationService
    {
        public event NavigatedEventHandler? Navigated;
        public bool CanGoBack => false;
        public Frame? Frame { get; set; }
        public bool NavigateTo(string pageKey, object? parameter = null, bool clearNavigation = false) => true;
        public bool GoBack() => false;
    }

    private sealed class NullPlaybackService : IPlaybackService
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

    private sealed class FakeSubsonicClient : ISubsonicClient
    {
        public Artist? ArtistToReturn { get; set; }
        public ArtistInfo2? ArtistInfo2ToReturn { get; set; }
        public Song[]? TopSongsToReturn { get; set; }
        public Dictionary<string, Child[]> AlbumSongs { get; set; } = [];
        public bool TopSongsShouldThrow { get; set; }
        public bool GetAlbumShouldThrow { get; set; }
        public int StarCallCount { get; set; }
        public int UnstarCallCount { get; set; }
        public bool StarShouldThrow { get; set; }

        public Task<GetArtistResponse> GetArtistAsync(string id) =>
            Task.FromResult(new GetArtistResponse { Artist = ArtistToReturn! });

        public Task<GetArtistInfo2Response> GetArtistInfo2Async(string id, int count = 20, bool includeNotPresent = false) =>
            Task.FromResult(new GetArtistInfo2Response { ArtistInfo2 = ArtistInfo2ToReturn ?? new ArtistInfo2() });

        public Task<GetTopSongsResponse> GetTopSongsAsync(string artist, int count = 50)
        {
            if (TopSongsShouldThrow) throw new HttpRequestException("Top songs unavailable");
            return Task.FromResult(new GetTopSongsResponse { TopSongs = new TopSongs { Song = TopSongsToReturn } });
        }

        public Task<GetAlbumResponse> GetAlbumAsync(string id)
        {
            if (GetAlbumShouldThrow) throw new HttpRequestException("Album fetch failed");
            AlbumSongs.TryGetValue(id, out var songs);
            return Task.FromResult(new GetAlbumResponse
            {
                Album = new AlbumID3WithSongs { Id = id, Song = songs },
            });
        }

        public Task<SubsonicResponse> StarAsync(string? id = null, string? albumId = null, string? artistId = null)
        {
            if (StarShouldThrow) throw new HttpRequestException("Star failed");
            StarCallCount++;
            return Task.FromResult(new SubsonicResponse());
        }

        public Task<SubsonicResponse> UnstarAsync(string? id = null, string? albumId = null, string? artistId = null)
        {
            if (StarShouldThrow) throw new HttpRequestException("Unstar failed");
            UnstarCallCount++;
            return Task.FromResult(new SubsonicResponse());
        }

        public Uri GetCoverArtUri(string id, string? size = null) => new($"https://test/cover/{id}");
        public Uri GetStreamUri(string id, int? maxBitrate = null, string? format = null, int? timeOffset = null, string? size = null, bool estimateContentLength = false, bool converted = false) => new($"https://test/stream/{id}");

        // Unused stubs
        public Task<SubsonicResponse> PingAsync() => throw new NotImplementedException();
        public Task<GetLicenseResponse> GetLicenseAsync() => throw new NotImplementedException();
        public Task<GetOpenSubsonicExtensionsResponse> GetOpenSubsonicExtensions() => throw new NotImplementedException();
        public Task<GetMusicFoldersResponse> GetMusicFoldersAsync() => throw new NotImplementedException();
        public Task<GetIndexesResponse> GetIndexesAsync(string? musicFolderId = null, ulong? ifModifiedSince = null) => throw new NotImplementedException();
        public Task<GetMusicDirectoryResponse> GetMusicDirectoryAsync(string id) => throw new NotImplementedException();
        public Task<GetGenresResponse> GetGenresAsync() => throw new NotImplementedException();
        public Task<GetArtistsResponse> GetArtistsAsync(string? musicFolderId = null) => throw new NotImplementedException();
        public Task<GetSongResponse> GetSongAsync(string id) => throw new NotImplementedException();
        public Task<GetArtistInfoResponse> GetArtistInfoAsync(string id, int count = 20, bool includeNotPresent = false) => throw new NotImplementedException();
        public Task<GetAlbumInfoResponse> GetAlbumInfoAsync(string id) => throw new NotImplementedException();
        public Task<GetAlbumInfo2Response> GetAlbumInfo2Async(string id) => throw new NotImplementedException();
        public Task<GetSimilarSongsResponse> GetSimiarSongsAsync(string id, int count = 50) => throw new NotImplementedException();
        public Task<GetSimilarSongs2Response> GetSimiarSongs2Async(string id, int count = 50) => throw new NotImplementedException();
        public Task<GetAlbumListResponse> GetAlbumListAsync(string type, int? size = null, int? offset = null, int? fromYear = null, int? toYear = null, string? genre = null, string? musicFolderId = null) => throw new NotImplementedException();
        public Task<GetAlbumList2Response> GetAlbumList2Async(string type, int? size = null, int? offset = null, int? fromYear = null, int? toYear = null, string? genre = null, string? musicFolderId = null) => throw new NotImplementedException();
        public Task<GetRandomSongsResponse> GetRandomSongsAsync(int? size = null, string? genre = null, int? fromYear = null, int? toYear = null, string? musicFolderId = null) => throw new NotImplementedException();
        public Task<GetSongsByGenreResponse> GetSongsByGenreAsync(string genre, int? count = null, int? offset = null, string? musicFolderId = null) => throw new NotImplementedException();
        public Task<GetNowPlayingResponse> GetNowPlayingAsync() => throw new NotImplementedException();
        public Task<GetStarredResponse> GetStarredAsync(string? musicFolderId = null) => throw new NotImplementedException();
        public Task<GetStarred2Response> GetStarred2Async(string? musicFolderId = null) => throw new NotImplementedException();
        public Task<Search2Response> Search2Async(string query, int? artistCount = null, int? artistOffset = null, int? albumCount = null, int? albumOffset = null, int? songCount = null, int? songOffset = null, string? musicFolderId = null) => throw new NotImplementedException();
        public Task<Search3Response> Search3Async(string query, int? artistCount = null, int? artistOffset = null, int? albumCount = null, int? albumOffset = null, int? songCount = null, int? songOffset = null, string? musicFolderId = null) => throw new NotImplementedException();
        public Task<GetPlaylistsResponse> GetPlaylistsAsync(string? username = null) => throw new NotImplementedException();
        public Task<GetPlaylistResponse> GetPlaylistAsync(string id) => throw new NotImplementedException();
        public Task<SubsonicResponse> CreatePlaylistAsync(string? playlistId = null, string? name = null, string[]? songId = null) => throw new NotImplementedException();
        public Task<SubsonicResponse> UpdatePlaylistAsync(string playlistId, string? name = null, string? comment = null, bool? @public = null, string[]? songIdToAdd = null, int[]? songIndexToRemove = null) => throw new NotImplementedException();
        public Task<SubsonicResponse> DeletePlaylistAsync(string id) => throw new NotImplementedException();
        public Task<Stream> StreamAsync(string id, int? maxBitrate = null, string? format = null, int? timeOffset = null, string? size = null, bool estimateContentLength = false, bool converted = false) => throw new NotImplementedException();
        public Task<Stream> DownloadAsync(string id) => throw new NotImplementedException();
        public Task<Stream> GetCoverArtAsync(string id, string? size = null) => throw new NotImplementedException();
        public Task<GetLyricsResponse> GetLyricsAsync(string? artist = null, string? title = null) => throw new NotImplementedException();
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
        public void CreateClient(ServerCredential credential) { }
        public Task<ConnectionTestResult> TestConnectionAsync(ServerCredential credential) =>
            throw new NotImplementedException();
    }

    private static Artist MakeArtist(string id, string name, params AlbumID3[] albums) => new()
    {
        Id = id,
        Name = name,
        AlbumCount = albums.Length,
        Album = albums,
    };

    private static AlbumID3 MakeAlbum(string id, int songCount = 1) => new()
    {
        Id = id,
        Name = $"Album {id}",
        SongCount = songCount,
    };

    private static Song MakeSong(string id, string artistId, string artistName, long playCount = 0, string? starred = null) => new()
    {
        Id = id,
        Title = $"Song {id}",
        Artist = artistName,
        ArtistId = artistId,
        Artists = [new ArtistID3 { Id = artistId, Name = artistName }],
        PlayCount = playCount,
        Starred = starred,
    };

    private ArtistDetailViewModel CreateVm(FakeSubsonicClient client) =>
        new(new FakeSubsonicClientService { Client = client },
            new FakeNavigationService(),
            new NullPlaybackService(),
            new NullLoggingService());

    #endregion

    #region IsArtistCredited tests

    [TestMethod]
    public void IsArtistCredited_MatchesByStructuredArtistId()
    {
        var child = new Child
        {
            Id = "s1",
            Artists = [new ArtistID3 { Id = "ar1", Name = "Artist A" }],
        };

        Assert.IsTrue(ArtistDetailViewModel.IsArtistCredited(child, "ar1", "Artist A"));
    }

    [TestMethod]
    public void IsArtistCredited_MatchesByLegacyArtistId()
    {
        var child = new Child
        {
            Id = "s1",
            ArtistId = "ar1",
            Artist = "Artist A",
        };

        Assert.IsTrue(ArtistDetailViewModel.IsArtistCredited(child, "ar1", "Artist A"));
    }

    [TestMethod]
    public void IsArtistCredited_NameFallbackOnlyWhenNoId()
    {
        var child = new Child
        {
            Id = "s1",
            Artists = [new ArtistID3 { Id = "", Name = "Shared Name" }],
        };

        Assert.IsTrue(ArtistDetailViewModel.IsArtistCredited(child, "ar1", "Shared Name"));
    }

    [TestMethod]
    public void IsArtistCredited_NameFallbackSkipsEntriesWithDifferentId()
    {
        var child = new Child
        {
            Id = "s1",
            Artists = [new ArtistID3 { Id = "ar-other", Name = "Shared Name" }],
        };

        Assert.IsFalse(ArtistDetailViewModel.IsArtistCredited(child, "ar1", "Shared Name"));
    }

    [TestMethod]
    public void IsArtistCredited_MultiArtistTrack_MatchesSecondaryArtist()
    {
        var child = new Child
        {
            Id = "s1",
            Artists =
            [
                new ArtistID3 { Id = "ar-primary", Name = "Primary" },
                new ArtistID3 { Id = "ar1", Name = "Featured" },
            ],
        };

        Assert.IsTrue(ArtistDetailViewModel.IsArtistCredited(child, "ar1", "Featured"));
    }

    [TestMethod]
    public void IsArtistCredited_FallbackToLegacyArtistName()
    {
        var child = new Child { Id = "s1", Artist = "Artist A" };

        Assert.IsTrue(ArtistDetailViewModel.IsArtistCredited(child, "ar1", "Artist A"));
    }

    [TestMethod]
    public void IsArtistCredited_NoMatchReturnsFalse()
    {
        var child = new Child
        {
            Id = "s1",
            Artist = "Someone Else",
            ArtistId = "ar-other",
            Artists = [new ArtistID3 { Id = "ar-other", Name = "Someone Else" }],
        };

        Assert.IsFalse(ArtistDetailViewModel.IsArtistCredited(child, "ar1", "Artist A"));
    }

    [TestMethod]
    public void IsArtistCredited_CaseInsensitiveNameMatch()
    {
        var child = new Child { Id = "s1", Artist = "artist a" };

        Assert.IsTrue(ArtistDetailViewModel.IsArtistCredited(child, "ar1", "Artist A"));
    }

    #endregion

    #region Source availability and selection

    [TestMethod]
    public async Task BothSourcesAvailable_DefaultsToCommunity()
    {
        var client = new FakeSubsonicClient
        {
            ArtistToReturn = MakeArtist("ar1", "Test Artist", MakeAlbum("alb1")),
            TopSongsToReturn = [MakeSong("s1", "ar1", "Test Artist", playCount: 10)],
            AlbumSongs = { ["alb1"] = [MakeSong("s2", "ar1", "Test Artist", playCount: 5)] },
        };

        var vm = CreateVm(client);
        await vm.LoadArtistAsync("ar1");

        Assert.IsTrue(vm.IsCommunityAvailable);
        Assert.IsTrue(vm.IsPersonalAvailable);
        Assert.IsTrue(vm.HasTopSongs);
        Assert.AreEqual(0, vm.SelectedTopSongsMode);
        Assert.AreEqual(1, vm.TopSongs.Count);
        Assert.AreEqual("s1", vm.TopSongs[0].Id);
    }

    [TestMethod]
    public async Task OnlyCommunityAvailable_SelectsCommunity()
    {
        var client = new FakeSubsonicClient
        {
            ArtistToReturn = MakeArtist("ar1", "Test Artist", MakeAlbum("alb1")),
            TopSongsToReturn = [MakeSong("s1", "ar1", "Test Artist", playCount: 10)],
            AlbumSongs = { ["alb1"] = [new Song { Id = "s2", Artist = "Test Artist", ArtistId = "ar1", PlayCount = 0 }] },
        };

        var vm = CreateVm(client);
        await vm.LoadArtistAsync("ar1");

        Assert.IsTrue(vm.IsCommunityAvailable);
        Assert.IsFalse(vm.IsPersonalAvailable);
        Assert.AreEqual(0, vm.SelectedTopSongsMode);
    }

    [TestMethod]
    public async Task OnlyPersonalAvailable_SelectsPersonal()
    {
        var client = new FakeSubsonicClient
        {
            ArtistToReturn = MakeArtist("ar1", "Test Artist", MakeAlbum("alb1")),
            TopSongsToReturn = null,
            AlbumSongs = { ["alb1"] = [MakeSong("s1", "ar1", "Test Artist", playCount: 5)] },
        };

        var vm = CreateVm(client);
        await vm.LoadArtistAsync("ar1");

        Assert.IsFalse(vm.IsCommunityAvailable);
        Assert.IsTrue(vm.IsPersonalAvailable);
        Assert.AreEqual(1, vm.SelectedTopSongsMode);
        Assert.AreEqual("s1", vm.TopSongs[0].Id);
    }

    [TestMethod]
    public async Task NeitherSourceAvailable_HasTopSongsFalse()
    {
        var client = new FakeSubsonicClient
        {
            ArtistToReturn = MakeArtist("ar1", "Test Artist", MakeAlbum("alb1")),
            TopSongsToReturn = null,
            AlbumSongs = { ["alb1"] = [new Song { Id = "s1", Artist = "Test Artist", ArtistId = "ar1", PlayCount = 0 }] },
        };

        var vm = CreateVm(client);
        await vm.LoadArtistAsync("ar1");

        Assert.IsFalse(vm.HasTopSongs);
        Assert.AreEqual(0, vm.TopSongs.Count);
    }

    [TestMethod]
    public async Task ZeroPlayCount_PersonalUnavailable()
    {
        var client = new FakeSubsonicClient
        {
            ArtistToReturn = MakeArtist("ar1", "Test Artist", MakeAlbum("alb1")),
            TopSongsToReturn = [MakeSong("s1", "ar1", "Test Artist")],
            AlbumSongs = { ["alb1"] = [MakeSong("s2", "ar1", "Test Artist", playCount: 0)] },
        };

        var vm = CreateVm(client);
        await vm.LoadArtistAsync("ar1");

        Assert.IsTrue(vm.IsCommunityAvailable);
        Assert.IsFalse(vm.IsPersonalAvailable);
    }

    #endregion

    #region Failure isolation

    [TestMethod]
    public async Task CommunityFetchFails_PageStillWorks()
    {
        var client = new FakeSubsonicClient
        {
            ArtistToReturn = MakeArtist("ar1", "Test Artist", MakeAlbum("alb1")),
            TopSongsShouldThrow = true,
            AlbumSongs = { ["alb1"] = [MakeSong("s1", "ar1", "Test Artist", playCount: 5)] },
        };

        var vm = CreateVm(client);
        await vm.LoadArtistAsync("ar1");

        Assert.IsNull(vm.ErrorMessage);
        Assert.IsFalse(vm.IsCommunityAvailable);
        Assert.IsTrue(vm.IsPersonalAvailable);
        Assert.AreEqual(1, vm.SelectedTopSongsMode);
    }

    [TestMethod]
    public async Task AlbumFetchFails_PersonalGracefullyUnavailable()
    {
        var client = new FakeSubsonicClient
        {
            ArtistToReturn = MakeArtist("ar1", "Test Artist", MakeAlbum("alb1")),
            TopSongsToReturn = [MakeSong("s1", "ar1", "Test Artist")],
            GetAlbumShouldThrow = true,
        };

        var vm = CreateVm(client);
        await vm.LoadArtistAsync("ar1");

        Assert.IsNull(vm.ErrorMessage);
        Assert.IsTrue(vm.IsCommunityAvailable);
        Assert.IsFalse(vm.IsPersonalAvailable);
    }

    [TestMethod]
    public async Task BothSourcesFail_PageStillFunctional()
    {
        var client = new FakeSubsonicClient
        {
            ArtistToReturn = MakeArtist("ar1", "Test Artist", MakeAlbum("alb1")),
            TopSongsShouldThrow = true,
            GetAlbumShouldThrow = true,
        };

        var vm = CreateVm(client);
        await vm.LoadArtistAsync("ar1");

        Assert.IsNull(vm.ErrorMessage);
        Assert.IsFalse(vm.HasTopSongs);
        Assert.AreEqual("Test Artist", vm.ArtistName);
    }

    [TestMethod]
    public async Task ArtistFetchFails_NoTopSongsLoading()
    {
        var client = new FakeSubsonicClient
        {
            ArtistToReturn = null!,
        };

        var vm = CreateVm(client);
        await vm.LoadArtistAsync("ar1");

        Assert.IsFalse(vm.IsTopSongsLoading);
        Assert.IsFalse(vm.HasTopSongs);
    }

    #endregion

    #region Loading state

    [TestMethod]
    public async Task LoadingState_TopSongsLoadAfterMainContent()
    {
        var client = new FakeSubsonicClient
        {
            ArtistToReturn = MakeArtist("ar1", "Test Artist", MakeAlbum("alb1")),
            TopSongsToReturn = [MakeSong("s1", "ar1", "Test Artist")],
            AlbumSongs = { ["alb1"] = [MakeSong("s2", "ar1", "Test Artist", playCount: 3)] },
        };

        var vm = CreateVm(client);
        await vm.LoadArtistAsync("ar1");

        Assert.IsFalse(vm.IsLoading);
        Assert.IsFalse(vm.IsTopSongsLoading);
        Assert.AreEqual("Test Artist", vm.ArtistName);
    }

    #endregion

    #region Mode switching

    [TestMethod]
    public async Task ModeSwitching_ReplacesTopSongsCollection()
    {
        var client = new FakeSubsonicClient
        {
            ArtistToReturn = MakeArtist("ar1", "Test Artist", MakeAlbum("alb1")),
            TopSongsToReturn = [MakeSong("s1", "ar1", "Test Artist")],
            AlbumSongs = { ["alb1"] = [MakeSong("s2", "ar1", "Test Artist", playCount: 5)] },
        };

        var vm = CreateVm(client);
        await vm.LoadArtistAsync("ar1");

        Assert.AreEqual("s1", vm.TopSongs[0].Id);

        vm.SelectedTopSongsMode = 1;
        vm.ApplyTopSongsMode();

        Assert.AreEqual("s2", vm.TopSongs[0].Id);

        vm.SelectedTopSongsMode = 0;
        vm.ApplyTopSongsMode();

        Assert.AreEqual("s1", vm.TopSongs[0].Id);
    }

    #endregion

    #region Ranking

    [TestMethod]
    public async Task PersonalMode_SortedByPlayCountDescending_RankedSequentially()
    {
        var client = new FakeSubsonicClient
        {
            ArtistToReturn = MakeArtist("ar1", "Test Artist", MakeAlbum("alb1")),
            TopSongsToReturn = null,
            AlbumSongs =
            {
                ["alb1"] =
                [
                    MakeSong("s1", "ar1", "Test Artist", playCount: 3),
                    MakeSong("s2", "ar1", "Test Artist", playCount: 10),
                    MakeSong("s3", "ar1", "Test Artist", playCount: 7),
                ],
            },
        };

        var vm = CreateVm(client);
        await vm.LoadArtistAsync("ar1");

        Assert.AreEqual(3, vm.TopSongs.Count);
        Assert.AreEqual("s2", vm.TopSongs[0].Id);
        Assert.AreEqual("s3", vm.TopSongs[1].Id);
        Assert.AreEqual("s1", vm.TopSongs[2].Id);
        Assert.AreEqual(1, vm.TopSongs[0].PositionNumber);
        Assert.AreEqual(2, vm.TopSongs[1].PositionNumber);
        Assert.AreEqual(3, vm.TopSongs[2].PositionNumber);
    }

    #endregion

    #region Cross-cache favorite sync

    [TestMethod]
    public async Task StarToggle_SyncsBothCaches()
    {
        var sharedSong = MakeSong("s1", "ar1", "Test Artist", playCount: 5);
        var client = new FakeSubsonicClient
        {
            ArtistToReturn = MakeArtist("ar1", "Test Artist", MakeAlbum("alb1")),
            TopSongsToReturn = [MakeSong("s1", "ar1", "Test Artist")],
            AlbumSongs = { ["alb1"] = [MakeSong("s1", "ar1", "Test Artist", playCount: 5)] },
        };

        var vm = CreateVm(client);
        await vm.LoadArtistAsync("ar1");

        Assert.AreEqual(0, vm.SelectedTopSongsMode);
        var communitySong = vm.TopSongs[0];
        Assert.IsFalse(communitySong.IsStarred);

        await vm.ToggleTopSongStarCommand.ExecuteAsync(communitySong);

        Assert.IsTrue(communitySong.IsStarred);

        vm.SelectedTopSongsMode = 1;
        vm.ApplyTopSongsMode();

        Assert.IsTrue(vm.TopSongs[0].IsStarred);
    }

    [TestMethod]
    public async Task StarToggle_FailureRollsBackBothCaches()
    {
        var client = new FakeSubsonicClient
        {
            ArtistToReturn = MakeArtist("ar1", "Test Artist", MakeAlbum("alb1")),
            TopSongsToReturn = [MakeSong("s1", "ar1", "Test Artist")],
            AlbumSongs = { ["alb1"] = [MakeSong("s1", "ar1", "Test Artist", playCount: 5)] },
        };

        var vm = CreateVm(client);
        await vm.LoadArtistAsync("ar1");

        var communitySong = vm.TopSongs[0];
        Assert.IsFalse(communitySong.IsStarred);

        client.StarShouldThrow = true;
        await vm.ToggleTopSongStarCommand.ExecuteAsync(communitySong);

        Assert.IsFalse(communitySong.IsStarred);

        vm.SelectedTopSongsMode = 1;
        vm.ApplyTopSongsMode();

        Assert.IsFalse(vm.TopSongs[0].IsStarred);
    }

    #endregion
}
