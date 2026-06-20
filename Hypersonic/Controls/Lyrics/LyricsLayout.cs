using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;

namespace Hypersonic.Controls.Lyrics;

/// <summary>
/// Turns the platform-independent <see cref="LyricsTimingModel"/> into a laid-out
/// <see cref="LyricsVisualModel"/> at the current control width: one wrapping
/// <see cref="CanvasTextLayout"/> per line (so a long line becomes multiple visual rows), the rows and
/// per-word row-fragments derived from the layout's character regions, and the reading-order pixel
/// spans the wipe walks. Runs only on a track / width / font change — never per frame. See the Phase 2
/// design doc §3.4.
/// </summary>
internal static class LyricsLayout
{
    /// <summary>Minimum horizontal scale for an over-wide token before it is allowed to clip.</summary>
    private const double MinScaleToFit = 0.5;

    public static LyricsVisualModel Build(
        LyricsTimingModel timing, ICanvasResourceCreator device, CanvasTextFormat format, double availableWidth)
    {
        if (timing.Lines.Count == 0 || availableWidth <= 0)
        {
            return LyricsVisualModel.Empty;
        }

        var lines = new List<LyricsVisualLine>(timing.Lines.Count);
        var y = 0.0;

        foreach (var timingLine in timing.Lines)
        {
            var layoutText = BuildLayoutText(timingLine);
            var layout = new CanvasTextLayout(device, layoutText, format, (float)availableWidth, float.MaxValue)
            {
                WordWrapping = CanvasWordWrapping.Wrap,
            };

            var line = new LyricsVisualLine
            {
                Timing = timingLine,
                Layout = layout,
                HasWordTiming = timingLine.HasWordTiming,
                Top = y,
                Height = layout.LayoutBounds.Height,
            };

            BuildRows(line, layout, layoutText.Length);
            if (!timing.IsStatic)
            {
                BuildWordsAndCumulatives(line, layout, timingLine);
            }

            line.ScaleToFit = ResolveScaleToFit(line, availableWidth);

            lines.Add(line);
            y += line.Height + LineGap(line);
        }

        return new LyricsVisualModel { Lines = lines, ContentHeight = y, IsStatic = timing.IsStatic };
    }

    /// <summary>The text actually laid out: the words concatenated (so word char-offsets line up exactly
    /// with the layout), or the line text for a static line with no words.</summary>
    private static string BuildLayoutText(LyricsTimingLine line)
        => line.Words.Count > 0 ? string.Concat(line.Words.Select(w => w.Text)) : line.Text;

    private static void BuildRows(LyricsVisualLine line, CanvasTextLayout layout, int textLength)
    {
        if (textLength == 0)
        {
            line.Rows.Add(new LyricsVisualRow { Top = 0, Height = line.Height, CharStart = 0, CharCount = 0 });
            return;
        }

        // GetCharacterRegions over the whole line returns one region per visual row (wrapping splits the
        // range), each carrying that row's pixel rect and char range. Coalesce by Y in case a row comes
        // back as several runs.
        var regions = layout.GetCharacterRegions(0, textLength);
        LyricsVisualRow? current = null;
        foreach (var region in regions)
        {
            var bounds = region.LayoutBounds;
            if (current is null || Math.Abs(bounds.Y - current.Top) > 0.5)
            {
                current = new LyricsVisualRow
                {
                    Top = bounds.Y,
                    Height = bounds.Height,
                    CharStart = region.CharacterIndex,
                    CharCount = region.CharacterCount,
                    Left = bounds.X,
                    Right = bounds.X + bounds.Width,
                    CumulativeStartPx = double.PositiveInfinity,
                    CumulativeEndPx = double.NegativeInfinity,
                };
                line.Rows.Add(current);
            }
            else
            {
                current.CharCount += region.CharacterCount;
                current.Height = Math.Max(current.Height, bounds.Height);
                current.Left = Math.Min(current.Left, bounds.X);
                current.Right = Math.Max(current.Right, bounds.X + bounds.Width);
            }
        }

        if (line.Rows.Count == 0)
        {
            line.Rows.Add(new LyricsVisualRow { Top = 0, Height = line.Height, CharStart = 0, CharCount = textLength });
        }
    }

    private static void BuildWordsAndCumulatives(LyricsVisualLine line, CanvasTextLayout layout, LyricsTimingLine timingLine)
    {
        var charPos = 0;
        var cum = 0.0;

        foreach (var word in timingLine.Words)
        {
            var visual = new LyricsVisualWord { Timing = word };
            var length = word.Text.Length;

            if (length > 0)
            {
                foreach (var region in layout.GetCharacterRegions(charPos, length))
                {
                    var bounds = region.LayoutBounds;
                    visual.Fragments.Add(new WordFragment
                    {
                        RowIndex = FindRowIndex(line.Rows, region.CharacterIndex, bounds.Y),
                        XStart = bounds.X,
                        XEnd = bounds.X + bounds.Width,
                    });
                }
            }

            visual.CumulativeStartPx = cum;
            foreach (var fragment in visual.Fragments)
            {
                var fragStart = cum;
                var fragEnd = cum + fragment.Width;
                var row = line.Rows[fragment.RowIndex];
                row.CumulativeStartPx = Math.Min(row.CumulativeStartPx, fragStart);
                row.CumulativeEndPx = Math.Max(row.CumulativeEndPx, fragEnd);
                cum = fragEnd;
            }

            visual.WidthPx = cum - visual.CumulativeStartPx;
            line.Words.Add(visual);
            charPos += length;
        }

        // Rows touched by no word fragment (e.g. an all-whitespace wrap) collapse to a zero-width span at
        // the running head so the wipe steps cleanly past them.
        foreach (var row in line.Rows)
        {
            if (double.IsInfinity(row.CumulativeStartPx) || double.IsInfinity(row.CumulativeEndPx))
            {
                row.CumulativeStartPx = cum;
                row.CumulativeEndPx = cum;
            }
        }

        line.WipeWords = line.Words
            .Select(w => (w.Timing.StartMs, w.Timing.EndMs, w.WidthPx))
            .ToArray();
    }

    private static int FindRowIndex(List<LyricsVisualRow> rows, int characterIndex, double y)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (characterIndex >= rows[i].CharStart && characterIndex < rows[i].CharStart + rows[i].CharCount)
            {
                return i;
            }
        }

        // Fallback: nearest row by Y (handles a trailing region past the last row's char range).
        var best = 0;
        var bestDist = double.MaxValue;
        for (var i = 0; i < rows.Count; i++)
        {
            var dist = Math.Abs(rows[i].Top - y);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = i;
            }
        }

        return best;
    }

    private static double ResolveScaleToFit(LyricsVisualLine line, double availableWidth)
    {
        var widest = 0.0;
        foreach (var row in line.Rows)
        {
            widest = Math.Max(widest, row.Width);
        }

        if (widest <= availableWidth || widest <= 0)
        {
            return 1;
        }

        return Math.Max(availableWidth / widest, MinScaleToFit);
    }

    private static double LineGap(LyricsVisualLine line)
    {
        var rowCount = Math.Max(1, line.Rows.Count);
        return line.Height / rowCount * 0.5; // ~half a row of breathing space between lines
    }
}
