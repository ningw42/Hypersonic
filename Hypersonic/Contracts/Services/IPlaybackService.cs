using Hypersonic.Models;

namespace Hypersonic.Contracts.Services;

public interface IPlaybackService
{
    PlaybackTrack? NowPlaying { get; }
    IReadOnlyList<PlaybackTrack> Queue { get; }
    int QueueIndex { get; }
    bool IsPlaying { get; }
    double Position { get; }
    double Duration { get; }
    double Volume { get; set; }
    bool HasNext { get; }
    bool HasPrevious { get; }

    event EventHandler? PlaybackStateChanged;
    event EventHandler? TrackChanged;
    event EventHandler? PositionChanged;

    void PlayQueue(IList<PlaybackTrack> tracks, int startIndex = 0);
    void Resume();
    void Pause();
    void Stop();
    void NextTrack();
    void PreviousTrack();
    void Seek(double positionSeconds);
}
