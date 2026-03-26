using Hypersonic.Contracts.Services;
using Hypersonic.Models;

using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;

using Microsoft.UI.Dispatching;

namespace Hypersonic.Services;

public class PlaybackService : IPlaybackService, IDisposable
{
    private readonly MediaPlayer _mediaPlayer;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly DispatcherQueueTimer _positionTimer;
    private readonly PlaybackQueue _playbackQueue = new();
    private QueueSource? _source;

    public PlaybackTrack? NowPlaying => _playbackQueue.CurrentTrack;
    public IReadOnlyList<PlaybackTrack> Queue => _playbackQueue.Tracks;
    public int QueueIndex => _playbackQueue.CurrentIndex;
    public int EffectiveQueueIndex => _playbackQueue.EffectiveIndex;
    public bool IsPlaying { get; private set; }
    public double Position { get; private set; }
    public double Duration { get; private set; }
    public bool HasNext => _playbackQueue.HasNext;
    public bool HasPrevious => _playbackQueue.HasPrevious;
    public QueueSource? Source => _source;
    public IReadOnlyList<PlaybackTrack> History => _playbackQueue.History;

    public bool ShuffleEnabled
    {
        get => _playbackQueue.ShuffleEnabled;
        set
        {
            if (_playbackQueue.ShuffleEnabled == value)
            {
                return;
            }

            _playbackQueue.SetShuffle(value);
            QueueChanged?.Invoke(this, EventArgs.Empty);
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
            UpdateSmtcState();
        }
    }

    public RepeatMode RepeatMode
    {
        get => _playbackQueue.RepeatMode;
        set
        {
            if (_playbackQueue.RepeatMode == value)
            {
                return; // Idempotent.
            }

            _playbackQueue.RepeatMode = value;
            // Always fire PlaybackStateChanged on repeat mode change per design.
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
            UpdateSmtcState();
        }
    }

    public double Volume
    {
        get => _mediaPlayer.Volume;
        set
        {
            _mediaPlayer.Volume = Math.Clamp(value, 0, 1);
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? PlaybackStateChanged;
    public event EventHandler? TrackChanged;
    public event EventHandler? PositionChanged;
    public event EventHandler? QueueChanged;

    public PlaybackService()
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _positionTimer = _dispatcherQueue.CreateTimer();
        _positionTimer.Interval = TimeSpan.FromMilliseconds(500);
        _positionTimer.Tick += OnPositionTimerTick;

        _mediaPlayer = new MediaPlayer();
        _mediaPlayer.MediaOpened += OnMediaOpened;
        _mediaPlayer.MediaEnded += OnMediaEnded;
        _mediaPlayer.MediaFailed += OnMediaFailed;
        _mediaPlayer.CurrentStateChanged += OnCurrentStateChanged;

        // Disable the automatic CommandManager so SMTC doesn't add its own
        // taskbar thumbnail buttons (we use ITaskbarList3 for those instead).
        // We manage SMTC metadata manually via MediaPlaybackItem display properties.
        _mediaPlayer.CommandManager.IsEnabled = false;

        // Wire up SMTC transport controls directly for media key support.
        var smtc = _mediaPlayer.SystemMediaTransportControls;
        smtc.IsEnabled = true;
        smtc.IsPlayEnabled = true;
        smtc.IsPauseEnabled = true;
        smtc.IsNextEnabled = false;
        smtc.IsPreviousEnabled = false;
        smtc.ButtonPressed += OnSmtcButtonPressed;
    }

    public void PlayQueue(IList<PlaybackTrack> tracks, int startIndex = 0, QueueSource? source = null)
    {
        _source = source;
        _playbackQueue.SetQueue(tracks, startIndex);

        if (NowPlaying?.StreamUri != null)
        {
            PlayCurrent();
        }
        else
        {
            // NowPlaying changed but track can't be played — still notify consumers.
            Duration = NowPlaying?.Duration ?? 0;
            Position = 0;
            TrackChanged?.Invoke(this, EventArgs.Empty);
        }

        QueueChanged?.Invoke(this, EventArgs.Empty);
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        UpdateSmtcState();
    }

    public void Resume()
    {
        if (NowPlaying != null)
        {
            _mediaPlayer.Play();
        }
    }

    public void Pause()
    {
        _mediaPlayer.Pause();
    }

    public void Stop()
    {
        _mediaPlayer.Pause();
        _mediaPlayer.Position = TimeSpan.Zero;
        Position = 0;
        IsPlaying = false;
        PositionChanged?.Invoke(this, EventArgs.Empty);
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        UpdateSmtcState();
    }

    public void NextTrack()
    {
        var prevHasNext = HasNext;
        var prevHasPrevious = HasPrevious;
        var next = _playbackQueue.AdvanceManual();
        if (next != null)
        {
            PlayCurrent();
        }
        // If null: at end of queue, no movement happened, no state to clean up.
        FirePlaybackStateChangedIfHasChanged(prevHasNext, prevHasPrevious);
    }

    public void PreviousTrack()
    {
        // If more than 3 seconds in, restart current track.
        if (Position > 3)
        {
            Seek(0);
            return;
        }

        var prevHasNext = HasNext;
        var prevHasPrevious = HasPrevious;
        var previous = _playbackQueue.GoBack();
        if (previous != null)
        {
            PlayCurrent();
        }
        else
        {
            // Nothing to go back to — restart current track.
            Seek(0);
        }
        FirePlaybackStateChangedIfHasChanged(prevHasNext, prevHasPrevious);
    }

    public void Seek(double positionSeconds)
    {
        _mediaPlayer.Position = TimeSpan.FromSeconds(positionSeconds);
        Position = positionSeconds;
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void AddNext(IList<PlaybackTrack> tracks)
    {
        var prevHasNext = HasNext;
        var prevHasPrevious = HasPrevious;
        _playbackQueue.AddNext(tracks);
        QueueChanged?.Invoke(this, EventArgs.Empty);
        FirePlaybackStateChangedIfHasChanged(prevHasNext, prevHasPrevious);
    }

    public void AddToEnd(IList<PlaybackTrack> tracks)
    {
        var prevHasNext = HasNext;
        var prevHasPrevious = HasPrevious;
        _playbackQueue.AddToEnd(tracks);
        QueueChanged?.Invoke(this, EventArgs.Empty);
        FirePlaybackStateChangedIfHasChanged(prevHasNext, prevHasPrevious);
    }

    public void RemoveAt(int index)
    {
        var prevHasNext = HasNext;
        var prevHasPrevious = HasPrevious;
        var newCurrent = _playbackQueue.RemoveAt(index);

        QueueChanged?.Invoke(this, EventArgs.Empty);

        if (newCurrent != null)
        {
            // Current track was removed and we advanced to next.
            if (newCurrent.StreamUri != null)
            {
                PlayCurrent();
            }
            else
            {
                // New current track exists but can't be played — still notify consumers.
                Duration = newCurrent.Duration;
                Position = 0;
                TrackChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        else if (_playbackQueue.CurrentTrack == null && _playbackQueue.Tracks.Count == 0)
        {
            // Queue is empty after removal — full teardown.
            _mediaPlayer.Pause();
            _mediaPlayer.Source = null;
            IsPlaying = false;
            Position = 0;
            Duration = 0;
            _source = null;
            ClearSmtcDisplay();
            TrackChanged?.Invoke(this, EventArgs.Empty);
            PositionChanged?.Invoke(this, EventArgs.Empty);
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
            UpdateSmtcState();
            return;
        }

        FirePlaybackStateChangedIfHasChanged(prevHasNext, prevHasPrevious);
    }

    public void MoveTrack(int fromIndex, int toIndex)
    {
        var moved = _playbackQueue.MoveTrack(fromIndex, toIndex);
        if (moved)
        {
            QueueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ClearUpcoming()
    {
        var prevHasNext = HasNext;
        var prevHasPrevious = HasPrevious;
        _playbackQueue.ClearUpcoming();
        QueueChanged?.Invoke(this, EventArgs.Empty);
        FirePlaybackStateChangedIfHasChanged(prevHasNext, prevHasPrevious);
    }

    public void ClearAll()
    {
        _mediaPlayer.Pause();
        _mediaPlayer.Source = null;
        _source = null;
        _playbackQueue.ClearAll();
        IsPlaying = false;
        Position = 0;
        Duration = 0;
        ClearSmtcDisplay();
        QueueChanged?.Invoke(this, EventArgs.Empty);
        TrackChanged?.Invoke(this, EventArgs.Empty);
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        PositionChanged?.Invoke(this, EventArgs.Empty);
        UpdateSmtcState();
    }

    public IReadOnlyList<PlaybackTrack> GetEffectiveQueue() => _playbackQueue.GetEffectiveQueue();

    public void JumpToTrack(string trackId)
    {
        var prevHasNext = HasNext;
        var prevHasPrevious = HasPrevious;
        var result = _playbackQueue.JumpToTrack(trackId);
        if (result == null)
        {
            return;
        }

        PlayCurrent();
        FirePlaybackStateChangedIfHasChanged(prevHasNext, prevHasPrevious);
    }

    private void PlayCurrent()
    {
        var track = NowPlaying;
        if (track?.StreamUri == null)
        {
            return;
        }

        Duration = track.Duration;
        Position = 0;

        var source = MediaSource.CreateFromUri(track.StreamUri);
        var playbackItem = new MediaPlaybackItem(source);

        var displayProperties = playbackItem.GetDisplayProperties();
        displayProperties.Type = MediaPlaybackType.Music;
        displayProperties.MusicProperties.Title = track.Title ?? string.Empty;
        displayProperties.MusicProperties.Artist = track.Artist ?? string.Empty;
        displayProperties.MusicProperties.AlbumTitle = track.Album ?? string.Empty;
        if (track.CoverArtUri != null)
        {
            displayProperties.Thumbnail = RandomAccessStreamReference.CreateFromUri(track.CoverArtUri);
        }
        playbackItem.ApplyDisplayProperties(displayProperties);

        _mediaPlayer.Source = playbackItem;
        _mediaPlayer.Play();

        // Fire TrackChanged after source is set — consumers can read NowPlaying.
        TrackChanged?.Invoke(this, EventArgs.Empty);

        // Update SMTC display manually since CommandManager is disabled
        var smtc = _mediaPlayer.SystemMediaTransportControls;
        smtc.PlaybackStatus = MediaPlaybackStatus.Playing;
        var updater = smtc.DisplayUpdater;
        updater.Type = MediaPlaybackType.Music;
        updater.MusicProperties.Title = track.Title ?? string.Empty;
        updater.MusicProperties.Artist = track.Artist ?? string.Empty;
        updater.MusicProperties.AlbumTitle = track.Album ?? string.Empty;
        if (track.CoverArtUri != null)
        {
            updater.Thumbnail = RandomAccessStreamReference.CreateFromUri(track.CoverArtUri);
        }
        updater.Update();

        UpdateSmtcState();
    }

    private void UpdateSmtcState()
    {
        var smtc = _mediaPlayer.SystemMediaTransportControls;
        smtc.IsNextEnabled = HasNext;
        smtc.IsPreviousEnabled = HasPrevious;
    }

    private void ClearSmtcDisplay()
    {
        var smtc = _mediaPlayer.SystemMediaTransportControls;
        smtc.PlaybackStatus = MediaPlaybackStatus.Closed;
        var updater = smtc.DisplayUpdater;
        updater.ClearAll();
        updater.Update();
    }

    private void FirePlaybackStateChangedIfHasChanged(bool prevHasNext, bool prevHasPrevious)
    {
        if (HasNext != prevHasNext || HasPrevious != prevHasPrevious)
        {
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
            UpdateSmtcState();
        }
    }

    private void OnSmtcButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            switch (args.Button)
            {
                case SystemMediaTransportControlsButton.Play:
                    Resume();
                    break;
                case SystemMediaTransportControlsButton.Pause:
                    Pause();
                    break;
                case SystemMediaTransportControlsButton.Next:
                    NextTrack();
                    break;
                case SystemMediaTransportControlsButton.Previous:
                    PreviousTrack();
                    break;
            }
        });
    }

    private void OnMediaOpened(MediaPlayer sender, object args)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (sender.NaturalDuration.TotalSeconds > 0)
            {
                Duration = sender.NaturalDuration.TotalSeconds;
            }
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private void OnMediaEnded(MediaPlayer sender, object args)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            var prevHasNext = HasNext;
            var prevHasPrevious = HasPrevious;
            var next = _playbackQueue.Advance();

            if (next != null)
            {
                PlayCurrent();
                FirePlaybackStateChangedIfHasChanged(prevHasNext, prevHasPrevious);
            }
            else
            {
                // End of queue — stop playback, reset position and duration.
                // NowPlaying remains set so the UI shows what was last playing.
                IsPlaying = false;
                Position = 0;
                Duration = 0;
                PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
                PositionChanged?.Invoke(this, EventArgs.Empty);
                UpdateSmtcState();
            }
        });
    }

    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            IsPlaying = false;
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
            System.Diagnostics.Debug.WriteLine($"Media failed: {args.ErrorMessage}");
        });
    }

    private void OnCurrentStateChanged(MediaPlayer sender, object args)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            var wasPlaying = IsPlaying;
            IsPlaying = sender.CurrentState == MediaPlayerState.Playing;

            if (IsPlaying)
            {
                _positionTimer.Start();
            }
            else
            {
                _positionTimer.Stop();
            }

            // Keep SMTC playback status in sync
            _mediaPlayer.SystemMediaTransportControls.PlaybackStatus = sender.CurrentState switch
            {
                MediaPlayerState.Playing => MediaPlaybackStatus.Playing,
                MediaPlayerState.Paused => MediaPlaybackStatus.Paused,
                MediaPlayerState.Stopped => MediaPlaybackStatus.Stopped,
                _ => MediaPlaybackStatus.Closed,
            };

            if (wasPlaying != IsPlaying)
            {
                PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    private void OnPositionTimerTick(DispatcherQueueTimer sender, object args)
    {
        Position = _mediaPlayer.Position.TotalSeconds;
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _positionTimer.Stop();
        _mediaPlayer.MediaOpened -= OnMediaOpened;
        _mediaPlayer.MediaEnded -= OnMediaEnded;
        _mediaPlayer.MediaFailed -= OnMediaFailed;
        _mediaPlayer.CurrentStateChanged -= OnCurrentStateChanged;
        _mediaPlayer.SystemMediaTransportControls.ButtonPressed -= OnSmtcButtonPressed;
        _mediaPlayer.Dispose();
    }
}
