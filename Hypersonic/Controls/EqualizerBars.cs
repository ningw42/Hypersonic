using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

using System.Numerics;

namespace Hypersonic.Controls;

public sealed class EqualizerBars : Control
{
    private Border? _bar1;
    private Border? _bar2;
    private Border? _bar3;
    private Visual? _visual1;
    private Visual? _visual2;
    private Visual? _visual3;
    private Vector3KeyFrameAnimation? _anim1;
    private Vector3KeyFrameAnimation? _anim2;
    private Vector3KeyFrameAnimation? _anim3;

    public static readonly DependencyProperty IsAnimatingProperty =
        DependencyProperty.Register(nameof(IsAnimating), typeof(bool), typeof(EqualizerBars),
            new PropertyMetadata(false, OnIsAnimatingChanged));

    public bool IsAnimating
    {
        get => (bool)GetValue(IsAnimatingProperty);
        set => SetValue(IsAnimatingProperty, value);
    }

    public EqualizerBars()
    {
        DefaultStyleKey = typeof(EqualizerBars);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _bar1 = GetTemplateChild("PART_Bar1") as Border;
        _bar2 = GetTemplateChild("PART_Bar2") as Border;
        _bar3 = GetTemplateChild("PART_Bar3") as Border;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SetupAnimations();
        ApplyAnimationState(IsAnimating);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        StopAllAnimations();
        _visual1 = null;
        _visual2 = null;
        _visual3 = null;
        _anim1 = null;
        _anim2 = null;
        _anim3 = null;
    }

    private void SetupAnimations()
    {
        if (_bar1 == null || _bar2 == null || _bar3 == null)
        {
            return;
        }

        var accentColor = (Windows.UI.Color)Application.Current.Resources["SystemAccentColorLight2"];
        var brush = new SolidColorBrush(accentColor);
        _bar1.Background = brush;
        _bar2.Background = brush;
        _bar3.Background = brush;

        _visual1 = ElementCompositionPreview.GetElementVisual(_bar1);
        _visual2 = ElementCompositionPreview.GetElementVisual(_bar2);
        _visual3 = ElementCompositionPreview.GetElementVisual(_bar3);

        _visual1.CenterPoint = new Vector3(1.5f, 16f, 0f);
        _visual2.CenterPoint = new Vector3(1.5f, 16f, 0f);
        _visual3.CenterPoint = new Vector3(1.5f, 16f, 0f);

        _visual1.Scale = new Vector3(1f, 0.6f, 1f);
        _visual2.Scale = new Vector3(1f, 0.35f, 1f);
        _visual3.Scale = new Vector3(1f, 0.8f, 1f);

        var compositor = _visual1.Compositor;

        _anim1 = CreateBarAnimation(compositor, 0.3f, 1.0f, TimeSpan.FromMilliseconds(400));
        _anim2 = CreateBarAnimation(compositor, 0.2f, 0.9f, TimeSpan.FromMilliseconds(550));
        _anim3 = CreateBarAnimation(compositor, 0.4f, 1.0f, TimeSpan.FromMilliseconds(700));
    }

    private static Vector3KeyFrameAnimation CreateBarAnimation(
        Compositor compositor, float minScale, float maxScale, TimeSpan duration)
    {
        var easing = compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.42f, 0f), new Vector2(0.58f, 1f));

        var animation = compositor.CreateVector3KeyFrameAnimation();
        animation.InsertKeyFrame(0f, new Vector3(1f, minScale, 1f), easing);
        animation.InsertKeyFrame(0.5f, new Vector3(1f, maxScale, 1f), easing);
        animation.InsertKeyFrame(1f, new Vector3(1f, minScale, 1f), easing);
        animation.Duration = duration;
        animation.IterationBehavior = AnimationIterationBehavior.Forever;
        return animation;
    }

    private void ApplyAnimationState(bool animate)
    {
        if (_visual1 == null || _visual2 == null || _visual3 == null)
        {
            return;
        }

        if (animate)
        {
            _visual1.StartAnimation("Scale", _anim1);
            _visual2.StartAnimation("Scale", _anim2);
            _visual3.StartAnimation("Scale", _anim3);
        }
        else
        {
            StopAllAnimations();
        }
    }

    private void StopAllAnimations()
    {
        _visual1?.StopAnimation("Scale");
        _visual2?.StopAnimation("Scale");
        _visual3?.StopAnimation("Scale");
    }

    private static void OnIsAnimatingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EqualizerBars bars)
        {
            bars.ApplyAnimationState((bool)e.NewValue);
        }
    }
}
