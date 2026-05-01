using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;
using Hypersonic.Core.Services;

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
    private readonly ILogger _log;
    private QueueSource? _source;
    // MediaPlayer events can arrive after the app has already switched sources.
    // Each playback source transition increments this generation; handlers capture
    // it when the event fires and ignore the callback after dispatch if it no longer
    // matches. If playback races keep growing, move these signals into an explicit
    // dispatcher-owned playback command pipeline while keeping this as the source token.
    private int _playbackSourceGeneration;
    private bool _pendingPlayOnOpen;
    private MediaPlaybackItem? _currentPlaybackItem;
    private MediaSource? _currentMediaSource;

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

    public bool IsMuted
    {
        get => _mediaPlayer.IsMuted;
        set
        {
            if (_mediaPlayer.IsMuted == value)
            {
                return;
            }

            _mediaPlayer.IsMuted = value;
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? PlaybackStateChanged;
    public event EventHandler<TrackChangedEventArgs>? TrackChanged;
    public event EventHandler<TrackEndedEventArgs>? TrackEnded;
    public event EventHandler? PositionChanged;
    public event EventHandler? QueueChanged;

    public PlaybackService(ILoggingService logService, IAppStateService appStateService)
    {
        _log = logService.GetLogger(nameof(PlaybackService));
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _positionTimer = _dispatcherQueue.CreateTimer();
        _positionTimer.Interval = TimeSpan.FromMilliseconds(500);
        _positionTimer.Tick += OnPositionTimerTick;

        _mediaPlayer = new MediaPlayer();
        _mediaPlayer.MediaOpened += OnMediaOpened;
        _mediaPlayer.MediaEnded += OnMediaEnded;
        _mediaPlayer.MediaFailed += OnMediaFailed;
        _mediaPlayer.CurrentStateChanged += OnCurrentStateChanged;

        // Hook CommandManager for next/previous so they route through PlaybackQueue.
        // Play/pause and playback status are managed automatically by CommandManager.
        var cmd = _mediaPlayer.CommandManager;
        cmd.NextReceived += OnCommandManagerNextReceived;
        cmd.PreviousReceived += OnCommandManagerPreviousReceived;

        // Restore persisted playback preferences.
        var playback = appStateService.State.Playback;
        _mediaPlayer.Volume = Math.Clamp(playback.Volume / 100.0, 0, 1);
        _mediaPlayer.IsMuted = playback.IsMuted;
        _playbackQueue.SetShuffle(playback.ShuffleEnabled);
        _playbackQueue.RepeatMode = playback.RepeatMode;
    }

    public void PlayQueue(IList<PlaybackTrack> tracks, int startIndex = 0, QueueSource? source = null)
    {
        _log.Info("Queue started", new { TrackCount = tracks.Count, StartIndex = startIndex });
        _source = source;
        _playbackQueue.SetQueue(tracks, startIndex);

        if (NowPlaying?.StreamUri != null)
        {
            PlayCurrent(TrackChangeDirection.Jump);
        }
        else
        {
            ClearCurrentPlaybackMedia();
            UpdateCurrentTrackWithoutPlayback(NowPlaying, TrackChangeDirection.Jump);
        }

        QueueChanged?.Invoke(this, EventArgs.Empty);
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        UpdateSmtcState();
    }

    public void Resume()
    {
        if (NowPlaying?.StreamUri != null && _currentPlaybackItem != null)
        {
            _mediaPlayer.Play();
        }
    }

    public void Pause()
    {
        _pendingPlayOnOpen = false;
        _mediaPlayer.Pause();
    }

    public void Stop()
    {
        _pendingPlayOnOpen = false;
        _mediaPlayer.Pause();
        _mediaPlayer.Position = TimeSpan.Zero;
        Position = 0;
        IsPlaying = false;
        _positionTimer.Stop();
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
            PlayCurrent(TrackChangeDirection.Next);
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
            PlayCurrent(TrackChangeDirection.Previous);
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
        _log.Debug("Adding tracks after current", new { Count = tracks.Count });
        var prevHasNext = HasNext;
        var prevHasPrevious = HasPrevious;
        _playbackQueue.AddNext(tracks);
        QueueChanged?.Invoke(this, EventArgs.Empty);
        FirePlaybackStateChangedIfHasChanged(prevHasNext, prevHasPrevious);
    }

    public void AddToEnd(IList<PlaybackTrack> tracks)
    {
        _log.Debug("Adding tracks to end", new { Count = tracks.Count });
        var prevHasNext = HasNext;
        var prevHasPrevious = HasPrevious;
        _playbackQueue.AddToEnd(tracks);
        QueueChanged?.Invoke(this, EventArgs.Empty);
        FirePlaybackStateChangedIfHasChanged(prevHasNext, prevHasPrevious);
    }

    public void RemoveAt(int index)
    {
        _log.Debug("Removing track from queue", new { Index = index });
        var prevHasNext = HasNext;
        var prevHasPrevious = HasPrevious;
        var newCurrent = _playbackQueue.RemoveAt(index);

        QueueChanged?.Invoke(this, EventArgs.Empty);

        if (newCurrent != null)
        {
            // Current track was removed and we advanced to next.
            if (newCurrent.StreamUri != null)
            {
                PlayCurrent(TrackChangeDirection.Jump);
            }
            else
            {
                ClearCurrentPlaybackMedia();
                UpdateCurrentTrackWithoutPlayback(newCurrent, TrackChangeDirection.Jump);
            }
        }
        else if (_playbackQueue.CurrentTrack == null && _playbackQueue.Tracks.Count == 0)
        {
            // Queue is empty after removal — full teardown.
            ClearCurrentPlaybackMedia();
            IsPlaying = false;
            _positionTimer.Stop();
            Position = 0;
            Duration = 0;
            _source = null;
            ClearSmtcDisplay();
            TrackChanged?.Invoke(this, new TrackChangedEventArgs(TrackChangeDirection.Jump));
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
        _log.Info("Queue cleared");
        ClearCurrentPlaybackMedia();
        _source = null;
        _playbackQueue.ClearAll();
        IsPlaying = false;
        _positionTimer.Stop();
        Position = 0;
        Duration = 0;
        ClearSmtcDisplay();
        QueueChanged?.Invoke(this, EventArgs.Empty);
        TrackChanged?.Invoke(this, new TrackChangedEventArgs(TrackChangeDirection.Jump));
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
            _log.Debug("JumpToTrack ignored, track not in queue", new { TrackId = trackId });
            return;
        }

        _log.Info("Jumped to track", new { TrackId = trackId });

        PlayCurrent(TrackChangeDirection.Jump);
        FirePlaybackStateChangedIfHasChanged(prevHasNext, prevHasPrevious);
    }

    public void JumpToQueueIndex(int effectiveIndex)
    {
        var prevHasNext = HasNext;
        var prevHasPrevious = HasPrevious;
        var result = _playbackQueue.JumpToQueueIndex(effectiveIndex);
        if (result == null)
        {
            _log.Debug("JumpToQueueIndex ignored", new { EffectiveIndex = effectiveIndex });
            return;
        }

        _log.Info("Jumped to queue index", new { EffectiveIndex = effectiveIndex });

        PlayCurrent(TrackChangeDirection.Jump);
        FirePlaybackStateChangedIfHasChanged(prevHasNext, prevHasPrevious);
    }

    private void PlayCurrent(TrackChangeDirection direction)
    {
        var track = NowPlaying;
        if (track?.StreamUri == null)
        {
            ClearCurrentPlaybackMedia();
            UpdateCurrentTrackWithoutPlayback(track, direction);
            return;
        }

        ClearCurrentPlaybackMedia();

        Duration = track.Duration;
        Position = 0;

        _currentMediaSource = MediaSource.CreateFromUri(track.StreamUri);
        _currentPlaybackItem = new MediaPlaybackItem(_currentMediaSource);

        var displayProperties = _currentPlaybackItem.GetDisplayProperties();
        displayProperties.Type = MediaPlaybackType.Music;
        displayProperties.MusicProperties.Title = track.Title ?? string.Empty;
        displayProperties.MusicProperties.Artist = track.FlatArtistText;
        displayProperties.MusicProperties.AlbumTitle = track.Album ?? string.Empty;
        if (track.CoverArtUri != null)
        {
            displayProperties.Thumbnail = RandomAccessStreamReference.CreateFromUri(track.CoverArtUri);
        }
        _currentPlaybackItem.ApplyDisplayProperties(displayProperties);

        var gen = Interlocked.Increment(ref _playbackSourceGeneration);
        _log.Info("Playing track", new { TrackId = track.Id, track.Title, Generation = gen });

        _pendingPlayOnOpen = true;
        _mediaPlayer.Source = _currentPlaybackItem;

        TrackChanged?.Invoke(this, new TrackChangedEventArgs(direction));

        UpdateSmtcState();
    }

    private void ClearCurrentPlaybackMedia()
    {
        Interlocked.Increment(ref _playbackSourceGeneration);
        _pendingPlayOnOpen = false;
        _mediaPlayer.Pause();
        _mediaPlayer.Source = null;
        DisposeCurrentSource();
    }

    private void UpdateCurrentTrackWithoutPlayback(PlaybackTrack? track, TrackChangeDirection direction)
    {
        Duration = track?.Duration ?? 0;
        Position = 0;
        IsPlaying = false;
        _positionTimer.Stop();
        ClearSmtcDisplay();
        TrackChanged?.Invoke(this, new TrackChangedEventArgs(direction));
        PositionChanged?.Invoke(this, EventArgs.Empty);
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        UpdateSmtcState();
    }

    private void DisposeCurrentSource()
    {
        _currentPlaybackItem = null;

        if (_currentMediaSource != null)
        {
            try
            {
                _currentMediaSource.Dispose();
            }
            catch (Exception ex)
            {
                _log.Warning("MediaSource dispose failed", exception: ex);
            }
            _currentMediaSource = null;
        }
    }

    private bool IsCurrentMediaEvent(int generation, string eventName)
    {
        var currentGeneration = Volatile.Read(ref _playbackSourceGeneration);
        if (PlaybackMediaEventGuard.IsEventForCurrentSource(
            generation,
            currentGeneration,
            _currentPlaybackItem != null))
        {
            return true;
        }

        if (eventName != nameof(MediaPlayer.CurrentStateChanged))
        {
            _log.Debug("Ignoring stale media event", new
            {
                EventName = eventName,
                EventGeneration = generation,
                CurrentGeneration = currentGeneration,
                HasCurrentItem = _currentPlaybackItem != null,
                TrackId = NowPlaying?.Id
            });
        }
        return false;
    }

    private void UpdatePlaybackState(bool isPlaying)
    {
        var wasPlaying = IsPlaying;
        IsPlaying = isPlaying;

        if (IsPlaying)
        {
            _positionTimer.Start();
        }
        else
        {
            _positionTimer.Stop();
        }

        if (wasPlaying != IsPlaying)
        {
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void UpdateSmtcState()
    {
        var cmd = _mediaPlayer.CommandManager;
        cmd.NextBehavior.EnablingRule = HasNext
            ? MediaCommandEnablingRule.Always
            : MediaCommandEnablingRule.Never;
        cmd.PreviousBehavior.EnablingRule = HasPrevious
            ? MediaCommandEnablingRule.Always
            : MediaCommandEnablingRule.Never;
    }

    private void ClearSmtcDisplay()
    {
        var updater = _mediaPlayer.SystemMediaTransportControls.DisplayUpdater;
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

    private void OnCommandManagerNextReceived(MediaPlaybackCommandManager sender,
        MediaPlaybackCommandManagerNextReceivedEventArgs args)
    {
        var deferral = args.GetDeferral();
        var enqueued = _dispatcherQueue.TryEnqueue(() =>
        {
            args.Handled = true;
            NextTrack();
            deferral.Complete();
        });

        if (!enqueued)
        {
            deferral.Complete();
        }
    }

    private void OnCommandManagerPreviousReceived(MediaPlaybackCommandManager sender,
        MediaPlaybackCommandManagerPreviousReceivedEventArgs args)
    {
        var deferral = args.GetDeferral();
        var enqueued = _dispatcherQueue.TryEnqueue(() =>
        {
            args.Handled = true;
            PreviousTrack();
            deferral.Complete();
        });

        if (!enqueued)
        {
            deferral.Complete();
        }
    }

    private void OnMediaOpened(MediaPlayer sender, object args)
    {
        var genAtFire = Volatile.Read(ref _playbackSourceGeneration);
        var naturalDurationSeconds = sender.NaturalDuration.TotalSeconds;
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (!IsCurrentMediaEvent(genAtFire, nameof(MediaPlayer.MediaOpened)))
            {
                return;
            }

            if (naturalDurationSeconds > 0)
            {
                Duration = naturalDurationSeconds;
            }

            if (_pendingPlayOnOpen)
            {
                _pendingPlayOnOpen = false;
                if (!IsCurrentMediaEvent(genAtFire, nameof(MediaPlayer.MediaOpened)))
                {
                    return;
                }

                _mediaPlayer.Position = TimeSpan.Zero;
                _mediaPlayer.Play();
            }

            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private void OnMediaEnded(MediaPlayer sender, object args)
    {
        var genAtFire = Volatile.Read(ref _playbackSourceGeneration);
        var posAtFire = sender.Position.TotalSeconds;
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (!IsCurrentMediaEvent(genAtFire, nameof(MediaPlayer.MediaEnded)))
            {
                return;
            }

            var mediaEndedGuardResult = PlaybackMediaEventGuard.EvaluateMediaEnded(
                _pendingPlayOnOpen,
                posAtFire,
                Duration);

            if (mediaEndedGuardResult == MediaEndedGuardResult.IgnorePendingOpen)
            {
                _log.Debug("Ignoring MediaEnded while current track is still opening", new
                {
                    TrackId = NowPlaying?.Id,
                    Position = posAtFire,
                    Duration
                });
                return;
            }

            if (mediaEndedGuardResult == MediaEndedGuardResult.IgnoreBeforeDuration)
            {
                _log.Debug("Ignoring MediaEnded before current track duration", new
                {
                    TrackId = NowPlaying?.Id,
                    Position = posAtFire,
                    Duration
                });
                return;
            }

            _log.Info("Track ended", new
            {
                TrackId = NowPlaying?.Id,
                Position = posAtFire,
                Duration
            });

            if (NowPlaying != null)
            {
                TrackEnded?.Invoke(this, new TrackEndedEventArgs
                {
                    TrackId = NowPlaying.Id,
                    Duration = Duration
                });
            }

            // TrackEnded subscribers may start another transition; do not also auto-advance.
            if (!IsCurrentMediaEvent(genAtFire, nameof(MediaPlayer.MediaEnded)))
            {
                return;
            }

            var prevHasNext = HasNext;
            var prevHasPrevious = HasPrevious;
            var next = _playbackQueue.Advance();

            if (next != null)
            {
                _log.Debug("Auto-advancing to next track");
                PlayCurrent(TrackChangeDirection.Next);
                FirePlaybackStateChangedIfHasChanged(prevHasNext, prevHasPrevious);
            }
            else
            {
                // End of queue — stop playback, reset position and duration.
                // NowPlaying remains set so the UI shows what was last playing.
                _log.Info("Queue ended");
                IsPlaying = false;
                _positionTimer.Stop();
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
        var genAtFire = Volatile.Read(ref _playbackSourceGeneration);
        var errorCode = args.ExtendedErrorCode.HResult;
        var errorMessage = args.ErrorMessage;
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (!IsCurrentMediaEvent(genAtFire, nameof(MediaPlayer.MediaFailed)))
            {
                return;
            }

            _pendingPlayOnOpen = false;
            IsPlaying = false;
            _positionTimer.Stop();
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
            _log.Error("Media playback failed", new
            {
                ErrorCode = errorCode,
                ErrorMessage = errorMessage,
                TrackId = NowPlaying?.Id,
            });
        });
    }

    private void OnCurrentStateChanged(MediaPlayer sender, object args)
    {
        var genAtFire = Volatile.Read(ref _playbackSourceGeneration);
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (!IsCurrentMediaEvent(genAtFire, nameof(MediaPlayer.CurrentStateChanged)))
            {
                return;
            }

            UpdatePlaybackState(sender.CurrentState == MediaPlayerState.Playing);
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
        _mediaPlayer.CommandManager.NextReceived -= OnCommandManagerNextReceived;
        _mediaPlayer.CommandManager.PreviousReceived -= OnCommandManagerPreviousReceived;
        _mediaPlayer.Source = null;
        DisposeCurrentSource();
        _mediaPlayer.Dispose();
    }
}
