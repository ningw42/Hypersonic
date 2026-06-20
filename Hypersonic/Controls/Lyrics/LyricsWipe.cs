namespace Hypersonic.Controls.Lyrics;

/// <summary>
/// The analytic heart of the gradient wipe (Phase 2 design doc §5.1). A single played-head walks the
/// line's words in reading order; <see cref="PlayedHeadPx"/> gives its pixel position, and
/// <see cref="RowFraction"/> projects that head onto one visual row so a wrapped line wipes row-by-row.
/// Pure functions of position + cached geometry — no stored state, trivially testable.
/// </summary>
internal static class LyricsWipe
{
    /// <summary>
    /// The played head in pixels along the line's flattened reading order at time <paramref name="t"/>:
    /// the summed width of fully-played words plus the in-progress fraction of the current word.
    /// </summary>
    public static double PlayedHeadPx(IReadOnlyList<(int StartMs, int EndMs, double WidthPx)> words, double t)
    {
        var head = 0.0;
        foreach (var (startMs, endMs, widthPx) in words)
        {
            if (t >= endMs)
            {
                head += widthPx;
                continue;
            }

            if (t <= startMs)
            {
                break;
            }

            var duration = endMs - startMs;
            var fraction = duration <= 0 ? 1.0 : (t - startMs) / duration;
            head += widthPx * Clamp01(fraction);
            break;
        }

        return head;
    }

    /// <summary>
    /// The wipe fraction for a single visual row, given the head's pixel position and the row's pixel
    /// span along the reading order: 0 before the row, 1 once past it, the local fraction inside it.
    /// </summary>
    public static double RowFraction(double headPx, double rowStartPx, double rowEndPx)
    {
        if (headPx >= rowEndPx)
        {
            return 1;
        }

        if (headPx <= rowStartPx)
        {
            return 0;
        }

        var width = rowEndPx - rowStartPx;
        return width <= 0 ? 1 : (headPx - rowStartPx) / width;
    }

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
}
