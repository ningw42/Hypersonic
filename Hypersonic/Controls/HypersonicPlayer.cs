using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Hypersonic.Client;
using Hypersonic.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace Hypersonic.Controls;

public sealed class HypersonicPlayer : Control, INotifyPropertyChanged
{
    private MediaPlayer _mediaPlayer;
    private DispatcherTimer _positionTimer;
    private bool _isPlaying;
    private bool _isLoading;
    private double _position;
    private double _duration;
    private double _volume = 1.0;
    private ISubsonicClient _subsonicClient;

    public event PropertyChangedEventHandler PropertyChanged;

    public static readonly DependencyProperty NowPlayingRecordingProperty = DependencyProperty.Register(
        nameof(NowPlayingRecording),
        typeof(IRecording),
        typeof(HypersonicPlayer),
        new PropertyMetadata(default(IRecording), OnNowPlayingRecordingChanged));

    public static readonly DependencyProperty IsPlayingProperty = DependencyProperty.Register(
        nameof(IsPlaying),
        typeof(bool),
        typeof(HypersonicPlayer),
        new PropertyMetadata(false));

    public static readonly DependencyProperty VolumeProperty = DependencyProperty.Register(
        nameof(Volume),
        typeof(double),
        typeof(HypersonicPlayer),
        new PropertyMetadata(1.0, OnVolumeChanged));

    public static readonly DependencyProperty PositionProperty = DependencyProperty.Register(
        nameof(Position),
        typeof(double),
        typeof(HypersonicPlayer),
        new PropertyMetadata(0.0, OnPositionChanged));

    public static readonly DependencyProperty DurationProperty = DependencyProperty.Register(
        nameof(Duration),
        typeof(double),
        typeof(HypersonicPlayer),
        new PropertyMetadata(0.0));

    public static readonly DependencyProperty SubsonicClientProperty = DependencyProperty.Register(
        nameof(SubsonicClient),
        typeof(ISubsonicClient),
        typeof(HypersonicPlayer),
        new PropertyMetadata(null, OnSubsonicClientChanged));

    public IRecording NowPlayingRecording
    {
        get => (IRecording)GetValue(NowPlayingRecordingProperty);
        set => SetValue(NowPlayingRecordingProperty, value);
    }

    public bool IsPlaying
    {
        get => (bool)GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    public double Volume
    {
        get => (double)GetValue(VolumeProperty);
        set => SetValue(VolumeProperty, value);
    }

    public double Position
    {
        get => (double)GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    public double Duration
    {
        get => (double)GetValue(DurationProperty);
        private set => SetValue(DurationProperty, value);
    }

    public ISubsonicClient SubsonicClient
    {
        get => (ISubsonicClient)GetValue(SubsonicClientProperty);
        set => SetValue(SubsonicClientProperty, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            _isLoading = value;
            OnPropertyChanged();
        }
    }

    public string Artist => NowPlayingRecording?.Artist ?? "Unknown Artist";

    public string Title => NowPlayingRecording?.Title ?? "Unknown Title";

    public string Album => NowPlayingRecording?.Album ?? "Unknown Album";

    public HypersonicPlayer()
    {
        this.DefaultStyleKey = typeof(HypersonicPlayer);
        InitializeMediaPlayer();
    }    private void InitializeMediaPlayer()
    {
        _mediaPlayer = new MediaPlayer();
        _mediaPlayer.MediaOpened += OnMediaOpened;
        _mediaPlayer.MediaEnded += OnMediaEnded;
        _mediaPlayer.MediaFailed += OnMediaFailed;
        _mediaPlayer.CurrentStateChanged += OnCurrentStateChanged;

        // Timer to update position
        _positionTimer = new DispatcherTimer();
        _positionTimer.Interval = TimeSpan.FromMilliseconds(500);
        _positionTimer.Tick += OnPositionTimerTick;
    }

    public async Task PlayAsync()
    {
        if (NowPlayingRecording == null || SubsonicClient == null) return;

        try
        {
            IsLoading = true;
            
            // Get streaming URI from Subsonic client
            var streamUri = SubsonicClient.GetStreamUri(NowPlayingRecording.Id);
            var mediaSource = MediaSource.CreateFromUri(streamUri);
            _mediaPlayer.Source = mediaSource;
            _mediaPlayer.Play();
        }
        catch (Exception ex)
        {
            // Handle playback error
            System.Diagnostics.Debug.WriteLine($"Playback error: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void Pause()
    {
        _mediaPlayer?.Pause();
    }

    public void Stop()
    {
        _mediaPlayer?.Pause();
        Position = 0;
    }

    public void Seek(TimeSpan position)
    {
        if (_mediaPlayer != null)
        {
            _mediaPlayer.Position = position;
        }
    }

    private static void OnNowPlayingRecordingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HypersonicPlayer player)
        {
            player.OnPropertyChanged(nameof(Artist));
            player.OnPropertyChanged(nameof(Title));
            player.OnPropertyChanged(nameof(Album));
            
            // Auto-play new recording if currently playing
            if (player.IsPlaying && e.NewValue != null)
            {
                _ = player.PlayAsync();
            }
        }
    }

    private static void OnVolumeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HypersonicPlayer player && player._mediaPlayer != null)
        {
            player._mediaPlayer.Volume = (double)e.NewValue;
        }
    }

    private static void OnPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HypersonicPlayer player && player._mediaPlayer != null)
        {
            var position = TimeSpan.FromSeconds((double)e.NewValue);
            player._mediaPlayer.Position = position;
        }
    }

    private static void OnSubsonicClientChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HypersonicPlayer player)
        {
            player._subsonicClient = e.NewValue as ISubsonicClient;
        }
    }

    private void OnMediaOpened(MediaPlayer sender, object args)
    {
        Duration = sender.NaturalDuration.TotalSeconds;
        IsLoading = false;
    }

    private void OnMediaEnded(MediaPlayer sender, object args)
    {
        IsPlaying = false;
        Position = 0;
    }

    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        IsLoading = false;
        IsPlaying = false;
        System.Diagnostics.Debug.WriteLine($"Media failed: {args.ErrorMessage}");
    }    private void OnCurrentStateChanged(MediaPlayer sender, object args)
    {
        IsPlaying = sender.CurrentState == MediaPlayerState.Playing;
        
        if (IsPlaying)
        {
            _positionTimer.Start();
        }
        else
        {
            _positionTimer.Stop();
        }
    }

    private void OnPositionTimerTick(object sender, object e)
    {
        if (_mediaPlayer != null && IsPlaying)
        {
            Position = _mediaPlayer.Position.TotalSeconds;
        }
    }

    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        
        // Find and wire up template parts if needed
        if (GetTemplateChild("PlayButton") is Button playButton)
        {
            playButton.Click += async (s, e) => await PlayAsync();
        }

        if (GetTemplateChild("PauseButton") is Button pauseButton)
        {
            pauseButton.Click += (s, e) => Pause();
        }

        if (GetTemplateChild("StopButton") is Button stopButton)
        {
            stopButton.Click += (s, e) => Stop();
        }

        if (GetTemplateChild("VolumeSlider") is Slider volumeSlider)
        {
            volumeSlider.ValueChanged += (s, e) => Volume = e.NewValue / 100.0;
        }

        if (GetTemplateChild("PositionSlider") is Slider positionSlider)
        {
            positionSlider.ValueChanged += (s, e) => Position = e.NewValue;
        }
    }    ~HypersonicPlayer()
    {
        _positionTimer?.Stop();
        _mediaPlayer?.Dispose();
    }
}
