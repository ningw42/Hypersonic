using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

using System.Numerics;

namespace Hypersonic.Controls;

/// <summary>
/// A TextBlock that scrolls horizontally when its text overflows the available width.
/// </summary>
public sealed class MarqueeTextBlock : Control
{
    private TextBlock? _textBlock;
    private Canvas? _canvas;
    private CompositionPropertySet? _animProps;
    private ExpressionAnimation? _scrollAnimation;
    private ScalarKeyFrameAnimation? _progressAnimation;

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(MarqueeTextBlock),
            new PropertyMetadata(string.Empty, OnTextChanged));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public MarqueeTextBlock()
    {
        DefaultStyleKey = typeof(MarqueeTextBlock);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _textBlock = GetTemplateChild("PART_TextBlock") as TextBlock;
        _canvas = GetTemplateChild("PART_Canvas") as Canvas;

        if (_textBlock != null)
        {
            _textBlock.SizeChanged += OnTextBlockSizeChanged;
        }

        SizeChanged += OnControlSizeChanged;
        PointerEntered += OnPointerEntered;
        PointerExited += OnPointerExited;
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarqueeTextBlock m)
        {
            m.StopAnimation();
        }
    }

    private void OnTextBlockSizeChanged(object sender, SizeChangedEventArgs e)
    {
        StopAnimation();
    }

    private void OnControlSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_canvas != null)
        {
            _canvas.Clip = new RectangleGeometry
            {
                Rect = new Windows.Foundation.Rect(0, 0, ActualWidth, ActualHeight)
            };
        }

        StopAnimation();
    }

    private void OnPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        TryStartAnimation();
    }

    private void OnPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        StopAnimation();
    }

    private void TryStartAnimation()
    {
        if (_textBlock == null)
        {
            return;
        }

        var overflow = _textBlock.ActualWidth - ActualWidth;
        if (overflow <= 0)
        {
            return;
        }

        var visual = ElementCompositionPreview.GetElementVisual(_textBlock);
        var compositor = visual.Compositor;

        _animProps = compositor.CreatePropertySet();
        _animProps.InsertScalar("progress", 0f);

        _scrollAnimation = compositor.CreateExpressionAnimation("-props.progress * overflow");
        _scrollAnimation.SetReferenceParameter("props", _animProps);
        _scrollAnimation.SetScalarParameter("overflow", (float)overflow);

        var duration = TimeSpan.FromSeconds(Math.Max(2, overflow / 30));
        _progressAnimation = compositor.CreateScalarKeyFrameAnimation();
        _progressAnimation.InsertKeyFrame(0f, 0f);
        _progressAnimation.InsertKeyFrame(0.1f, 0f);
        _progressAnimation.InsertKeyFrame(0.5f, 1f);
        _progressAnimation.InsertKeyFrame(0.6f, 1f);
        _progressAnimation.InsertKeyFrame(1f, 0f);
        _progressAnimation.Duration = duration;
        _progressAnimation.IterationBehavior = AnimationIterationBehavior.Forever;

        visual.StartAnimation("Offset.X", _scrollAnimation);
        _animProps.StartAnimation("progress", _progressAnimation);
    }

    private void StopAnimation()
    {
        if (_textBlock == null)
        {
            return;
        }

        var visual = ElementCompositionPreview.GetElementVisual(_textBlock);
        visual.StopAnimation("Offset.X");
        visual.Offset = new Vector3(0, 0, 0);

        _animProps = null;
        _scrollAnimation = null;
        _progressAnimation = null;
    }
}
