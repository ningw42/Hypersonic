using Hypersonic.Client;
using Hypersonic.Client.Models.Responses;
using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;
using Hypersonic.Services;
using Hypersonic.Tests.Fakes;

using Microsoft.Extensions.Time.Testing;

namespace Hypersonic.Tests.Services;

[TestClass]
public class ScrobblingServiceTests
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

    private sealed class FakeLocalSettingsService : ILocalSettingsService
    {
        private readonly Dictionary<string, object?> _store = new();

        public Task<T?> ReadSettingAsync<T>(string key) =>
            Task.FromResult(_store.TryGetValue(key, out var val) ? (T?)val : default(T?));

        public Task SaveSettingAsync<T>(string key, T value)
        {
            _store[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveSettingAsync(string key)
        {
            _store.Remove(key);
            return Task.CompletedTask;
        }

        public void Set(string key, object value) => _store[key] = value;
    }

    private sealed class RecordingSubsonicClient : ISubsonicClient
    {
        public System.Collections.Concurrent.ConcurrentQueue<(string Id, ulong? Time, bool Submission)> ScrobbleCalls { get; } = [];

        public Task<SubsonicResponse> ScrobbleAsync(string id, ulong? time = null, bool submission = true)
        {
            ScrobbleCalls.Enqueue((id, time, submission));
            return Task.FromResult(new SubsonicResponse());
        }

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
        public Uri GetStreamUri(string id, int? maxBitrate = null, string? format = null, int? timeOffset = null, string? size = null, bool estimateContentLength = false, bool converted = false) => throw new NotImplementedException();
        public Task<Stream> DownloadAsync(string id) => throw new NotImplementedException();
        public Task<Stream> GetCoverArtAsync(string id, string? size = null) => throw new NotImplementedException();
        public Uri GetCoverArtUri(string id, string? size = null) => throw new NotImplementedException();
        public Task<GetLyricsResponse> GetLyricsAsync(string? artist = null, string? title = null) => throw new NotImplementedException();
        public Task<GetLyricsBySongIdResponse> GetLyricsBySongIdAsync(string id) => throw new NotImplementedException();
        public Task<Stream> GetAvatarAsync(string username) => throw new NotImplementedException();
        public Uri GetAvatarUri(string username) => throw new NotImplementedException();
        public Task<SubsonicResponse> StarAsync(string? id = null, string? albumId = null, string? artistId = null) => throw new NotImplementedException();
        public Task<SubsonicResponse> UnstarAsync(string? id = null, string? albumId = null, string? artistId = null) => throw new NotImplementedException();
        public Task<SubsonicResponse> SetRatingAsync(string id, int rating) => throw new NotImplementedException();
        public Task<GetUserResponse> GetUserAsync(string username) => throw new NotImplementedException();
        public Task<GetUsersResponse> GetUsersAsync() => throw new NotImplementedException();
        public Task<GetScanStatusResponse> GetScanStatusAsync() => throw new NotImplementedException();
        public Task<StartScanResponse> StartScanAsync() => throw new NotImplementedException();
    }

    private sealed class FakeSubsonicClientService : ISubsonicClientService
    {
        public ISubsonicClient? Client { get; set; }
        public string? ActiveUserIdentity { get; set; }
        public void CreateClient(ServerCredential credential) { }
        public Task<ConnectionTestResult> TestConnectionAsync(ServerCredential credential) => throw new NotImplementedException();
    }

    private static PlaybackTrack MakeTrack(string id = "t1", int duration = 240) => new()
    {
        Id = id,
        Title = "Test",
        Artist = "Artist",
        ArtistId = "a1",
        Album = "Album",
        AlbumId = "al1",
        Duration = duration,
    };

    private FakePlaybackService _playback = null!;
    private RecordingSubsonicClient _subsonicClient = null!;
    private FakeSubsonicClientService _clientService = null!;
    private FakeLocalSettingsService _settings = null!;
    private FakeTimeProvider _time = null!;
    private ScrobblingService _sut = null!;

    [TestInitialize]
    public void Setup()
    {
        _playback = new FakePlaybackService();
        _subsonicClient = new RecordingSubsonicClient();
        _clientService = new FakeSubsonicClientService { Client = _subsonicClient };
        _settings = new FakeLocalSettingsService();
        _time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        _sut = new ScrobblingService(
            _playback, _clientService, _settings, new NullLoggingService(), _time);
    }

    private async Task InitEnabled(
        int playtime = 30, int percentage = 50,
        ScrobbleConditionMode mode = ScrobbleConditionMode.Or,
        bool nowPlaying = true)
    {
        _settings.Set("ScrobblingEnabled", true);
        _settings.Set("ScrobbleNowPlaying", nowPlaying);
        _settings.Set("ScrobblePlaytimeThreshold", playtime);
        _settings.Set("ScrobblePercentageThreshold", percentage);
        _settings.Set("ScrobbleConditionMode", mode);
        await _sut.InitializeAsync();
    }

    private void SimulateTrackStart(string trackId = "t1", int duration = 240)
    {
        _playback.NowPlaying = MakeTrack(trackId, duration);
        _playback.Duration = duration;
        _playback.IsPlaying = true;
        _playback.RaiseTrackChanged();
        _playback.RaisePlaybackStateChanged();
    }

    private void AdvanceAndTick(double seconds)
    {
        _time.Advance(TimeSpan.FromSeconds(seconds));
        _playback.RaisePositionChanged();
    }

    private async Task FlushTasks()
    {
        await _sut.PendingScrobbleTask;
        await _sut.PendingNowPlayingTask;
    }

    private int ScrobbleCount => _subsonicClient.ScrobbleCalls.Count(c => c.Submission);
    private int NowPlayingCount => _subsonicClient.ScrobbleCalls.Count(c => !c.Submission);

    // ------------------------------------------------------------------
    // OR mode tests
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task Scrobble_OrMode_PlaytimeFirst()
    {
        await InitEnabled(playtime: 30, percentage: 50, mode: ScrobbleConditionMode.Or);
        SimulateTrackStart(duration: 240);

        AdvanceAndTick(31);
        await FlushTasks();

        Assert.AreEqual(1, ScrobbleCount);
    }

    [TestMethod]
    public async Task Scrobble_OrMode_PercentageFirst()
    {
        await InitEnabled(playtime: 30, percentage: 50, mode: ScrobbleConditionMode.Or);
        SimulateTrackStart(duration: 20);

        AdvanceAndTick(11);
        await FlushTasks();

        Assert.AreEqual(1, ScrobbleCount);
    }

    // ------------------------------------------------------------------
    // AND mode tests
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task Scrobble_AndMode_BothRequired()
    {
        await InitEnabled(playtime: 30, percentage: 50, mode: ScrobbleConditionMode.And);
        SimulateTrackStart(duration: 240);

        AdvanceAndTick(31);
        await FlushTasks();
        Assert.AreEqual(0, ScrobbleCount, "Playtime met but percentage not yet");

        AdvanceAndTick(90);
        await FlushTasks();
        Assert.AreEqual(1, ScrobbleCount, "Both met at 121s");
    }

    [TestMethod]
    public async Task Scrobble_AndMode_CompletionFailsafe()
    {
        await InitEnabled(playtime: 30, percentage: 50, mode: ScrobbleConditionMode.And);
        SimulateTrackStart(duration: 20);

        AdvanceAndTick(20);
        _playback.IsPlaying = false;
        _playback.RaisePlaybackStateChanged();
        _playback.RaiseTrackEnded("t1", 20);
        await FlushTasks();

        Assert.AreEqual(1, ScrobbleCount);
    }

    [TestMethod]
    public async Task Scrobble_AndMode_SeekAbuse_NoScrobble()
    {
        await InitEnabled(playtime: 30, percentage: 50, mode: ScrobbleConditionMode.And);
        SimulateTrackStart(duration: 240);

        AdvanceAndTick(31);
        await FlushTasks();
        Assert.AreEqual(0, ScrobbleCount);

        _playback.IsPlaying = false;
        _playback.RaisePlaybackStateChanged();
        _playback.RaiseTrackEnded("t1", 240);
        await FlushTasks();

        Assert.AreEqual(0, ScrobbleCount, "Playtime 31s ✓ but percentage 12.9% < 50% ✗ → AND blocks");
    }

    [TestMethod]
    public async Task Scrobble_LongTrack_NoToleranceOnPlaytime()
    {
        await InitEnabled(playtime: 30, percentage: 50, mode: ScrobbleConditionMode.Or);
        SimulateTrackStart(duration: 240);

        AdvanceAndTick(29);
        _playback.IsPlaying = false;
        _playback.RaisePlaybackStateChanged();
        _playback.RaiseTrackEnded("t1", 240);
        await FlushTasks();

        Assert.AreEqual(0, ScrobbleCount, "29s < 30s threshold, no tolerance for long tracks");
    }

    [TestMethod]
    public async Task Scrobble_SeekNearEnd_NoFalseScrobble()
    {
        await InitEnabled(playtime: 30, percentage: 50, mode: ScrobbleConditionMode.Or);
        SimulateTrackStart(duration: 240);

        AdvanceAndTick(2);
        _playback.IsPlaying = false;
        _playback.RaisePlaybackStateChanged();
        _playback.RaiseTrackEnded("t1", 240);
        await FlushTasks();

        Assert.AreEqual(0, ScrobbleCount, "2s playtime, 0.8% → no scrobble");
    }

    // ------------------------------------------------------------------
    // At-most-once guard
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task Scrobble_AtMostOnce()
    {
        await InitEnabled(playtime: 15, percentage: 10, mode: ScrobbleConditionMode.Or);
        SimulateTrackStart(duration: 240);

        AdvanceAndTick(16);
        await FlushTasks();
        Assert.AreEqual(1, ScrobbleCount);

        AdvanceAndTick(100);
        await FlushTasks();
        Assert.AreEqual(1, ScrobbleCount, "Should not double-scrobble");
    }

    // ------------------------------------------------------------------
    // Skip without scrobble
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task Scrobble_SkipNoScrobble()
    {
        await InitEnabled(playtime: 30, percentage: 50, mode: ScrobbleConditionMode.Or);
        SimulateTrackStart(duration: 240);

        AdvanceAndTick(5);
        await FlushTasks();

        _playback.NowPlaying = MakeTrack("t2", 180);
        _playback.Duration = 180;
        _playback.RaiseTrackChanged();
        await FlushTasks();

        Assert.AreEqual(0, ScrobbleCount, "Skip after 5s should not scrobble");
    }

    // ------------------------------------------------------------------
    // Disabled behavior
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task Scrobble_Disabled_NoScrobble()
    {
        _settings.Set("ScrobblingEnabled", false);
        await _sut.InitializeAsync();

        SimulateTrackStart(duration: 60);
        AdvanceAndTick(61);
        await FlushTasks();

        Assert.AreEqual(0, ScrobbleCount);
        Assert.AreEqual(0, NowPlayingCount);
    }

    [TestMethod]
    public async Task Scrobble_DisabledMidTrack_NoScrobble()
    {
        await InitEnabled(playtime: 30, percentage: 50, mode: ScrobbleConditionMode.Or);
        SimulateTrackStart(duration: 240);

        AdvanceAndTick(20);
        await FlushTasks();
        Assert.AreEqual(0, ScrobbleCount);

        await _sut.SetEnabledAsync(false);

        AdvanceAndTick(20);
        await FlushTasks();
        Assert.AreEqual(0, ScrobbleCount, "Disabling mid-track resets session");
    }

    [TestMethod]
    public async Task Scrobble_DisableCancelsInFlightNowPlaying()
    {
        await InitEnabled();
        SimulateTrackStart(duration: 240);
        await FlushTasks();
        var npBefore = NowPlayingCount;

        await _sut.SetEnabledAsync(false);

        _playback.NowPlaying = MakeTrack("t2", 120);
        _playback.Duration = 120;
        _playback.IsPlaying = true;
        _playback.RaiseTrackChanged();
        _playback.RaisePlaybackStateChanged();
        await FlushTasks();

        Assert.AreEqual(npBefore, NowPlayingCount, "No Now Playing sent while disabled");
        Assert.AreEqual(0, _subsonicClient.ScrobbleCalls.Count(c => c.Submission),
            "No scrobble sent while disabled");
    }

    [TestMethod]
    public async Task Scrobble_DisableReEnable_SessionReset()
    {
        await InitEnabled(playtime: 15, percentage: 50, mode: ScrobbleConditionMode.Or);
        SimulateTrackStart(duration: 240);

        AdvanceAndTick(20);
        await FlushTasks();
        Assert.AreEqual(1, ScrobbleCount);

        await _sut.SetEnabledAsync(false);
        await _sut.SetEnabledAsync(true);

        AdvanceAndTick(10);
        await FlushTasks();
        Assert.AreEqual(1, ScrobbleCount, "Must re-earn threshold after re-enable");
    }

    [TestMethod]
    public async Task Scrobble_DisableReEnable_NewSessionCanScrobbleAgain()
    {
        await InitEnabled(playtime: 15, percentage: 50, mode: ScrobbleConditionMode.Or);
        SimulateTrackStart(duration: 240);

        AdvanceAndTick(20);
        await FlushTasks();
        Assert.AreEqual(1, ScrobbleCount);

        await _sut.SetEnabledAsync(false);
        await _sut.SetEnabledAsync(true);

        AdvanceAndTick(20);
        await FlushTasks();
        Assert.AreEqual(2, ScrobbleCount, "New session after re-enable can scrobble again");
    }

    [TestMethod]
    public async Task Scrobble_DisabledPlayback_NoSegmentStart()
    {
        _settings.Set("ScrobblingEnabled", false);
        await _sut.InitializeAsync();

        SimulateTrackStart(duration: 240);
        AdvanceAndTick(20);
        await FlushTasks();
        Assert.AreEqual(0, ScrobbleCount);

        await _sut.SetEnabledAsync(true);
        AdvanceAndTick(10);
        await FlushTasks();
        Assert.AreEqual(0, ScrobbleCount, "Fresh segment starts from re-enable, 10s < 30s");

        AdvanceAndTick(25);
        await FlushTasks();
        Assert.AreEqual(1, ScrobbleCount, "35s total after enable ≥ 30s threshold");
    }

    // ------------------------------------------------------------------
    // Duration unknown / updated later
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task Scrobble_DurationUnknown_FailsafeUsesFullThreshold()
    {
        await InitEnabled(playtime: 30, percentage: 50, mode: ScrobbleConditionMode.Or);

        _playback.NowPlaying = MakeTrack("t1", 0);
        _playback.Duration = 0;
        _playback.IsPlaying = true;
        _playback.RaiseTrackChanged();
        _playback.RaisePlaybackStateChanged();

        AdvanceAndTick(25);
        _playback.IsPlaying = false;
        _playback.RaisePlaybackStateChanged();
        _playback.RaiseTrackEnded("t1", 0);
        await FlushTasks();

        Assert.AreEqual(0, ScrobbleCount, "25s < 30s and duration unknown → no scrobble");
    }

    [TestMethod]
    public async Task Scrobble_CompletionPercentage100_TimingTolerance()
    {
        await InitEnabled(playtime: 30, percentage: 100, mode: ScrobbleConditionMode.And);
        SimulateTrackStart(duration: 20);

        _time.Advance(TimeSpan.FromSeconds(19.8));
        _playback.IsPlaying = false;
        _playback.RaisePlaybackStateChanged();
        _playback.RaiseTrackEnded("t1", 20);
        await FlushTasks();

        Assert.AreEqual(1, ScrobbleCount,
            "19.8s on 20s track: failsafe playtime=max(19,0)=19 ✓, percentage snaps to 100% ✓");
    }

    [TestMethod]
    public async Task Scrobble_DurationZero_PercentageSkipped()
    {
        await InitEnabled(playtime: 30, percentage: 50, mode: ScrobbleConditionMode.Or);

        _playback.NowPlaying = MakeTrack("t1", 0);
        _playback.Duration = 0;
        _playback.IsPlaying = true;
        _playback.RaiseTrackChanged();
        _playback.RaisePlaybackStateChanged();

        AdvanceAndTick(31);
        await FlushTasks();

        Assert.AreEqual(1, ScrobbleCount, "Playtime threshold met even with unknown duration");
    }

    [TestMethod]
    public async Task Scrobble_DurationUpdatedLater()
    {
        await InitEnabled(playtime: 120, percentage: 50, mode: ScrobbleConditionMode.Or);

        _playback.NowPlaying = MakeTrack("t1", 0);
        _playback.Duration = 0;
        _playback.IsPlaying = true;
        _playback.RaiseTrackChanged();
        _playback.RaisePlaybackStateChanged();

        AdvanceAndTick(10);
        await FlushTasks();
        Assert.AreEqual(0, ScrobbleCount);

        _playback.Duration = 60;
        AdvanceAndTick(21);
        await FlushTasks();

        Assert.AreEqual(1, ScrobbleCount, "After duration updated to 60s, 31s = 51.7% ≥ 50%");
    }

    // ------------------------------------------------------------------
    // Repeat-one
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task Scrobble_RepeatOne_MultipleScrobbles()
    {
        await InitEnabled(playtime: 15, percentage: 50, mode: ScrobbleConditionMode.Or);
        SimulateTrackStart(duration: 20);

        AdvanceAndTick(20);
        _playback.IsPlaying = false;
        _playback.RaisePlaybackStateChanged();
        _playback.RaiseTrackEnded("t1", 20);
        await FlushTasks();
        Assert.AreEqual(1, ScrobbleCount, "First loop scrobble");

        _playback.NowPlaying = MakeTrack("t1", 20);
        _playback.Duration = 20;
        _playback.RaiseTrackChanged();
        _playback.IsPlaying = true;
        _playback.RaisePlaybackStateChanged();

        AdvanceAndTick(20);
        _playback.IsPlaying = false;
        _playback.RaisePlaybackStateChanged();
        _playback.RaiseTrackEnded("t1", 20);
        await FlushTasks();
        Assert.AreEqual(2, ScrobbleCount, "Second loop scrobble");
    }

    // ------------------------------------------------------------------
    // Now Playing tests
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task NowPlaying_SentOnPlaybackConfirmed()
    {
        await InitEnabled();
        SimulateTrackStart(duration: 240);
        await FlushTasks();

        Assert.AreEqual(1, NowPlayingCount);
    }

    [TestMethod]
    public async Task NowPlaying_NotSentOnFailedStream()
    {
        await InitEnabled();

        _playback.NowPlaying = MakeTrack("t1", 240);
        _playback.Duration = 240;
        _playback.IsPlaying = false;
        _playback.RaiseTrackChanged();
        await FlushTasks();

        Assert.AreEqual(0, NowPlayingCount, "Never entered Playing → no Now Playing");
    }

    [TestMethod]
    public async Task NowPlaying_NotSentWhenDisabled()
    {
        await InitEnabled(nowPlaying: false);
        SimulateTrackStart(duration: 240);
        await FlushTasks();

        Assert.AreEqual(0, NowPlayingCount);
    }

    [TestMethod]
    public async Task NowPlaying_SentOncePerSession()
    {
        await InitEnabled();
        SimulateTrackStart(duration: 240);
        await FlushTasks();
        Assert.AreEqual(1, NowPlayingCount);

        _playback.IsPlaying = false;
        _playback.RaisePlaybackStateChanged();

        _playback.IsPlaying = true;
        _playback.RaisePlaybackStateChanged();
        await FlushTasks();

        Assert.AreEqual(1, NowPlayingCount, "Pause/resume should not re-send Now Playing");
    }

    [TestMethod]
    public async Task NowPlaying_SentOnEnableMidTrack()
    {
        _settings.Set("ScrobblingEnabled", false);
        _settings.Set("ScrobbleNowPlaying", true);
        await _sut.InitializeAsync();

        SimulateTrackStart(duration: 240);
        await FlushTasks();
        Assert.AreEqual(0, NowPlayingCount);

        await _sut.SetEnabledAsync(true);
        await FlushTasks();

        Assert.AreEqual(1, NowPlayingCount, "Now Playing sent when enabling mid-track");
    }

    [TestMethod]
    public async Task NowPlaying_SentViaSafetyNet()
    {
        await InitEnabled();

        _playback.NowPlaying = MakeTrack("t1", 240);
        _playback.Duration = 240;
        _playback.IsPlaying = true;
        _playback.RaiseTrackChanged();

        _playback.RaisePositionChanged();
        await FlushTasks();

        Assert.AreEqual(1, NowPlayingCount, "Safety net in OnPositionChanged sends Now Playing");
    }

    // ------------------------------------------------------------------
    // Pause/Resume accumulation
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task Scrobble_PauseResume_AccumulatesCorrectly()
    {
        await InitEnabled(playtime: 30, percentage: 50, mode: ScrobbleConditionMode.Or);
        SimulateTrackStart(duration: 240);

        AdvanceAndTick(15);
        _playback.IsPlaying = false;
        _playback.RaisePlaybackStateChanged();

        _time.Advance(TimeSpan.FromSeconds(60));

        _playback.IsPlaying = true;
        _playback.RaisePlaybackStateChanged();

        AdvanceAndTick(16);
        await FlushTasks();

        Assert.AreEqual(1, ScrobbleCount, "15s + 16s = 31s ≥ 30s, pause time not counted");
    }

    [TestMethod]
    public async Task Scrobble_SeekDoesNotInflate()
    {
        await InitEnabled(playtime: 30, percentage: 50, mode: ScrobbleConditionMode.Or);
        SimulateTrackStart(duration: 240);

        AdvanceAndTick(10);
        await FlushTasks();
        Assert.AreEqual(0, ScrobbleCount);

        AdvanceAndTick(21);
        await FlushTasks();

        Assert.AreEqual(1, ScrobbleCount, "Wall-clock time 31s ≥ 30s");
    }

    // ------------------------------------------------------------------
    // Auto-advance safety net
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task Scrobble_AutoAdvance_SegmentSafetyNet()
    {
        await InitEnabled(playtime: 15, percentage: 50, mode: ScrobbleConditionMode.Or);

        _playback.NowPlaying = MakeTrack("t2", 60);
        _playback.Duration = 60;
        _playback.IsPlaying = true;
        _playback.RaiseTrackChanged();

        // First tick: safety net detects IsPlaying + no active segment → starts segment, returns
        _playback.RaisePositionChanged();

        // Second tick: segment is now active, elapsed time is evaluated
        AdvanceAndTick(16);
        await FlushTasks();

        Assert.AreEqual(1, ScrobbleCount, "PositionChanged safety net started segment");
    }

    // ------------------------------------------------------------------
    // Null client
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task Scrobble_NullClient_NoException()
    {
        _clientService.Client = null;
        await InitEnabled(playtime: 15, percentage: 50, mode: ScrobbleConditionMode.Or);
        SimulateTrackStart(duration: 60);

        AdvanceAndTick(16);
        await FlushTasks();

        Assert.AreEqual(0, _subsonicClient.ScrobbleCalls.Count, "No calls with null client");
    }

    // ------------------------------------------------------------------
    // Settings validation
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task Settings_Validation_InvalidPlaytime()
    {
        _settings.Set("ScrobblingEnabled", true);
        _settings.Set("ScrobblePlaytimeThreshold", 99);
        await _sut.InitializeAsync();

        Assert.AreEqual(30, _sut.PlaytimeThreshold, "Invalid playtime falls back to 30");
    }

    [TestMethod]
    public async Task Settings_Validation_OutOfRangePercentage()
    {
        _settings.Set("ScrobblingEnabled", true);
        _settings.Set("ScrobblePercentageThreshold", 200);
        await _sut.InitializeAsync();

        Assert.AreEqual(50, _sut.PercentageThreshold, "Out-of-range percentage falls back to 50");
    }

    [TestMethod]
    public async Task Settings_Validation_NonStepPercentage()
    {
        _settings.Set("ScrobblingEnabled", true);
        _settings.Set("ScrobblePercentageThreshold", 37);
        await _sut.InitializeAsync();

        Assert.AreEqual(50, _sut.PercentageThreshold, "Non-step percentage falls back to 50");
    }

    [TestMethod]
    public async Task Settings_Validation_InvalidConditionMode()
    {
        _settings.Set("ScrobblingEnabled", true);
        _settings.Set("ScrobbleConditionMode", "invalid");
        await _sut.InitializeAsync();

        Assert.AreEqual(ScrobbleConditionMode.Or, _sut.ConditionMode, "Invalid mode falls back to Or");
    }

    // ------------------------------------------------------------------
    // Idempotent initialization
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task Initialize_Idempotent()
    {
        _settings.Set("ScrobblingEnabled", true);
        var t1 = _sut.InitializeAsync();
        var t2 = _sut.InitializeAsync();
        await t1;
        await t2;

        SimulateTrackStart(duration: 60);
        AdvanceAndTick(31);
        await FlushTasks();

        Assert.AreEqual(1, ScrobbleCount, "No duplicate scrobbles from double-init");
    }

    // ------------------------------------------------------------------
    // Rapid skip → only latest Now Playing
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task NowPlaying_RapidSkip_OnlyLatestSent()
    {
        await InitEnabled();

        _playback.NowPlaying = MakeTrack("t1", 60);
        _playback.Duration = 60;
        _playback.IsPlaying = true;
        _playback.RaiseTrackChanged();
        _playback.RaisePlaybackStateChanged();

        _playback.NowPlaying = MakeTrack("t2", 60);
        _playback.Duration = 60;
        _playback.RaiseTrackChanged();
        _playback.RaisePlaybackStateChanged();

        _playback.NowPlaying = MakeTrack("t3", 60);
        _playback.Duration = 60;
        _playback.RaiseTrackChanged();
        _playback.RaisePlaybackStateChanged();

        await FlushTasks();

        var nowPlayingCalls = _subsonicClient.ScrobbleCalls.Where(c => !c.Submission).ToList();
        Assert.IsTrue(nowPlayingCalls.Count >= 1);
        Assert.AreEqual("t3", nowPlayingCalls.Last().Id, "Final Now Playing should be for the last track");
    }

    // ------------------------------------------------------------------
    // Setter validation
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task Setter_InvalidPlaytime_Rejected()
    {
        await InitEnabled(playtime: 30);
        await _sut.SetPlaytimeThresholdAsync(99);
        Assert.AreEqual(30, _sut.PlaytimeThreshold, "Invalid playtime rejected");
    }
}
