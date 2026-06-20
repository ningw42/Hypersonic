using Microsoft.Graphics.Canvas.Text;

namespace Hypersonic.Controls.Lyrics;

/// <summary>One pixel piece of a word on a single visual row (a word straddling a wrap has more than one).</summary>
internal struct WordFragment
{
    public int RowIndex;
    public double XStart;
    public double XEnd;
    public readonly double Width => XEnd - XStart;
}

/// <summary>
/// A word in the render model: its timing, the row fragments it occupies, its reading-order pixel span
/// (used by the wipe), and the bloom onset timestamp (<see cref="double.NegativeInfinity"/> until it
/// first becomes active).
/// </summary>
internal sealed class LyricsVisualWord
{
    public required LyricsTimingWord Timing { get; init; }
    public List<WordFragment> Fragments { get; } = new();
    public double CumulativeStartPx { get; set; }
    public double WidthPx { get; set; }
    public double OnsetMs { get; set; } = double.NegativeInfinity;
}

/// <summary>One visual row of a (possibly wrapped) line: its local Y/height, char range, pixel bounds,
/// and reading-order pixel span used to project the played head onto this row.</summary>
internal sealed class LyricsVisualRow
{
    public double Top { get; set; }
    public double Height { get; set; }
    public int CharStart { get; set; }
    public int CharCount { get; set; }
    public double Left { get; set; }
    public double Right { get; set; }
    public double CumulativeStartPx { get; set; }
    public double CumulativeEndPx { get; set; }

    public double Width => Right - Left;
}

/// <summary>
/// A line in the render model: the timing, its wrapping <see cref="CanvasTextLayout"/>, the stacked Y
/// in content space, its derived rows and words, and the per-line emphasis tweens (opacity / scale /
/// blur) the choreographer eases as the active line changes.
/// </summary>
internal sealed class LyricsVisualLine : IDisposable
{
    public required LyricsTimingLine Timing { get; init; }
    public required CanvasTextLayout Layout { get; init; }
    public required bool HasWordTiming { get; init; }

    public double Top { get; set; }
    public double Height { get; set; }

    /// <summary>Horizontal scale-to-fit for the rare line whose widest row still overruns the viewport
    /// (a single token wider than the control). 1 in the normal wrapped case; clamped to a floor.</summary>
    public double ScaleToFit { get; set; } = 1;

    public List<LyricsVisualRow> Rows { get; } = new();
    public List<LyricsVisualWord> Words { get; } = new();

    /// <summary>Per-word (start, end, total width) in reading order — the input to the wipe head walk.</summary>
    public (int StartMs, int EndMs, double WidthPx)[] WipeWords { get; set; } = Array.Empty<(int, int, double)>();

    public Tween Opacity { get; } = new(1) { TimeConstant = 0.12 };
    public Tween Scale { get; } = new(1) { TimeConstant = 0.12 };
    public Tween Blur { get; } = new(0) { TimeConstant = 0.12 };

    public void Dispose() => Layout.Dispose();
}

/// <summary>
/// The per-track render model: the laid-out lines plus total content height. Built lazily on first draw
/// (it needs the canvas device + width to lay out text) and rebuilt on a track or width change. Holds
/// <see cref="CanvasTextLayout"/>s, so it is disposable.
/// </summary>
internal sealed class LyricsVisualModel : IDisposable
{
    public static readonly LyricsVisualModel Empty = new();

    public IReadOnlyList<LyricsVisualLine> Lines { get; init; } = Array.Empty<LyricsVisualLine>();
    public double ContentHeight { get; init; }
    public bool IsStatic { get; init; }

    public void Dispose()
    {
        foreach (var line in Lines)
        {
            line.Dispose();
        }
    }
}
