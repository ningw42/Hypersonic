using Hypersonic.Core.Models;

namespace Hypersonic.Core.Contracts.Services;

/// <summary>
/// Read-only observation surface for the playback engine — the query half of the command/query
/// split with <see cref="IPlaybackService"/>. Pure observers depend on this so they physically
/// cannot mutate playback. Grown incrementally as more pure-observer consumers migrate; today it
/// carries the progress-sync slice. Implemented by the same singleton as <see cref="IPlaybackService"/>.
/// </summary>
public interface IReadOnlyPlaybackState
{
    PlaybackTrack? NowPlaying { get; }
    double Position { get; }            // seconds; the coarse ~500ms anchor
    double Duration { get; }            // seconds
    bool IsPlaying { get; }

    event EventHandler? PlaybackStateChanged;
    event EventHandler<TrackChangedEventArgs>? TrackChanged;
    event EventHandler? PositionChanged;
}
