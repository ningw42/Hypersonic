using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;
using Hypersonic.Messages;
using Hypersonic.Models;

using Microsoft.UI.Dispatching;

namespace Hypersonic.ViewModels;

public partial class PlayerViewModel : ObservableRecipient, IRecipient<FavoritesChangedMessage>
{
    private readonly IPlaybackService _playbackService;
    private readonly IAppStateService _appStateService;
    private readonly IFavoritesService _favorites;
    private readonly ILogger _log;

    private bool _isExternalUpdate;
    private bool _isSavingFromExternal;
    private readonly DispatcherQueueTimer? _volumeSaveTimer;

    /// <summary>
    /// Direction of the most recent track change. Mirrors
    /// <see cref="TrackChangedEventArgs.Direction"/> from the playback layer
    /// — the authoritative source. Reset to <see cref="TrackChangeDirection.None"/>
    /// at the end of <see cref="OnTrackChanged"/> so it's only valid during
    /// the synchronous handler chain. Views that need to observe it should
    /// subscribe to <see cref="TrackChangedWithDirection"/> instead of reading
    /// this field after the fact.
    /// </summary>
    public TrackChangeDirection LastChangeDirection { get; private set; } = TrackChangeDirection.None;

    /// <summary>
    /// Raised at the end of <see cref="OnTrackChanged"/> with the direction
    /// from the playback layer. This is the channel views should use to drive
    /// direction-aware animations (e.g. cover-art camera slide). It carries
    /// the direction in args so consumers don't have to read mutable VM state.
    /// </summary>
    public event EventHandler<TrackChangedEventArgs>? TrackChangedWithDirection;

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
    [NotifyPropertyChangedFor(nameof(HasCoverArt))]
    public partial ImageReference? CoverImage { get; set; }

    /// <summary>
    /// True only when there's a non-empty <see cref="CoverImage"/>. Distinct
    /// from <see cref="HasTrack"/>: a track may exist without artwork (server
    /// returned no image field). UI uses this to keep the music-glyph
    /// placeholder showing for art-less tracks instead of going blank.
    ///
    /// Note: this does NOT cover load failures (404, decode error) on a URL
    /// that is present — the placeholder will hide and the Image element will
    /// render empty in that case. Add ImageFailed handling if that becomes a
    /// real-world issue.
    /// </summary>
    public bool HasCoverArt => CoverImage != null;

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
    public partial bool IsStarred { get; set; }

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

    public PlayerViewModel(
        IPlaybackService playbackService,
        IAppStateService appStateService,
        IFavoritesService favoritesService,
        IMessenger messenger,
        ILoggingService logService)
        : this(playbackService, appStateService, favoritesService, messenger, logService, createTimer: true)
    {
    }

    internal PlayerViewModel(
        IPlaybackService playbackService,
        IAppStateService appStateService,
        IFavoritesService favoritesService,
        IMessenger messenger,
        ILoggingService logService,
        bool createTimer)
        : base(messenger)
    {
        _playbackService = playbackService;
        _appStateService = appStateService;
        _favorites = favoritesService;
        _log = logService.GetLogger(nameof(PlayerViewModel));
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
            CoverImage = track.CoverImage;
            Duration = track.Duration;
            DurationText = FormatTime(track.Duration);
            IsPlaying = _playbackService.IsPlaying;
            IsStarred = track.IsStarred;
        }

        // Always-active subscriber: WeakReferenceMessenger holds this weakly, so an
        // off-screen player still stays current and is collected with no explicit unregister.
        IsActive = true;
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
        OnPropertyChanged(nameof(IsStarred));
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
        // Direction is owned by the playback layer and surfaced via
        // TrackChangedEventArgs — see OnTrackChanged. The VM no longer tracks
        // direction itself; it just delegates the call. This means SMTC and
        // taskbar buttons that bypass the VM still produce correct directions.
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
    private async Task ToggleStarAsync()
    {
        var track = _playbackService.NowPlaying;
        if (!HasTrack || track is null || string.IsNullOrEmpty(track.Id))
        {
            return;
        }

        // Delegate to the single writer. The optimistic flip arrives back here reentrantly
        // via the FavoritesChangedMessage broadcast (see Receive), so the heart updates
        // instantly without any per-VM optimistic/revert code.
        await _favorites.ToggleAsync(track.Id, !IsStarred);
    }

    [RelayCommand]
    private async Task ToggleQueueItemStarAsync(QueueItem? item)
    {
        if (item?.Track is null || string.IsNullOrEmpty(item.Track.Id))
        {
            return;
        }

        await _favorites.ToggleAsync(item.Track.Id, !item.Track.IsStarred);
    }

    /// <summary>
    /// Reacts to a star change broadcast by <c>FavoritesService</c>. Updates only the
    /// instances this VM owns: every effective-queue <see cref="PlaybackTrack"/> whose id is
    /// in the message (which redraws the bound queue rows), and the player mirror
    /// <see cref="IsStarred"/> only when the now-playing track is in the message — so a revert
    /// that lands after playback advanced never clobbers the new track's heart.
    /// </summary>
    public void Receive(FavoritesChangedMessage message)
    {
        var ids = message.Ids as IReadOnlySet<string> ?? new HashSet<string>(message.Ids);

        foreach (var track in _playbackService.GetEffectiveQueue())
        {
            if (ids.Contains(track.Id))
            {
                track.IsStarred = message.IsStarred;
            }
        }

        var nowPlaying = _playbackService.NowPlaying;
        if (nowPlaying is not null && ids.Contains(nowPlaying.Id))
        {
            IsStarred = message.IsStarred;
        }
    }

    public event EventHandler? NowPlayingMetadataChanged;

    public void RefreshQueue()
    {
        var effective = _playbackService.GetEffectiveQueue();
        var currentIndex = _playbackService.EffectiveQueueIndex;

        // Fast path: only when every position's wrapper still references the
        // exact same PlaybackTrack instance. ID-equality is insufficient — see
        // the Refresh contract in the now-playing design doc.
        var existing = QueueItems;
        if (existing.Count == effective.Count)
        {
            var match = true;
            for (var i = 0; i < effective.Count; i++)
            {
                if (!ReferenceEquals(existing[i].Track, effective[i]))
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

    public void JumpToQueueIndex(QueueItem item)
    {
        var index = IndexOfQueueItem(item);
        if (index >= 0)
        {
            _playbackService.JumpToQueueIndex(index);
        }
    }

    private int IndexOfQueueItem(QueueItem item)
    {
        var queue = QueueItems;
        for (var i = 0; i < queue.Count; i++)
        {
            if (ReferenceEquals(queue[i], item))
            {
                return i;
            }
        }
        return -1;
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

    private void OnTrackChanged(object? sender, TrackChangedEventArgs e)
    {
        // Direction is authoritative from the playback layer — works the same
        // for VM commands, SMTC, taskbar buttons, and auto-advance.
        LastChangeDirection = e.Direction;

        var track = _playbackService.NowPlaying;
        HasTrack = track != null;
        IsStarred = track?.IsStarred ?? false;
        TrackTitle = track?.Title ?? string.Empty;
        AlbumName = track?.Album ?? string.Empty;
        AlbumId = track?.AlbumId;
        HasStructuredArtists = track?.HasStructuredArtists ?? false;
        DisplayArtist = track?.DisplayArtist;
        Artists = track?.Artists ?? [];
        FlatArtistText = track?.FlatArtistText ?? string.Empty;
        CoverImage = track?.CoverImage;
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

        // Surface the direction-aware event for views that need to drive
        // direction-based animations. Carries the direction in args so consumers
        // don't have to read VM state at the right moment in time.
        TrackChangedWithDirection?.Invoke(this, e);

        // Reset LastChangeDirection so anyone reading it outside the synchronous
        // chain (e.g. via a queued dispatcher callback) sees None instead of a
        // stale value from a prior change.
        LastChangeDirection = TrackChangeDirection.None;
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
