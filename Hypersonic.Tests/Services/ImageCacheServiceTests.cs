using System.Net.Http;
using System.Reflection;

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
    private FakeSubsonicClientService _clientService = null!;
    private SqliteImageCacheStore _store = null!;

    [TestInitialize]
    public void Setup()
    {
        _cacheRoot = Path.Combine(Path.GetTempPath(), "hypersonic-image-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_cacheRoot);
        _time = new FakeTimeProvider(new DateTimeOffset(2026, 5, 3, 0, 0, 0, TimeSpan.Zero));
        _client = new FakeSubsonicClient();
        _clientService = new FakeSubsonicClientService { Client = _client };
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            _store?.Dispose();
        }
        catch
        {
            // Best-effort.
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        if (Directory.Exists(_cacheRoot))
        {
            try
            {
                Directory.Delete(_cacheRoot, recursive: true);
            }
            catch (IOException)
            {
                // SQLite may briefly hold sidecar handles; cleanup is best-effort.
            }
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

        var serialized = await SerializeStoreEntriesAsync(_store);
        Assert.IsFalse(serialized.Contains("password", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serialized.Contains("alice", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serialized.Contains("secret-cover-id", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serialized.Contains("u=", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serialized.Contains("p=", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task DifferentUserIdentities_ProduceDistinctCacheEntriesForSameCoverId()
    {
        _client.Images["shared-cover"] = PngA;
        var service = CreateService();

        _clientService.ActiveUserIdentity = "alice";
        var aliceUri = await service.GetImageAsync(CoverArt("shared-cover"), ImageRequestOptions.ForSize("300"));

        _clientService.ActiveUserIdentity = "bob";
        var bobUri = await service.GetImageAsync(CoverArt("shared-cover"), ImageRequestOptions.ForSize("300"));

        await _store.FlushAsync();
        var stats = await service.GetStatsAsync();
        var serialized = await SerializeStoreEntriesAsync(_store);

        Assert.IsNotNull(aliceUri);
        Assert.IsNotNull(bobUri);
        Assert.AreNotEqual(aliceUri, bobUri);
        Assert.IsTrue(File.Exists(aliceUri!.LocalPath));
        Assert.IsTrue(File.Exists(bobUri!.LocalPath));
        Assert.AreEqual(2, stats.EntryCount);
        Assert.AreEqual(2, _client.FetchCounts["shared-cover|300"]);
        Assert.IsFalse(serialized.Contains("alice", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serialized.Contains("bob", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task MissingUserIdentity_SkipsFetchAndReturnsNull()
    {
        _client.Images["cover-a"] = PngA;
        _clientService.ActiveUserIdentity = null;
        var service = CreateService();

        var result = await service.GetImageAsync(CoverArt("cover-a"), ImageRequestOptions.ForSize("300"));

        Assert.IsNull(result);
        Assert.IsFalse(_client.FetchCounts.ContainsKey("cover-a|300"));
    }

    [TestMethod]
    public async Task LegacyJsonManifest_IsDiscardedAndImageFilesRemoved()
    {
        var manifestPath = Path.Combine(_cacheRoot, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, "{ \"Version\": 2, \"Entries\": {} }");

        var orphanCachedImage = Path.Combine(_cacheRoot, new string('a', 64) + ".png");
        await File.WriteAllBytesAsync(orphanCachedImage, PngA);

        var unrelatedFile = Path.Combine(_cacheRoot, "user-notes.txt");
        await File.WriteAllTextAsync(unrelatedFile, "do not delete");

        var store = CreateStore();
        var entry = await store.GetEntryAsync("legacy-key");

        Assert.IsNull(entry);
        Assert.IsFalse(File.Exists(manifestPath), "Legacy manifest.json should be deleted.");
        Assert.IsFalse(File.Exists(orphanCachedImage), "Cached image file should be deleted during JSON discard.");
        Assert.IsTrue(File.Exists(unrelatedFile), "Files that don't match the cached-image predicate must be left alone.");
        Assert.IsTrue(File.Exists(Path.Combine(_cacheRoot, "cache.db")), "SQLite database file should be created.");
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
    public async Task ReconcileAsync_RemovesCachedImageFilesWithoutManifestEntries_AndPreservesOthers()
    {
        var matchingOrphan = Path.Combine(_cacheRoot, new string('b', 64) + ".png");
        var unrelatedOrphan = Path.Combine(_cacheRoot, "orphan.png");
        await File.WriteAllBytesAsync(matchingOrphan, PngA);
        await File.WriteAllBytesAsync(unrelatedOrphan, PngA);

        var service = CreateService();

        await service.ReconcileAsync();

        Assert.IsFalse(File.Exists(matchingOrphan), "Cached-image-shaped orphan should be deleted.");
        Assert.IsTrue(File.Exists(unrelatedOrphan), "File not matching cached-image predicate should be left alone.");
        Assert.IsTrue(File.Exists(Path.Combine(_cacheRoot, "cache.db")), "SQLite database must survive ReconcileAsync.");
    }

    [TestMethod]
    public async Task ClearAsync_PreservesSqliteDatabaseFile()
    {
        _client.Images["cover-a"] = PngA;
        var service = CreateService();
        await service.GetImageAsync(CoverArt("cover-a"), ImageRequestOptions.ForSize("300"));

        await service.ClearAsync();

        Assert.IsTrue(File.Exists(Path.Combine(_cacheRoot, "cache.db")), "ClearAsync must not delete the SQLite database file.");
        var stats = await service.GetStatsAsync();
        Assert.AreEqual(0, stats.EntryCount);
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
    public async Task CorruptDatabase_RecoversWithEmptyStore()
    {
        await File.WriteAllBytesAsync(Path.Combine(_cacheRoot, "cache.db"), [0x42, 0x42, 0x42, 0x42, 0x42]);
        var service = CreateService();

        var stats = await service.GetStatsAsync();

        Assert.AreEqual(0, stats.EntryCount);
        Assert.AreEqual(0, stats.TotalBytes);
    }

    [TestMethod]
    public async Task ReconcileAsync_AbortsWithoutDeletingFiles_WhenStoreEnumerationFails()
    {
        // A real cached image file lives in the folder, but the store can't enumerate (DB read error).
        // Reconcile must NOT treat that as "table is empty" and delete the file.
        var cachedImage = Path.Combine(_cacheRoot, new string('a', 64) + ".png");
        await File.WriteAllBytesAsync(cachedImage, PngA);

        var throwingStore = new ThrowingGetEntriesStore();
        var service = CreateService(throwingStore);

        await service.ReconcileAsync();

        Assert.IsTrue(
            File.Exists(cachedImage),
            "ReconcileAsync must abort and leave files alone when the store's entry enumeration fails.");
    }

    private SqliteImageCacheStore CreateStore(ImageCacheOptions? options = null)
    {
        options ??= new ImageCacheOptions { RootPath = _cacheRoot };
        options.RootPath = _cacheRoot;
        return new SqliteImageCacheStore(Options.Create(options), new NullLoggingService());
    }

    private CachedImageService CreateService(ImageCacheOptions? options = null)
    {
        options ??= new ImageCacheOptions { RootPath = _cacheRoot };
        options.RootPath = _cacheRoot;
        _store = CreateStore(options);

        return new CachedImageService(
            _clientService,
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
            _clientService,
            store,
            Options.Create(options),
            _time,
            new NullLoggingService());
    }

    private static ImageReference CoverArt(string id)
    {
        return ImageReference.SubsonicCoverArt(id)!;
    }

    private static async Task<string> SerializeStoreEntriesAsync(IImageCacheStore store)
    {
        var entries = await store.GetEntriesAsync();
        var sb = new System.Text.StringBuilder();
        foreach (var item in entries)
        {
            var entry = item.Entry;
            sb.Append(item.KeyHash).Append('|')
              .Append(entry.FileName).Append('|')
              .Append(entry.Provider).Append('|')
              .Append(entry.Kind).Append('|')
              .Append(entry.ServerHash).Append('|')
              .Append(entry.ServerScopeHash).Append('|')
              .Append(entry.Size).Append('\n');
        }
        return sb.ToString();
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
        public string? ActiveUserIdentity { get; set; } = "alice";
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

    private sealed class ThrowingGetEntriesStore : IImageCacheStore
    {
        public Task<ImageCacheEntry?> GetEntryAsync(string keyHash, CancellationToken cancellationToken = default) => Task.FromResult<ImageCacheEntry?>(null);

        public Task TouchEntryAsync(string keyHash, DateTimeOffset lastAccessUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpsertEntryAsync(string keyHash, ImageCacheEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RemoveEntryAsync(string keyHash, bool flushImmediately = false, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<ImageCacheStoreStats> GetStatsAsync(CancellationToken cancellationToken = default) => Task.FromResult(new ImageCacheStoreStats(0, 0));

        public Task<IReadOnlyList<ImageCacheStoreEntry>> GetEntriesAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Image cache database is unavailable; cannot enumerate entries.");

        public Task<IReadOnlyList<ImageCacheStoreEntry>> GetEvictionCandidatesAsync(
            string? protectedKeyHash = null,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ImageCacheStoreEntry>>([]);

        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
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
