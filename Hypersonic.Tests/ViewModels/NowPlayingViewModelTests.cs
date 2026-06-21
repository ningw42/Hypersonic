using Hypersonic.Contracts.Services;
using Hypersonic.Core.Models;
using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Helpers;
using Hypersonic.Models;
using Hypersonic.Tests.Fakes;
using Hypersonic.ViewModels;

using Microsoft.Extensions.Options;

namespace Hypersonic.Tests.ViewModels;

[TestClass]
public class NowPlayingViewModelTests
{
    private sealed class FakeLyricsService : ILyricsService
    {
        private readonly Func<LyricsQuery, Task<LyricsDocument?>> _handler;

        public FakeLyricsService(Func<LyricsQuery, Task<LyricsDocument?>> handler) => _handler = handler;

        public int CallCount { get; private set; }
        public LyricsQuery? LastQuery { get; private set; }
        public TimeSpan? LastBudget { get; private set; }
        public bool LastForceRefresh { get; private set; }

        public Task<LyricsDocument?> GetLyricsAsync(
            LyricsQuery query, TimeSpan? budget = null, bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastQuery = query;
            LastBudget = budget;
            LastForceRefresh = forceRefresh;
            return _handler(query);
        }

        public Task ReconcileAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static PlaybackTrack T(string id, string title = "T", string artist = "A") =>
        new() { Id = id, Title = title, Artist = artist };

    private static LyricsDocument LineDoc(params string[] lines) => new()
    {
        SyncKind = LyricsSyncKind.Line,
        Lines = lines.Select((t, i) => new LyricsLine { StartMs = i * 1000, Text = t }).ToList(),
    };

    private static LyricsDocument WordDoc(params string[] lines) => new()
    {
        SyncKind = LyricsSyncKind.Word,
        Lines = lines.Select((t, i) => new LyricsLine
        {
            StartMs = i * 1000,
            Text = t,
            Words = new[] { new LyricsWord { StartMs = i * 1000, EndMs = i * 1000 + 500, Text = t } },
        }).ToList(),
    };

    private static readonly IOptions<LyricsOrchestrationOptions> Opts =
        Options.Create(new LyricsOrchestrationOptions());

    private static NowPlayingViewModel Build(FakePlaybackService svc, Func<LyricsQuery, Task<LyricsDocument?>> handler, Action<Action>? enqueueOverride = null)
        => new(svc, new FakeLyricsService(handler), Opts, new NullLoggingService(), synchronous: true, enqueueOverride: enqueueOverride);

    private static NowPlayingViewModel Build(FakePlaybackService svc, FakeLyricsService service, Action<Action>? enqueueOverride = null)
        => new(svc, service, Opts, new NullLoggingService(), synchronous: true, enqueueOverride: enqueueOverride);

    [TestMethod]
    public async Task NullTrack_OnNavigatedTo_LandsOnNoTrack()
    {
        var svc = new FakePlaybackService { NowPlaying = null };
        var vm = Build(svc, _ => Task.FromResult<LyricsDocument?>(LineDoc("x")));

        vm.OnNavigatedTo(null!);

        Assert.AreEqual(LyricsLoadState.NoTrack, vm.LyricsLoadState);
        Assert.IsNull(vm.CurrentLyrics);
        Assert.IsNull(vm.TechnicalDetail);
        if (vm.PendingLyricsFetch != null) await vm.PendingLyricsFetch;
    }

    [TestMethod]
    public async Task TrackWithDocument_LandsOnLoaded_SetsCurrentLyrics()
    {
        var svc = new FakePlaybackService { NowPlaying = T("a") };
        var doc = LineDoc("line one", "line two");
        var vm = Build(svc, _ => Task.FromResult<LyricsDocument?>(doc));

        vm.OnNavigatedTo(null!);
        await vm.PendingLyricsFetch!;

        Assert.AreEqual(LyricsLoadState.Loaded, vm.LyricsLoadState);
        Assert.AreSame(doc, vm.CurrentLyrics);
    }

    [TestMethod]
    public async Task NullResult_LandsOnNoLyrics()
    {
        var svc = new FakePlaybackService { NowPlaying = T("a") };
        var vm = Build(svc, _ => Task.FromResult<LyricsDocument?>(null));

        vm.OnNavigatedTo(null!);
        await vm.PendingLyricsFetch!;

        Assert.AreEqual(LyricsLoadState.NoLyrics, vm.LyricsLoadState);
        Assert.IsNull(vm.CurrentLyrics);
    }

    [TestMethod]
    public async Task NoneSyncKind_LandsOnNoLyrics()
    {
        var svc = new FakePlaybackService { NowPlaying = T("a") };
        var vm = Build(svc, _ => Task.FromResult<LyricsDocument?>(LyricsDocument.Empty()));

        vm.OnNavigatedTo(null!);
        await vm.PendingLyricsFetch!;

        Assert.AreEqual(LyricsLoadState.NoLyrics, vm.LyricsLoadState);
        Assert.IsNull(vm.CurrentLyrics);
    }

    [TestMethod]
    public async Task BuildsQueryFromTrackFields()
    {
        var svc = new FakePlaybackService
        {
            NowPlaying = new PlaybackTrack { Id = "song-1", Title = "Title", Artist = "Artist", Album = "Album", Duration = 210 },
        };
        var service = new FakeLyricsService(_ => Task.FromResult<LyricsDocument?>(null));
        var vm = Build(svc, service);

        vm.OnNavigatedTo(null!);
        await vm.PendingLyricsFetch!;

        Assert.IsNotNull(service.LastQuery);
        Assert.AreEqual("song-1", service.LastQuery!.SongId);
        Assert.AreEqual("Artist", service.LastQuery.Artist);
        Assert.AreEqual("Title", service.LastQuery.Title);
        Assert.AreEqual("Album", service.LastQuery.Album);
        Assert.AreEqual(210, service.LastQuery.DurationSeconds);
    }

    [TestMethod]
    public async Task TrackChanged_ToNull_ClearsAndLandsOnNoTrack()
    {
        var svc = new FakePlaybackService { NowPlaying = T("a") };
        var vm = Build(svc, _ => Task.FromResult<LyricsDocument?>(LineDoc("hi")));
        vm.OnNavigatedTo(null!);
        await vm.PendingLyricsFetch!;
        Assert.AreEqual(LyricsLoadState.Loaded, vm.LyricsLoadState);

        svc.NowPlaying = null;
        svc.RaiseTrackChanged();

        Assert.AreEqual(LyricsLoadState.NoTrack, vm.LyricsLoadState);
        Assert.IsNull(vm.CurrentLyrics);
        Assert.IsNull(vm.TechnicalDetail);
    }

    [TestMethod]
    public async Task TechnicalDetail_PopulatedFromTrackFields()
    {
        var svc = new FakePlaybackService
        {
            NowPlaying = new PlaybackTrack { Id = "a", Suffix = "flac", BitDepth = 24, SamplingRate = 96000, BitRate = 4608 },
        };
        var vm = Build(svc, _ => Task.FromResult<LyricsDocument?>(LineDoc("x")));

        vm.OnNavigatedTo(null!);
        await vm.PendingLyricsFetch!;

        Assert.AreEqual("FLAC · 24-bit/96kHz · 4608 kbps", vm.TechnicalDetail);
    }

    [TestMethod]
    public async Task SelectedTab_DefaultsToLyrics()
    {
        var svc = new FakePlaybackService { NowPlaying = T("a") };
        var vm = Build(svc, _ => Task.FromResult<LyricsDocument?>(LineDoc("x")));

        Assert.AreEqual(NowPlayingTab.Lyrics, vm.SelectedTab);
        vm.OnNavigatedTo(null!);
        await vm.PendingLyricsFetch!;
        Assert.AreEqual(NowPlayingTab.Lyrics, vm.SelectedTab);
    }

    [TestMethod]
    public async Task ServiceThrows_LandsOnNoLyrics()
    {
        var svc = new FakePlaybackService { NowPlaying = T("a") };
        var vm = Build(svc, _ => throw new InvalidOperationException("boom"));

        vm.OnNavigatedTo(null!);
        await vm.PendingLyricsFetch!;

        Assert.AreEqual(LyricsLoadState.NoLyrics, vm.LyricsLoadState);
        Assert.IsNull(vm.CurrentLyrics);
    }

    [TestMethod]
    public async Task Stale_FetchGuard_DiscardsOldGenerationResult()
    {
        var tcsA = new TaskCompletionSource<LyricsDocument?>();
        var tcsB = new TaskCompletionSource<LyricsDocument?>();
        var svc = new FakePlaybackService { NowPlaying = T("a") };
        var vm = Build(svc, q => q.SongId == "a" ? tcsA.Task : tcsB.Task);
        vm.OnNavigatedTo(null!);

        // Switch to b before a's fetch completes.
        svc.NowPlaying = T("b");
        svc.RaiseTrackChanged();

        tcsA.SetResult(LineDoc("a-text"));   // stale — must be discarded
        tcsB.SetResult(LineDoc("b-text"));
        await vm.PendingLyricsFetch!;

        Assert.AreEqual("b-text", vm.CurrentLyrics!.Lines[0].Text);
    }

    [TestMethod]
    public async Task VmActive_Guard_DiscardsResponseAfterOnNavigatedFrom()
    {
        var tcs = new TaskCompletionSource<LyricsDocument?>();
        var svc = new FakePlaybackService { NowPlaying = T("a") };
        var vm = Build(svc, _ => tcs.Task);
        vm.OnNavigatedTo(null!);

        vm.OnNavigatedFrom();
        tcs.SetResult(LineDoc("late"));
        await vm.PendingLyricsFetch!;

        // Pre-OnNavigatedFrom state was Loading; the late completion must not flip it.
        Assert.AreEqual(LyricsLoadState.Loading, vm.LyricsLoadState);
        Assert.IsNull(vm.CurrentLyrics);
    }

    [TestMethod]
    public async Task LateEnqueue_AfterOnNavigatedFrom_DoesNotStartFetch()
    {
        var captured = new Queue<Action>();
        var service = new FakeLyricsService(_ => Task.FromResult<LyricsDocument?>(LineDoc("x")));
        var svc = new FakePlaybackService { NowPlaying = T("a") };
        var vm = new NowPlayingViewModel(svc, service, Opts, new NullLoggingService(), synchronous: false, enqueueOverride: a => captured.Enqueue(a));

        vm.OnNavigatedTo(null!);
        if (vm.PendingLyricsFetch != null) await vm.PendingLyricsFetch;
        var callsAfterInit = service.CallCount;

        svc.NowPlaying = T("b");
        svc.RaiseTrackChanged();
        Assert.AreEqual(1, captured.Count);

        vm.OnNavigatedFrom();

        // The _vmActive guard inside the queued lambda must short-circuit — no fetch starts.
        captured.Dequeue()();
        Assert.AreEqual(callsAfterInit, service.CallCount);
    }

    [TestMethod]
    public void Proxies_SeedFromPlaybackService_OnNavigatedTo()
    {
        var svc = new FakePlaybackService
        {
            NowPlaying = T("a"), Position = 12.5, Duration = 200, IsPlaying = true,
        };
        var vm = Build(svc, _ => Task.FromResult<LyricsDocument?>(LineDoc("x")));

        vm.OnNavigatedTo(null!);

        Assert.AreEqual(12500.0, vm.PositionMs);
        Assert.AreEqual(200000.0, vm.TrackDurationMs);
        Assert.IsTrue(vm.IsPlaying);
    }

    [TestMethod]
    public void PositionChanged_UpdatesPositionMs()
    {
        var svc = new FakePlaybackService { NowPlaying = T("a"), Position = 0 };
        var vm = Build(svc, _ => Task.FromResult<LyricsDocument?>(LineDoc("x")));
        vm.OnNavigatedTo(null!);

        svc.Position = 3.0;
        svc.RaisePositionChanged();

        Assert.AreEqual(3000.0, vm.PositionMs);
    }

    [TestMethod]
    public void PlaybackStateChanged_UpdatesIsPlaying()
    {
        var svc = new FakePlaybackService { NowPlaying = T("a"), IsPlaying = false };
        var vm = Build(svc, _ => Task.FromResult<LyricsDocument?>(LineDoc("x")));
        vm.OnNavigatedTo(null!);

        svc.IsPlaying = true;
        svc.RaisePlaybackStateChanged();

        Assert.IsTrue(vm.IsPlaying);
    }

    [TestMethod]
    public void TrackDurationMs_IsNull_WhenDurationNonPositive()
    {
        var svc = new FakePlaybackService { NowPlaying = T("a"), Duration = 0 };
        var vm = Build(svc, _ => Task.FromResult<LyricsDocument?>(LineDoc("x")));

        vm.OnNavigatedTo(null!);

        Assert.IsNull(vm.TrackDurationMs);
    }

    [TestMethod]
    public void PositionChanged_AfterOnNavigatedFrom_DoesNotUpdate()
    {
        var svc = new FakePlaybackService { NowPlaying = T("a"), Position = 1.0 };
        var vm = Build(svc, _ => Task.FromResult<LyricsDocument?>(LineDoc("x")));
        vm.OnNavigatedTo(null!);
        vm.OnNavigatedFrom();

        svc.Position = 99.0;
        svc.RaisePositionChanged();

        Assert.AreEqual(1000.0, vm.PositionMs); // frozen at the last value before detach
    }

    [TestMethod]
    public async Task EnableKaraokeWipe_TrueForWordSynced()
    {
        var svc = new FakePlaybackService { NowPlaying = T("a") };
        var vm = Build(svc, _ => Task.FromResult<LyricsDocument?>(WordDoc("la")));

        vm.OnNavigatedTo(null!);
        await vm.PendingLyricsFetch!;

        Assert.IsTrue(vm.EnableKaraokeWipe);
    }

    [TestMethod]
    public async Task EnableKaraokeWipe_FalseForLineSynced()
    {
        var svc = new FakePlaybackService { NowPlaying = T("a") };
        var vm = Build(svc, _ => Task.FromResult<LyricsDocument?>(LineDoc("la")));

        vm.OnNavigatedTo(null!);
        await vm.PendingLyricsFetch!;

        Assert.IsFalse(vm.EnableKaraokeWipe);
    }

    [TestMethod]
    public async Task EnableKaraokeWipe_FalseWhenNoLyrics()
    {
        var svc = new FakePlaybackService { NowPlaying = T("a") };
        var vm = Build(svc, _ => Task.FromResult<LyricsDocument?>(null));

        vm.OnNavigatedTo(null!);
        await vm.PendingLyricsFetch!;

        Assert.IsFalse(vm.EnableKaraokeWipe);
    }

    [TestMethod]
    public async Task Refresh_ForceRefreshesWithManualBudget()
    {
        var svc = new FakePlaybackService { NowPlaying = T("a") };
        var service = new FakeLyricsService(_ => Task.FromResult<LyricsDocument?>(LineDoc("fresh")));
        var vm = Build(svc, service);
        vm.OnNavigatedTo(null!);
        await vm.PendingLyricsFetch!;

        await vm.RefreshLyricsCommand.ExecuteAsync(null);

        Assert.IsTrue(service.LastForceRefresh);
        Assert.AreEqual(TimeSpan.FromMilliseconds(new LyricsOrchestrationOptions().ManualRefreshBudgetMs), service.LastBudget);
        Assert.AreEqual("fresh", vm.CurrentLyrics!.Lines[0].Text);
    }

    [TestMethod]
    public async Task Refresh_DoesNotClearCurrentLyricsWhileRunning()
    {
        var tcs = new TaskCompletionSource<LyricsDocument?>();
        var svc = new FakePlaybackService { NowPlaying = T("a") };
        var first = LineDoc("original");
        var calls = 0;
        var service = new FakeLyricsService(_ => ++calls == 1 ? Task.FromResult<LyricsDocument?>(first) : tcs.Task);
        var vm = Build(svc, service);
        vm.OnNavigatedTo(null!);
        await vm.PendingLyricsFetch!;
        Assert.AreSame(first, vm.CurrentLyrics);

        var refresh = vm.RefreshLyricsCommand.ExecuteAsync(null);
        // While the refresh is in flight, the original lyrics remain on screen.
        Assert.AreSame(first, vm.CurrentLyrics);
        Assert.AreEqual(LyricsLoadState.Loaded, vm.LyricsLoadState);

        tcs.SetResult(LineDoc("updated"));
        await refresh;
        Assert.AreEqual("updated", vm.CurrentLyrics!.Lines[0].Text);
    }

    [TestMethod]
    public async Task Refresh_StaleResultDiscarded_WhenTrackChangesMidRefresh()
    {
        var tcs = new TaskCompletionSource<LyricsDocument?>();
        var svc = new FakePlaybackService { NowPlaying = T("a") };
        var calls = 0;
        // Track b resolves immediately; track a's refresh (call #2) is deferred via tcs so it completes late.
        var service = new FakeLyricsService(q =>
        {
            calls++;
            if (q.SongId == "b") return Task.FromResult<LyricsDocument?>(LineDoc("b-text"));
            return calls <= 1 ? Task.FromResult<LyricsDocument?>(LineDoc("a-orig")) : tcs.Task;
        });
        var vm = Build(svc, service);
        vm.OnNavigatedTo(null!);
        await vm.PendingLyricsFetch!;

        var refresh = vm.RefreshLyricsCommand.ExecuteAsync(null); // refresh for track a → deferred via tcs (gen N)
        // Track changes before the refresh completes; track b's fetch resolves right away (gen N+1).
        svc.NowPlaying = T("b");
        svc.RaiseTrackChanged();
        if (vm.PendingLyricsFetch != null) await vm.PendingLyricsFetch;

        tcs.SetResult(LineDoc("a-stale")); // the stale refresh for track a completes late
        await refresh;

        // The stale refresh result must not overwrite track b's state.
        Assert.AreEqual("b-text", vm.CurrentLyrics!.Lines[0].Text);
    }
}
