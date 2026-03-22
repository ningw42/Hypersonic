using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using Windows.Foundation;

namespace Hypersonic.Controls;

/// <summary>
/// A panel that enforces a square aspect ratio based on available width.
/// </summary>
public class SquarePanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var side = double.IsInfinity(availableSize.Width) ? availableSize.Height : availableSize.Width;
        var childSize = new Size(side, side);

        foreach (var child in Children)
        {
            child.Measure(childSize);
        }

        return childSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var side = Math.Min(finalSize.Width, finalSize.Height);
        var rect = new Rect(0, 0, side, side);

        foreach (var child in Children)
        {
            child.Arrange(rect);
        }

        return new Size(side, side);
    }
}
