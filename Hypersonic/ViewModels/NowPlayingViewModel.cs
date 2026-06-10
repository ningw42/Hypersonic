using CommunityToolkit.Mvvm.ComponentModel;

using Hypersonic.Contracts.Services;
using Hypersonic.Contracts.ViewModels;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;
using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Helpers;

using Microsoft.UI.Dispatching;

namespace Hypersonic.ViewModels;

/// <summary>
/// Now Playing surface state. Lyric resolution and caching now live in the singleton
/// <see cref="ILyricsService"/>; the VM is a pure consumer that maps a track change to a
/// <see cref="LyricsDocument"/> (for the Phase 2 renderer) plus a flattened-text fallback and a
/// load state. The generation guard, dispatcher marshalling, active/inactive lifecycle, and
/// <see cref="TechnicalDetail"/> are unchanged from the pre-migration VM.
/// </summary>
public partial class NowPlayingViewModel : ObservableRecipient, INavigationAware
{
    private readonly IPlaybackService _playbackService;
    private readonly ILyricsService _lyricsService;
    private readonly ILogger _log;
    private readonly DispatcherQueue? _vmDispatcher;
    private readonly bool _synchronous;
    private readonly Action<Action> _enqueue;

    private bool _vmActive;
    private long _lyricsGeneration;

    [ObservableProperty]
    public partial NowPlayingTab SelectedTab { get; set; } = NowPlayingTab.Lyrics;

    /// <summary>Flattened lyric text — fallback for today's text UI until the Phase 2 renderer replaces it.</summary>
    [ObservableProperty]
    public partial string? LyricsText { get; set; }

    /// <summary>The validated lyrics document for the current track, consumed by the Phase 2 renderer.</summary>
    [ObservableProperty]
    public partial LyricsDocument? CurrentLyrics { get; set; }

    [ObservableProperty]
    public partial LyricsLoadState LyricsLoadState { get; set; } = LyricsLoadState.NoTrack;

    [ObservableProperty]
    public partial string? TechnicalDetail { get; set; }

    /// <summary>Test-only handle to the most-recent fetch task (null until a fetch runs).</summary>
    internal Task? PendingLyricsFetch { get; private set; }

    public NowPlayingViewModel(
        IPlaybackService playbackService,
        ILyricsService lyricsService,
        ILoggingService logService)
    {
        _playbackService = playbackService;
        _lyricsService = lyricsService;
        _log = logService.GetLogger(nameof(NowPlayingViewModel));
        _vmDispatcher = DispatcherQueue.GetForCurrentThread();
        _synchronous = false;
        _enqueue = action => _vmDispatcher!.TryEnqueue(() => action());
    }

    internal NowPlayingViewModel(
        IPlaybackService playbackService,
        ILyricsService lyricsService,
        ILoggingService logService,
        bool synchronous,
        Action<Action>? enqueueOverride = null)
    {
        _playbackService = playbackService;
        _lyricsService = lyricsService;
        _log = logService.GetLogger(nameof(NowPlayingViewModel));
        _vmDispatcher = null;
        _synchronous = synchronous;
        _enqueue = enqueueOverride ?? (action => action());
    }

    public void OnNavigatedTo(object parameter)
    {
        _vmActive = true;
        _playbackService.TrackChanged += OnTrackChanged;
        HandleTrackChange();
    }

    public void OnNavigatedFrom()
    {
        _vmActive = false;
        _playbackService.TrackChanged -= OnTrackChanged;
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

    private void HandleTrackChange()
    {
        var track = _playbackService.NowPlaying;
        var generation = Interlocked.Increment(ref _lyricsGeneration);

        if (track == null)
        {
            TechnicalDetail = null;
            LyricsText = null;
            CurrentLyrics = null;
            LyricsLoadState = LyricsLoadState.NoTrack;
            return;
        }

        TechnicalDetail = TechnicalDetailFormatter.Format(
            track.Suffix, track.BitDepth, track.SamplingRate, track.BitRate);

        LyricsText = null;
        CurrentLyrics = null;
        LyricsLoadState = LyricsLoadState.Loading;
        PendingLyricsFetch = LoadLyricsAsync(track, generation);
    }

    private async Task LoadLyricsAsync(PlaybackTrack track, long generation)
    {
        try
        {
            var query = new LyricsQuery(track.Id, track.Artist, track.Title, track.Album, track.Duration);
            var document = await _lyricsService.GetLyricsAsync(query, CancellationToken.None);

            if (!_vmActive || Volatile.Read(ref _lyricsGeneration) != generation)
            {
                return;
            }

            // null (no usable lyrics this attempt) and SyncKind=None (definitively none) both present
            // as NoLyrics — the cache-vs-not distinction is the service's concern, invisible here.
            if (document is null || document.SyncKind == LyricsSyncKind.None)
            {
                CurrentLyrics = null;
                LyricsText = null;
                LyricsLoadState = LyricsLoadState.NoLyrics;
            }
            else
            {
                CurrentLyrics = document;
                LyricsText = Flatten(document);
                LyricsLoadState = LyricsLoadState.Loaded;
            }
        }
        catch (Exception ex)
        {
            _log.Warning("Lyrics fetch threw", new { TrackId = track.Id }, ex);
            if (!_vmActive || Volatile.Read(ref _lyricsGeneration) != generation)
            {
                return;
            }

            CurrentLyrics = null;
            LyricsText = null;
            LyricsLoadState = LyricsLoadState.NoLyrics;
        }
    }

    private static string Flatten(LyricsDocument document)
        => string.Join("\n", document.Lines.Select(line => line.Text));
}
