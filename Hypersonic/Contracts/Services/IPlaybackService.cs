using Hypersonic.Models;

namespace Hypersonic.Contracts.Services;

public interface IPlaybackService
{
    PlaybackTrack? NowPlaying { get; }
    IReadOnlyList<PlaybackTrack> Queue { get; }
    int QueueIndex { get; }
    int EffectiveQueueIndex { get; }
    bool IsPlaying { get; }
    double Position { get; }
    double Duration { get; }
    double Volume { get; set; }
    bool HasNext { get; }
    bool HasPrevious { get; }
    QueueSource? Source { get; }
    bool ShuffleEnabled { get; set; }
    RepeatMode RepeatMode { get; set; }
    IReadOnlyList<PlaybackTrack> History { get; }

    event EventHandler? PlaybackStateChanged;
    event EventHandler? TrackChanged;
    event EventHandler? PositionChanged;
    event EventHandler? QueueChanged;

    void PlayQueue(IList<PlaybackTrack> tracks, int startIndex = 0, QueueSource? source = null);
    void Resume();
    void Pause();
    void Stop();
    void NextTrack();
    void PreviousTrack();
    void Seek(double positionSeconds);
    void AddNext(IList<PlaybackTrack> tracks);
    void AddToEnd(IList<PlaybackTrack> tracks);
    void RemoveAt(int index);
    void MoveTrack(int fromIndex, int toIndex);
    void ClearUpcoming();
    void ClearAll();
    IReadOnlyList<PlaybackTrack> GetEffectiveQueue();
}
