using Hypersonic.Core.Models;

namespace Hypersonic.Core.Contracts.Services;

public class TrackEndedEventArgs : EventArgs
{
    public required string TrackId { get; init; }
    public double Duration { get; init; }
}

/// <summary>
/// Event payload for <see cref="IPlaybackService.TrackChanged"/>. Carries the
/// direction of the change so consumers (e.g. cover-art animations) can pick
/// the right transition without having to read mutable state on the source.
/// </summary>
public sealed class TrackChangedEventArgs : EventArgs
{
    public TrackChangeDirection Direction { get; }

    public TrackChangedEventArgs(TrackChangeDirection direction)
    {
        Direction = direction;
    }
}

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
    bool IsMuted { get; set; }
    bool HasNext { get; }
    bool HasPrevious { get; }
    QueueSource? Source { get; }
    bool ShuffleEnabled { get; set; }
    RepeatMode RepeatMode { get; set; }
    IReadOnlyList<PlaybackTrack> History { get; }

    event EventHandler? PlaybackStateChanged;
    event EventHandler<TrackChangedEventArgs>? TrackChanged;
    event EventHandler<TrackEndedEventArgs>? TrackEnded;
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
    void JumpToTrack(string trackId);
    void JumpToQueueIndex(int effectiveIndex);
    IReadOnlyList<PlaybackTrack> GetEffectiveQueue();
}
