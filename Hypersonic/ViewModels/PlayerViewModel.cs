using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Hypersonic.Contracts.Services;

namespace Hypersonic.ViewModels;

public partial class PlayerViewModel : ObservableRecipient
{
    private readonly IPlaybackService _playbackService;

    [ObservableProperty]
    private string trackTitle = string.Empty;

    [ObservableProperty]
    private string artistName = string.Empty;

    [ObservableProperty]
    private string albumName = string.Empty;

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

    private bool _isExternalUpdate;
    private double _volumeBeforeMute = 100;

    public PlayerViewModel(IPlaybackService playbackService)
    {
        _playbackService = playbackService;
        _playbackService.PlaybackStateChanged += OnPlaybackStateChanged;
        _playbackService.TrackChanged += OnTrackChanged;
        _playbackService.PositionChanged += OnPositionChanged;
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
    }

    private void OnTrackChanged(object? sender, EventArgs e)
    {
        var track = _playbackService.NowPlaying;
        HasTrack = track != null;
        TrackTitle = track?.Title ?? string.Empty;
        ArtistName = track?.Artist ?? string.Empty;
        AlbumName = track?.Album ?? string.Empty;
        CoverArtUrl = track?.CoverArtUri?.ToString();
        Duration = track?.Duration ?? 0;
        DurationText = FormatTime(track?.Duration ?? 0);
        _isExternalUpdate = true;
        Position = 0;
        _isExternalUpdate = false;
        PositionText = "0:00";
        HasNext = _playbackService.HasNext;
        HasPrevious = _playbackService.HasPrevious;
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
