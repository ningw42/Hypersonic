using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;
using Hypersonic.Helpers;
using Hypersonic.ViewModels;

using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Hypersonic.Tests.ViewModels;

[TestClass]
public class NowPlayingViewModelTests
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

    private sealed class FakeSubsonicClientService : ISubsonicClientService
    {
        public Hypersonic.Client.ISubsonicClient? Client => null;
        public void CreateClient(ServerCredential credential) { }
        public Task<ConnectionTestResult> TestConnectionAsync(ServerCredential credential)
            => throw new NotImplementedException();
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
        public event EventHandler? TrackChanged;
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

        public void RaiseTrackChanged() => TrackChanged?.Invoke(this, EventArgs.Empty);

        public void Touch()
        {
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
            TrackEnded?.Invoke(this, new TrackEndedEventArgs { TrackId = "" });
            PositionChanged?.Invoke(this, EventArgs.Empty);
            QueueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static PlaybackTrack T(string id, string title = "T", string artist = "A") =>
        new() { Id = id, Title = title, Artist = artist };

    private static NowPlayingViewModel BuildSync(
        FakePlaybackService svc,
        LyricsOutcome structured,
        LyricsOutcome legacy,
        Action<int>? structuredCounter = null,
        Action<int>? legacyCounter = null,
        Action<Action>? enqueueOverride = null)
    {
        StructuredLyricsFetcher s = (id, ct) =>
        {
            structuredCounter?.Invoke(0);
            return Task.FromResult(structured);
        };
        LegacyLyricsFetcher l = (artist, title, ct) =>
        {
            legacyCounter?.Invoke(0);
            return Task.FromResult(legacy);
        };
        return new NowPlayingViewModel(
            svc,
            new FakeSubsonicClientService(),
            new NullLoggingService(),
            synchronous: true,
            enqueueOverride: enqueueOverride,
            structuredOverride: s,
            legacyOverride: l);
    }

    private static NowPlayingViewModel BuildSyncWithFetchers(
        FakePlaybackService svc,
        StructuredLyricsFetcher structured,
        LegacyLyricsFetcher legacy,
        Action<Action>? enqueueOverride = null)
    {
        return new NowPlayingViewModel(
            svc,
            new FakeSubsonicClientService(),
            new NullLoggingService(),
            synchronous: true,
            enqueueOverride: enqueueOverride,
            structuredOverride: structured,
            legacyOverride: legacy);
    }

    [TestMethod]
    public async Task NullTrack_OnNavigatedTo_LandsOnNoTrack()
    {
        var svc = new FakePlaybackService { NowPlaying = null };
        var vm = BuildSync(svc, LyricsOutcome.Content("x"), LyricsOutcome.Transient);

        vm.OnNavigatedTo(null!);

        Assert.AreEqual(LyricsLoadState.NoTrack, vm.LyricsLoadState);
        Assert.IsNull(vm.LyricsText);
        Assert.IsNull(vm.TechnicalDetail);
        if (vm.PendingLyricsFetch != null)
        {
            await vm.PendingLyricsFetch;
        }
        Assert.AreEqual(LyricsLoadState.NoTrack, vm.LyricsLoadState);
    }

    [TestMethod]
    public async Task TrackChanged_ToNull_ClearsAndLandsOnNoTrack()
    {
        var svc = new FakePlaybackService { NowPlaying = T("a") };
        var vm = BuildSync(svc, LyricsOutcome.Content("hello"), LyricsOutcome.Transient);
        vm.OnNavigatedTo(null!);
        if (vm.PendingLyricsFetch != null) await vm.PendingLyricsFetch;
        Assert.AreEqual(LyricsLoadState.Loaded, vm.LyricsLoadState);

        // Track disappears (queue cleared).
        svc.NowPlaying = null;
        svc.RaiseTrackChanged();

        Assert.AreEqual(LyricsLoadState.NoTrack, vm.LyricsLoadState);
        Assert.IsNull(vm.LyricsText);
        Assert.IsNull(vm.TechnicalDetail);
    }

    [TestMethod]
    public async Task CacheHit_NoBlankCached_LandsOnLoaded_AndNoFetch()
    {
        var fetchCount = 0;
        StructuredLyricsFetcher s = (id, ct) =>
        {
            fetchCount++;
            return Task.FromResult(LyricsOutcome.Content("first call"));
        };
        LegacyLyricsFetcher l = (artist, title, ct) => Task.FromResult(LyricsOutcome.Transient);

        var svc = new FakePlaybackService { NowPlaying = T("a") };
        var vm = BuildSyncWithFetchers(svc, s, l);
        vm.OnNavigatedTo(null!);
        await vm.PendingLyricsFetch!;
        Assert.AreEqual("first call", vm.LyricsText);
        Assert.AreEqual(LyricsLoadState.Loaded, vm.LyricsLoadState);
        Assert.AreEqual(1, fetchCount);

        // Switch away and back.
        svc.NowPlaying = T("b");
        svc.RaiseTrackChanged();
        await vm.PendingLyricsFetch!;
        Assert.AreEqual(2, fetchCount);

        var pendingBefore = vm.PendingLyricsFetch;
        svc.NowPlaying = T("a");
        svc.RaiseTrackChanged();
        // Cache hit — no new fetch.
        Assert.AreSame(pendingBefore, vm.PendingLyricsFetch);
        Assert.AreEqual(2, fetchCount);
        Assert.AreEqual("first call", vm.LyricsText);
        Assert.AreEqual(LyricsLoadState.Loaded, vm.LyricsLoadState);
    }

    [TestMethod]
    public async Task CacheHit_BlankCached_LandsOnNoLyrics()
    {
        var svc = new FakePlaybackService { NowPlaying = T("a") };
        // Both definitive empty → cached as null.
        var vm = BuildSync(svc, LyricsOutcome.DefinitiveEmpty, LyricsOutcome.DefinitiveEmpty);
        vm.OnNavigatedTo(null!);
        await vm.PendingLyricsFetch!;
        Assert.AreEqual(LyricsLoadState.NoLyrics, vm.LyricsLoadState);

        // Switch away to b (need a different track to refetch a).
        svc.NowPlaying = T("b");
        svc.RaiseTrackChanged();
        await vm.PendingLyricsFetch!;

        // Switch back to a — cache hit.
        var pendingBefore = vm.PendingLyricsFetch;
        svc.NowPlaying = T("a");
        svc.RaiseTrackChanged();
        Assert.AreSame(pendingBefore, vm.PendingLyricsFetch);
        Assert.AreEqual(LyricsLoadState.NoLyrics, vm.LyricsLoadState);
        Assert.IsTrue(string.IsNullOrWhiteSpace(vm.LyricsText));
    }

    [TestMethod]
    public async Task TransientFailure_NotCached_RetriesNextTime()
    {
        var fetchCount = 0;
        StructuredLyricsFetcher s = (id, ct) =>
        {
            fetchCount++;
            return Task.FromResult(LyricsOutcome.Transient);
        };
        LegacyLyricsFetcher l = (artist, title, ct) => Task.FromResult(LyricsOutcome.Transient);

        var svc = new FakePlaybackService { NowPlaying = T("a") };
        var vm = BuildSyncWithFetchers(svc, s, l);
        vm.OnNavigatedTo(null!);
        await vm.PendingLyricsFetch!;
        Assert.AreEqual(LyricsLoadState.NoLyrics, vm.LyricsLoadState);
        Assert.AreEqual(1, fetchCount);

        // Switch away to b.
        svc.NowPlaying = T("b");
        svc.RaiseTrackChanged();
        await vm.PendingLyricsFetch!;

        // Switch back to a — transient was not cached, so we re-fetch.
        svc.NowPlaying = T("a");
        svc.RaiseTrackChanged();
        await vm.PendingLyricsFetch!;
        Assert.AreEqual(3, fetchCount);
    }

    [TestMethod]
    public async Task TechnicalDetail_PopulatedFromTrackFields()
    {
        var svc = new FakePlaybackService
        {
            NowPlaying = new PlaybackTrack
            {
                Id = "a", Title = "T", Artist = "A",
                Suffix = "flac", BitDepth = 24, SamplingRate = 96000, BitRate = 4608,
            }
        };
        var vm = BuildSync(svc, LyricsOutcome.Content("x"), LyricsOutcome.Transient);
        vm.OnNavigatedTo(null!);
        if (vm.PendingLyricsFetch != null) await vm.PendingLyricsFetch;
        Assert.AreEqual("FLAC · 24-bit/96kHz · 4608 kbps", vm.TechnicalDetail);
    }

    [TestMethod]
    public async Task Stale_FetchGuard_DiscardsOldGenerationResult()
    {
        var tcsA = new TaskCompletionSource<LyricsOutcome>();
        var tcsB = new TaskCompletionSource<LyricsOutcome>();
        StructuredLyricsFetcher s = (id, ct) => id == "a" ? tcsA.Task : tcsB.Task;
        LegacyLyricsFetcher l = (artist, title, ct) => Task.FromResult(LyricsOutcome.Transient);

        var svc = new FakePlaybackService { NowPlaying = T("a") };
        var vm = BuildSyncWithFetchers(svc, s, l);
        vm.OnNavigatedTo(null!);

        // Switch to b before a's fetch completes.
        svc.NowPlaying = T("b");
        svc.RaiseTrackChanged();

        // Now complete a's fetch with content; it must be discarded as stale.
        tcsA.SetResult(LyricsOutcome.Content("a-text"));
        // Then complete b's fetch.
        tcsB.SetResult(LyricsOutcome.Content("b-text"));
        await Task.WhenAll(tcsA.Task, tcsB.Task);
        if (vm.PendingLyricsFetch != null) await vm.PendingLyricsFetch;

        Assert.AreEqual("b-text", vm.LyricsText);
    }

    [TestMethod]
    public async Task VmActive_Guard_DiscardsResponseAfterOnNavigatedFrom()
    {
        var tcs = new TaskCompletionSource<LyricsOutcome>();
        StructuredLyricsFetcher s = (id, ct) => tcs.Task;
        LegacyLyricsFetcher l = (artist, title, ct) => Task.FromResult(LyricsOutcome.Transient);
        var svc = new FakePlaybackService { NowPlaying = T("a") };
        var vm = BuildSyncWithFetchers(svc, s, l);
        vm.OnNavigatedTo(null!);

        vm.OnNavigatedFrom();
        tcs.SetResult(LyricsOutcome.Content("late"));
        if (vm.PendingLyricsFetch != null) await vm.PendingLyricsFetch;

        // Pre-OnNavigatedFrom state was Loading; the late completion must not flip it.
        Assert.AreEqual(LyricsLoadState.Loading, vm.LyricsLoadState);
        Assert.IsNull(vm.LyricsText);
    }

    [TestMethod]
    public async Task LateEnqueue_AfterOnNavigatedFrom_DoesNotStartFetch()
    {
        var captured = new Queue<Action>();
        Action<Action> enqueue = a => captured.Enqueue(a);

        var fetchCount = 0;
        StructuredLyricsFetcher s = (id, ct) =>
        {
            fetchCount++;
            return Task.FromResult(LyricsOutcome.Content("x"));
        };
        LegacyLyricsFetcher l = (artist, title, ct) => Task.FromResult(LyricsOutcome.Transient);

        var svc = new FakePlaybackService { NowPlaying = T("a") };
        var vm = new NowPlayingViewModel(
            svc, new FakeSubsonicClientService(), new NullLoggingService(),
            synchronous: false, enqueueOverride: enqueue, structuredOverride: s, legacyOverride: l);

        vm.OnNavigatedTo(null!);
        if (vm.PendingLyricsFetch != null) await vm.PendingLyricsFetch;
        var fetchCountAfterInit = fetchCount;

        // Track changes — enqueue captures the body, doesn't run it.
        svc.NowPlaying = T("b");
        svc.RaiseTrackChanged();
        Assert.AreEqual(1, captured.Count);

        vm.OnNavigatedFrom();

        // Now run the late callback. The _vmActive guard inside the lambda
        // must short-circuit, so HandleTrackChange does NOT run and no fetch
        // is kicked off.
        captured.Dequeue()();
        Assert.AreEqual(fetchCountAfterInit, fetchCount);
    }

    [TestMethod]
    public async Task FallbackMatrix_StructuredEmpty_LegacyContent_LoadsLegacy()
    {
        var svc = new FakePlaybackService { NowPlaying = T("a") };
        var vm = BuildSync(svc, LyricsOutcome.DefinitiveEmpty, LyricsOutcome.Content("from legacy"));
        vm.OnNavigatedTo(null!);
        await vm.PendingLyricsFetch!;
        Assert.AreEqual("from legacy", vm.LyricsText);
        Assert.AreEqual(LyricsLoadState.Loaded, vm.LyricsLoadState);
    }

    [TestMethod]
    public async Task StructuredContent_DoesNotInvokeLegacy()
    {
        // Design test row (j): structured success with content is the ONLY case
        // where legacy is not called.
        var legacyCalls = 0;
        StructuredLyricsFetcher s = (id, ct) => Task.FromResult(LyricsOutcome.Content("structured text"));
        LegacyLyricsFetcher l = (artist, title, ct) =>
        {
            legacyCalls++;
            return Task.FromResult(LyricsOutcome.Content("legacy text"));
        };

        var svc = new FakePlaybackService { NowPlaying = T("a") };
        var vm = BuildSyncWithFetchers(svc, s, l);
        vm.OnNavigatedTo(null!);
        await vm.PendingLyricsFetch!;

        Assert.AreEqual("structured text", vm.LyricsText);
        Assert.AreEqual(0, legacyCalls);
    }

    [TestMethod]
    public async Task NonContentStructured_AlwaysInvokesLegacy()
    {
        // Sanity check: every non-content structured outcome falls through to legacy.
        // NOTE: each iteration uses a unique track id so the per-VM lyrics cache
        // doesn't short-circuit subsequent iterations and silently mask a regression.
        // (A fresh VM per iteration is also a guard, but the unique id makes the
        // intent explicit and would survive someone refactoring this loop.)
        foreach (var structuredOutcome in new[]
        {
            LyricsOutcome.DefinitiveEmpty,
            LyricsOutcome.DefinitiveUnsupported,
            LyricsOutcome.Transient,
        })
        {
            var legacyCalls = 0;
            StructuredLyricsFetcher s = (id, ct) => Task.FromResult(structuredOutcome);
            LegacyLyricsFetcher l = (artist, title, ct) =>
            {
                legacyCalls++;
                return Task.FromResult(LyricsOutcome.DefinitiveEmpty);
            };

            var svc = new FakePlaybackService { NowPlaying = T($"id-{structuredOutcome.Kind}") };
            var vm = BuildSyncWithFetchers(svc, s, l);
            vm.OnNavigatedTo(null!);
            await vm.PendingLyricsFetch!;

            Assert.AreEqual(1, legacyCalls,
                $"Legacy should have been called exactly once for structured kind {structuredOutcome.Kind}");
        }
    }

    [TestMethod]
    public async Task SelectedTab_DefaultsToLyrics_OnNavigation()
    {
        // Design "Tab default" row — each navigation lands on Lyrics.
        var svc = new FakePlaybackService { NowPlaying = T("a") };
        var vm = BuildSync(svc, LyricsOutcome.Content("x"), LyricsOutcome.Transient);

        // Even after a manual switch to Queue, a fresh OnNavigatedTo + fresh VM
        // (the design specifies a new VM per navigation) lands on Lyrics.
        Assert.AreEqual(NowPlayingTab.Lyrics, vm.SelectedTab);

        vm.OnNavigatedTo(null!);
        if (vm.PendingLyricsFetch != null) await vm.PendingLyricsFetch;
        Assert.AreEqual(NowPlayingTab.Lyrics, vm.SelectedTab);
    }
}
