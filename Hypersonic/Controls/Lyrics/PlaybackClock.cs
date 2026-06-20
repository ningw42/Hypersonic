namespace Hypersonic.Controls.Lyrics;

/// <summary>
/// An anchored, self-interpolating playback-position estimator. <see cref="IPlaybackService.Position"/>
/// only refreshes every ~500 ms, so forwarding it raw makes the lyric wipe step in coarse jumps. This
/// clock free-runs against a monotonic wall clock between updates so the wipe stays fluid, and folds
/// each authoritative update back in either as a hard <i>seek</i> (large delta → snap) or a smooth
/// <i>converge</i> (small delta → absorb the residual over the next frames, no visible jump). Paused →
/// frozen. See the Phase 2 design doc §3.2.
/// </summary>
/// <remarks>
/// "Now" is injected as a millisecond source (production: a monotonic <see cref="System.Diagnostics.Stopwatch"/>
/// timestamp; tests: a controllable lambda) so the estimator is deterministic under test.
/// </remarks>
internal sealed class PlaybackClock
{
    private readonly Func<double> _nowMs;

    private double _anchorMs;       // authoritative position at the anchor
    private double _anchorWallMs;   // wall time at the anchor
    private double _correctionMs;   // residual visual offset that decays to 0 (smooth converge)
    private double _lastWallMs;     // wall time of the last correction decay
    private bool _isPlaying;

    /// <summary>Above this |delta| an update is a seek (snap); at or below it converges smoothly.</summary>
    public double SeekThresholdMs { get; set; } = 250;

    /// <summary>Time constant (ms) over which a converge residual decays. ~120 ms ≈ a few frames.</summary>
    public double ConvergenceTimeConstantMs { get; set; } = 120;

    public PlaybackClock(Func<double> nowMs)
    {
        _nowMs = nowMs;
        _anchorWallMs = _nowMs();
        _lastWallMs = _anchorWallMs;
    }

    /// <summary>Sets play state, re-anchoring at the current estimate so the transition never jumps.</summary>
    public void SetPlaying(bool playing)
    {
        if (playing == _isPlaying)
        {
            return;
        }

        ReanchorToCurrent();
        _isPlaying = playing;
    }

    /// <summary>
    /// Folds in an authoritative position. A delta beyond <see cref="SeekThresholdMs"/> snaps (seek);
    /// otherwise the difference becomes a decaying correction so the displayed value does not jump.
    /// </summary>
    public void SetPosition(double authoritativeMs)
    {
        var now = _nowMs();
        var predicted = Estimate(); // advances decay up to `now`

        _anchorMs = authoritativeMs;
        _anchorWallMs = now;
        _lastWallMs = now;

        var jump = authoritativeMs - predicted;
        _correctionMs = Math.Abs(jump) > SeekThresholdMs
            ? 0                          // seek: snap, no smoothing
            : predicted - authoritativeMs; // converge: keep showing `predicted`, then decay to authoritative
    }

    /// <summary>The estimated position now, smoothed between authoritative updates.</summary>
    public double Estimate()
    {
        var now = _nowMs();

        var dt = now - _lastWallMs;
        if (dt > 0 && _correctionMs != 0)
        {
            _correctionMs *= Math.Exp(-dt / ConvergenceTimeConstantMs);
        }
        _lastWallMs = now;

        var raw = _anchorMs + (_isPlaying ? now - _anchorWallMs : 0);
        return raw + _correctionMs;
    }

    private void ReanchorToCurrent()
    {
        var now = _nowMs();
        var current = Estimate();
        _anchorMs = current;       // fold the correction into the anchor so raw == current here
        _anchorWallMs = now;
        _correctionMs = 0;
        _lastWallMs = now;
    }
}
