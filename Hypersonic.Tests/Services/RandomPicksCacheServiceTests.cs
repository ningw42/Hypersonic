using System.Net.Http;

using Hypersonic.Client;
using Hypersonic.Client.Models.Entities;
using Hypersonic.Client.Models.Responses;
using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;
using Hypersonic.Services;

using AlbumInfo = Hypersonic.Core.Models.AlbumInfo;

namespace Hypersonic.Tests.Services;

[TestClass]
public class RandomPicksCacheServiceTests
{
    private sealed class NullLogger : ILogger
    {
        public string Category => string.Empty;
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

    private sealed class FakeSubsonicClientService : ISubsonicClientService
    {
        public ISubsonicClient? Client { get; set; }
        public string? ActiveUserIdentity { get; set; }
        public void CreateClient(ServerCredential credential) { }
        public Task<ConnectionTestResult> TestConnectionAsync(ServerCredential credential) => throw new NotImplementedException();
    }

    private sealed class CountingSubsonicClient : ISubsonicClient
    {
        public int CallCount;
        public bool ShouldThrow;
        public Func<int, AlbumID3[]?>? AlbumsForCall;
        public TaskCompletionSource? Gate;

        public async Task<GetAlbumList2Response> GetAlbumList2Async(string type, int? size = null, int? offset = null, int? fromYear = null, int? toYear = null, string? genre = null, string? musicFolderId = null)
        {
            var n = Interlocked.Increment(ref CallCount);
            if (Gate is not null)
            {
                await Gate.Task;
            }
            if (ShouldThrow)
            {
                throw new HttpRequestException("simulated");
            }
            var albums = AlbumsForCall?.Invoke(n);
            return new GetAlbumList2Response { AlbumList2 = new AlbumList2 { Album = albums } };
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

    private static AlbumID3 MakeAlbum(string id) =>
        new() { Id = id, Name = id, Artist = "A", ArtistId = "ar1", Year = 2025, CoverArt = $"cov-{id}", SongCount = 1, Duration = 60 };

    private static (RandomPicksCacheService Service, CountingSubsonicClient Client) CreateService(
        Func<int, AlbumID3[]?>? albumsForCall = null,
        bool shouldThrow = false)
    {
        var client = new CountingSubsonicClient
        {
            AlbumsForCall = albumsForCall ?? (_ => [MakeAlbum("a1"), MakeAlbum("a2")]),
            ShouldThrow = shouldThrow,
        };
        var clientService = new FakeSubsonicClientService { Client = client };
        var service = new RandomPicksCacheService(clientService, new NullLoggingService());
        return (service, client);
    }

    [TestMethod]
    public async Task GetOrFetchAsync_FirstCallFetches_SecondCallReturnsCache()
    {
        var (service, client) = CreateService();

        var first = await service.GetOrFetchAsync();
        var second = await service.GetOrFetchAsync();

        Assert.AreEqual(1, client.CallCount, "Second call must not hit the API.");
        Assert.AreSame(first, second);
        Assert.IsTrue(service.HasLoaded);
        Assert.IsFalse(service.IsLoading);
    }

    [TestMethod]
    public async Task GetOrFetchAsync_RaisesPicksChanged_OnFirstSuccess()
    {
        var (service, _) = CreateService();
        var raises = 0;
        service.PicksChanged += (_, _) => Interlocked.Increment(ref raises);

        await service.GetOrFetchAsync();

        Assert.AreEqual(1, raises);
    }

    [TestMethod]
    public async Task RefreshAsync_TriggersFetch_AndRaisesPicksChanged()
    {
        var call = 0;
        var (service, client) = CreateService(_ =>
        {
            call++;
            return call == 1
                ? [MakeAlbum("first")]
                : [MakeAlbum("second-a"), MakeAlbum("second-b")];
        });

        await service.GetOrFetchAsync();
        var raises = 0;
        service.PicksChanged += (_, _) => Interlocked.Increment(ref raises);

        await service.RefreshAsync();

        Assert.AreEqual(2, client.CallCount);
        Assert.AreEqual(1, raises);
        Assert.AreEqual(2, service.CurrentPicks.Count);
        Assert.AreEqual("second-a", service.CurrentPicks[0].Id);
    }

    [TestMethod]
    public async Task RefreshAsync_OnFailure_RetainsPreviousList_AndDoesNotRaiseChanged()
    {
        var (service, client) = CreateService();
        await service.GetOrFetchAsync();
        var snapshot = service.CurrentPicks;

        client.ShouldThrow = true;
        var raises = 0;
        service.PicksChanged += (_, _) => Interlocked.Increment(ref raises);

        await service.RefreshAsync();

        Assert.AreEqual(0, raises, "PicksChanged must not fire on failure.");
        Assert.AreSame(snapshot, service.CurrentPicks);
        Assert.IsTrue(service.HasLoaded);
    }

    [TestMethod]
    public async Task GetOrFetchAsync_OnFirstFailure_DoesNotRaiseChanged_AndStaysNotLoaded()
    {
        var (service, _) = CreateService(shouldThrow: true);
        var raises = 0;
        service.PicksChanged += (_, _) => Interlocked.Increment(ref raises);

        var result = await service.GetOrFetchAsync();

        Assert.AreEqual(0, result.Count);
        Assert.AreEqual(0, raises);
        Assert.IsFalse(service.HasLoaded, "Failed first fetch must leave the cache in 'not loaded' state so the next read retries.");
    }

    [TestMethod]
    public async Task GetOrFetchAsync_AfterFirstFailure_RetriesOnNextCall()
    {
        // Regression: a transient first-load failure used to lock HasLoaded=true, which
        // hid Random Picks for the rest of the process. Subsequent reads must retry.
        var (service, client) = CreateService(shouldThrow: true);
        await service.GetOrFetchAsync();
        Assert.AreEqual(1, client.CallCount);

        client.ShouldThrow = false;
        var picks = await service.GetOrFetchAsync();

        Assert.AreEqual(2, client.CallCount, "Second call after a failed first attempt must retry.");
        Assert.AreEqual(2, picks.Count);
        Assert.IsTrue(service.HasLoaded);
    }

    [TestMethod]
    public async Task ConcurrentGetOrFetch_IsSingleFlight()
    {
        var gate = new TaskCompletionSource();
        var (service, client) = CreateService();
        client.Gate = gate;

        var t1 = service.GetOrFetchAsync();
        var t2 = service.GetOrFetchAsync();
        var t3 = service.GetOrFetchAsync();

        Assert.IsFalse(t1.IsCompleted);
        Assert.IsFalse(t2.IsCompleted);
        Assert.IsFalse(t3.IsCompleted);

        gate.SetResult();
        await Task.WhenAll(t1, t2, t3);

        Assert.AreEqual(1, client.CallCount, "Three concurrent reads must coalesce into one API call.");
        Assert.AreSame(t1.Result, t2.Result);
        Assert.AreSame(t2.Result, t3.Result);
    }

    [TestMethod]
    public async Task RefreshDuringInflightLoad_IsCoalesced_NoSecondCall()
    {
        var gate = new TaskCompletionSource();
        var (service, client) = CreateService();
        client.Gate = gate;

        var firstRead = service.GetOrFetchAsync();
        // Refresh while the initial fetch is still in flight — must NOT issue a second call.
        var refresh = service.RefreshAsync();
        Assert.IsFalse(firstRead.IsCompleted);
        Assert.IsFalse(refresh.IsCompleted);

        gate.SetResult();
        await Task.WhenAll(firstRead, refresh);

        Assert.AreEqual(1, client.CallCount, "Refresh during in-flight load must coalesce, not stack.");
    }

    [TestMethod]
    public async Task LoadingChanged_FiresTrueThenFalse_AroundFetch()
    {
        var gate = new TaskCompletionSource();
        var (service, client) = CreateService();
        client.Gate = gate;

        var states = new List<bool>();
        service.LoadingChanged += (_, _) =>
        {
            lock (states) { states.Add(service.IsLoading); }
        };

        var task = service.GetOrFetchAsync();
        Assert.IsTrue(service.IsLoading);

        gate.SetResult();
        await task;

        Assert.IsFalse(service.IsLoading);
        lock (states)
        {
            CollectionAssert.AreEqual(new[] { true, false }, states);
        }
    }

    [TestMethod]
    public async Task GetOrFetchAsync_WithNullClient_DoesNotThrow_AndStaysNotLoaded()
    {
        // No client = no fetch attempted = no successful load. Must stay "not loaded" so
        // the next read retries once a client becomes available.
        var clientService = new FakeSubsonicClientService { Client = null };
        var service = new RandomPicksCacheService(clientService, new NullLoggingService());

        var result = await service.GetOrFetchAsync();

        Assert.AreEqual(0, result.Count);
        Assert.IsFalse(service.HasLoaded);
        Assert.IsFalse(service.IsLoading);
    }
}
