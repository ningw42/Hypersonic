namespace Hypersonic.Core.Helpers;

/// <summary>
/// Pure, row-aware insertion-index math for a chip strip. Given a pointer (control-relative) and the realized
/// chips' bounds in reading order, returns the index at which a dropped chip should be inserted. A chip precedes
/// the pointer when it sits in an earlier row, or in the same row and left of the pointer. WinUI-free so it is
/// unit-testable; the control feeds it real container bounds.
/// </summary>
public static class ReorderDropIndex
{
    public static int Resolve(
        double pointerX,
        double pointerY,
        IReadOnlyList<(double Left, double Top, double Width, double Height)> chipBounds)
    {
        var index = 0;
        foreach (var (left, top, width, height) in chipBounds)
        {
            var centerX = left + (width / 2);
            var centerY = top + (height / 2);
            var rowTolerance = height / 2;

            var earlierRow = centerY < pointerY - rowTolerance;
            var sameRowAndLeft = System.Math.Abs(centerY - pointerY) <= rowTolerance && centerX <= pointerX;

            if (earlierRow || sameRowAndLeft)
            {
                index++;
            }
        }

        return index;
    }
}
