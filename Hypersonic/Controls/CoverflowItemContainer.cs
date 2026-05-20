using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Hypersonic.Controls;

/// <summary>
/// Per-item wrapper produced by <see cref="CoverflowControl"/>'s repeater
/// template. Owns focus while the parent control owns pointer/tap routing
/// across overlapping items. Hosts the user template through a ContentPresenter;
/// DataContext flows down automatically.
/// </summary>
public sealed class CoverflowItemContainer : ContentControl
{
    public static readonly DependencyProperty DimmingOpacityProperty =
        DependencyProperty.Register(nameof(DimmingOpacity), typeof(double), typeof(CoverflowItemContainer),
            new PropertyMetadata(0.0));

    public static readonly DependencyProperty DimmingCornerRadiusProperty =
        DependencyProperty.Register(nameof(DimmingCornerRadius), typeof(CornerRadius), typeof(CoverflowItemContainer),
            new PropertyMetadata(new CornerRadius(10)));

    internal CoverflowControl? Owner { get; set; }
    internal int Index { get; set; } = -1;

    public double DimmingOpacity
    {
        get => (double)GetValue(DimmingOpacityProperty);
        set => SetValue(DimmingOpacityProperty, value);
    }

    public CornerRadius DimmingCornerRadius
    {
        get => (CornerRadius)GetValue(DimmingCornerRadiusProperty);
        set => SetValue(DimmingCornerRadiusProperty, value);
    }

    internal double ArtworkSide
    {
        get
        {
            if (ActualWidth > 0)
            {
                return ActualWidth;
            }

            if (!double.IsNaN(Width) && Width > 0)
            {
                return Width;
            }

            return Owner?.ItemWidth ?? 0;
        }
    }

    internal double ArtworkCenterY
    {
        get => ArtworkSide / 2;
    }

    internal double ArtworkCenterYRatio
    {
        get
        {
            var height = ActualHeight > 0
                ? ActualHeight
                : !double.IsNaN(Height) && Height > 0
                    ? Height
                    : ArtworkSide;
            return height > 0 ? ArtworkCenterY / height : 0.5;
        }
    }

    public CoverflowItemContainer()
    {
        DefaultStyleKey = typeof(CoverflowItemContainer);
        IsTabStop = true;
        UseSystemFocusVisuals = true;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
    }
}
