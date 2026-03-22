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
    private List<PlaybackTrack> _queue = [];
    private int _queueIndex;

    public PlaybackTrack? NowPlaying => _queueIndex >= 0 && _queueIndex < _queue.Count ? _queue[_queueIndex] : null;
    public IReadOnlyList<PlaybackTrack> Queue => _queue;
    public int QueueIndex => _queueIndex;
    public bool IsPlaying { get; private set; }
    public double Position { get; private set; }
    public double Duration { get; private set; }
    public bool HasNext => _queueIndex < _queue.Count - 1;
    public bool HasPrevious => _queueIndex > 0;

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

    public void PlayQueue(IList<PlaybackTrack> tracks, int startIndex = 0)
    {
        _queue = new List<PlaybackTrack>(tracks);
        _queueIndex = startIndex;
        PlayCurrent();
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
        PositionChanged?.Invoke(this, EventArgs.Empty);
        UpdateSmtcState();
    }

    public void NextTrack()
    {
        if (HasNext)
        {
            _queueIndex++;
            PlayCurrent();
        }
    }

    public void PreviousTrack()
    {
        // If more than 3 seconds in, restart current track; otherwise go to previous
        if (Position > 3 || !HasPrevious)
        {
            Seek(0);
        }
        else
        {
            _queueIndex--;
            PlayCurrent();
        }
    }

    public void Seek(double positionSeconds)
    {
        _mediaPlayer.Position = TimeSpan.FromSeconds(positionSeconds);
        Position = positionSeconds;
        PositionChanged?.Invoke(this, EventArgs.Empty);
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
        TrackChanged?.Invoke(this, EventArgs.Empty);

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
            if (HasNext)
            {
                _queueIndex++;
                PlayCurrent();
            }
            else
            {
                IsPlaying = false;
                Position = 0;
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
        _mediaPlayer.Dispose();
    }
}
