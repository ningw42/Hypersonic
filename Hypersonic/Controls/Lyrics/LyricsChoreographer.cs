namespace Hypersonic.Controls.Lyrics;

/// <summary>
/// Per-frame motion director. Given the active line and estimated position it: springs the scroll
/// offset so the active line centres at the top-offset factor; eases each line's emphasis
/// (opacity / scale / blur) toward distance-based targets; and fires a word's bloom onset the first
/// time it becomes active. All stored animation state lives here (the scroll <see cref="Spring"/> and
/// the per-line emphasis <see cref="Tween"/>s); the wipe itself is computed analytically by the
/// renderer. See the Phase 2 design doc §3.6 / §5.3.
/// </summary>
internal sealed class LyricsChoreographer
{
    private readonly Spring _scroll = new(0) { AngularFrequency = 14.0 };
    private bool _hasAnchor;

    /// <summary>Fraction of viewport height at which the active line's centre sits (~0.40).</summary>
    public double TopOffsetFactor { get; set; } = 0.40;

    /// <summary>Fixed bloom onset duration (ms), independent of word length (§5.2).</summary>
    public double BloomDurationMs { get; set; } = 180;

    public double ScrollY => _scroll.Value;

    /// <summary>Forget scroll state so the next frame jumps (no slide) — call on a model rebuild.</summary>
    public void Reset()
    {
        _hasAnchor = false;
        _scroll.Jump(0);
    }

    public void Update(LyricsVisualModel model, int activeIndex, double estimatedMs, double viewportHeight, double dtSeconds)
    {
        if (model.Lines.Count == 0)
        {
            return;
        }

        var targetScroll = ComputeTargetScroll(model, activeIndex, viewportHeight);
        if (!_hasAnchor)
        {
            _scroll.Jump(targetScroll);
            _hasAnchor = true;
        }
        else
        {
            _scroll.Target = targetScroll;
            _scroll.Update(dtSeconds);
        }
        for (var i = 0; i < model.Lines.Count; i++)
        {
            var line = model.Lines[i];
            var (opacity, scale, blur) = EmphasisTargets(i, activeIndex, model.IsStatic);

            line.Opacity.Target = opacity;
            line.Scale.Target = scale;
            line.Blur.Target = blur;
            line.Opacity.Update(dtSeconds);
            line.Scale.Update(dtSeconds);
            line.Blur.Update(dtSeconds);
        }

        FireBloomOnsets(model, activeIndex, estimatedMs);
    }

    private double ComputeTargetScroll(LyricsVisualModel model, int activeIndex, double viewportHeight)
    {
        // Static (Plain / unsynced) lyrics have no active line to follow — their line starts are all 0,
        // so the synchronizer would walk to the last line. Pin them near the top instead, clear of the
        // edge fade, so they read from the beginning (user-scroll lands in Phase 3).
        if (model.IsStatic)
        {
            return -viewportHeight * 0.08;
        }

        var index = Math.Clamp(activeIndex, 0, model.Lines.Count - 1);
        var line = model.Lines[index];
        var lineCentre = line.Top + line.Height / 2;
        return lineCentre - viewportHeight * TopOffsetFactor;
    }

    private static (double Opacity, double Scale, double Blur) EmphasisTargets(int index, int activeIndex, bool isStatic)
    {
        if (isStatic)
        {
            return (1.0, 1.0, 0.0);
        }

        if (index == activeIndex)
        {
            return (1.0, 1.0, 0.0);
        }

        var distance = Math.Abs(index - activeIndex);
        var opacity = Math.Max(0.25, 0.5 - 0.12 * (distance - 1));
        var blur = Math.Min(4.0, 1.5 * distance);
        return (opacity, 0.95, blur);
    }

    private void FireBloomOnsets(LyricsVisualModel model, int activeIndex, double estimatedMs)
    {
        if (model.IsStatic || activeIndex < 0 || activeIndex >= model.Lines.Count)
        {
            return;
        }

        var line = model.Lines[activeIndex];
        if (!line.HasWordTiming)
        {
            return;
        }

        foreach (var word in line.Words)
        {
            if (double.IsNegativeInfinity(word.OnsetMs)
                && estimatedMs >= word.Timing.StartMs
                && estimatedMs <= word.Timing.EndMs + BloomDurationMs)
            {
                word.OnsetMs = estimatedMs;
            }
        }
    }
}
