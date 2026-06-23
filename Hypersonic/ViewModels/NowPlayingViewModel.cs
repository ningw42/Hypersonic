using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Hypersonic.Contracts.Services;
using Hypersonic.Contracts.ViewModels;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;
using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Helpers;
using Hypersonic.Models;

using Microsoft.Extensions.Options;
using Microsoft.UI.Dispatching;

namespace Hypersonic.ViewModels;

/// <summary>
/// Now Playing surface state. Lyric resolution and caching now live in the singleton
/// <see cref="ILyricsService"/>; the VM is a pure consumer that maps a track change to a
/// <see cref="LyricsDocument"/> (for the lyrics canvas) plus a load state. The generation
/// guard, dispatcher marshalling, active/inactive lifecycle, and
/// <see cref="TechnicalDetail"/> are unchanged from the pre-migration VM.
/// </summary>
public partial class NowPlayingViewModel : ObservableRecipient, INavigationAware
{
    private readonly IReadOnlyPlaybackState _playbackService;
    private readonly ILyricsService _lyricsService;
    private readonly LyricsOrchestrationOptions _orchestrationOptions;
    private readonly ILogger _log;
    private readonly DispatcherQueue? _vmDispatcher;
    private readonly bool _synchronous;
    private readonly Action<Action> _enqueue;

    private bool _vmActive;
    private long _lyricsGeneration;

    [ObservableProperty]
    public partial NowPlayingTab SelectedTab { get; set; } = NowPlayingTab.Lyrics;

    /// <summary>The validated lyrics document for the current track, consumed by the lyrics canvas.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EnableKaraokeWipe))]
    public partial LyricsDocument? CurrentLyrics { get; set; }

    /// <summary>Karaoke gradient wipe is enabled only for word-synced lyrics (§4); line/plain get a solid highlight.</summary>
    public bool EnableKaraokeWipe => CurrentLyrics?.SyncKind == LyricsSyncKind.Word;

    /// <summary>Playback position in milliseconds — the lyrics canvas's clock anchor (seconds → ms).</summary>
    [ObservableProperty]
    public partial double PositionMs { get; set; }

    /// <summary>Whether playback is advancing — drives the canvas clock.</summary>
    [ObservableProperty]
    public partial bool IsPlaying { get; set; }

    /// <summary>Track length in milliseconds, or null when unknown (duration ≤ 0).</summary>
    [ObservableProperty]
    public partial double? TrackDurationMs { get; set; }

    [ObservableProperty]
    public partial LyricsLoadState LyricsLoadState { get; set; } = LyricsLoadState.NoTrack;

    [ObservableProperty]
    public partial string? TechnicalDetail { get; set; }

    /// <summary>Test-only handle to the most-recent fetch task (null until a fetch runs).</summary>
    internal Task? PendingLyricsFetch { get; private set; }

    public NowPlayingViewModel(
        IReadOnlyPlaybackState playbackService,
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

    internal NowPlayingViewModel(
        IReadOnlyPlaybackState playbackService,
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

    public void OnNavigatedTo(object parameter)
    {
        _vmActive = true;
        _playbackService.TrackChanged += OnTrackChanged;
        _playbackService.PositionChanged += OnPositionChanged;
        _playbackService.PlaybackStateChanged += OnPlaybackStateChanged;
        SyncPlaybackProxies();
        HandleTrackChange();
    }

    public void OnNavigatedFrom()
    {
        _vmActive = false;
        _playbackService.TrackChanged -= OnTrackChanged;
        _playbackService.PositionChanged -= OnPositionChanged;
        _playbackService.PlaybackStateChanged -= OnPlaybackStateChanged;
        Interlocked.Increment(ref _lyricsGeneration);
    }

    private void OnTrackChanged(object? sender, EventArgs e)
    {
        if (_synchronous)
        {
            if (!_vmActive) return;
            HandleTrackChange();
        }
        else
        {
            _enqueue(() =>
            {
                if (!_vmActive) return;
                HandleTrackChange();
            });
        }
    }

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

    private void HandleTrackChange()
    {
        var track = _playbackService.NowPlaying;
        var generation = Interlocked.Increment(ref _lyricsGeneration);

        if (track == null)
        {
            TechnicalDetail = null;
            CurrentLyrics = null;
            LyricsLoadState = LyricsLoadState.NoTrack;
            return;
        }

        TechnicalDetail = TechnicalDetailFormatter.Format(
            track.Suffix, track.BitDepth, track.SamplingRate, track.BitRate);

        CurrentLyrics = null;
        LyricsLoadState = LyricsLoadState.Loading;
        PendingLyricsFetch = LoadLyricsAsync(track, generation);
    }

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
}
