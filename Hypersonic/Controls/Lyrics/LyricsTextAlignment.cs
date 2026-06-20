using Microsoft.Graphics.Canvas.Text;

namespace Hypersonic.Controls.Lyrics;

/// <summary>Horizontal alignment of the lyric text — a caller input on <see cref="LyricsCanvasControl"/>.</summary>
public enum LyricsTextAlignment
{
    Left,
    Center,
    Right,
}

/// <summary>
/// Maps <see cref="LyricsTextAlignment"/> to the text-format alignment and to the active-line scale
/// pivot. The pivot anchors the scale at the alignment edge so the emphasised line grows away from that
/// edge (left → grows right, right → grows left, centre → grows both ways) instead of sliding sideways.
/// </summary>
internal static class LyricsAlign
{
    /// <summary>The x the active-line scale pivots around, in screen pixels.</summary>
    public static double PivotX(LyricsTextAlignment alignment, double marginX, double availableWidth) => alignment switch
    {
        LyricsTextAlignment.Center => marginX + availableWidth / 2,
        LyricsTextAlignment.Right => marginX + availableWidth,
        _ => marginX,
    };

    public static CanvasHorizontalAlignment ToCanvas(LyricsTextAlignment alignment) => alignment switch
    {
        LyricsTextAlignment.Center => CanvasHorizontalAlignment.Center,
        LyricsTextAlignment.Right => CanvasHorizontalAlignment.Right,
        _ => CanvasHorizontalAlignment.Left,
    };
}
