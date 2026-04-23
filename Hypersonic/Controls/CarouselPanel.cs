using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Hypersonic.Controls;

public class CarouselPanel : ContentControl
{
    private ScrollViewer? _scroller;
    private Button? _leftArrowBtn;
    private Button? _rightArrowBtn;

    public CarouselPanel()
    {
        DefaultStyleKey = typeof(CarouselPanel);
        SizeChanged += CarouselPanel_SizeChanged;
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (_leftArrowBtn != null)
            _leftArrowBtn.Click -= LeftArrowBtn_Click;
        if (_rightArrowBtn != null)
            _rightArrowBtn.Click -= RightArrowBtn_Click;
        if (_scroller != null)
            _scroller.ViewChanged -= Scroller_ViewChanged;

        _scroller = GetTemplateChild("Scroller") as ScrollViewer;
        _leftArrowBtn = GetTemplateChild("LeftArrowBtn") as Button;
        _rightArrowBtn = GetTemplateChild("RightArrowBtn") as Button;

        if (_scroller != null)
            _scroller.ViewChanged += Scroller_ViewChanged;
        if (_leftArrowBtn != null)
            _leftArrowBtn.Click += LeftArrowBtn_Click;
        if (_rightArrowBtn != null)
            _rightArrowBtn.Click += RightArrowBtn_Click;
    }

    private void CarouselPanel_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateButtonVisibility();
    }

    private void Scroller_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        UpdateButtonVisibility();
    }

    private void UpdateButtonVisibility()
    {
        if (_scroller == null || _leftArrowBtn == null || _rightArrowBtn == null)
            return;

        var hasOverflow = _scroller.ScrollableWidth > 0;
        var visibility = hasOverflow ? Visibility.Visible : Visibility.Collapsed;
        _leftArrowBtn.Visibility = visibility;
        _rightArrowBtn.Visibility = visibility;

        if (hasOverflow)
            UpdateArrowStates();
    }

    private void UpdateArrowStates()
    {
        if (_scroller == null || _leftArrowBtn == null || _rightArrowBtn == null)
            return;

        var atStart = _scroller.HorizontalOffset <= 0;
        var atEnd = _scroller.HorizontalOffset >= _scroller.ScrollableWidth;

        _leftArrowBtn.IsEnabled = !atStart;
        _leftArrowBtn.Opacity = atStart ? 0.4 : 1.0;
        _rightArrowBtn.IsEnabled = !atEnd;
        _rightArrowBtn.Opacity = atEnd ? 0.4 : 1.0;
    }

    private void LeftArrowBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_scroller == null) return;
        var newOffset = _scroller.HorizontalOffset - _scroller.ViewportWidth;
        _scroller.ChangeView(Math.Max(0, newOffset), null, null);
    }

    private void RightArrowBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_scroller == null) return;
        var newOffset = _scroller.HorizontalOffset + _scroller.ViewportWidth;
        _scroller.ChangeView(Math.Min(_scroller.ScrollableWidth, newOffset), null, null);
    }
}
