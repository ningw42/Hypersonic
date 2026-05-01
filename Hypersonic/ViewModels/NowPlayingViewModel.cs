using CommunityToolkit.Mvvm.ComponentModel;

using Hypersonic.Client.Models.Entities;
using Hypersonic.Contracts.Services;
using Hypersonic.Contracts.ViewModels;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;
using Hypersonic.Helpers;

using Microsoft.UI.Dispatching;

namespace Hypersonic.ViewModels;

/// <summary>
/// Calls the OpenSubsonic structured-lyrics endpoint for the given track id
/// and classifies the response.
/// </summary>
internal delegate Task<LyricsOutcome> StructuredLyricsFetcher(string trackId, CancellationToken ct);

/// <summary>
/// Calls the legacy lyrics endpoint for the given artist + title and
/// classifies the response.
/// </summary>
internal delegate Task<LyricsOutcome> LegacyLyricsFetcher(string? artist, string? title, CancellationToken ct);

public partial class NowPlayingViewModel : ObservableRecipient, INavigationAware
{
    private readonly IPlaybackService _playbackService;
    private readonly ISubsonicClientService _subsonicClientService;
    private readonly ILogger _log;
    private readonly DispatcherQueue? _vmDispatcher;
    private readonly bool _synchronous;
    private readonly Action<Action> _enqueue;
    private readonly StructuredLyricsFetcher _structuredFetcher;
    private readonly LegacyLyricsFetcher _legacyFetcher;

    private bool _vmActive;
    private long _lyricsGeneration;

    private const int LyricsCacheCapacity = 10;
    private readonly LinkedList<string> _lyricsCacheOrder = new();
    private readonly Dictionary<string, (LinkedListNode<string> Node, string? Text)> _lyricsCache = new();

    [ObservableProperty]
    public partial NowPlayingTab SelectedTab { get; set; } = NowPlayingTab.Lyrics;

    [ObservableProperty]
    public partial string? LyricsText { get; set; }

    [ObservableProperty]
    public partial LyricsLoadState LyricsLoadState { get; set; } = LyricsLoadState.NoTrack;

    [ObservableProperty]
    public partial string? TechnicalDetail { get; set; }

    /// <summary>Test-only handle to the most-recent fetch task (null until a fetch runs).</summary>
    internal Task? PendingLyricsFetch { get; private set; }

    public NowPlayingViewModel(
        IPlaybackService playbackService,
        ISubsonicClientService subsonicClientService,
        ILoggingService logService)
    {
        _playbackService = playbackService;
        _subsonicClientService = subsonicClientService;
        _log = logService.GetLogger(nameof(NowPlayingViewModel));
        _vmDispatcher = DispatcherQueue.GetForCurrentThread();
        _synchronous = false;
        _enqueue = action => _vmDispatcher!.TryEnqueue(() => action());
        _structuredFetcher = DefaultStructuredFetcherAsync;
        _legacyFetcher = DefaultLegacyFetcherAsync;
    }

    internal NowPlayingViewModel(
        IPlaybackService playbackService,
        ISubsonicClientService subsonicClientService,
        ILoggingService logService,
        bool synchronous,
        Action<Action>? enqueueOverride = null,
        StructuredLyricsFetcher? structuredOverride = null,
        LegacyLyricsFetcher? legacyOverride = null)
    {
        _playbackService = playbackService;
        _subsonicClientService = subsonicClientService;
        _log = logService.GetLogger(nameof(NowPlayingViewModel));
        _vmDispatcher = null;
        _synchronous = synchronous;
        _enqueue = enqueueOverride ?? (action => action());
        _structuredFetcher = structuredOverride ?? DefaultStructuredFetcherAsync;
        _legacyFetcher = legacyOverride ?? DefaultLegacyFetcherAsync;
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
            LyricsLoadState = LyricsLoadState.NoTrack;
            return;
        }

        TechnicalDetail = TechnicalDetailFormatter.Format(
            track.Suffix, track.BitDepth, track.SamplingRate, track.BitRate);

        if (TryGetCached(track.Id, out var cached))
        {
            LyricsText = cached;
            LyricsLoadState = string.IsNullOrWhiteSpace(cached)
                ? LyricsLoadState.NoLyrics
                : LyricsLoadState.Loaded;
            return;
        }

        LyricsText = null;
        LyricsLoadState = LyricsLoadState.Loading;
        PendingLyricsFetch = LoadLyricsAsync(track, generation);
    }

    private async Task LoadLyricsAsync(PlaybackTrack track, long generation)
    {
        try
        {
            var structured = await _structuredFetcher(track.Id, CancellationToken.None);
            // Short-circuit: when structured returns content, do NOT call legacy.
            // (Design test row (j) — the only case this is allowed.)
            var legacy = structured.Kind == LyricsOutcomeKind.Content
                ? LyricsOutcome.Transient
                : await _legacyFetcher(track.Artist, track.Title, CancellationToken.None);
            var combined = LyricsFetchPolicy.Combine(structured, legacy);

            if (!_vmActive || Volatile.Read(ref _lyricsGeneration) != generation)
            {
                return;
            }

            if (combined.ShouldCache)
            {
                StoreCached(track.Id, combined.Text);
            }

            LyricsText = combined.Text;
            LyricsLoadState = string.IsNullOrWhiteSpace(combined.Text)
                ? LyricsLoadState.NoLyrics
                : LyricsLoadState.Loaded;
        }
        catch (Exception ex)
        {
            _log.Warning("Lyrics fetch threw", new { TrackId = track.Id }, ex);
            if (!_vmActive || Volatile.Read(ref _lyricsGeneration) != generation)
            {
                return;
            }
            LyricsText = null;
            LyricsLoadState = LyricsLoadState.NoLyrics;
        }
    }

    private bool TryGetCached(string trackId, out string? text)
    {
        if (_lyricsCache.TryGetValue(trackId, out var entry))
        {
            text = entry.Text;
            return true;
        }
        text = null;
        return false;
    }

    private void StoreCached(string trackId, string? text)
    {
        if (_lyricsCache.TryGetValue(trackId, out var existing))
        {
            _lyricsCacheOrder.Remove(existing.Node);
        }

        var node = _lyricsCacheOrder.AddLast(trackId);
        _lyricsCache[trackId] = (node, text);

        while (_lyricsCache.Count > LyricsCacheCapacity)
        {
            var oldest = _lyricsCacheOrder.First!;
            _lyricsCacheOrder.RemoveFirst();
            _lyricsCache.Remove(oldest.Value);
        }
    }

    private async Task<LyricsOutcome> DefaultStructuredFetcherAsync(string trackId, CancellationToken ct)
    {
        var client = _subsonicClientService.Client;
        if (client == null)
        {
            return LyricsOutcome.Transient;
        }

        try
        {
            var response = await client.GetLyricsBySongIdAsync(trackId);
            if (response.Status == "failed")
            {
                var code = response.Error?.Code ?? 0;
                var classified = LyricsFetchPolicy.ClassifyFailedStatus(code);
                if (classified.Kind == LyricsOutcomeKind.Transient)
                {
                    _log.Warning("Structured lyrics fetch failed (transient)", new { TrackId = trackId, Code = code });
                }
                return classified;
            }

            var text = LyricsFetchPolicy.FlattenStructured(response.LyricsList?.StructuredLyrics);
            return string.IsNullOrWhiteSpace(text)
                ? LyricsOutcome.DefinitiveEmpty
                : LyricsOutcome.Content(text);
        }
        catch (Exception ex)
        {
            _log.Warning("Structured lyrics fetch threw", new { TrackId = trackId }, ex);
            return LyricsOutcome.Transient;
        }
    }

    private async Task<LyricsOutcome> DefaultLegacyFetcherAsync(string? artist, string? title, CancellationToken ct)
    {
        var client = _subsonicClientService.Client;
        if (client == null)
        {
            return LyricsOutcome.Transient;
        }

        try
        {
            var response = await client.GetLyricsAsync(artist, title);
            if (response.Status == "failed")
            {
                var code = response.Error?.Code ?? 0;
                var classified = LyricsFetchPolicy.ClassifyFailedStatus(code);
                if (classified.Kind == LyricsOutcomeKind.Transient)
                {
                    _log.Warning("Legacy lyrics fetch failed (transient)", new { Artist = artist, Title = title, Code = code });
                }
                return classified;
            }

            var text = response.Lyrics?.Value;
            return string.IsNullOrWhiteSpace(text)
                ? LyricsOutcome.DefinitiveEmpty
                : LyricsOutcome.Content(text);
        }
        catch (Exception ex)
        {
            _log.Warning("Legacy lyrics fetch threw", new { Artist = artist, Title = title }, ex);
            return LyricsOutcome.Transient;
        }
    }
}
