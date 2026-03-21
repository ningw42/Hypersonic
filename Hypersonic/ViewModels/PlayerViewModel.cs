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
    private double volume = 1.0;

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

    private bool _isSeeking;

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

    public void OnPositionSliderChanged(double newPosition)
    {
        _isSeeking = true;
        _playbackService.Seek(newPosition);
        _isSeeking = false;
    }

    partial void OnVolumeChanged(double value)
    {
        _playbackService.Volume = value;
    }

    private void OnPlaybackStateChanged(object? sender, EventArgs e)
    {
        IsPlaying = _playbackService.IsPlaying;
        HasNext = _playbackService.HasNext;
        HasPrevious = _playbackService.HasPrevious;
        Volume = _playbackService.Volume;
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
        Position = 0;
        PositionText = "0:00";
        HasNext = _playbackService.HasNext;
        HasPrevious = _playbackService.HasPrevious;
    }

    private void OnPositionChanged(object? sender, EventArgs e)
    {
        if (!_isSeeking)
        {
            Position = _playbackService.Position;
            PositionText = FormatTime(_playbackService.Position);
        }
    }

    private static string FormatTime(double totalSeconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
        return ts.Hours > 0
            ? ts.ToString(@"h\:mm\:ss")
            : ts.ToString(@"m\:ss");
    }
}
