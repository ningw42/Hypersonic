namespace Hypersonic.Controls.Lyrics;

/// <summary>
/// A first-order eased-approach scalar — the soft transition used for per-line emphasis
/// (opacity / scale / blur) as the active line changes. <see cref="Value"/> closes the gap to
/// <see cref="Target"/> exponentially with a time constant, so retargeting mid-flight stays smooth and
/// never overshoots. The static helpers (<see cref="EaseOutCubic"/>, <see cref="Pulse"/>) drive the
/// analytic per-word bloom envelopes, which need no stored state — only a word's onset timestamp.
/// </summary>
internal sealed class Tween
{
    public double Value { get; private set; }
    public double Target { get; set; }

    /// <summary>Seconds to close ~63% of the remaining gap. Smaller → snappier.</summary>
    public double TimeConstant { get; set; } = 0.12;

    public Tween(double initial = 0)
    {
        Value = initial;
        Target = initial;
    }

    public void Jump(double value)
    {
        Value = value;
        Target = value;
    }

    public double Update(double dtSeconds)
    {
        if (dtSeconds > 0 && TimeConstant > 0)
        {
            var alpha = 1.0 - Math.Exp(-dtSeconds / TimeConstant);
            Value += (Target - Value) * alpha;
        }

        return Value;
    }

    /// <summary>Ease-out cubic on [0,1] (clamped). Fast start, gentle finish — the bloom scale onset.</summary>
    public static double EaseOutCubic(double t)
    {
        t = Clamp01(t);
        var inv = 1.0 - t;
        return 1.0 - (inv * inv * inv);
    }

    /// <summary>A 0→1→0 envelope peaking at the midpoint (clamped). Shapes the bloom pop/float/glow onset.</summary>
    public static double Pulse(double t) => Math.Sin(Math.PI * Clamp01(t));

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
}
