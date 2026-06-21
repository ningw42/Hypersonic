# Lyrics on Now Playing — Phase 3 Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the text-only lyrics area on `NowPlayingPage` with the Phase 2 `LyricsCanvasControl`, wired to live playback, with a manual refresh button — and retire the Phase 2 dev harness.

**Architecture:** `NowPlayingViewModel` (transient) gains ms-unit proxy properties sourced from the already-injected `IPlaybackService` (the true origin of position/play-state/duration), a computed `EnableKaraokeWipe` flag, and a `RefreshLyricsCommand`. `NowPlayingPage.xaml` swaps its Loaded-state `ScrollViewer`/`TextBlock` for `LyricsCanvasControl` with hardcoded effect defaults; the code-behind pushes theme-aware colors. The dev harness (`LyricsLabPage`/`ViewModel`, nav entry, registrations) is deleted.

**Tech Stack:** WinUI 3, .NET 10, CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`), MSTest, Win2D (`LyricsCanvasControl` already built).

---

## Design reference

Spec: [`docs/design/ui/2026-06-21-lyrics-now-playing-integration.md`](../../design/ui/2026-06-21-lyrics-now-playing-integration.md).

**Approved refinement (§3 source):** The spec says the VM subscribes to `PlayerViewModel.PropertyChanged`. Per implementation review, the VM instead sources position/play-state/duration from the **already-injected `IPlaybackService`** — the true origin (`PlayerViewModel` only forwards it). Same observable outcome; more testable (the existing `FakePlaybackService` drives it), no transient→singleton subscription leak, and it rides the VM's existing `OnNavigatedTo`/`OnNavigatedFrom` subscription lifecycle.

## File structure

| File | Change | Responsibility |
|---|---|---|
| `Hypersonic/ViewModels/NowPlayingViewModel.cs` | Modify | Add ms proxies, `EnableKaraokeWipe`, `RefreshLyricsCommand`; remove `LyricsText`/`Flatten`; extract `ApplyLyricsResult` |
| `Hypersonic.Tests/ViewModels/NowPlayingViewModelTests.cs` | Modify | Update assertions off `LyricsText`; add tests for proxies, wipe flag, refresh |
| `Hypersonic/Views/NowPlayingPage.xaml` | Modify | Swap Loaded child to `LyricsCanvasControl`; add refresh button |
| `Hypersonic/Views/NowPlayingPage.xaml.cs` | Modify | Push theme colors to the canvas; re-push on theme change |
| `Hypersonic/Converters/SecondsToMillisecondsConverter.cs` | — | Not needed (conversion lives in VM) |
| `Hypersonic/App.xaml.cs` | Modify | Remove `LyricsLab*` registrations |
| `Hypersonic/Services/PageService.cs` | Modify | Remove `LyricsLab*` mapping |
| `Hypersonic/Views/ShellPage.xaml` | Modify | Remove "Lyrics Lab" nav item |
| `Hypersonic/Views/LyricsLabPage.xaml(.cs)` | Delete | Harness view |
| `Hypersonic/ViewModels/LyricsLabViewModel.cs` | Delete | Harness VM |
| `LYRICS.md` | Modify | Mark Phase 3 complete |

**Build/test commands** (from repo root):
- App build: `dotnet build Hypersonic/Hypersonic.csproj -p:Platform=x64`
- VM tests: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64`
- Single test: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~TestName"`

---

## Task 1: VM — replace `LyricsText` with shared apply path

**Files:**
- Modify: `Hypersonic/ViewModels/NowPlayingViewModel.cs`
- Test: `Hypersonic.Tests/ViewModels/NowPlayingViewModelTests.cs`

The current `LoadLyricsAsync` inlines the null/`None`/document decision and sets a flattened `LyricsText` string. We extract that decision into `ApplyLyricsResult(document, generation)` so the refresh command (Task 4) reuses it, and we delete `LyricsText`/`Flatten` (the `TextBlock` that consumed them is removed in Task 5).

- [ ] **Step 1: Update existing tests that assert on `LyricsText`**

In `NowPlayingViewModelTests.cs`, `LyricsText` is being removed. Replace its assertions with `CurrentLyrics`-based ones. Make these edits:

In `TrackWithDocument_LandsOnLoaded_SetsCurrentLyricsAndFlattenedText` (rename + body):

```csharp
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
```

In `NullTrack_OnNavigatedTo_LandsOnNoTrack`: delete the line `Assert.IsNull(vm.LyricsText);`.

In `TrackChanged_ToNull_ClearsAndLandsOnNoTrack`: delete the line `Assert.IsNull(vm.LyricsText);`.

In `Stale_FetchGuard_DiscardsOldGenerationResult`: replace `Assert.AreEqual("b-text", vm.LyricsText);` with:

```csharp
        Assert.AreEqual("b-text", vm.CurrentLyrics!.Lines[0].Text);
```

In `VmActive_Guard_DiscardsResponseAfterOnNavigatedFrom`: delete the line `Assert.IsNull(vm.LyricsText);`.

- [ ] **Step 2: Run the suite to confirm it fails to compile**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~NowPlayingViewModelTests"`
Expected: BUILD FAILS — `LyricsText` still referenced by the VM but tests changed is fine; the real failure will resolve once Step 3 removes the property. (If build passes here because `LyricsText` still exists, that's also acceptable — proceed.)

- [ ] **Step 3: Refactor the VM — extract `ApplyLyricsResult`, delete `LyricsText`/`Flatten`**

In `NowPlayingViewModel.cs`:

Delete the `LyricsText` property:

```csharp
    /// <summary>Flattened lyric text — fallback for today's text UI until the Phase 2 renderer replaces it.</summary>
    [ObservableProperty]
    public partial string? LyricsText { get; set; }
```

In `HandleTrackChange`, remove the two `LyricsText = null;` assignments (in the null-track branch and the main branch).

Replace the body of `LoadLyricsAsync`'s try-block result handling and delete `Flatten`. The new `LoadLyricsAsync` and `ApplyLyricsResult`:

```csharp
    private async Task LoadLyricsAsync(PlaybackTrack track, long generation)
    {
        try
        {
            var query = new LyricsQuery(track.Id, track.Artist, track.Title, track.Album, track.Duration);
            var document = await _lyricsService.GetLyricsAsync(query, cancellationToken: CancellationToken.None);
            ApplyLyricsResult(document, generation);
        }
        catch (Exception ex)
        {
            _log.Warning("Lyrics fetch threw", new { TrackId = track.Id }, ex);
            ApplyLyricsResult(null, generation);
        }
    }

    /// <summary>
    /// Applies a resolved lyrics document (or a failure/none) to the surface state, guarded by the
    /// generation token. Shared by the track-change fetch and the manual refresh command. A null or
    /// <see cref="LyricsSyncKind.None"/> document presents as <see cref="LyricsLoadState.NoLyrics"/>.
    /// </summary>
    private void ApplyLyricsResult(LyricsDocument? document, long generation)
    {
        if (!_vmActive || Volatile.Read(ref _lyricsGeneration) != generation)
        {
            return;
        }

        if (document is null || document.SyncKind == LyricsSyncKind.None)
        {
            CurrentLyrics = null;
            LyricsLoadState = LyricsLoadState.NoLyrics;
        }
        else
        {
            CurrentLyrics = document;
            LyricsLoadState = LyricsLoadState.Loaded;
        }
    }
```

Remove the now-unused `Flatten` method:

```csharp
    private static string Flatten(LyricsDocument document)
        => string.Join("\n", document.Lines.Select(line => line.Text));
```

- [ ] **Step 4: Run the suite to verify it passes**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~NowPlayingViewModelTests"`
Expected: PASS (all existing tests green with `CurrentLyrics`-based assertions).

- [ ] **Step 5: Commit**

```bash
git add Hypersonic/ViewModels/NowPlayingViewModel.cs Hypersonic.Tests/ViewModels/NowPlayingViewModelTests.cs
git commit -m "refactor(lyrics): extract ApplyLyricsResult, drop flattened LyricsText"
```

---

## Task 2: VM — ms-unit proxy properties from `IPlaybackService`

**Files:**
- Modify: `Hypersonic/ViewModels/NowPlayingViewModel.cs`
- Test: `Hypersonic.Tests/ViewModels/NowPlayingViewModelTests.cs`

Add `PositionMs` / `IsPlaying` / `TrackDurationMs` proxies. Source them from `_playbackService` (seconds → ms; duration null when ≤ 0). Keep them current by subscribing to `_playbackService.PositionChanged` and `_playbackService.PlaybackStateChanged` on the existing `OnNavigatedTo`/`OnNavigatedFrom` boundary (where `TrackChanged` is already wired).

- [ ] **Step 1: Write the failing tests**

Add to `NowPlayingViewModelTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the new tests to verify they fail**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~Proxies_SeedFromPlaybackService_OnNavigatedTo"`
Expected: BUILD FAILS — `PositionMs` / `TrackDurationMs` not defined.

- [ ] **Step 3: Add the proxy properties + subscriptions**

In `NowPlayingViewModel.cs`, add the properties near `CurrentLyrics`:

```csharp
    /// <summary>Playback position in milliseconds — the lyrics canvas's clock anchor (seconds → ms).</summary>
    [ObservableProperty]
    public partial double PositionMs { get; set; }

    /// <summary>Whether playback is advancing — drives the canvas clock.</summary>
    [ObservableProperty]
    public partial bool IsPlaying { get; set; }

    /// <summary>Track length in milliseconds, or null when unknown (duration ≤ 0).</summary>
    [ObservableProperty]
    public partial double? TrackDurationMs { get; set; }
```

Add a sync helper:

```csharp
    private void SyncPlaybackProxies()
    {
        PositionMs = _playbackService.Position * 1000.0;
        IsPlaying = _playbackService.IsPlaying;
        TrackDurationMs = _playbackService.Duration > 0 ? _playbackService.Duration * 1000.0 : null;
    }

    private void OnPositionChanged(object? sender, EventArgs e)
    {
        if (_synchronous)
        {
            if (_vmActive) SyncPlaybackProxies();
        }
        else
        {
            _enqueue(() => { if (_vmActive) SyncPlaybackProxies(); });
        }
    }

    private void OnPlaybackStateChanged(object? sender, EventArgs e)
    {
        if (_synchronous)
        {
            if (_vmActive) SyncPlaybackProxies();
        }
        else
        {
            _enqueue(() => { if (_vmActive) SyncPlaybackProxies(); });
        }
    }
```

In `OnNavigatedTo`, after the existing `TrackChanged` subscription, add the two subscriptions and an initial sync:

```csharp
    public void OnNavigatedTo(object parameter)
    {
        _vmActive = true;
        _playbackService.TrackChanged += OnTrackChanged;
        _playbackService.PositionChanged += OnPositionChanged;
        _playbackService.PlaybackStateChanged += OnPlaybackStateChanged;
        SyncPlaybackProxies();
        HandleTrackChange();
    }
```

In `OnNavigatedFrom`, detach both:

```csharp
    public void OnNavigatedFrom()
    {
        _vmActive = false;
        _playbackService.TrackChanged -= OnTrackChanged;
        _playbackService.PositionChanged -= OnPositionChanged;
        _playbackService.PlaybackStateChanged -= OnPlaybackStateChanged;
        Interlocked.Increment(ref _lyricsGeneration);
    }
```

> Note on the `PositionChanged_AfterOnNavigatedFrom` test: in the synchronous test path the detach happens before the raise, so the handler is gone — `PositionMs` stays frozen. The `_vmActive` guard is belt-and-suspenders for the async path.

- [ ] **Step 4: Run the new tests to verify they pass**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~NowPlayingViewModelTests"`
Expected: PASS (all, including the five new proxy tests).

- [ ] **Step 5: Commit**

```bash
git add Hypersonic/ViewModels/NowPlayingViewModel.cs Hypersonic.Tests/ViewModels/NowPlayingViewModelTests.cs
git commit -m "feat(lyrics): NowPlaying VM ms-unit playback proxies for the canvas"
```

---

## Task 3: VM — `EnableKaraokeWipe` derived from sync kind

**Files:**
- Modify: `Hypersonic/ViewModels/NowPlayingViewModel.cs`
- Test: `Hypersonic.Tests/ViewModels/NowPlayingViewModelTests.cs`

The karaoke wipe is on only for word-synced lyrics. Expose a computed `bool` that recomputes when `CurrentLyrics` changes, using CommunityToolkit's `[NotifyPropertyChangedFor]`.

- [ ] **Step 1: Write the failing tests**

Add a word-doc helper near `LineDoc` in the test class:

```csharp
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
```

Add the tests:

```csharp
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
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~EnableKaraokeWipe"`
Expected: BUILD FAILS — `EnableKaraokeWipe` not defined.

- [ ] **Step 3: Add the computed property**

In `NowPlayingViewModel.cs`, annotate `CurrentLyrics` to notify the new property and add the getter. Change the `CurrentLyrics` declaration to:

```csharp
    /// <summary>The validated lyrics document for the current track, consumed by the Phase 2 renderer.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EnableKaraokeWipe))]
    public partial LyricsDocument? CurrentLyrics { get; set; }

    /// <summary>Karaoke gradient wipe is enabled only for word-synced lyrics (§4); line/plain get a solid highlight.</summary>
    public bool EnableKaraokeWipe => CurrentLyrics?.SyncKind == LyricsSyncKind.Word;
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~NowPlayingViewModelTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Hypersonic/ViewModels/NowPlayingViewModel.cs Hypersonic.Tests/ViewModels/NowPlayingViewModelTests.cs
git commit -m "feat(lyrics): derive EnableKaraokeWipe from sync kind"
```

---

## Task 4: VM — `RefreshLyricsCommand`

**Files:**
- Modify: `Hypersonic/ViewModels/NowPlayingViewModel.cs`
- Test: `Hypersonic.Tests/ViewModels/NowPlayingViewModelTests.cs`

A `[RelayCommand]` async method that force-refreshes with the manual budget, applies the result via `ApplyLyricsResult` (Task 1), and increments the generation so a mid-refresh track change discards the stale result. The budget comes from injected `LyricsOrchestrationOptions`. `CurrentLyrics` is NOT cleared during the refresh.

- [ ] **Step 1: Add the options dependency to the test builder**

The VM constructor will gain an `IOptions<LyricsOrchestrationOptions>` parameter. Update the test helpers and add a using. At the top of `NowPlayingViewModelTests.cs` add:

```csharp
using Microsoft.Extensions.Options;
using Hypersonic.Models;
```

Replace the two `Build` helpers with versions that pass options (default budget values are fine):

```csharp
    private static readonly IOptions<LyricsOrchestrationOptions> Opts =
        Options.Create(new LyricsOrchestrationOptions());

    private static NowPlayingViewModel Build(FakePlaybackService svc, Func<LyricsQuery, Task<LyricsDocument?>> handler, Action<Action>? enqueueOverride = null)
        => new(svc, new FakeLyricsService(handler), Opts, new NullLoggingService(), synchronous: true, enqueueOverride: enqueueOverride);

    private static NowPlayingViewModel Build(FakePlaybackService svc, FakeLyricsService service, Action<Action>? enqueueOverride = null)
        => new(svc, service, Opts, new NullLoggingService(), synchronous: true, enqueueOverride: enqueueOverride);
```

Also update the one inline constructor call in `LateEnqueue_AfterOnNavigatedFrom_DoesNotStartFetch`:

```csharp
        var vm = new NowPlayingViewModel(svc, service, Opts, new NullLoggingService(), synchronous: false, enqueueOverride: a => captured.Enqueue(a));
```

Extend `FakeLyricsService` to capture the refresh args. Replace its body's `GetLyricsAsync`:

```csharp
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
```

- [ ] **Step 2: Write the failing refresh tests**

```csharp
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
        var service = new FakeLyricsService(_ => ++calls <= 1 ? Task.FromResult<LyricsDocument?>(LineDoc("a-orig")) : tcs.Task);
        var vm = Build(svc, service);
        vm.OnNavigatedTo(null!);
        await vm.PendingLyricsFetch!;

        var refresh = vm.RefreshLyricsCommand.ExecuteAsync(null);
        // Track changes before the refresh completes.
        svc.NowPlaying = T("b");
        svc.RaiseTrackChanged();
        if (vm.PendingLyricsFetch != null) await vm.PendingLyricsFetch;

        tcs.SetResult(LineDoc("a-stale")); // refresh for track a completes late
        await refresh;

        // The stale refresh result must not overwrite track b's state.
        Assert.AreNotEqual("a-stale", vm.CurrentLyrics?.Lines[0].Text);
    }
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~Refresh_"`
Expected: BUILD FAILS — constructor arity and `RefreshLyricsCommand` not defined.

- [ ] **Step 4: Implement the constructor change + command**

In `NowPlayingViewModel.cs`, add usings:

```csharp
using CommunityToolkit.Mvvm.Input;
using Hypersonic.Models;
using Microsoft.Extensions.Options;
```

Add the field and inject options in both constructors. Add field near the others:

```csharp
    private readonly LyricsOrchestrationOptions _orchestrationOptions;
```

Public constructor — add the parameter and assignment:

```csharp
    public NowPlayingViewModel(
        IPlaybackService playbackService,
        ILyricsService lyricsService,
        IOptions<LyricsOrchestrationOptions> orchestrationOptions,
        ILoggingService logService)
    {
        _playbackService = playbackService;
        _lyricsService = lyricsService;
        _orchestrationOptions = orchestrationOptions.Value;
        _log = logService.GetLogger(nameof(NowPlayingViewModel));
        _vmDispatcher = DispatcherQueue.GetForCurrentThread();
        _synchronous = false;
        _enqueue = action => _vmDispatcher!.TryEnqueue(() => action());
    }
```

Internal (test) constructor — add the parameter:

```csharp
    internal NowPlayingViewModel(
        IPlaybackService playbackService,
        ILyricsService lyricsService,
        IOptions<LyricsOrchestrationOptions> orchestrationOptions,
        ILoggingService logService,
        bool synchronous,
        Action<Action>? enqueueOverride = null)
    {
        _playbackService = playbackService;
        _lyricsService = lyricsService;
        _orchestrationOptions = orchestrationOptions.Value;
        _log = logService.GetLogger(nameof(NowPlayingViewModel));
        _vmDispatcher = null;
        _synchronous = synchronous;
        _enqueue = enqueueOverride ?? (action => action());
    }
```

Add the command (place near `LoadLyricsAsync`):

```csharp
    /// <summary>
    /// User-initiated force-refresh: bypasses the cache and re-fetches with the generous manual budget
    /// to recover the best available lyrics. The current lyrics stay on screen the whole time — the
    /// busy state lives in the button (CommunityToolkit's generated IsRunning) — and the result swaps in
    /// through the shared <see cref="ApplyLyricsResult"/> path. Shares the generation guard so a track
    /// change mid-refresh discards the stale result.
    /// </summary>
    [RelayCommand]
    private async Task RefreshLyricsAsync()
    {
        var track = _playbackService.NowPlaying;
        if (track is null)
        {
            return;
        }

        var generation = Interlocked.Increment(ref _lyricsGeneration);
        var budget = TimeSpan.FromMilliseconds(_orchestrationOptions.ManualRefreshBudgetMs);

        try
        {
            var query = new LyricsQuery(track.Id, track.Artist, track.Title, track.Album, track.Duration);
            var document = await _lyricsService.GetLyricsAsync(query, budget, forceRefresh: true, CancellationToken.None);
            ApplyLyricsResult(document, generation);
        }
        catch (Exception ex)
        {
            _log.Warning("Manual lyrics refresh threw", new { TrackId = track.Id }, ex);
            ApplyLyricsResult(null, generation);
        }
    }
```

> Note: `RefreshLyricsAsync` increments the generation, so the in-flight track-change `LoadLyricsAsync` (if any) is invalidated — its `ApplyLyricsResult` will no-op. This is intended: the refresh supersedes a pending auto-fetch for the same track. In the `StaleResultDiscarded` test, the subsequent `RaiseTrackChanged` increments again, invalidating the refresh.

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~NowPlayingViewModelTests"`
Expected: PASS (all, including the three refresh tests).

- [ ] **Step 6: Commit**

```bash
git add Hypersonic/ViewModels/NowPlayingViewModel.cs Hypersonic.Tests/ViewModels/NowPlayingViewModelTests.cs
git commit -m "feat(lyrics): manual RefreshLyricsCommand (force-refresh, shared apply path)"
```

---

## Task 5: Page XAML — swap Loaded surface to the canvas + refresh button

**Files:**
- Modify: `Hypersonic/Views/NowPlayingPage.xaml`

Replace the Loaded-state `ScrollViewer`/`TextBlock` with `LyricsCanvasControl`, bind its inputs, hardcode the effect defaults, and add the refresh button overlay. Colors are set from code-behind (Task 6), so XAML uses placeholders that the code-behind overwrites — but to be safe we still pass `{ThemeResource}` colors so the control renders before the first code-behind push.

- [ ] **Step 1: Add the control namespace**

In `NowPlayingPage.xaml`, add to the page root element's xmlns list (next to `xmlns:controls`):

```xml
    xmlns:lyrics="using:Hypersonic.Controls.Lyrics"
```

- [ ] **Step 2: Replace the Loaded child and add the refresh button**

In the `LyricsSurface` grid (currently NowPlayingPage.xaml:140-163), replace the `ScrollViewer`...`</ScrollViewer>` block with the canvas, and add the refresh button as a final overlay child. The full new `LyricsSurface` grid:

```xml
            <!-- Lyrics surface -->
            <Grid Grid.Row="1" x:Name="LyricsSurface">
                <ProgressRing IsActive="True" Width="32" Height="32"
                              HorizontalAlignment="Center" VerticalAlignment="Center"
                              Visibility="{x:Bind ViewModel.LyricsLoadState, Mode=OneWay, Converter={StaticResource LyricsLoadStateToVisibilityConverter}, ConverterParameter=Loading}"/>

                <lyrics:LyricsCanvasControl
                    x:Name="LyricsCanvas"
                    Lyrics="{x:Bind ViewModel.CurrentLyrics, Mode=OneWay}"
                    PositionMs="{x:Bind ViewModel.PositionMs, Mode=OneWay}"
                    IsPlaying="{x:Bind ViewModel.IsPlaying, Mode=OneWay}"
                    TrackDurationMs="{x:Bind ViewModel.TrackDurationMs, Mode=OneWay}"
                    Alignment="Center"
                    EnableBloom="False"
                    EnableNeighborBlur="True"
                    EnableKaraokeWipe="{x:Bind ViewModel.EnableKaraokeWipe, Mode=OneWay}"
                    Visibility="{x:Bind ViewModel.LyricsLoadState, Mode=OneWay, Converter={StaticResource LyricsLoadStateToVisibilityConverter}, ConverterParameter=Loaded}"/>

                <TextBlock Text="No lyrics available"
                           HorizontalAlignment="Center" VerticalAlignment="Center"
                           Foreground="{ThemeResource TextFillColorTertiaryBrush}"
                           FontSize="13"
                           Visibility="{x:Bind ViewModel.LyricsLoadState, Mode=OneWay, Converter={StaticResource LyricsLoadStateToVisibilityConverter}, ConverterParameter=NoLyrics}"/>

                <!-- Manual refresh: feedback lives in the button; the canvas keeps rendering. Enabled when
                     there's a track, so it works in both Loaded and NoLyrics states. -->
                <Button x:Name="RefreshLyricsButton"
                        HorizontalAlignment="Right" VerticalAlignment="Top"
                        Margin="0,4,4,0" Width="36" Height="36"
                        Style="{StaticResource TransparentButtonStyle}"
                        Command="{x:Bind ViewModel.RefreshLyricsCommand}"
                        IsEnabled="{x:Bind Player.HasTrack, Mode=OneWay}"
                        ToolTipService.ToolTip="Refresh lyrics">
                    <FontIcon Glyph="&#xE72C;" FontSize="16"/>
                </Button>
            </Grid>
```

> The refresh button's in-flight busy visual (e.g. swapping the glyph for a tiny ring) is deferred per design §9; `TransparentButtonStyle` + the command's auto-disable while running is the v1 affordance. `Player.HasTrack` is already a binding source on this page.

- [ ] **Step 3: Build the app to verify XAML compiles**

Run: `dotnet build Hypersonic/Hypersonic.csproj -p:Platform=x64`
Expected: BUILD SUCCEEDS. (Confirms x:Bind paths `CurrentLyrics`, `PositionMs`, `IsPlaying`, `TrackDurationMs`, `EnableKaraokeWipe`, `RefreshLyricsCommand` all resolve against the VM.)

- [ ] **Step 4: Commit**

```bash
git add Hypersonic/Views/NowPlayingPage.xaml
git commit -m "feat(lyrics): render NowPlaying lyrics via LyricsCanvasControl + refresh button"
```

---

## Task 6: Page code-behind — theme-aware canvas colors

**Files:**
- Modify: `Hypersonic/Views/NowPlayingPage.xaml.cs`

Push `PlayedColor` (primary foreground) and `UnplayedColor` (tertiary foreground) onto the canvas, resolved from the existing brush-provider `TextBlock`s, and re-push on theme change. The page already has `PrimaryBrush`/`TertiaryBrush` helpers (reading the hidden provider elements) and an `OnActualThemeChanged` → `RenderNowPlaying` path.

- [ ] **Step 1: Add a color-push helper**

In `NowPlayingPage.xaml.cs`, add a method (place near `RenderNowPlaying`):

```csharp
    private void ApplyLyricsColors()
    {
        // The canvas takes Color, not Brush. Resolve from the same brush providers the metadata text uses,
        // so the lyrics track Light/Dark in lockstep with the rest of the page (re-pushed on theme change).
        if (PrimaryBrush is Microsoft.UI.Xaml.Media.SolidColorBrush played)
        {
            LyricsCanvas.PlayedColor = played.Color;
        }
        if (TertiaryBrush is Microsoft.UI.Xaml.Media.SolidColorBrush unplayed)
        {
            LyricsCanvas.UnplayedColor = unplayed.Color;
        }
    }
```

- [ ] **Step 2: Call it on render and theme change**

In `RenderNowPlaying`, add a call at the end of the method:

```csharp
        ApplyLyricsColors();
```

`RenderNowPlaying` is already invoked from the constructor and from `OnActualThemeChanged`, so the colors seed on load and refresh on theme switch. No new event wiring needed.

- [ ] **Step 3: Build to verify**

Run: `dotnet build Hypersonic/Hypersonic.csproj -p:Platform=x64`
Expected: BUILD SUCCEEDS.

- [ ] **Step 4: Commit**

```bash
git add Hypersonic/Views/NowPlayingPage.xaml.cs
git commit -m "feat(lyrics): push theme-aware played/unplayed colors to the canvas"
```

---

## Task 7: Retire the Phase 2 dev harness

**Files:**
- Delete: `Hypersonic/Views/LyricsLabPage.xaml`, `Hypersonic/Views/LyricsLabPage.xaml.cs`
- Delete: `Hypersonic/ViewModels/LyricsLabViewModel.cs`
- Modify: `Hypersonic/App.xaml.cs`, `Hypersonic/Services/PageService.cs`, `Hypersonic/Views/ShellPage.xaml`

Remove the harness page, VM, nav entry, and registrations. Keep `LocalFileLyricsSource`, `TtmlLyricsParser`, and the override hook — they're real features, not harness-only.

- [ ] **Step 1: Delete the harness files**

```bash
git rm Hypersonic/Views/LyricsLabPage.xaml Hypersonic/Views/LyricsLabPage.xaml.cs Hypersonic/ViewModels/LyricsLabViewModel.cs
```

- [ ] **Step 2: Remove DI registrations**

In `App.xaml.cs`, delete these two lines (181-182):

```csharp
            services.AddTransient<LyricsLabViewModel>(); // Phase 2 throwaway dev harness
            services.AddTransient<LyricsLabPage>();
```

- [ ] **Step 3: Remove the page-service mapping**

In `PageService.cs`, delete this line (26):

```csharp
        Configure<LyricsLabViewModel, LyricsLabPage>(); // Phase 2 throwaway dev harness
```

- [ ] **Step 4: Remove the nav item**

In `ShellPage.xaml`, delete the "Lyrics Lab" `NavigationViewItem` block (272-277):

```xml
                    <!-- Phase 2 throwaway dev harness for the Win2D lyrics control (removed in Phase 3). -->
                    <NavigationViewItem Content="Lyrics Lab" helpers:NavigationHelper.NavigateTo="Hypersonic.ViewModels.LyricsLabViewModel">
                        <NavigationViewItem.Icon>
                            <FontIcon FontFamily="{StaticResource SymbolThemeFontFamily}" Glyph="&#xE8D6;"/>
                        </NavigationViewItem.Icon>
                    </NavigationViewItem>
```

- [ ] **Step 5: Build to verify no dangling references**

Run: `dotnet build Hypersonic/Hypersonic.csproj -p:Platform=x64`
Expected: BUILD SUCCEEDS (no references to `LyricsLab*` remain).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "chore(lyrics): retire Phase 2 dev harness (LyricsLab)"
```

---

## Task 8: Full test pass, docs, manual smoke

**Files:**
- Modify: `LYRICS.md`

- [ ] **Step 1: Run the full app test suite**

Run: `dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64`
Expected: PASS (all green, including `NowPlayingPageLayoutTests`).

- [ ] **Step 2: Mark Phase 3 complete in LYRICS.md**

In `LYRICS.md`, update the Phase 3 heading (line 97) and its bullets to reflect completion. Change:

```markdown
### Phase 3 — Integration on `NowPlayingPage`
```

to:

```markdown
### Phase 3 — Integration on `NowPlayingPage` — ✅ shipped

> **Source of truth:** [`docs/design/ui/2026-06-21-lyrics-now-playing-integration.md`](docs/design/ui/2026-06-21-lyrics-now-playing-integration.md). Two refinements emerged: all sync kinds (including Plain) route through the canvas, and the manual refresh button's feedback lives entirely in the button. Position/play-state/duration are sourced from `IPlaybackService` (the true origin) rather than `PlayerViewModel`.
```

- [ ] **Step 3: Commit the docs**

```bash
git add LYRICS.md
git commit -m "docs(lyrics): mark Phase 3 integration shipped"
```

- [ ] **Step 4: Manual smoke check (verifies the page wiring unit tests can't reach)**

Deploy and run the app (Visual Studio F5 or MSIX deploy — `dotnet run` is unsupported for the main app). Confirm on the Now Playing page, per design §8:
- A **word-synced** track shows the karaoke gradient wipe sweeping; the played text is bright (primary), un-sung is dimmed (tertiary).
- A **line-synced** track advances/centers lines with a solid highlight (no sweep).
- A **plain** track shows static dimmed lines pinned near the top.
- Position advances smoothly (no 500ms stutter — the canvas clock smooths it).
- The **refresh button** (top-right) re-fetches; the current lyrics stay visible while it runs; it disables briefly then returns.
- Toggle **Light/Dark** in Settings — the lyrics colors flip in lockstep with the page.
- Resize the window — lyrics re-wrap without resetting effects.
- The "Lyrics Lab" nav item is gone.

---

## Self-review notes

- **Spec coverage:** §1 scope (all tasks) · §2 surface swap (Task 5, ProgressRing/NoLyrics kept native) · §3 ms proxies from IPlaybackService (Task 2, approved refinement) · §4 effect defaults incl. dynamic wipe (Tasks 3, 5) · §5 theme colors (Task 6) · §6 refresh button (Tasks 4, 5) · §7 harness retirement (Task 7) · §8 testing + smoke (all tasks + Task 8) · §9 implementation notes (subscription lifecycle → Task 2 OnNavigatedTo/From; budget injection → Task 4; in-button visual deferred → Task 5 note).
- **Type consistency:** `ApplyLyricsResult(LyricsDocument?, long)` defined Task 1, reused Task 4. `SyncPlaybackProxies()` Task 2. `EnableKaraokeWipe` getter Task 3, bound Task 5. `RefreshLyricsCommand` (generated from `RefreshLyricsAsync`) Task 4, bound Task 5. Constructor arity change (Task 4) updates all three test call sites in the same task.
- **Glyph:** `&#xE72C;` is the Segoe MDL2 "Refresh" glyph.
