using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;

namespace Hypersonic.Tests.Fakes;

public class FakePlaybackService : IPlaybackService
{
    public PlaybackTrack? NowPlaying { get; set; }
    public IReadOnlyList<PlaybackTrack> Queue => throw new NotImplementedException();
    public int QueueIndex => throw new NotImplementedException();
    public int EffectiveQueueIndex => throw new NotImplementedException();
    public bool IsPlaying { get; set; }
    public double Position { get; set; }
    public double Duration { get; set; }
    public double Volume { get; set; }
    public bool IsMuted { get; set; }
    public bool HasNext => throw new NotImplementedException();
    public bool HasPrevious => throw new NotImplementedException();
    public QueueSource? Source => throw new NotImplementedException();
    public bool ShuffleEnabled { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public RepeatMode RepeatMode { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public IReadOnlyList<PlaybackTrack> History => throw new NotImplementedException();

    public event EventHandler? PlaybackStateChanged;
    public event EventHandler<TrackChangedEventArgs>? TrackChanged;
    public event EventHandler<TrackEndedEventArgs>? TrackEnded;
    public event EventHandler? PositionChanged;
    public event EventHandler? QueueChanged;

    public void RaiseTrackChanged() => TrackChanged?.Invoke(this, new TrackChangedEventArgs(TrackChangeDirection.Jump));
    public void RaiseTrackChanged(TrackChangeDirection direction) => TrackChanged?.Invoke(this, new TrackChangedEventArgs(direction));
    public void RaisePlaybackStateChanged() => PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
    public void RaisePositionChanged() => PositionChanged?.Invoke(this, EventArgs.Empty);
    public void RaiseTrackEnded(string trackId, double duration) =>
        TrackEnded?.Invoke(this, new TrackEndedEventArgs { TrackId = trackId, Duration = duration });

    public void PlayQueue(IList<PlaybackTrack> tracks, int startIndex = 0, QueueSource? source = null) => throw new NotImplementedException();
    public void Resume() => throw new NotImplementedException();
    public void Pause() => throw new NotImplementedException();
    public void Stop() => throw new NotImplementedException();
    public void NextTrack() => throw new NotImplementedException();
    public void PreviousTrack() => throw new NotImplementedException();
    public void Seek(double positionSeconds) => throw new NotImplementedException();
    public void AddNext(IList<PlaybackTrack> tracks) => throw new NotImplementedException();
    public void AddToEnd(IList<PlaybackTrack> tracks) => throw new NotImplementedException();
    public void RemoveAt(int index) => throw new NotImplementedException();
    public void MoveTrack(int fromIndex, int toIndex) => throw new NotImplementedException();
    public void ClearUpcoming() => throw new NotImplementedException();
    public void ClearAll() => throw new NotImplementedException();
    public void JumpToTrack(string trackId) => throw new NotImplementedException();
    public int? LastJumpToQueueIndex { get; private set; }
    public void JumpToQueueIndex(int effectiveIndex) => LastJumpToQueueIndex = effectiveIndex;
    public IReadOnlyList<PlaybackTrack> GetEffectiveQueue() => throw new NotImplementedException();
}
