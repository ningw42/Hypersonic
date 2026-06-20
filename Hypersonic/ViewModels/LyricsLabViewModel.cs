using CommunityToolkit.Mvvm.ComponentModel;

using Hypersonic.Contracts.Services;
using Hypersonic.Contracts.ViewModels;
using Hypersonic.Controls.Lyrics;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;
using Hypersonic.Core.Models.Lyrics;

using Microsoft.UI.Dispatching;

namespace Hypersonic.ViewModels;

/// <summary>
/// Throwaway dev-harness ViewModel for the Phase 2 <c>LyricsCanvasControl</c> smoke test. It mirrors the
/// singleton <see cref="PlayerViewModel"/>'s position / play-state / duration onto control-shaped inputs
/// (ms) and resolves the current track's lyrics through <see cref="ILyricsService"/> (which honours a
/// sideloaded <c>&lt;songId&gt;.ttml</c> override). Deleted in Phase 3 when the control is swapped into
/// <c>NowPlayingPage</c>.
/// </summary>
public partial class LyricsLabViewModel : ObservableObject, INavigationAware
{
    private readonly ILyricsService _lyricsService;
    private readonly IPlaybackService _playbackService;
    private readonly IAppFolderLocator _folders;
    private readonly PlayerViewModel _player;
    private readonly ILogger _log;
    private readonly DispatcherQueue? _dispatcher;

    private long _generation;
    private bool _active;

    /// <summary>The actual folder the override source reads — resolved live, so it's correct for both the
    /// packaged (MSIX <c>LocalState</c>) and unpackaged builds rather than a hardcoded guess.</summary>
    public string OverrideHint { get; }

    [ObservableProperty]
    public partial LyricsDocument? CurrentLyrics { get; set; }

    [ObservableProperty]
    public partial double PositionMs { get; set; }

    [ObservableProperty]
    public partial bool IsPlaying { get; set; }

    [ObservableProperty]
    public partial double? TrackDurationMs { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "No track.";

    [ObservableProperty]
    public partial bool EnableBloom { get; set; } = true;

    [ObservableProperty]
    public partial bool EnableNeighborBlur { get; set; }

    [ObservableProperty]
    public partial bool EnableKaraokeWipe { get; set; } = true;

    /// <summary>Alignment as a combo-box index (0=Left, 1=Centre, 2=Right); exposed as the enum via <see cref="Alignment"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Alignment))]
    public partial int AlignmentIndex { get; set; }

    public LyricsTextAlignment Alignment => (LyricsTextAlignment)AlignmentIndex;

    public LyricsLabViewModel(
        ILyricsService lyricsService,
        IPlaybackService playbackService,
        IAppFolderLocator folders,
        PlayerViewModel player,
        ILoggingService logService)
    {
        _lyricsService = lyricsService;
        _playbackService = playbackService;
        _folders = folders;
        _player = player;
        _log = logService.GetLogger(nameof(LyricsLabViewModel));
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        OverrideHint = $"Phase 2 smoke test. Drop \"<songId>.ttml\" (AMLL word-timed TTML) into  {_folders.GetLyricsOverrideFolder()}  "
            + "to force word-synced lyrics — it wins outright over the cache and network. Otherwise the live providers' Line/Plain lyrics render with the graceful fallback.";
    }

    public void OnNavigatedTo(object parameter)
    {
        _active = true;
        _player.PropertyChanged += OnPlayerPropertyChanged;
        _playbackService.TrackChanged += OnTrackChanged;
        SyncFromPlayer();
        _ = LoadLyricsAsync();
    }

    public void OnNavigatedFrom()
    {
        _active = false;
        _player.PropertyChanged -= OnPlayerPropertyChanged;
        _playbackService.TrackChanged -= OnTrackChanged;
        Interlocked.Increment(ref _generation);
    }

    private void OnPlayerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PlayerViewModel.Position):
            case nameof(PlayerViewModel.IsPlaying):
            case nameof(PlayerViewModel.Duration):
                SyncFromPlayer();
                break;
        }
    }

    private void OnTrackChanged(object? sender, EventArgs e)
    {
        void Handle()
        {
            if (!_active)
            {
                return;
            }

            SyncFromPlayer();
            _ = LoadLyricsAsync();
        }

        if (_dispatcher is null)
        {
            Handle();
        }
        else
        {
            _dispatcher.TryEnqueue(Handle);
        }
    }

    private void SyncFromPlayer()
    {
        PositionMs = _player.Position * 1000.0;
        IsPlaying = _player.IsPlaying;
        TrackDurationMs = _player.Duration > 0 ? _player.Duration * 1000.0 : null;
    }

    private async Task LoadLyricsAsync()
    {
        var track = _playbackService.NowPlaying;
        var generation = Interlocked.Increment(ref _generation);

        if (track is null)
        {
            CurrentLyrics = null;
            StatusText = "No track playing.";
            return;
        }

        StatusText = $"Loading lyrics for \"{track.Title}\"…";

        try
        {
            var query = new LyricsQuery(track.Id, track.Artist, track.Title, track.Album, track.Duration);
            var document = await _lyricsService.GetLyricsAsync(query, cancellationToken: CancellationToken.None);

            if (!_active || Volatile.Read(ref _generation) != generation)
            {
                return;
            }

            if (document is null || document.SyncKind == LyricsSyncKind.None)
            {
                CurrentLyrics = null;
                StatusText = $"No lyrics found for \"{track.Title}\".";
            }
            else
            {
                CurrentLyrics = document;
                StatusText = $"{document.SyncKind} lyrics ({document.Lines.Count} lines) — source: {document.SourceProvider ?? "?"}";
            }
        }
        catch (Exception ex)
        {
            _log.Warning("Lyrics lab fetch threw", new { TrackId = track.Id }, ex);
            if (_active && Volatile.Read(ref _generation) == generation)
            {
                CurrentLyrics = null;
                StatusText = "Lyrics fetch failed (see log).";
            }
        }
    }
}
