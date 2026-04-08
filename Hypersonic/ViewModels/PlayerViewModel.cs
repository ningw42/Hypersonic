using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Hypersonic.Contracts.Services;
using Hypersonic.Models;

namespace Hypersonic.ViewModels;

public partial class PlayerViewModel : ObservableRecipient
{
    private readonly IPlaybackService _playbackService;
    private readonly INavigationService _navigationService;

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

    private bool _isExternalUpdate;
    private double _volumeBeforeMute = 100;

    public PlayerViewModel(IPlaybackService playbackService, INavigationService navigationService)
    {
        _playbackService = playbackService;
        _navigationService = navigationService;
        _playbackService.PlaybackStateChanged += OnPlaybackStateChanged;
        _playbackService.TrackChanged += OnTrackChanged;
        _playbackService.PositionChanged += OnPositionChanged;
        _playbackService.QueueChanged += OnQueueChanged;

        // Hydrate from current service state
        ShuffleEnabled = _playbackService.ShuffleEnabled;
        RepeatMode = _playbackService.RepeatMode;
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
    }

    [RelayCommand]
    private void ToggleMute()
    {
        if (IsMuted)
        {
            VolumePercent = _volumeBeforeMute;
            IsMuted = false;
        }
        else
        {
            _volumeBeforeMute = VolumePercent;
            VolumePercent = 0;
            IsMuted = true;
        }
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
        if (value > 0 && IsMuted)
        {
            IsMuted = false;
        }
    }

    private void UpdateVolumeGlyph()
    {
        VolumeGlyph = VolumePercent switch
        {
            0 => "\uE74F",    // Mute
            < 34 => "\uE993",  // Volume0
            < 67 => "\uE994",  // Volume1
            _ => "\uE767"      // Volume3
        };
    }

    private void OnPlaybackStateChanged(object? sender, EventArgs e)
    {
        IsPlaying = _playbackService.IsPlaying;
        HasNext = _playbackService.HasNext;
        HasPrevious = _playbackService.HasPrevious;
        VolumePercent = Math.Round(_playbackService.Volume * 100);
        Duration = _playbackService.Duration;
        DurationText = FormatTime(_playbackService.Duration);
        ShuffleEnabled = _playbackService.ShuffleEnabled;
        RepeatMode = _playbackService.RepeatMode;
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
