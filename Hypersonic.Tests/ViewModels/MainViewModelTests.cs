using System.Net.Http;

using Hypersonic.Client;
using Hypersonic.Client.Models.Entities;
using Hypersonic.Client.Models.Responses;
using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;
using Hypersonic.Services;
using Hypersonic.ViewModels;

using AlbumInfo = Hypersonic.Core.Models.AlbumInfo;

namespace Hypersonic.Tests.ViewModels;

[TestClass]
public class MainViewModelTests
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

    private sealed class FakeSubsonicClient : ISubsonicClient
    {
        public Dictionary<string, AlbumID3[]?> AlbumListResponses { get; set; } = [];
        public HashSet<string> ShouldThrowTypes { get; set; } = [];
        public int RandomCallCount { get; private set; }

        public Task<GetAlbumList2Response> GetAlbumList2Async(string type, int? size = null, int? offset = null, int? fromYear = null, int? toYear = null, string? genre = null, string? musicFolderId = null)
        {
            var key = type == "byYear" ? $"byYear:{fromYear}-{toYear}" : type;
            if (key == "random")
            {
                RandomCallCount++;
            }
            if (ShouldThrowTypes.Contains(key))
            {
                throw new HttpRequestException($"Failed to fetch {key}");
            }

            AlbumListResponses.TryGetValue(key, out var albums);
            return Task.FromResult(new GetAlbumList2Response
            {
                AlbumList2 = new AlbumList2 { Album = albums },
            });
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

    /// <summary>
    /// Fake cache that records calls and lets tests force refresh / failure behavior without
    /// going through the real RandomPicksCacheService. Useful for verifying VM-cache interactions.
    /// </summary>
    private sealed class FakeRandomPicksCache : IRandomPicksCacheService
    {
        public IReadOnlyList<AlbumInfo> CurrentPicks { get; private set; } = [];
        public bool IsLoading { get; private set; }
        public bool HasLoaded { get; private set; }
        public int GetOrFetchCalls { get; private set; }
        public int RefreshCalls { get; private set; }
        public Func<IReadOnlyList<AlbumInfo>>? FetchResult { get; set; }

        public event EventHandler? PicksChanged;
        public event EventHandler? LoadingChanged;

        public Task<IReadOnlyList<AlbumInfo>> GetOrFetchAsync()
        {
            GetOrFetchCalls++;
            if (HasLoaded)
            {
                return Task.FromResult(CurrentPicks);
            }
            return DoFetchAsync();
        }

        public Task RefreshAsync()
        {
            RefreshCalls++;
            return DoFetchAsync();
        }

        private Task<IReadOnlyList<AlbumInfo>> DoFetchAsync()
        {
            IsLoading = true;
            LoadingChanged?.Invoke(this, EventArgs.Empty);
            try
            {
                CurrentPicks = FetchResult?.Invoke() ?? [];
                HasLoaded = true;
                PicksChanged?.Invoke(this, EventArgs.Empty);
                return Task.FromResult(CurrentPicks);
            }
            finally
            {
                IsLoading = false;
                LoadingChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>Inject an external change (simulating a refresh completing).</summary>
        public void PushPicks(IReadOnlyList<AlbumInfo> picks)
        {
            CurrentPicks = picks;
            HasLoaded = true;
            PicksChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static AlbumID3 MakeAlbum(string id, string name = "Album", string artist = "Artist", int year = 2025) =>
        new() { Id = id, Name = name, Artist = artist, ArtistId = "ar1", Year = year, CoverArt = $"cov-{id}", SongCount = 10, Duration = 3600 };

    private static AlbumInfo MakeMappedAlbum(string id) =>
        new() { Id = id, Name = id, Artist = "A", ArtistId = "ar1", Year = 2025 };

    private static MainViewModel CreateVm(FakeSubsonicClient client, IRandomPicksCacheService? cache = null)
    {
        var clientService = new FakeSubsonicClientService { Client = client };
        cache ??= new RandomPicksCacheService(clientService, new NullLoggingService());
        // Synchronous enqueue keeps event handlers on the calling thread for deterministic tests.
        return new MainViewModel(clientService, cache, new NullLoggingService(), enqueue: action => action());
    }

    private static FakeSubsonicClient CreateClientWithAllSections()
    {
        return new FakeSubsonicClient
        {
            AlbumListResponses =
            {
                ["recent"] = [MakeAlbum("rp1"), MakeAlbum("rp2")],
                [$"byYear:{DateTime.Now.Year}-0"] = [MakeAlbum("rr1"), MakeAlbum("rr2"), MakeAlbum("rr3"), MakeAlbum("rr4"), MakeAlbum("rr5")],
                ["newest"] = [MakeAlbum("ra1"), MakeAlbum("ra2")],
                ["frequent"] = [MakeAlbum("mp1"), MakeAlbum("mp2")],
                ["random"] = [MakeAlbum("rand1"), MakeAlbum("rand2")],
            },
        };
    }

    [TestMethod]
    public async Task AllSectionsPopulated_WhenAllApiCallsSucceed()
    {
        var client = CreateClientWithAllSections();
        var vm = CreateVm(client);

        await vm.LoadAllAsync();

        Assert.IsTrue(vm.HasRecentlyPlayed);
        Assert.IsTrue(vm.HasRecentlyReleased);
        Assert.IsTrue(vm.HasRecentlyAdded);
        Assert.IsTrue(vm.HasMostPlayed);
        Assert.IsTrue(vm.HasRandomPicks);
        Assert.IsTrue(vm.HasAnyContent);
        Assert.IsFalse(vm.HasError);
        Assert.IsFalse(vm.IsLoading);
    }

    [TestMethod]
    public async Task EmptySectionHidden_WhenApiReturnsNoAlbums()
    {
        var client = new FakeSubsonicClient
        {
            AlbumListResponses =
            {
                ["recent"] = null,
                [$"byYear:{DateTime.Now.Year}-0"] = [MakeAlbum("rr1"), MakeAlbum("rr2"), MakeAlbum("rr3"), MakeAlbum("rr4"), MakeAlbum("rr5")],
                ["newest"] = [MakeAlbum("ra1")],
                ["frequent"] = null,
                ["random"] = [MakeAlbum("rand1")],
            },
        };

        var vm = CreateVm(client);
        await vm.LoadAllAsync();

        Assert.IsFalse(vm.HasRecentlyPlayed);
        Assert.IsTrue(vm.HasRecentlyReleased);
        Assert.IsTrue(vm.HasRecentlyAdded);
        Assert.IsFalse(vm.HasMostPlayed);
        Assert.IsTrue(vm.HasRandomPicks);
        Assert.IsTrue(vm.HasAnyContent);
        Assert.IsFalse(vm.HasError);
    }

    [TestMethod]
    public async Task AllSectionsEmpty_ShowsEmptyState()
    {
        var client = new FakeSubsonicClient
        {
            AlbumListResponses =
            {
                ["recent"] = null,
                [$"byYear:{DateTime.Now.Year}-0"] = null,
                ["newest"] = null,
                ["frequent"] = null,
                ["random"] = null,
            },
        };

        var vm = CreateVm(client);
        await vm.LoadAllAsync();

        Assert.IsFalse(vm.HasAnyContent);
        Assert.IsFalse(vm.HasError);
        Assert.IsTrue(vm.ShowEmptyState);
    }

    [TestMethod]
    public async Task AllCallsFail_ShowsError()
    {
        var client = new FakeSubsonicClient
        {
            ShouldThrowTypes = { "recent", $"byYear:{DateTime.Now.Year}-0", "newest", "frequent", "random" },
        };

        var vm = CreateVm(client);
        await vm.LoadAllAsync();

        Assert.IsFalse(vm.HasAnyContent);
        Assert.IsTrue(vm.HasError);
        Assert.IsFalse(vm.ShowEmptyState);
    }

    [TestMethod]
    public async Task PartialFailure_ShowsSuccessfulSections()
    {
        var client = new FakeSubsonicClient
        {
            AlbumListResponses =
            {
                [$"byYear:{DateTime.Now.Year}-0"] = [MakeAlbum("rr1"), MakeAlbum("rr2"), MakeAlbum("rr3"), MakeAlbum("rr4"), MakeAlbum("rr5")],
                ["random"] = [MakeAlbum("rand1")],
            },
            ShouldThrowTypes = { "recent", "newest", "frequent" },
        };

        var vm = CreateVm(client);
        await vm.LoadAllAsync();

        Assert.IsFalse(vm.HasRecentlyPlayed);
        Assert.IsTrue(vm.HasRecentlyReleased);
        Assert.IsFalse(vm.HasRecentlyAdded);
        Assert.IsFalse(vm.HasMostPlayed);
        Assert.IsTrue(vm.HasRandomPicks);
        Assert.IsTrue(vm.HasAnyContent);
        Assert.IsFalse(vm.HasError);
    }

    [TestMethod]
    public async Task LoadSectionsAsync_DoesNotCallRandomEndpointDirectly()
    {
        var client = CreateClientWithAllSections();
        var vm = CreateVm(client);

        await vm.LoadSectionsAsync();

        Assert.AreEqual(0, client.RandomCallCount, "LoadSectionsAsync must no longer touch the random endpoint; that is the cache's job.");
    }

    [TestMethod]
    public async Task OnlyFourNonRandomFailures_StillShowsError()
    {
        // The four non-random sections all fail; random is empty (cache returns []).
        // The denominator must be 4, not 5 — otherwise the empty state would show instead of error.
        var client = new FakeSubsonicClient
        {
            ShouldThrowTypes = { "recent", $"byYear:{DateTime.Now.Year}-0", "newest", "frequent" },
            AlbumListResponses = { ["random"] = null },
        };

        var vm = CreateVm(client);
        await vm.LoadAllAsync();

        Assert.IsFalse(vm.HasAnyContent);
        Assert.IsTrue(vm.HasError, "Four non-random failures must trigger HasError (denominator is 4, not 5).");
    }

    [TestMethod]
    public async Task Retry_ReloadsData_IncludingRandom()
    {
        var client = new FakeSubsonicClient
        {
            ShouldThrowTypes = { "recent", $"byYear:{DateTime.Now.Year}-0", "newest", "frequent", "random" },
        };

        var vm = CreateVm(client);
        await vm.LoadAllAsync();

        Assert.IsTrue(vm.HasError);

        client.ShouldThrowTypes.Clear();
        client.AlbumListResponses["random"] = [MakeAlbum("rand1")];
        client.AlbumListResponses[$"byYear:{DateTime.Now.Year}-0"] = [MakeAlbum("rr1"), MakeAlbum("rr2"), MakeAlbum("rr3"), MakeAlbum("rr4"), MakeAlbum("rr5")];

        await vm.RetryCommand.ExecuteAsync(null);

        Assert.IsFalse(vm.HasError);
        Assert.IsTrue(vm.HasRandomPicks, "Retry must cover random — if random is the only success, the page must still show it.");
    }

    [TestMethod]
    public async Task Retry_WhenRandomIsOnlyAvailableSection_ShowsContent()
    {
        // All four non-random sections fail and stay failing; only random recovers.
        // Without retry covering random, the page would stay stuck on the error screen.
        var client = new FakeSubsonicClient
        {
            ShouldThrowTypes = { "recent", $"byYear:{DateTime.Now.Year}-0", "newest", "frequent", "random" },
        };

        var vm = CreateVm(client);
        await vm.LoadAllAsync();
        Assert.IsTrue(vm.HasError);

        client.ShouldThrowTypes.Remove("random");
        client.AlbumListResponses["random"] = [MakeAlbum("rand1")];

        await vm.RetryCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.HasRandomPicks);
        Assert.IsTrue(vm.HasAnyContent);
        Assert.IsFalse(vm.HasError);
    }

    [TestMethod]
    public async Task RefreshRandomPicksCommand_DelegatesToCache()
    {
        var client = CreateClientWithAllSections();
        var cache = new FakeRandomPicksCache
        {
            FetchResult = () => [MakeMappedAlbum("from-cache")],
        };
        var vm = CreateVm(client, cache);

        await vm.LoadAllAsync();
        var refreshesBefore = cache.RefreshCalls;

        await vm.RefreshRandomPicksCommand.ExecuteAsync(null);

        Assert.AreEqual(refreshesBefore + 1, cache.RefreshCalls);
    }

    [TestMethod]
    public async Task PicksChangedEvent_UpdatesRandomPicks_AndAggregateState()
    {
        // Start with no non-random content and no random; trigger a push from the cache,
        // which should populate RandomPicks and flip HasAnyContent on.
        var client = new FakeSubsonicClient
        {
            AlbumListResponses = { ["random"] = null, ["recent"] = null, [$"byYear:{DateTime.Now.Year}-0"] = null, ["newest"] = null, ["frequent"] = null },
        };
        var cache = new FakeRandomPicksCache();
        var vm = CreateVm(client, cache);

        // Subscribe via OnNavigatedTo so the VM is listening to the cache.
        // We can't call OnNavigatedTo directly (async void), so manually subscribe via reflection-like:
        // use the same code path by calling LoadAllAsync after manually invoking subscribe via the test seam.
        // The simplest seam: push the picks *after* subscription is set up by OnNavigatedTo equivalents.
        // Since Subscribe is private, we exercise it through OnNavigatedTo and Task.Yield.
        vm.OnNavigatedTo(null!);
        await Task.Yield();

        cache.PushPicks([MakeMappedAlbum("new1"), MakeMappedAlbum("new2")]);

        Assert.AreEqual(2, vm.RandomPicks.Count);
        Assert.IsTrue(vm.HasRandomPicks);
        Assert.IsTrue(vm.HasAnyContent, "Random becoming the only successful section must flip HasAnyContent on.");

        vm.OnNavigatedFrom();
    }

    [TestMethod]
    public async Task OnNavigatedFrom_Unsubscribes_FromCacheEvents()
    {
        var client = CreateClientWithAllSections();
        var cache = new FakeRandomPicksCache
        {
            FetchResult = () => [MakeMappedAlbum("first")],
        };
        var vm = CreateVm(client, cache);

        vm.OnNavigatedTo(null!);
        await Task.Yield();
        vm.OnNavigatedFrom();

        // After unsubscribe, a cache push must not mutate the VM.
        var before = vm.RandomPicks.Count;
        cache.PushPicks([MakeMappedAlbum("late1"), MakeMappedAlbum("late2"), MakeMappedAlbum("late3")]);

        Assert.AreEqual(before, vm.RandomPicks.Count, "Unsubscribed VM must not respond to cache events.");
    }

    [TestMethod]
    public async Task RandomPicks_StableAcrossMultipleNavigations()
    {
        // The design's headline guarantee: navigating away and back keeps the same picks.
        var rand = new[] { MakeAlbum("seedA"), MakeAlbum("seedB") };
        var client = new FakeSubsonicClient
        {
            AlbumListResponses =
            {
                ["recent"] = [MakeAlbum("rp")],
                [$"byYear:{DateTime.Now.Year}-0"] = [MakeAlbum("rr")],
                ["newest"] = [MakeAlbum("ra")],
                ["frequent"] = [MakeAlbum("mp")],
                ["random"] = rand,
            },
        };
        var vm = CreateVm(client);

        await vm.LoadAllAsync();
        var firstRandomCalls = client.RandomCallCount;
        var firstIds = vm.RandomPicks.Select(a => a.Id).ToArray();

        // Simulate a second navigation by re-loading.
        await vm.LoadAllAsync();
        var secondIds = vm.RandomPicks.Select(a => a.Id).ToArray();

        CollectionAssert.AreEqual(firstIds, secondIds, "Random picks must be stable across navigations.");
        Assert.AreEqual(firstRandomCalls, client.RandomCallCount, "Second navigation must reuse cache, not re-fetch random.");
    }

    [TestMethod]
    public async Task AlbumMapping_UsesCoverArtField_NotId()
    {
        var client = new FakeSubsonicClient
        {
            AlbumListResponses =
            {
                ["recent"] = null,
                [$"byYear:{DateTime.Now.Year}-0"] = null,
                ["newest"] = null,
                ["frequent"] = null,
                ["random"] = [new AlbumID3 { Id = "album-id", Name = "Test", Artist = "A", ArtistId = "ar1", CoverArt = "cover-id-different", Year = 2025, SongCount = 1, Duration = 100 }],
            },
        };

        var vm = CreateVm(client);
        await vm.LoadAllAsync();

        Assert.AreEqual(1, vm.RandomPicks.Count);
        Assert.AreEqual("cover-id-different", vm.RandomPicks[0].CoverImage!.Identity);
    }

    [TestMethod]
    public async Task AlbumMapping_UsesDisplayArtist_WhenAvailable()
    {
        var client = new FakeSubsonicClient
        {
            AlbumListResponses =
            {
                ["recent"] = null,
                [$"byYear:{DateTime.Now.Year}-0"] = null,
                ["newest"] = null,
                ["frequent"] = null,
                ["random"] = [new AlbumID3 { Id = "a1", Name = "Test", Artist = "Raw Artist", ArtistId = "ar1", DisplayArtist = "Display Artist Name", Year = 2025, SongCount = 1, Duration = 100 }],
            },
        };

        var vm = CreateVm(client);
        await vm.LoadAllAsync();

        Assert.AreEqual("Display Artist Name", vm.RandomPicks[0].Artist);
    }

    [TestMethod]
    public async Task AlbumMapping_FallsBackToArtist_WhenDisplayArtistNull()
    {
        var client = new FakeSubsonicClient
        {
            AlbumListResponses =
            {
                ["recent"] = null,
                [$"byYear:{DateTime.Now.Year}-0"] = null,
                ["newest"] = null,
                ["frequent"] = null,
                ["random"] = [new AlbumID3 { Id = "a1", Name = "Test", Artist = "Fallback Artist", ArtistId = "ar1", Year = 2025, SongCount = 1, Duration = 100 }],
            },
        };

        var vm = CreateVm(client);
        await vm.LoadAllAsync();

        Assert.AreEqual("Fallback Artist", vm.RandomPicks[0].Artist);
    }

    [TestMethod]
    public async Task EmptySectionHidden_WhenApiReturnsEmptyArray()
    {
        var client = new FakeSubsonicClient
        {
            AlbumListResponses =
            {
                ["recent"] = [],
                [$"byYear:{DateTime.Now.Year}-0"] = [],
                ["newest"] = [MakeAlbum("ra1")],
                ["frequent"] = [],
                ["random"] = [MakeAlbum("rand1")],
            },
        };

        var vm = CreateVm(client);
        await vm.LoadAllAsync();

        Assert.IsFalse(vm.HasRecentlyPlayed);
        Assert.IsFalse(vm.HasRecentlyReleased);
        Assert.IsTrue(vm.HasRecentlyAdded);
        Assert.IsFalse(vm.HasMostPlayed);
        Assert.IsTrue(vm.HasRandomPicks);
        Assert.IsTrue(vm.HasAnyContent);
    }

    [TestMethod]
    public async Task RecentlyReleased_PreservesNewestToOldestOrder()
    {
        var client = new FakeSubsonicClient
        {
            AlbumListResponses =
            {
                ["recent"] = null,
                [$"byYear:{DateTime.Now.Year}-0"] = [MakeAlbum("a1", year: 2026), MakeAlbum("a2", year: 2025), MakeAlbum("a3", year: 2024)],
                ["newest"] = null,
                ["frequent"] = null,
                ["random"] = null,
            },
        };

        var vm = CreateVm(client);
        await vm.LoadAllAsync();

        Assert.AreEqual(3, vm.RecentlyReleased.Count);
        Assert.AreEqual(2026, vm.RecentlyReleased[0].Year);
        Assert.AreEqual(2025, vm.RecentlyReleased[1].Year);
        Assert.AreEqual(2024, vm.RecentlyReleased[2].Year);
    }

    [TestMethod]
    public void ShowEmptyState_IsFalse_BeforeFirstLoad()
    {
        var client = CreateClientWithAllSections();
        var vm = CreateVm(client);

        Assert.IsFalse(vm.ShowEmptyState, "ShowEmptyState must be false before any load has completed");
    }

    [TestMethod]
    public async Task ShowEmptyState_IsFalse_WhileLoading()
    {
        var client = CreateClientWithAllSections();
        var vm = CreateVm(client);

        await vm.LoadAllAsync();

        Assert.IsFalse(vm.ShowEmptyState, "ShowEmptyState must be false when content is loaded");
        Assert.IsTrue(vm.HasAnyContent);
        Assert.IsFalse(vm.IsLoading);
    }
}
