using System.Numerics;

using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;

using Windows.Foundation;
using Windows.UI;

namespace Hypersonic.Controls.Lyrics;

/// <summary>
/// Draws one frame (Phase 2 design doc §3.7 / §5). For visible lines only: the active line gets a
/// per-visual-row 4-stop gradient wipe (§5.1) plus per-word bloom (§5.2); inactive lines draw solid at
/// their eased emphasis (opacity / scale, optional gaussian blur); a top/bottom edge fade dissolves
/// lines at the viewport edges. Bloom and neighbour-blur are toggleable so the §7 smoke test can layer
/// effects on incrementally.
/// </summary>
internal sealed class LyricsSceneRenderer
{
    /// <summary>Width of the leading soft edge of the wipe, as a fraction of the row.</summary>
    public double WipeSoftness { get; set; } = 0.08;

    // Reused across frames: the wipe's 4 gradient stops are rebuilt in place each call (positions/colours
    // only) instead of allocating a fresh array per row per frame.
    private readonly CanvasGradientStop[] _wipeStops = new CanvasGradientStop[4];

    /// <summary>Per-word pop/float/glow on the active line. The headline effect; active line only.</summary>
    public bool EnableBloom { get; set; } = true;

    /// <summary>Gaussian blur on inactive lines. Off by default — enable during the perf smoke test.</summary>
    public bool EnableNeighborBlur { get; set; } = false;

    /// <summary>
    /// Active-line fill mode. <c>true</c> (default) → the gradient karaoke wipe (word-level when timed,
    /// a line-level fill otherwise). <c>false</c> → a discrete solid <see cref="PlayedColor"/> highlight of
    /// the whole active line (Spotify-style), useful for line-synced sources whose sweep timing is coarse.
    /// </summary>
    public bool EnableKaraokeWipe { get; set; } = true;

    public void Draw(
        CanvasDrawingSession ds,
        LyricsVisualModel model,
        int activeIndex,
        double estimatedMs,
        double scrollY,
        double width,
        double height,
        double marginX,
        LyricsTextAlignment alignment,
        Color playedColor,
        Color unplayedColor)
    {
        if (model.Lines.Count == 0 || width <= 0 || height <= 0)
        {
            return;
        }

        var availableWidth = Math.Max(1.0, width - 2 * marginX);
        var pivotX = (float)LyricsAlign.PivotX(alignment, marginX, availableWidth);

        for (var i = 0; i < model.Lines.Count; i++)
        {
            var line = model.Lines[i];
            var screenTop = line.Top - scrollY;
            if (screenTop + line.Height < -40 || screenTop > height + 40)
            {
                continue; // off-screen — cull
            }

            var centreY = screenTop + line.Height / 2;
            var opacity = Clamp01(line.Opacity.Value * EdgeFade(centreY, height));
            if (opacity <= 0.01)
            {
                continue;
            }

            var scale = line.Scale.Value;
            // Pivot the scale at the alignment edge so an emphasised line grows away from that edge
            // (left → right, right → left, centre → both) instead of sliding sideways. Vertical pivot
            // stays the line centre, which reads naturally for stacked lines.
            var centre = new Vector2(pivotX, (float)(screenTop + line.Height / 2));
            var emphasis = Matrix3x2.CreateScale((float)(scale * line.ScaleToFit), (float)scale, centre);
            var isActive = i == activeIndex && !model.IsStatic;

            var previous = ds.Transform;
            ds.Transform = emphasis;
            // Fold the line opacity (emphasis falloff × edge fade) into each draw colour's alpha rather than
            // pushing an offscreen CreateLayer(opacity) per visible line — the single biggest per-frame win.
            // Equivalent for a line's non-overlapping glyphs; for the blur path the pre-blur alpha scales the
            // blurred output identically (blur is linear in alpha).
            if (isActive && EnableKaraokeWipe)
            {
                DrawWipe(ds, line, estimatedMs, marginX, screenTop,
                    WithOpacity(playedColor, opacity), WithOpacity(unplayedColor, opacity));
            }
            else if (isActive)
            {
                // Discrete highlight: the whole active line in the played colour, no sweep.
                ds.DrawTextLayout(line.Layout, (float)marginX, (float)screenTop, WithOpacity(playedColor, opacity));
            }
            else
            {
                DrawSolid(ds, line, marginX, screenTop, WithOpacity(unplayedColor, opacity));
            }
            ds.Transform = previous;

            if (isActive && EnableKaraokeWipe && EnableBloom && line.HasWordTiming)
            {
                DrawBloom(ds, line, estimatedMs, marginX, screenTop, emphasis, playedColor, unplayedColor);
            }
        }
    }

    private void DrawWipe(CanvasDrawingSession ds, LyricsVisualLine line, double estimatedMs, double marginX, double screenTop, Color played, Color unplayed)
    {
        var headPx = LyricsWipe.PlayedHeadPx(line.WipeWords, estimatedMs);

        foreach (var row in line.Rows)
        {
            var pRow = LyricsWipe.RowFraction(headPx, row.CumulativeStartPx, row.CumulativeEndPx);
            var rowRect = new Rect(marginX + row.Left - 1, screenTop + row.Top, row.Width + 2, row.Height + 1);

            if (pRow <= 0.0001)
            {
                DrawClippedText(ds, line, marginX, screenTop, rowRect, unplayed);
            }
            else if (pRow >= 0.9999)
            {
                DrawClippedText(ds, line, marginX, screenTop, rowRect, played);
            }
            else
            {
                using var brush = BuildWipeBrush(ds, row, marginX, screenTop, pRow, played, unplayed);
                using (ds.CreateLayer(1f, rowRect))
                {
                    ds.DrawTextLayout(line.Layout, (float)marginX, (float)screenTop, brush);
                }
            }
        }
    }

    private CanvasLinearGradientBrush BuildWipeBrush(CanvasDrawingSession ds, LyricsVisualRow row, double marginX, double screenTop, double pRow, Color played, Color unplayed)
    {
        var edge = Math.Min(pRow + WipeSoftness, 1.0);
        _wipeStops[0] = new CanvasGradientStop { Position = 0f, Color = played };
        _wipeStops[1] = new CanvasGradientStop { Position = (float)pRow, Color = played };
        _wipeStops[2] = new CanvasGradientStop { Position = (float)edge, Color = unplayed };
        _wipeStops[3] = new CanvasGradientStop { Position = 1f, Color = unplayed };

        var midY = (float)(screenTop + row.Top + row.Height / 2);
        return new CanvasLinearGradientBrush(ds, _wipeStops)
        {
            StartPoint = new Vector2((float)(marginX + row.Left), midY),
            EndPoint = new Vector2((float)(marginX + row.Right), midY),
        };
    }

    /// <summary>Returns <paramref name="color"/> with its alpha scaled by <paramref name="opacity"/> — lets a
    /// line's emphasis/edge-fade opacity be baked into the draw colour instead of an offscreen layer.</summary>
    private static Color WithOpacity(Color color, double opacity)
    {
        var a = (byte)Math.Clamp(Math.Round(color.A * opacity), 0, 255);
        return Color.FromArgb(a, color.R, color.G, color.B);
    }

    private static void DrawClippedText(CanvasDrawingSession ds, LyricsVisualLine line, double marginX, double screenTop, Rect clip, Color color)
    {
        using (ds.CreateLayer(1f, clip))
        {
            ds.DrawTextLayout(line.Layout, (float)marginX, (float)screenTop, color);
        }
    }

    private void DrawSolid(CanvasDrawingSession ds, LyricsVisualLine line, double marginX, double screenTop, Color color)
    {
        if (EnableNeighborBlur && line.Blur.Value > 0.1)
        {
            using var commands = new CanvasCommandList(ds);
            using (var commandSession = commands.CreateDrawingSession())
            {
                commandSession.DrawTextLayout(line.Layout, (float)marginX, (float)screenTop, color);
            }

            using var blur = new GaussianBlurEffect { Source = commands, BlurAmount = (float)line.Blur.Value };
            ds.DrawImage(blur);
        }
        else
        {
            ds.DrawTextLayout(line.Layout, (float)marginX, (float)screenTop, color);
        }
    }

    private void DrawBloom(CanvasDrawingSession ds, LyricsVisualLine line, double estimatedMs, double marginX, double screenTop, Matrix3x2 emphasis, Color played, Color unplayed)
    {
        var headPx = LyricsWipe.PlayedHeadPx(line.WipeWords, estimatedMs);

        foreach (var word in line.Words)
        {
            if (double.IsNegativeInfinity(word.OnsetMs))
            {
                continue;
            }

            var progress = (estimatedMs - word.OnsetMs) / Math.Max(1.0, BloomDuration);
            if (progress < 0 || progress > 1)
            {
                continue;
            }

            var envelope = Tween.Pulse(progress);
            if (envelope <= 0.01)
            {
                continue;
            }

            var bloomScale = 1.0 + 0.08 * envelope;

            foreach (var fragment in word.Fragments)
            {
                var row = line.Rows[fragment.RowIndex];
                var pRow = LyricsWipe.RowFraction(headPx, row.CumulativeStartPx, row.CumulativeEndPx);

                var wordCentre = new Vector2(
                    (float)(marginX + (fragment.XStart + fragment.XEnd) / 2),
                    (float)(screenTop + row.Top + row.Height / 2));

                // Pad the clip enough to hold the scaled-up glyphs (no vertical lift → no ghost).
                var padX = fragment.Width * 0.12 + 3;
                var padY = row.Height * 0.12 + 2;
                var clip = new Rect(
                    marginX + fragment.XStart - padX,
                    screenTop + row.Top - padY,
                    fragment.Width + 2 * padX,
                    row.Height + 2 * padY);

                var bloom = Matrix3x2.CreateScale((float)bloomScale, (float)bloomScale, wordCentre);

                var previous = ds.Transform;
                ds.Transform = emphasis; // capture the clip in emphasis space
                // Opaque, same wipe colouring as the base, scaled about the word centre: the larger copy
                // fully covers the at-rest word, so it reads as a seamless swell rather than a doubled
                // "shadow". At envelope 0 (scale 1.0) it coincides exactly with the base.
                using (ds.CreateLayer(1f, clip))
                {
                    ds.Transform = bloom * emphasis;
                    if (pRow <= 0.0001)
                    {
                        ds.DrawTextLayout(line.Layout, (float)marginX, (float)screenTop, unplayed);
                    }
                    else if (pRow >= 0.9999)
                    {
                        ds.DrawTextLayout(line.Layout, (float)marginX, (float)screenTop, played);
                    }
                    else
                    {
                        using var brush = BuildWipeBrush(ds, row, marginX, screenTop, pRow, played, unplayed);
                        ds.DrawTextLayout(line.Layout, (float)marginX, (float)screenTop, brush);
                    }
                }
                ds.Transform = previous;
            }
        }
    }

    /// <summary>Bloom onset duration in ms — kept in sync with the choreographer.</summary>
    public double BloomDuration { get; set; } = 180;

    private static double EdgeFade(double centreY, double height)
    {
        var zone = height * 0.18;
        if (zone <= 0)
        {
            return 1;
        }

        var fade = 1.0;
        if (centreY < zone)
        {
            fade = Math.Min(fade, Smoothstep(centreY / zone));
        }

        if (centreY > height - zone)
        {
            fade = Math.Min(fade, Smoothstep((height - centreY) / zone));
        }

        return Clamp01(fade);
    }

    private static double Smoothstep(double t)
    {
        t = Clamp01(t);
        return t * t * (3 - 2 * t);
    }

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
}
