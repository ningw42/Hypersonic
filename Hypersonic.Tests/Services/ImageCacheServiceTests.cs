using System.Net.Http;
using System.Reflection;
using System.Text.Json;

using Hypersonic.Client;
using Hypersonic.Client.Models.Responses;
using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;
using Hypersonic.Models;
using Hypersonic.Services;

using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hypersonic.Tests.Services;

[TestClass]
public class ImageCacheServiceTests
{
    private static readonly byte[] PngA = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4];
    private static readonly byte[] PngB = [0x89, 0x50, 0x4E, 0x47, 5, 6, 7, 8];

    private string _cacheRoot = null!;
    private FakeTimeProvider _time = null!;
    private FakeSubsonicClient _client = null!;
    private JsonImageCacheStore _store = null!;

    [TestInitialize]
    public void Setup()
    {
        _cacheRoot = Path.Combine(Path.GetTempPath(), "hypersonic-image-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_cacheRoot);
        _time = new FakeTimeProvider(new DateTimeOffset(2026, 5, 3, 0, 0, 0, TimeSpan.Zero));
        _client = new FakeSubsonicClient();
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_cacheRoot))
        {
            Directory.Delete(_cacheRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task GetImageAsync_WritesImageAndReusesFreshEntry()
    {
        _client.Images["cover-a"] = PngA;
        var service = CreateService();

        var first = await service.GetImageAsync(CoverArt("cover-a"), ImageRequestOptions.ForSize("300"));
        var second = await service.GetImageAsync(CoverArt("cover-a"), ImageRequestOptions.ForSize("300"));
        var stats = await service.GetStatsAsync();

        Assert.IsNotNull(first);
        Assert.AreEqual(first, second);
        Assert.AreEqual(1, _client.FetchCounts["cover-a|300"]);
        Assert.AreEqual(1, stats.EntryCount);
        Assert.AreEqual(PngA.Length, stats.TotalBytes);
        Assert.IsTrue(File.Exists(first!.LocalPath));
    }

    [TestMethod]
    public async Task Manifest_DoesNotPersistCredentialsOrCoverArtIdentifier()
    {
        _client.Images["secret-cover-id"] = PngA;
        var service = CreateService();

        await service.GetImageAsync(CoverArt("secret-cover-id"), ImageRequestOptions.ForSize("600"));
        await _store.FlushAsync();

        var manifest = await File.ReadAllTextAsync(Path.Combine(_cacheRoot, "manifest.json"));
        Assert.IsFalse(manifest.Contains("password", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(manifest.Contains("alice", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(manifest.Contains("secret-cover-id", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(manifest.Contains("u=", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(manifest.Contains("p=", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task ExpiredEntry_RefetchesSameIdentifier()
    {
        _client.Images["cover-a"] = PngA;
        var service = CreateService(new ImageCacheOptions
        {
            RootPath = _cacheRoot,
            TimeToLiveDays = 1,
        });

        var first = await service.GetImageAsync(CoverArt("cover-a"), ImageRequestOptions.ForSize("300"));
        _time.Advance(TimeSpan.FromDays(2));
        _client.Images["cover-a"] = PngB;

        var second = await service.GetImageAsync(CoverArt("cover-a"), ImageRequestOptions.ForSize("300"));

        Assert.AreEqual(first, second);
        Assert.AreEqual(2, _client.FetchCounts["cover-a|300"]);
        CollectionAssert.AreEqual(PngB, await File.ReadAllBytesAsync(second!.LocalPath));
    }

    [TestMethod]
    public async Task ExpiredEntry_FetchFailureReturnsStaleEntry()
    {
        _client.Images["cover-a"] = PngA;
        var service = CreateService(new ImageCacheOptions
        {
            RootPath = _cacheRoot,
            TimeToLiveDays = 1,
        });

        var first = await service.GetImageAsync(CoverArt("cover-a"), ImageRequestOptions.ForSize("300"));
        _time.Advance(TimeSpan.FromDays(2));
        _client.FailImageIds.Add("cover-a");

        var stale = await service.GetImageAsync(CoverArt("cover-a"), ImageRequestOptions.ForSize("300"));

        Assert.AreEqual(first, stale);
        Assert.AreEqual(2, _client.FetchCounts["cover-a|300"]);
        CollectionAssert.AreEqual(PngA, await File.ReadAllBytesAsync(stale!.LocalPath));
    }

    [TestMethod]
    public async Task MaximumBytes_EvictsLeastRecentlyUsedEntry()
    {
        _client.Images["cover-a"] = PngA;
        _client.Images["cover-b"] = PngB;
        var service = CreateService(new ImageCacheOptions
        {
            RootPath = _cacheRoot,
            MaximumBytes = PngA.Length + 1,
        });

        await service.GetImageAsync(CoverArt("cover-a"), ImageRequestOptions.ForSize("300"));
        _time.Advance(TimeSpan.FromMinutes(1));
        await service.GetImageAsync(CoverArt("cover-b"), ImageRequestOptions.ForSize("300"));

        var stats = await service.GetStatsAsync();
        Assert.AreEqual(1, stats.EntryCount);

        await service.GetImageAsync(CoverArt("cover-a"), ImageRequestOptions.ForSize("300"));
        Assert.AreEqual(2, _client.FetchCounts["cover-a|300"]);
    }

    [TestMethod]
    public async Task ClearAsync_RemovesCachedFilesAndManifestEntries()
    {
        _client.Images["cover-a"] = PngA;
        var service = CreateService();
        var uri = await service.GetImageAsync(CoverArt("cover-a"), ImageRequestOptions.ForSize("300"));

        await service.ClearAsync();
        var stats = await service.GetStatsAsync();

        Assert.IsFalse(File.Exists(uri!.LocalPath));
        Assert.AreEqual(0, stats.EntryCount);
        Assert.AreEqual(0, stats.TotalBytes);
    }

    [TestMethod]
    public async Task TouchEntryAsync_PersistsLastAccessUtcAfterFlush()
    {
        var store = CreateStore();
        var firstAccess = _time.GetUtcNow();
        var secondAccess = firstAccess.AddMinutes(5);

        await store.UpsertEntryAsync(
            "key-a",
            new ImageCacheEntry
            {
                FileName = "key-a.png",
                Length = PngA.Length,
                Provider = ImageReference.SubsonicProvider,
                Kind = ImageReference.CoverArtKind,
                LastAccessUtc = firstAccess,
                CreatedAtUtc = firstAccess,
                ExpiresAtUtc = firstAccess.AddDays(30),
            });

        await store.TouchEntryAsync("key-a", secondAccess);
        await store.FlushAsync();

        var reloaded = CreateStore();
        var entry = await reloaded.GetEntryAsync("key-a");

        Assert.IsNotNull(entry);
        Assert.AreEqual(secondAccess, entry.LastAccessUtc);
    }

    [TestMethod]
    public async Task Dispose_FlushesDirtyManifestImmediately()
    {
        var store = CreateStore();
        var now = _time.GetUtcNow();

        await store.UpsertEntryAsync(
            "key-a",
            new ImageCacheEntry
            {
                FileName = "key-a.png",
                Length = PngA.Length,
                Provider = ImageReference.SubsonicProvider,
                Kind = ImageReference.CoverArtKind,
                LastAccessUtc = now,
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddDays(30),
            });

        store.Dispose();

        var reloaded = CreateStore();
        var entry = await reloaded.GetEntryAsync("key-a");

        Assert.IsNotNull(entry);
        Assert.AreEqual("key-a.png", entry.FileName);
    }

    [TestMethod]
    public async Task ReconcileAsync_RemovesFilesWithoutManifestEntries()
    {
        var orphanPath = Path.Combine(_cacheRoot, "orphan.png");
        await File.WriteAllBytesAsync(orphanPath, PngA);
        var service = CreateService();

        await service.ReconcileAsync();

        Assert.IsFalse(File.Exists(orphanPath));
    }

    [TestMethod]
    public async Task CancelledAwait_RemovesInFlightFetchAfterFetchCompletes()
    {
        _client.DeferredImageBytes = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = CreateService();
        using var cts = new CancellationTokenSource();

        var request = service.GetImageAsync(CoverArt("cover-a"), ImageRequestOptions.ForSize("300"), cts.Token);
        await WaitForFetchAttemptAsync("cover-a|300");
        await cts.CancelAsync();

        try
        {
            await request;
            Assert.Fail("The request should have been canceled.");
        }
        catch (OperationCanceledException)
        {
        }

        _client.DeferredImageBytes.SetResult(PngA);
        await WaitForInFlightFetchesAsync(service, expectedCount: 0);

        var uri = await service.GetImageAsync(CoverArt("cover-a"), ImageRequestOptions.ForSize("300"));

        Assert.IsNotNull(uri);
        Assert.AreEqual(1, _client.FetchCounts["cover-a|300"]);
    }

    [TestMethod]
    public async Task Dispose_CancelsInFlightFetchBeforeStoreIsDisposed()
    {
        _client.DeferredImageBytes = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = CreateService();

        var request = service.GetImageAsync(CoverArt("cover-a"), ImageRequestOptions.ForSize("300"));
        await WaitForFetchAttemptAsync("cover-a|300");

        service.Dispose();
        _store.Dispose();
        _client.DeferredImageBytes.SetResult(PngA);

        var result = await request.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task Dispose_CancelsRunningReconcileBeforeDisposingGate()
    {
        var store = new BlockingGetEntriesStore();
        var service = CreateService(store);

        var reconcile = service.ReconcileAsync();
        await store.GetEntriesStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        service.Dispose();

        await reconcile.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task CorruptManifest_RecoversWithEmptyStore()
    {
        await File.WriteAllTextAsync(Path.Combine(_cacheRoot, "manifest.json"), "{ not valid json");
        var service = CreateService();

        var stats = await service.GetStatsAsync();

        Assert.AreEqual(0, stats.EntryCount);
        Assert.AreEqual(0, stats.TotalBytes);
    }

    private JsonImageCacheStore CreateStore(ImageCacheOptions? options = null)
    {
        options ??= new ImageCacheOptions { RootPath = _cacheRoot };
        options.RootPath = _cacheRoot;
        return new JsonImageCacheStore(Options.Create(options), new NullLoggingService());
    }

    private CachedImageService CreateService(ImageCacheOptions? options = null)
    {
        options ??= new ImageCacheOptions { RootPath = _cacheRoot };
        options.RootPath = _cacheRoot;
        _store = CreateStore(options);

        return new CachedImageService(
            new FakeSubsonicClientService { Client = _client },
            _store,
            Options.Create(options),
            _time,
            new NullLoggingService());
    }

    private CachedImageService CreateService(IImageCacheStore store, ImageCacheOptions? options = null)
    {
        options ??= new ImageCacheOptions { RootPath = _cacheRoot };
        options.RootPath = _cacheRoot;

        return new CachedImageService(
            new FakeSubsonicClientService { Client = _client },
            store,
            Options.Create(options),
            _time,
            new NullLoggingService());
    }

    private static ImageReference CoverArt(string id)
    {
        return ImageReference.SubsonicCoverArt(id)!;
    }

    private async Task WaitForFetchAttemptAsync(string key)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (!_client.FetchCounts.ContainsKey(key))
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                Assert.Fail($"Timed out waiting for fetch attempt '{key}'.");
            }

            await Task.Delay(10);
        }
    }

    private static async Task WaitForInFlightFetchesAsync(CachedImageService service, int expectedCount)
    {
        var field = typeof(CachedImageService).GetField("_inFlightFetches", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (true)
        {
            var dictionary = field.GetValue(service)!;
            var count = (int)dictionary.GetType().GetProperty("Count")!.GetValue(dictionary)!;
            if (count == expectedCount)
            {
                return;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                Assert.Fail($"Timed out waiting for {expectedCount} in-flight fetches. Current count: {count}.");
            }

            await Task.Delay(10);
        }
    }

    private sealed class FakeSubsonicClientService : ISubsonicClientService
    {
        public ISubsonicClient? Client { get; set; }
        public void CreateClient(ServerCredential credential) { }
        public Task<ConnectionTestResult> TestConnectionAsync(ServerCredential credential) => throw new NotImplementedException();
    }

    private sealed class FakeSubsonicClient : ISubsonicClient
    {
        public Dictionary<string, byte[]> Images { get; } = [];
        public Dictionary<string, int> FetchCounts { get; } = [];
        public HashSet<string> FailImageIds { get; } = [];
        public TaskCompletionSource<byte[]>? DeferredImageBytes { get; set; }

        public Task<Stream> GetCoverArtAsync(string id, string? size = null)
        {
            var key = $"{id}|{size ?? string.Empty}";
            FetchCounts[key] = FetchCounts.GetValueOrDefault(key) + 1;
            if (FailImageIds.Contains(id))
            {
                throw new IOException("Image fetch failed.");
            }

            if (DeferredImageBytes != null)
            {
                return GetDeferredImageAsync(DeferredImageBytes);
            }

            return Task.FromResult<Stream>(new MemoryStream(Images[id]));
        }

        private static async Task<Stream> GetDeferredImageAsync(TaskCompletionSource<byte[]> source)
        {
            var bytes = await source.Task.ConfigureAwait(false);
            return new MemoryStream(bytes);
        }

        public Uri GetCoverArtUri(string id, string? size = null)
        {
            return new Uri($"https://alice:password@example.test/rest/getCoverArt.view?u=alice&p=password&id={id}&size={size}");
        }

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
        public Task<Search3Response> Search3Async(string query, int? artistCount = null, int? artistOffset = null, int? albumCount = null, int? albumOffset = null, int? songCount = null, int? songOffset = null, string? musicFolderId = null) => throw new NotImplementedException();
        public Task<GetPlaylistsResponse> GetPlaylistsAsync(string? username = null) => throw new NotImplementedException();
        public Task<GetPlaylistResponse> GetPlaylistAsync(string id) => throw new NotImplementedException();
        public Task<SubsonicResponse> CreatePlaylistAsync(string? playlistId = null, string? name = null, string[]? songId = null) => throw new NotImplementedException();
        public Task<SubsonicResponse> UpdatePlaylistAsync(string playlistId, string? name = null, string? comment = null, bool? @public = null, string[]? songIdToAdd = null, int[]? songIndexToRemove = null) => throw new NotImplementedException();
        public Task<SubsonicResponse> DeletePlaylistAsync(string id) => throw new NotImplementedException();
        public Task<Stream> StreamAsync(string id, int? maxBitrate = null, string? format = null, int? timeOffset = null, string? size = null, bool estimateContentLength = false, bool converted = false) => throw new NotImplementedException();
        public Task<Stream> DownloadAsync(string id) => throw new NotImplementedException();
        public Task<GetLyricsResponse> GetLyricsAsync(string? artist = null, string? title = null) => throw new NotImplementedException();
        public Task<GetLyricsBySongIdResponse> GetLyricsBySongIdAsync(string id) => throw new NotImplementedException();
        public Task<Stream> GetAvatarAsync(string username) => throw new NotImplementedException();
        public Uri GetAvatarUri(string username) => throw new NotImplementedException();
        public Task<SubsonicResponse> StarAsync(string? id = null, string? albumId = null, string? artistId = null) => throw new NotImplementedException();
        public Task<SubsonicResponse> UnstarAsync(string? id = null, string? albumId = null, string? artistId = null) => throw new NotImplementedException();
        public Task<SubsonicResponse> SetRatingAsync(string id) => throw new NotImplementedException();
        public Task<SubsonicResponse> SetRatingAsync(string id, int rating) => throw new NotImplementedException();
        public Task<SubsonicResponse> ScrobbleAsync(string id, ulong? time = null, bool submission = true) => throw new NotImplementedException();
        public Task<GetUserResponse> GetUserAsync(string username) => throw new NotImplementedException();
        public Task<GetUsersResponse> GetUsersAsync() => throw new NotImplementedException();
        public Task<GetScanStatusResponse> GetScanStatusAsync() => throw new NotImplementedException();
        public Task<StartScanResponse> StartScanAsync() => throw new NotImplementedException();
    }

    private sealed class BlockingGetEntriesStore : IImageCacheStore
    {
        public TaskCompletionSource GetEntriesStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ImageCacheEntry?> GetEntryAsync(string keyHash, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task TouchEntryAsync(string keyHash, DateTimeOffset lastAccessUtc, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task UpsertEntryAsync(string keyHash, ImageCacheEntry entry, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task RemoveEntryAsync(string keyHash, bool flushImmediately = false, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<ImageCacheStoreStats> GetStatsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public async Task<IReadOnlyList<ImageCacheStoreEntry>> GetEntriesAsync(CancellationToken cancellationToken = default)
        {
            GetEntriesStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return [];
        }

        public Task<IReadOnlyList<ImageCacheStoreEntry>> GetEvictionCandidatesAsync(
            string? protectedKeyHash = null,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task ClearAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task FlushAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
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
}
