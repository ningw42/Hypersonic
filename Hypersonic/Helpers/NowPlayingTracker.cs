using Microsoft.UI.Dispatching;

using Hypersonic.Core.Contracts.Services;
using Hypersonic.Models;

namespace Hypersonic.Helpers;

public class NowPlayingTracker
{
    private readonly IPlaybackService _playbackService;
    private readonly DispatcherQueue? _dispatcherQueue;
    private readonly Func<IEnumerable<SongInfo>> _getSongs;
    private readonly bool _synchronous;
    private bool _isActive;
    private string? _lastNowPlayingId;
    private bool _lastIsPlaying;

    public NowPlayingTracker(
        IPlaybackService playbackService,
        Func<IEnumerable<SongInfo>> getSongs)
    {
        _playbackService = playbackService;
        _getSongs = getSongs;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("NowPlayingTracker must be created on the UI thread.");
    }

    internal NowPlayingTracker(
        IPlaybackService playbackService,
        Func<IEnumerable<SongInfo>> getSongs,
        bool synchronous)
    {
        _playbackService = playbackService;
        _getSongs = getSongs;
        _synchronous = synchronous;
    }

    public void Subscribe()
    {
        if (_isActive)
        {
            return;
        }

        _isActive = true;
        _playbackService.TrackChanged += OnTrackChanged;
        _playbackService.PlaybackStateChanged += OnPlaybackStateChanged;
        Refresh(force: true);
    }

    public void Unsubscribe()
    {
        if (!_isActive)
        {
            return;
        }

        _isActive = false;
        _playbackService.TrackChanged -= OnTrackChanged;
        _playbackService.PlaybackStateChanged -= OnPlaybackStateChanged;

        foreach (var song in _getSongs())
        {
            song.IsNowPlaying = false;
            song.IsPlaying = false;
        }

        _lastNowPlayingId = null;
        _lastIsPlaying = false;
    }

    public void Refresh(bool force = false)
    {
        if (!_isActive)
        {
            return;
        }

        var nowPlayingId = _playbackService.NowPlaying?.Id;
        var isPlaying = _playbackService.IsPlaying;

        if (!force && nowPlayingId == _lastNowPlayingId && isPlaying == _lastIsPlaying)
        {
            return;
        }

        _lastNowPlayingId = nowPlayingId;
        _lastIsPlaying = isPlaying;

        foreach (var song in _getSongs())
        {
            var isNowPlaying = song.Id == nowPlayingId;
            song.IsNowPlaying = isNowPlaying;
            song.IsPlaying = isNowPlaying && isPlaying;
        }
    }

    private void OnTrackChanged(object? sender, EventArgs e)
    {
        DispatchRefresh();
    }

    private void OnPlaybackStateChanged(object? sender, EventArgs e)
    {
        DispatchRefresh();
    }

    private void DispatchRefresh()
    {
        if (_synchronous)
        {
            Refresh();
        }
        else
        {
            _dispatcherQueue!.TryEnqueue(() => Refresh());
        }
    }
}
