using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;
using Hypersonic.Models;

using Microsoft.UI.Dispatching;

namespace Hypersonic.ViewModels;

public partial class PlayerViewModel : ObservableRecipient
{
    private readonly IPlaybackService _playbackService;
    private readonly INavigationService _navigationService;
    private readonly IAppStateService _appStateService;

    private bool _isExternalUpdate;
    private bool _isSavingFromExternal;
    private readonly DispatcherQueueTimer? _volumeSaveTimer;

    [ObservableProperty]
    private string trackTitle = string.Empty;

    [ObservableProperty]
    private string artistName = string.Empty;

    [ObservableProperty]
    private string albumName = string.Empty;

    [ObservableProperty]
    private string? artistId;

    [ObservableProperty]
    private string? albumId;

    [ObservableProperty]
    private string? coverArtUrl;

    [ObservableProperty]
    private bool isPlaying;

    [ObservableProperty]
    private double position;

    [ObservableProperty]
    private double duration;

    [ObservableProperty]
    private double volumePercent = 100;

    [ObservableProperty]
    private bool hasNext;

    [ObservableProperty]
    private bool hasPrevious;

    [ObservableProperty]
    private bool hasTrack;

    [ObservableProperty]
    private string positionText = "0:00";

    [ObservableProperty]
    private string durationText = "0:00";

    [ObservableProperty]
    private bool isMuted;

    [ObservableProperty]
    private string volumeGlyph = "\uE767";

    [ObservableProperty]
    private IReadOnlyList<QueueItem> queueItems = Array.Empty<QueueItem>();

    [ObservableProperty]
    private int effectiveQueueIndex = -1;

    [ObservableProperty]
    private bool isQueueOpen;

    [ObservableProperty]
    private bool isQueueEmpty = true;

    [ObservableProperty]
    private string queueCountText = string.Empty;

    [ObservableProperty]
    private bool shuffleEnabled;

    [ObservableProperty]
    private RepeatMode repeatMode;

    public PlayerViewModel(IPlaybackService playbackService, INavigationService navigationService, IAppStateService appStateService)
        : this(playbackService, navigationService, appStateService, createTimer: true)
    {
    }

    /// <summary>
    /// Internal constructor for unit testing. Skips DispatcherQueue timer creation
    /// when <paramref name="createTimer"/> is false, allowing instantiation without a UI thread.
    /// </summary>
    internal PlayerViewModel(IPlaybackService playbackService, INavigationService navigationService, IAppStateService appStateService, bool createTimer)
    {
        _playbackService = playbackService;
        _navigationService = navigationService;
        _appStateService = appStateService;
        _playbackService.PlaybackStateChanged += OnPlaybackStateChanged;
        _playbackService.TrackChanged += OnTrackChanged;
        _playbackService.PositionChanged += OnPositionChanged;
        _playbackService.QueueChanged += OnQueueChanged;

        if (createTimer)
        {
            // Setup debounce timer for volume save (1 second).
            _volumeSaveTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            _volumeSaveTimer.Interval = TimeSpan.FromSeconds(1);
            _volumeSaveTimer.IsRepeating = false;
            _volumeSaveTimer.Tick += (_, _) =>
            {
                _appStateService.State.Playback.Volume = VolumePercent;
                _ = _appStateService.SavePlaybackAsync();
            };
        }

        // Hydrate from current service state (already restored from AppState in PlaybackService).
        // Guard prevents redundant saves during hydration (values already match AppState).
        _isSavingFromExternal = true;
        VolumePercent = Math.Round(_playbackService.Volume * 100);
        IsMuted = _playbackService.IsMuted;
        ShuffleEnabled = _playbackService.ShuffleEnabled;
        RepeatMode = _playbackService.RepeatMode;
        UpdateVolumeGlyph();
        _isSavingFromExternal = false;
    }

    /// <summary>
    /// Re-evaluates converter bindings that depend on the current theme.
    /// Called from ShellPage when ActualThemeChanged fires.
    /// </summary>
    internal void NotifyThemeChanged()
    {
        OnPropertyChanged(nameof(ShuffleEnabled));
        OnPropertyChanged(nameof(RepeatMode));
        OnPropertyChanged(nameof(IsQueueOpen));
    }

    [RelayCommand]
    private void PlayPause()
    {
        if (IsPlaying)
        {
            _playbackService.Pause();
        }
        else
        {
            _playbackService.Resume();
        }
    }

    [RelayCommand]
    private void Stop()
    {
        _playbackService.Stop();
    }

    [RelayCommand]
    private void Next()
    {
        _playbackService.NextTrack();
    }

    [RelayCommand]
    private void Previous()
    {
        _playbackService.PreviousTrack();
    }

    [RelayCommand]
    private void ToggleShuffle()
    {
        _playbackService.ShuffleEnabled = !_playbackService.ShuffleEnabled;
        _appStateService.State.Playback.ShuffleEnabled = _playbackService.ShuffleEnabled;
        _ = _appStateService.SavePlaybackAsync();
    }

    [RelayCommand]
    private void CycleRepeatMode()
    {
        _playbackService.RepeatMode = _playbackService.RepeatMode switch
        {
            RepeatMode.Off => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            RepeatMode.One => RepeatMode.Off,
            _ => RepeatMode.Off,
        };
        _appStateService.State.Playback.RepeatMode = _playbackService.RepeatMode;
        _ = _appStateService.SavePlaybackAsync();
    }

    [RelayCommand]
    private void ToggleMute()
    {
        _playbackService.IsMuted = !_playbackService.IsMuted;
        _appStateService.State.Playback.IsMuted = _playbackService.IsMuted;
        _ = _appStateService.SavePlaybackAsync();
    }

    [RelayCommand]
    private void NavigateToArtist()
    {
        if (!string.IsNullOrEmpty(ArtistId))
        {
            _navigationService.NavigateTo(typeof(ArtistDetailViewModel).FullName!, ArtistId);
        }
    }

    [RelayCommand]
    private void NavigateToAlbum()
    {
        if (!string.IsNullOrEmpty(AlbumId))
        {
            _navigationService.NavigateTo(typeof(AlbumDetailViewModel).FullName!, AlbumId);
        }
    }

    public void RefreshQueue()
    {
        var effective = _playbackService.GetEffectiveQueue();
        var currentIndex = _playbackService.EffectiveQueueIndex;

        // Check if the queue contents match the existing list (same tracks, same order).
        // If so, update IsCurrent in-place to avoid replacing the ItemsSource and resetting scroll.
        var existing = QueueItems;
        if (existing.Count == effective.Count)
        {
            var match = true;
            for (var i = 0; i < effective.Count; i++)
            {
                if (existing[i].Track.Id != effective[i].Id)
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                for (var i = 0; i < existing.Count; i++)
                {
                    existing[i].IsCurrent = i == currentIndex;
                }

                EffectiveQueueIndex = currentIndex;
                QueueCountText = existing.Count > 0
                    ? $"{currentIndex + 1} / {existing.Count}"
                    : string.Empty;
                return;
            }
        }

        // Queue contents changed — rebuild the list.
        var items = new List<QueueItem>(effective.Count);
        for (var i = 0; i < effective.Count; i++)
        {
            items.Add(new QueueItem
            {
                Track = effective[i],
                IsCurrent = i == currentIndex,
            });
        }

        QueueItems = items;
        EffectiveQueueIndex = currentIndex;
        IsQueueEmpty = items.Count == 0;
        QueueCountText = items.Count > 0
            ? $"{currentIndex + 1} / {items.Count}"
            : string.Empty;
    }

    public void JumpToQueueTrack(QueueItem item)
    {
        _playbackService.JumpToTrack(item.Track.Id);
    }

    /// <summary>
    /// Flushes any pending debounced volume save. Call on window close.
    /// </summary>
    internal void FlushPendingSaves()
    {
        if (_volumeSaveTimer is { IsRunning: true })
        {
            _volumeSaveTimer.Stop();
            _appStateService.State.Playback.Volume = VolumePercent;
            _appStateService.SaveSync();
        }
    }

    partial void OnPositionChanged(double value)
    {
        if (!_isExternalUpdate)
        {
            _playbackService.Seek(value);
        }
    }

    partial void OnVolumePercentChanged(double value)
    {
        _playbackService.Volume = value / 100.0;
        UpdateVolumeGlyph();

        if (!_isSavingFromExternal && _volumeSaveTimer is not null)
        {
            // Debounce volume save — slider fires continuously during drag.
            _volumeSaveTimer.Stop();
            _volumeSaveTimer.Start();
        }
    }

    partial void OnIsMutedChanged(bool value)
    {
        UpdateVolumeGlyph();
    }

    private void UpdateVolumeGlyph()
    {
        VolumeGlyph = IsMuted ? "\uE74F" : VolumePercent switch
        {
            0 => "\uE74F",    // Mute
            < 34 => "\uE993",  // Volume0
            < 67 => "\uE994",  // Volume1
            _ => "\uE767"      // Volume3
        };
    }

    private void OnPlaybackStateChanged(object? sender, EventArgs e)
    {
        _isSavingFromExternal = true;
        IsPlaying = _playbackService.IsPlaying;
        HasNext = _playbackService.HasNext;
        HasPrevious = _playbackService.HasPrevious;
        VolumePercent = Math.Round(_playbackService.Volume * 100);
        IsMuted = _playbackService.IsMuted;
        Duration = _playbackService.Duration;
        DurationText = FormatTime(_playbackService.Duration);
        ShuffleEnabled = _playbackService.ShuffleEnabled;
        RepeatMode = _playbackService.RepeatMode;
        _isSavingFromExternal = false;
    }

    private void OnTrackChanged(object? sender, EventArgs e)
    {
        var track = _playbackService.NowPlaying;
        HasTrack = track != null;
        TrackTitle = track?.Title ?? string.Empty;
        ArtistName = track?.Artist ?? string.Empty;
        AlbumName = track?.Album ?? string.Empty;
        ArtistId = track?.ArtistId;
        AlbumId = track?.AlbumId;
        CoverArtUrl = track?.CoverArtUri?.ToString();
        Duration = track?.Duration ?? 0;
        DurationText = FormatTime(track?.Duration ?? 0);
        _isExternalUpdate = true;
        Position = 0;
        _isExternalUpdate = false;
        PositionText = "0:00";
        HasNext = _playbackService.HasNext;
        HasPrevious = _playbackService.HasPrevious;
        RefreshQueue();
    }

    private void OnQueueChanged(object? sender, EventArgs e)
    {
        RefreshQueue();
    }

    private void OnPositionChanged(object? sender, EventArgs e)
    {
        _isExternalUpdate = true;
        Position = _playbackService.Position;
        _isExternalUpdate = false;
        PositionText = FormatTime(_playbackService.Position);
    }

    private static string FormatTime(double totalSeconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
        return ts.Hours > 0
            ? ts.ToString(@"h\:mm\:ss")
            : ts.ToString(@"m\:ss");
    }
}
