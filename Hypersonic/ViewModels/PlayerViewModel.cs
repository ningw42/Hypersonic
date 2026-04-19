using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;
using Hypersonic.Models;

using Microsoft.UI.Dispatching;

namespace Hypersonic.ViewModels;

public partial class PlayerViewModel : ObservableRecipient
{
    private readonly IPlaybackService _playbackService;
    private readonly IAppStateService _appStateService;

    private bool _isExternalUpdate;
    private bool _isSavingFromExternal;
    private readonly DispatcherQueueTimer? _volumeSaveTimer;

    [ObservableProperty]
    public partial string TrackTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? DisplayArtist { get; set; }

    [ObservableProperty]
    public partial List<SongArtistInfo> Artists { get; set; } = [];

    public bool HasStructuredArtists { get; private set; }

    [ObservableProperty]
    public partial string FlatArtistText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AlbumName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? AlbumId { get; set; }

    [ObservableProperty]
    public partial string? CoverArtUrl { get; set; }

    [ObservableProperty]
    public partial bool IsPlaying { get; set; }

    [ObservableProperty]
    public partial double Position { get; set; }

    [ObservableProperty]
    public partial double Duration { get; set; }

    [ObservableProperty]
    public partial double VolumePercent { get; set; } = 100;

    [ObservableProperty]
    public partial bool HasNext { get; set; }

    [ObservableProperty]
    public partial bool HasPrevious { get; set; }

    [ObservableProperty]
    public partial bool HasTrack { get; set; }

    [ObservableProperty]
    public partial string PositionText { get; set; } = "0:00";

    [ObservableProperty]
    public partial string DurationText { get; set; } = "0:00";

    [ObservableProperty]
    public partial bool IsMuted { get; set; }

    [ObservableProperty]
    public partial string VolumeGlyph { get; set; } = "\uE767";

    [ObservableProperty]
    public partial IReadOnlyList<QueueItem> QueueItems { get; set; } = Array.Empty<QueueItem>();

    [ObservableProperty]
    public partial int EffectiveQueueIndex { get; set; } = -1;

    [ObservableProperty]
    public partial bool IsQueueOpen { get; set; }

    [ObservableProperty]
    public partial bool IsQueueEmpty { get; set; } = true;

    [ObservableProperty]
    public partial string QueueCountText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ShuffleEnabled { get; set; }

    [ObservableProperty]
    public partial RepeatMode RepeatMode { get; set; }

    public PlayerViewModel(IPlaybackService playbackService, IAppStateService appStateService)
        : this(playbackService, appStateService, createTimer: true)
    {
    }

    internal PlayerViewModel(IPlaybackService playbackService, IAppStateService appStateService, bool createTimer)
    {
        _playbackService = playbackService;
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

        var track = _playbackService.NowPlaying;
        if (track != null)
        {
            HasTrack = true;
            TrackTitle = track.Title;
            AlbumName = track.Album;
            AlbumId = track.AlbumId;
            HasStructuredArtists = track.HasStructuredArtists;
            DisplayArtist = track.DisplayArtist;
            Artists = track.Artists;
            FlatArtistText = track.FlatArtistText;
            CoverArtUrl = track.CoverArtUri?.ToString();
            Duration = track.Duration;
            DurationText = FormatTime(track.Duration);
            IsPlaying = _playbackService.IsPlaying;
        }
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

    public event EventHandler? NowPlayingMetadataChanged;

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
        AlbumName = track?.Album ?? string.Empty;
        AlbumId = track?.AlbumId;
        HasStructuredArtists = track?.HasStructuredArtists ?? false;
        DisplayArtist = track?.DisplayArtist;
        Artists = track?.Artists ?? [];
        FlatArtistText = track?.FlatArtistText ?? string.Empty;
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
        NowPlayingMetadataChanged?.Invoke(this, EventArgs.Empty);
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
