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
        double side;
        var widthInf = double.IsInfinity(availableSize.Width);
        var heightInf = double.IsInfinity(availableSize.Height);

        if (widthInf && heightInf)
        {
            side = 0;
        }
        else if (widthInf)
        {
            side = availableSize.Height;
        }
        else if (heightInf)
        {
            side = availableSize.Width;
        }
        else
        {
            side = Math.Min(availableSize.Width, availableSize.Height);
        }

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
