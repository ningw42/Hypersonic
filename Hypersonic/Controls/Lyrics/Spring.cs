namespace Hypersonic.Controls.Lyrics;

/// <summary>
/// A critically-damped scalar spring — the glide-and-settle used for the lyric scroll offset. It
/// converges on <see cref="Target"/> with momentum but <b>no overshoot</b>, giving the soft
/// auto-scroll between active lines. This is the only piece of stored animation state on the scroll
/// path; everything else (wipe, active-line, bloom envelopes) is computed analytically per frame.
/// </summary>
/// <remarks>
/// Uses the closed-form critically-damped step (stable for any timestep), parameterised by an angular
/// frequency. The default settles to within ~1% in ~350 ms (ω·t ≈ 5), matching the design's scroll feel.
/// </remarks>
internal sealed class Spring
{
    public double Value { get; private set; }
    public double Velocity { get; private set; }
    public double Target { get; set; }

    /// <summary>Angular frequency (rad/s). Higher → snappier. ~14 settles in ~350 ms.</summary>
    public double AngularFrequency { get; set; } = 14.0;

    public Spring(double initial = 0)
    {
        Value = initial;
        Target = initial;
    }

    /// <summary>Snap to <paramref name="value"/> with zero velocity — used on track change / first layout.</summary>
    public void Jump(double value)
    {
        Value = value;
        Target = value;
        Velocity = 0;
    }

    /// <summary>Advances the spring by <paramref name="dtSeconds"/> and returns the new <see cref="Value"/>.</summary>
    public double Update(double dtSeconds)
    {
        if (dtSeconds <= 0 || AngularFrequency <= 0)
        {
            return Value;
        }

        // Closed-form critically-damped solution (Ryan Juckett's stable damped spring): unconditionally
        // stable and overshoot-free, unlike a naive explicit integrator at large dt.
        var omega = AngularFrequency;
        var exp = Math.Exp(-omega * dtSeconds);
        var delta = Value - Target;
        var temp = (Velocity + omega * delta) * dtSeconds;

        Value = Target + (delta + temp) * exp;
        Velocity = (Velocity - omega * temp) * exp;
        return Value;
    }
}
