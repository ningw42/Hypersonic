using System.Collections.Specialized;
using System.Numerics;

using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;

namespace Hypersonic.Controls;

/// <summary>
/// Carousel that renders items with a 3D perspective: the centered item is
/// enlarged and head-on, immediate neighbors are tilted toward the camera
/// and recede in depth. The control owns the focusable item container; the
/// supplied <see cref="ItemTemplate"/> is visual-only (no click handlers).
/// Activation policy: clicking a non-selected item centers it; clicking the
/// already-selected item raises <see cref="ItemInvoked"/>.
/// </summary>
public sealed class CoverflowControl : Control
{
    private const string PartScroller = "PART_Scroller";
    private const string PartRepeater = "PART_Repeater";
    private const string PartLeftArrow = "PART_LeftArrowBtn";
    private const string PartRightArrow = "PART_RightArrowBtn";
    private const string PartScrolledContent = "PART_ScrolledContent";

    private const double DefaultMaxRotationDegrees = 55.0;
    private const double DefaultRotationPerStep = 45.0;
    private const double DefaultMinimumScale = 0.78;
    private const double DefaultMaxDimmingOpacity = 0.20;
    private const double DefaultNonCenterItemOffsetZ = -160.0;
    private const double DefaultPressCancelDistance = 8.0;

    private ScrollViewer? _scroller;
    private ItemsRepeater? _repeater;
    private Button? _leftArrow;
    private Button? _rightArrow;
    private FrameworkElement? _scrolledContent;
    private ItemsSourceView? _observableItemsSourceView;
    private ItemsSourceView? _itemsSourceView;
    private object? _itemsSourceViewSource;

    private bool _isLoaded;
    private bool _templateHandlersAttached;
    private bool _suppressViewChanged;
    private bool _isSettingSelectedIndexInternally;
    private int _pressedCenterIndex = -1;
    private int _pendingRestoreInvokeIndex = -1;
    private int _pendingSelectionIndex = -1;
    private long _pendingRestoreInvokeVersion;
    private Point _pressedStartPoint;
    private bool _pressCanceled;
    private double _hoverRotationX;
    private double _hoverRotationY;

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(object), typeof(CoverflowControl),
            new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty ItemTemplateProperty =
        DependencyProperty.Register(nameof(ItemTemplate), typeof(DataTemplate), typeof(CoverflowControl),
            new PropertyMetadata(null, OnItemTemplateChanged));

    public static readonly DependencyProperty SelectedIndexProperty =
        DependencyProperty.Register(nameof(SelectedIndex), typeof(int), typeof(CoverflowControl),
            new PropertyMetadata(0, OnSelectedIndexChanged));

    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(nameof(SelectedItem), typeof(object), typeof(CoverflowControl),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ItemWidthProperty =
        DependencyProperty.Register(nameof(ItemWidth), typeof(double), typeof(CoverflowControl),
            new PropertyMetadata(220.0, OnLayoutMetricChanged));

    public static readonly DependencyProperty ItemSpacingProperty =
        DependencyProperty.Register(nameof(ItemSpacing), typeof(double), typeof(CoverflowControl),
            new PropertyMetadata(16.0, OnLayoutMetricChanged));

    public static readonly DependencyProperty HoverRotationXDegreesProperty =
        DependencyProperty.Register(nameof(HoverRotationXDegrees), typeof(double), typeof(CoverflowControl),
            new PropertyMetadata(5.0, OnInteractionMetricChanged));

    public static readonly DependencyProperty HoverRotationYDegreesProperty =
        DependencyProperty.Register(nameof(HoverRotationYDegrees), typeof(double), typeof(CoverflowControl),
            new PropertyMetadata(6.0, OnInteractionMetricChanged));

    public static readonly DependencyProperty PressedScaleProperty =
        DependencyProperty.Register(nameof(PressedScale), typeof(double), typeof(CoverflowControl),
            new PropertyMetadata(0.96));

    public static readonly DependencyProperty PressInDurationMillisecondsProperty =
        DependencyProperty.Register(nameof(PressInDurationMilliseconds), typeof(double), typeof(CoverflowControl),
            new PropertyMetadata(70.0));

    public static readonly DependencyProperty PressOutDurationMillisecondsProperty =
        DependencyProperty.Register(nameof(PressOutDurationMilliseconds), typeof(double), typeof(CoverflowControl),
            new PropertyMetadata(120.0));

    public static readonly DependencyProperty MaxRotationDegreesProperty =
        DependencyProperty.Register(nameof(MaxRotationDegrees), typeof(double), typeof(CoverflowControl),
            new PropertyMetadata(DefaultMaxRotationDegrees, OnInteractionMetricChanged));

    public static readonly DependencyProperty RotationPerStepProperty =
        DependencyProperty.Register(nameof(RotationPerStep), typeof(double), typeof(CoverflowControl),
            new PropertyMetadata(DefaultRotationPerStep, OnInteractionMetricChanged));

    public static readonly DependencyProperty MinimumScaleProperty =
        DependencyProperty.Register(nameof(MinimumScale), typeof(double), typeof(CoverflowControl),
            new PropertyMetadata(DefaultMinimumScale, OnInteractionMetricChanged));

    public static readonly DependencyProperty MaxDimmingOpacityProperty =
        DependencyProperty.Register(nameof(MaxDimmingOpacity), typeof(double), typeof(CoverflowControl),
            new PropertyMetadata(DefaultMaxDimmingOpacity, OnInteractionMetricChanged));

    public static readonly DependencyProperty NonCenterItemOffsetZProperty =
        DependencyProperty.Register(nameof(NonCenterItemOffsetZ), typeof(double), typeof(CoverflowControl),
            new PropertyMetadata(DefaultNonCenterItemOffsetZ, OnInteractionMetricChanged));

    public static readonly DependencyProperty PressCancelDistanceProperty =
        DependencyProperty.Register(nameof(PressCancelDistance), typeof(double), typeof(CoverflowControl),
            new PropertyMetadata(DefaultPressCancelDistance));

    public static readonly DependencyProperty DimmingOverlayCornerRadiusProperty =
        DependencyProperty.Register(nameof(DimmingOverlayCornerRadius), typeof(CornerRadius), typeof(CoverflowControl),
            new PropertyMetadata(new CornerRadius(10), OnContainerVisualChanged));

    public object? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public DataTemplate? ItemTemplate
    {
        get => (DataTemplate?)GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public double ItemWidth
    {
        get => (double)GetValue(ItemWidthProperty);
        set => SetValue(ItemWidthProperty, value);
    }

    public double ItemSpacing
    {
        get => (double)GetValue(ItemSpacingProperty);
        set => SetValue(ItemSpacingProperty, value);
    }

    public double HoverRotationXDegrees
    {
        get => (double)GetValue(HoverRotationXDegreesProperty);
        set => SetValue(HoverRotationXDegreesProperty, value);
    }

    public double HoverRotationYDegrees
    {
        get => (double)GetValue(HoverRotationYDegreesProperty);
        set => SetValue(HoverRotationYDegreesProperty, value);
    }

    public double PressedScale
    {
        get => (double)GetValue(PressedScaleProperty);
        set => SetValue(PressedScaleProperty, value);
    }

    public double PressInDurationMilliseconds
    {
        get => (double)GetValue(PressInDurationMillisecondsProperty);
        set => SetValue(PressInDurationMillisecondsProperty, value);
    }

    public double PressOutDurationMilliseconds
    {
        get => (double)GetValue(PressOutDurationMillisecondsProperty);
        set => SetValue(PressOutDurationMillisecondsProperty, value);
    }

    public double MaxRotationDegrees
    {
        get => (double)GetValue(MaxRotationDegreesProperty);
        set => SetValue(MaxRotationDegreesProperty, value);
    }

    public double RotationPerStep
    {
        get => (double)GetValue(RotationPerStepProperty);
        set => SetValue(RotationPerStepProperty, value);
    }

    public double MinimumScale
    {
        get => (double)GetValue(MinimumScaleProperty);
        set => SetValue(MinimumScaleProperty, value);
    }

    public double MaxDimmingOpacity
    {
        get => (double)GetValue(MaxDimmingOpacityProperty);
        set => SetValue(MaxDimmingOpacityProperty, value);
    }

    public double NonCenterItemOffsetZ
    {
        get => (double)GetValue(NonCenterItemOffsetZProperty);
        set => SetValue(NonCenterItemOffsetZProperty, value);
    }

    public double PressCancelDistance
    {
        get => (double)GetValue(PressCancelDistanceProperty);
        set => SetValue(PressCancelDistanceProperty, value);
    }

    public CornerRadius DimmingOverlayCornerRadius
    {
        get => (CornerRadius)GetValue(DimmingOverlayCornerRadiusProperty);
        set => SetValue(DimmingOverlayCornerRadiusProperty, value);
    }

    public event EventHandler<CoverflowItemInvokedEventArgs>? ItemInvoked;
    public event EventHandler<int>? SelectionChanged;

    public CoverflowControl()
    {
        DefaultStyleKey = typeof(CoverflowControl);
        IsTabStop = true;
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerExited += OnPointerExited;
        PointerCanceled += OnPointerCanceled;
        Tapped += OnTapped;
        Loaded += OnControlLoaded;
        Unloaded += OnControlUnloaded;
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        DetachTemplateHandlers();

        _scroller = GetTemplateChild(PartScroller) as ScrollViewer;
        _repeater = GetTemplateChild(PartRepeater) as ItemsRepeater;
        _leftArrow = GetTemplateChild(PartLeftArrow) as Button;
        _rightArrow = GetTemplateChild(PartRightArrow) as Button;
        _scrolledContent = GetTemplateChild(PartScrolledContent) as FrameworkElement;

        if (_repeater != null)
        {
            _repeater.ItemTemplate = new CoverflowContainerFactory(this);
            _repeater.ItemsSource = ItemsSource;
        }

        SyncLayoutSpacing();
        UpdateSelectedItem();
        AttachTemplateHandlers();

        UpdateLeadingPadding();
        UpdateArrowState();
    }

    private void OnControlLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        AttachCollectionChanged();
        AttachTemplateHandlers();

        if (_repeater != null)
        {
            _repeater.ItemsSource = ItemsSource;
        }

        UpdateSelectedItem();
        UpdateLeadingPadding();
        RecomputeAllTransforms();
        UpdateArrowState();
    }

    private void OnControlUnloaded(object sender, RoutedEventArgs e)
    {
        _pendingRestoreInvokeIndex = -1;
        _pendingRestoreInvokeVersion++;
        CancelCenterPress();
        ClearHoverTilt();
        DetachCollectionChanged();
        ClearItemsSourceView();
        DetachTemplateHandlers();
        _isLoaded = false;
    }

    private void AttachTemplateHandlers()
    {
        if (_templateHandlersAttached)
        {
            return;
        }

        var attachedAny = false;
        if (_scroller != null)
        {
            _scroller.ViewChanged += OnScrollerViewChanged;
            _scroller.SizeChanged += OnScrollerSizeChanged;
            attachedAny = true;
        }

        if (_repeater != null)
        {
            _repeater.ElementPrepared += OnElementPrepared;
            _repeater.ElementIndexChanged += OnElementIndexChanged;
            _repeater.ElementClearing += OnElementClearing;
            attachedAny = true;
        }

        if (_leftArrow != null)
        {
            _leftArrow.Click += OnLeftArrowClick;
            attachedAny = true;
        }

        if (_rightArrow != null)
        {
            _rightArrow.Click += OnRightArrowClick;
            attachedAny = true;
        }

        _templateHandlersAttached = attachedAny;
    }

    private void DetachTemplateHandlers()
    {
        if (!_templateHandlersAttached)
        {
            return;
        }

        if (_scroller != null)
        {
            _scroller.ViewChanged -= OnScrollerViewChanged;
            _scroller.SizeChanged -= OnScrollerSizeChanged;
        }
        if (_repeater != null)
        {
            _repeater.ElementPrepared -= OnElementPrepared;
            _repeater.ElementIndexChanged -= OnElementIndexChanged;
            _repeater.ElementClearing -= OnElementClearing;
        }
        if (_leftArrow != null)
        {
            _leftArrow.Click -= OnLeftArrowClick;
        }
        if (_rightArrow != null)
        {
            _rightArrow.Click -= OnRightArrowClick;
        }

        _templateHandlersAttached = false;
    }

    /// <summary>
    /// Element factory that produces a fresh CoverflowItemContainer for each
    /// realized data item. Avoids XamlReader.Load — runtime XAML parsing
    /// can't resolve user types outside the normal compilation context.
    /// </summary>
    private sealed class CoverflowContainerFactory : IElementFactory
    {
        private readonly CoverflowControl _owner;
        private readonly Stack<CoverflowItemContainer> _recycled = new();

        public CoverflowContainerFactory(CoverflowControl owner)
        {
            _owner = owner;
        }

        public UIElement GetElement(ElementFactoryGetArgs args)
        {
            var container = _recycled.Count > 0
                ? _recycled.Pop()
                : new CoverflowItemContainer();
            container.Owner = _owner;
            container.Content = args.Data;
            container.ContentTemplate = _owner.ItemTemplate;
            return container;
        }

        public void RecycleElement(ElementFactoryRecycleArgs args)
        {
            if (args.Element is CoverflowItemContainer container)
            {
                container.Content = null;
                ResetContainerVisualState(container);
                _recycled.Push(container);
            }
        }
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not CoverflowControl c)
        {
            return;
        }

        c.DetachCollectionChanged();
        c._pendingSelectionIndex = -1;
        c.RefreshItemsSourceView(e.NewValue);
        if (c._isLoaded)
        {
            c.AttachCollectionChanged();
        }

        if (c._repeater != null)
        {
            c._repeater.ItemsSource = e.NewValue;
            c.DispatcherQueue.TryEnqueue(() =>
            {
                c.UpdateSelectedItem();
                c.UpdateLeadingPadding();
                c.RecomputeAllTransforms();
                c.UpdateArrowState();
            });
        }
        else
        {
            c.UpdateSelectedItem();
        }
    }

    private static void OnItemTemplateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CoverflowControl c)
        {
            c.UpdateRealizedItemTemplates();
        }
    }

    private static void OnSelectedIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CoverflowControl c)
        {
            var requestedIndex = (int)e.NewValue;
            if (!c._isSettingSelectedIndexInternally)
            {
                c._pendingSelectionIndex = -1;
            }

            c.CancelCenterPress();
            c.ClearHoverTilt();
            c.UpdateSelectedItem();

            if (c._isSettingSelectedIndexInternally &&
                c._pendingSelectionIndex >= 0)
            {
                c.UpdateArrowState();
                return;
            }

            if (c.SelectedIndex != requestedIndex)
            {
                return;
            }

            if (requestedIndex < 0 || requestedIndex >= c.GetItemCount())
            {
                c.UpdateArrowState();
                return;
            }

            c.SelectionChanged?.Invoke(c, requestedIndex);
            c.CenterOnIndex(requestedIndex, animate: true);
            c.UpdateArrowState();
        }
    }

    private void AttachCollectionChanged()
    {
        var observableView = GetItemsSourceView();
        if (ReferenceEquals(_observableItemsSourceView, observableView))
        {
            return;
        }

        DetachCollectionChanged();
        _observableItemsSourceView = observableView;
        if (_observableItemsSourceView != null)
        {
            _observableItemsSourceView.CollectionChanged += OnItemsSourceCollectionChanged;
        }
    }

    private void DetachCollectionChanged()
    {
        if (_observableItemsSourceView != null)
        {
            _observableItemsSourceView.CollectionChanged -= OnItemsSourceCollectionChanged;
            _observableItemsSourceView = null;
        }
    }

    private void OnItemsSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (e.Action != NotifyCollectionChangedAction.Add)
            {
                _pendingSelectionIndex = -1;
            }

            UpdateRealizedItemContent();
            UpdateSelectedItem(preserveOutOfRangeSelection: e.Action == NotifyCollectionChangedAction.Add);
            RecomputeAllTransforms();
            UpdateArrowState();
            CenterOnIndex(SelectedIndex, animate: false);
        });
    }

    private static void OnLayoutMetricChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CoverflowControl c)
        {
            c.SyncLayoutSpacing();
            c.UpdateContainerMetrics();
            c.UpdateLeadingPadding();
            c.RecomputeAllTransforms();
            c.DispatcherQueue.TryEnqueue(() => c.CenterOnIndex(c.SelectedIndex, animate: false));
        }
    }

    private static void OnContainerVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CoverflowControl c)
        {
            c.UpdateContainerMetrics();
        }
    }

    private static void OnInteractionMetricChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CoverflowControl c)
        {
            c.RecomputeAllTransforms();
        }
    }

    private void SyncLayoutSpacing()
    {
        if (_repeater?.Layout is StackLayout stack)
        {
            stack.Spacing = ItemSpacing;
        }
    }

    private void OnScrollerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateLeadingPadding();
        DispatcherQueue.TryEnqueue(() => CenterOnIndex(SelectedIndex, animate: false));
    }

    private void OnScrollerViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        RecomputeAllTransforms();

        if (e.IsIntermediate)
        {
            return;
        }

        if (_suppressViewChanged)
        {
            _suppressViewChanged = false;
            return;
        }

        // Snap fallback: pick the nearest item by center and ChangeView there.
        var nearest = FindNearestIndexToCenter();
        if (nearest >= 0 && nearest != SelectedIndex)
        {
            SetValue(SelectedIndexProperty, nearest);
        }
        else if (nearest >= 0)
        {
            CenterOnIndex(nearest, animate: true);
        }
    }

    private void OnElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is CoverflowItemContainer container)
        {
            container.Owner = this;
            container.Index = args.Index;
            ApplyContainerMetrics(container);
            ResetContainerVisualState(container);
            SetPressScaleImmediate(container, 1.0);
            UpdateSelectedItem();
            // Run after layout pass so ActualWidth/Height are resolved.
            DispatcherQueue.TryEnqueue(() => RecomputeTransformForContainer(container));
        }
    }

    private void OnElementIndexChanged(ItemsRepeater sender, ItemsRepeaterElementIndexChangedEventArgs args)
    {
        if (args.Element is CoverflowItemContainer container)
        {
            container.Index = args.NewIndex;
            ApplyContainerMetrics(container);
            DispatcherQueue.TryEnqueue(() => RecomputeTransformForContainer(container));
        }
    }

    private void OnElementClearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args)
    {
        if (args.Element is CoverflowItemContainer container)
        {
            container.Owner = null;
            container.Index = -1;
            ResetContainerVisualState(container);
        }
    }

    private void UpdateContainerMetrics()
    {
        if (_repeater == null)
        {
            return;
        }

        var count = GetItemCount();
        for (var i = 0; i < count; i++)
        {
            if (_repeater.TryGetElement(i) is CoverflowItemContainer container)
            {
                ApplyContainerMetrics(container);
            }
        }
    }

    private void ApplyContainerMetrics(CoverflowItemContainer container)
    {
        container.Width = ItemWidth;
        container.Height = ItemWidth;
        container.DimmingCornerRadius = DimmingOverlayCornerRadius;
    }

    private static void ResetContainerVisualState(CoverflowItemContainer container)
    {
        if (container.Projection is PlaneProjection projection)
        {
            projection.RotationX = 0;
            projection.RotationY = 0;
            projection.LocalOffsetZ = 0;
            projection.CenterOfRotationX = 0.5;
            projection.CenterOfRotationY = 0.5;
        }
        else
        {
            container.Projection = null;
        }

        if (container.RenderTransform is ScaleTransform scale)
        {
            scale.ScaleX = 1.0;
            scale.ScaleY = 1.0;
        }
        else
        {
            container.RenderTransform = null;
        }

        container.Opacity = 1.0;
        container.DimmingOpacity = 0.0;
        Canvas.SetZIndex(container, 0);
    }

    private void UpdateRealizedItemContent()
    {
        if (_repeater == null)
        {
            return;
        }

        var count = GetItemCount();
        for (var i = 0; i < count; i++)
        {
            if (_repeater.TryGetElement(i) is CoverflowItemContainer container)
            {
                container.Index = i;
                container.Content = GetItemAt(i);
            }
        }
    }

    private void UpdateRealizedItemTemplates()
    {
        if (_repeater == null)
        {
            return;
        }

        var count = GetItemCount();
        for (var i = 0; i < count; i++)
        {
            if (_repeater.TryGetElement(i) is CoverflowItemContainer container)
            {
                container.ContentTemplate = ItemTemplate;
            }
        }

        DispatcherQueue.TryEnqueue(RecomputeAllTransforms);
    }

    private void OnLeftArrowClick(object sender, RoutedEventArgs e)
    {
        if (SelectedIndex > 0)
        {
            SelectedIndex--;
        }
    }

    private void OnRightArrowClick(object sender, RoutedEventArgs e)
    {
        var count = GetItemCount();
        if (SelectedIndex < count - 1)
        {
            SelectedIndex++;
        }
    }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        var count = GetItemCount();
        if (count == 0)
        {
            base.OnKeyDown(e);
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Left:
                if (SelectedIndex > 0)
                {
                    SelectedIndex--;
                }
                e.Handled = true;
                return;
            case VirtualKey.Right:
                if (SelectedIndex < count - 1)
                {
                    SelectedIndex++;
                }
                e.Handled = true;
                return;
            case VirtualKey.Home:
                SelectedIndex = 0;
                e.Handled = true;
                return;
            case VirtualKey.End:
                SelectedIndex = count - 1;
                e.Handled = true;
                return;
            case VirtualKey.Enter:
            case VirtualKey.Space:
                RaiseInvoked(SelectedIndex);
                e.Handled = true;
                return;
        }

        base.OnKeyDown(e);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (FindSourceContainer(e.OriginalSource) == null)
        {
            return;
        }

        var target = ResolveContainerAt(e.GetCurrentPoint(this).Position);
        if (target == null || !IsActiveCenterContainer(target))
        {
            CancelCenterPress();
            return;
        }

        _pressedCenterIndex = target.Index;
        _pressedStartPoint = e.GetCurrentPoint(this).Position;
        _pressCanceled = false;
        AnimatePressScale(target, GetSafePressedScale(), PressInDurationMilliseconds);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_pressedCenterIndex < 0)
        {
            return;
        }

        var target = ResolveContainerAt(e.GetCurrentPoint(this).Position);
        var index = _pressedCenterIndex;
        var pressedContainer = _repeater?.TryGetElement(index) as CoverflowItemContainer;
        var shouldInvoke = !_pressCanceled &&
            target?.Index == index &&
            pressedContainer != null &&
            IsActiveCenterContainer(pressedContainer);
        CancelCenterPress(pressedContainer);

        if (shouldInvoke)
        {
            e.Handled = true;
            InvokeAfterPressRestoreAsync(index, PressOutDurationMilliseconds);
        }
    }

    private void OnTapped(object sender, TappedRoutedEventArgs e)
    {
        if (FindSourceContainer(e.OriginalSource) == null)
        {
            return;
        }

        var target = ResolveContainerAt(e.GetPosition(this));
        if (target == null || target.Index < 0)
        {
            return;
        }

        if (target.Index != SelectedIndex)
        {
            SelectedIndex = target.Index;
            e.Handled = true;
            return;
        }

        if (_pendingRestoreInvokeIndex == target.Index)
        {
            e.Handled = true;
            return;
        }

        if (_pressedCenterIndex == target.Index && !_pressCanceled)
        {
            CancelCenterPress(target);
            InvokeAfterPressRestoreAsync(target.Index, PressOutDurationMilliseconds);
            e.Handled = true;
            return;
        }

        RaiseInvoked(target.Index);
        e.Handled = true;
    }

    private void RaiseInvoked(int index)
    {
        var item = GetItemAt(index);
        if (item is null)
        {
            return;
        }
        ItemInvoked?.Invoke(this, new CoverflowItemInvokedEventArgs(index, item));
    }

    private async void InvokeAfterPressRestoreAsync(int index, double restoreDurationMilliseconds)
    {
        if (_pendingRestoreInvokeIndex == index)
        {
            return;
        }

        var version = ++_pendingRestoreInvokeVersion;
        _pendingRestoreInvokeIndex = index;
        var delay = Math.Max(0, restoreDurationMilliseconds);
        await Task.Delay(TimeSpan.FromMilliseconds(delay));

        DispatcherQueue.TryEnqueue(() =>
        {
            if (version != _pendingRestoreInvokeVersion ||
                _pendingRestoreInvokeIndex != index)
            {
                return;
            }

            if (_isLoaded && SelectedIndex == index)
            {
                RaiseInvoked(index);
            }

            if (_pendingRestoreInvokeIndex == index)
            {
                _pendingRestoreInvokeIndex = -1;
            }
        });
    }

    private int GetItemCount()
    {
        if (ItemsSource is null)
        {
            return 0;
        }

        return GetItemsSourceView()?.Count ?? 0;
    }

    private object? GetItemAt(int index)
    {
        if (ItemsSource is null || index < 0)
        {
            return null;
        }

        var view = GetItemsSourceView();
        if (view == null)
        {
            return null;
        }

        return index < view.Count ? view.GetAt(index) : null;
    }

    private ItemsSourceView? GetItemsSourceView()
    {
        if (!ReferenceEquals(_itemsSourceViewSource, ItemsSource))
        {
            RefreshItemsSourceView(ItemsSource);
        }

        return _itemsSourceView;
    }

    private void RefreshItemsSourceView(object? source)
    {
        if (ReferenceEquals(_itemsSourceViewSource, source))
        {
            return;
        }

        ClearItemsSourceView();
        _itemsSourceViewSource = source;

        if (source == null)
        {
            return;
        }

        if (source is ItemsSourceView view)
        {
            _itemsSourceView = view;
            return;
        }

        _itemsSourceView = new ItemsSourceView(source);
    }

    private void ClearItemsSourceView()
    {
        _itemsSourceView = null;
        _itemsSourceViewSource = null;
    }

    private void SetSelectedIndexInternally(int index)
    {
        _isSettingSelectedIndexInternally = true;
        SetValue(SelectedIndexProperty, index);
        _isSettingSelectedIndexInternally = false;
    }

    private void UpdateSelectedItem(bool preserveOutOfRangeSelection = false)
    {
        var count = GetItemCount();
        if (count == 0)
        {
            _pendingSelectionIndex = SelectedIndex >= 0 ? SelectedIndex : -1;
            SetValue(SelectedItemProperty, null);
            return;
        }

        if (_pendingSelectionIndex >= 0)
        {
            if (_pendingSelectionIndex < count)
            {
                var pendingIndex = _pendingSelectionIndex;
                _pendingSelectionIndex = -1;
                if (SelectedIndex != pendingIndex)
                {
                    SetSelectedIndexInternally(pendingIndex);
                    return;
                }
            }
            else
            {
                var fallbackIndex = count - 1;
                if (SelectedIndex != fallbackIndex)
                {
                    SetSelectedIndexInternally(fallbackIndex);
                    return;
                }

                SetValue(SelectedItemProperty, GetItemAt(SelectedIndex));
                return;
            }
        }

        if (SelectedIndex < 0)
        {
            _pendingSelectionIndex = -1;
            SetSelectedIndexInternally(0);
            return;
        }

        if (SelectedIndex >= count)
        {
            if (preserveOutOfRangeSelection)
            {
                _pendingSelectionIndex = SelectedIndex;
                SetSelectedIndexInternally(count - 1);
                return;
            }

            _pendingSelectionIndex = -1;
            SetSelectedIndexInternally(count - 1);
            return;
        }

        _pendingSelectionIndex = -1;
        SetValue(SelectedItemProperty, GetItemAt(SelectedIndex));
    }

    private void UpdateLeadingPadding()
    {
        if (_scroller == null || _scrolledContent is not Border border)
        {
            return;
        }

        var viewport = _scroller.ViewportWidth;
        if (viewport <= 0)
        {
            return;
        }

        // Padding (not Margin) so the leading/trailing space contributes to
        // ScrollViewer's scrollable extent — otherwise the first card sits at
        // the left edge and the last card's center is past ScrollableWidth.
        var pad = Math.Max(0, (viewport - ItemWidth) / 2);
        border.Padding = new Thickness(pad, 0, pad, 0);
    }

    /// <summary>
    /// Center of item i in scroller content coords:
    ///   leadingPad + i * (ItemWidth + ItemSpacing) + ItemWidth/2
    /// We compute against the repeater layout instead of querying realized
    /// containers so unrealized off-screen items still get correct targets.
    /// </summary>
    private double GetItemCenterOffset(int index)
    {
        var leadingPad = (_scrolledContent as Border)?.Padding.Left ?? 0;
        return leadingPad + index * (ItemWidth + ItemSpacing) + ItemWidth / 2;
    }

    private void CenterOnIndex(int index, bool animate)
    {
        if (_scroller == null)
        {
            return;
        }

        var count = GetItemCount();
        if (count == 0 || index < 0 || index >= count)
        {
            return;
        }

        var center = GetItemCenterOffset(index);
        var target = center - _scroller.ViewportWidth / 2;
        target = Math.Max(0, Math.Min(target, Math.Max(0, _scroller.ScrollableWidth)));

        if (Math.Abs(_scroller.HorizontalOffset - target) < 0.5)
        {
            return;
        }

        _suppressViewChanged = true;
        if (!_scroller.ChangeView(target, null, null, disableAnimation: !animate))
        {
            _suppressViewChanged = false;
        }
    }

    private int FindNearestIndexToCenter()
    {
        if (_scroller == null)
        {
            return -1;
        }

        var count = GetItemCount();
        if (count == 0)
        {
            return -1;
        }

        var scrollerCenter = _scroller.HorizontalOffset + _scroller.ViewportWidth / 2;
        var step = ItemWidth + ItemSpacing;
        return FindNearestIndexToCenter(count, scrollerCenter, step);
    }

    private int FindNearestIndexToCenter(int count, double scrollerCenter, double step)
    {
        if (count == 0)
        {
            return -1;
        }

        if (step <= 0)
        {
            return 0;
        }

        var leadingPad = (_scrolledContent as Border)?.Padding.Left ?? 0;
        var idx = (int)Math.Round((scrollerCenter - leadingPad - ItemWidth / 2) / step);
        return Math.Max(0, Math.Min(count - 1, idx));
    }

    private CoverflowItemContainer? GetActiveCenterContainer()
    {
        var centerIndex = FindNearestIndexToCenter();
        if (centerIndex < 0)
        {
            centerIndex = SelectedIndex;
        }

        return _repeater?.TryGetElement(centerIndex) as CoverflowItemContainer;
    }

    private bool IsActiveCenterContainer(CoverflowItemContainer container)
    {
        return container.Index == SelectedIndex;
    }

    private void CancelCenterPress(CoverflowItemContainer? container = null)
    {
        if (_pressedCenterIndex < 0)
        {
            return;
        }

        var pressedIndex = _pressedCenterIndex;
        _pressedCenterIndex = -1;
        _pressCanceled = false;

        if (container != null)
        {
            AnimatePressScale(container, 1.0, PressOutDurationMilliseconds);
            return;
        }

        if (_repeater?.TryGetElement(pressedIndex) is CoverflowItemContainer pressedContainer)
        {
            AnimatePressScale(pressedContainer, 1.0, PressOutDurationMilliseconds);
        }
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        UpdatePressedCenterMovement(e);
        UpdateHoverTilt(e);
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (IsPointerWithin(e, this))
        {
            return;
        }

        ClearHoverTilt();
        CancelCenterPress();
    }

    private void OnPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        ClearHoverTilt();
        CancelCenterPress();
    }

    private void UpdatePressedCenterMovement(PointerRoutedEventArgs e)
    {
        if (_pressedCenterIndex < 0 || _pressCanceled)
        {
            return;
        }

        var position = e.GetCurrentPoint(this).Position;
        var deltaX = position.X - _pressedStartPoint.X;
        var deltaY = position.Y - _pressedStartPoint.Y;
        if (Math.Sqrt(deltaX * deltaX + deltaY * deltaY) <= GetSafePressCancelDistance())
        {
            return;
        }

        _pressCanceled = true;
        CancelCenterPress();
    }

    private void UpdateHoverTilt(PointerRoutedEventArgs e)
    {
        var centerContainer = GetActiveCenterContainer();
        if (centerContainer == null)
        {
            ClearHoverTilt();
            return;
        }

        var side = centerContainer.ArtworkSide;

        if (side <= 0)
        {
            return;
        }

        var position = e.GetCurrentPoint(centerContainer).Position;
        var left = (centerContainer.ActualWidth - side) / 2;
        var top = 0.0;
        var normalizedX = Math.Clamp(((position.X - left) / side - 0.5) * 2, -1.0, 1.0);
        var normalizedY = Math.Clamp(((position.Y - top) / side - 0.5) * 2, -1.0, 1.0);

        var rotationX = -normalizedY * HoverRotationXDegrees;
        var rotationY = normalizedX * HoverRotationYDegrees;

        if (Math.Abs(rotationX - _hoverRotationX) < 0.01 &&
            Math.Abs(rotationY - _hoverRotationY) < 0.01)
        {
            return;
        }

        _hoverRotationX = rotationX;
        _hoverRotationY = rotationY;
        RecomputeTransformForContainer(centerContainer);
    }

    private void ClearHoverTilt()
    {
        if (Math.Abs(_hoverRotationX) < 0.01 && Math.Abs(_hoverRotationY) < 0.01)
        {
            return;
        }

        _hoverRotationX = 0;
        _hoverRotationY = 0;

        var centerContainer = GetActiveCenterContainer();
        if (centerContainer != null)
        {
            RecomputeTransformForContainer(centerContainer);
        }
    }

    private double GetSafePressedScale()
    {
        return Math.Clamp(PressedScale, 0.1, 1.5);
    }

    private void SetPressScaleImmediate(CoverflowItemContainer container, double targetScale)
    {
        var visual = ElementCompositionPreview.GetElementVisual(container);
        UpdatePressVisualCenter(container, visual);
        visual.StopAnimation("Scale");

        var scale = (float)Math.Clamp(targetScale, 0.1, 1.5);
        visual.Scale = new Vector3(scale, scale, 1f);
    }

    private void AnimatePressScale(
        CoverflowItemContainer container,
        double targetScale,
        double durationMilliseconds)
    {
        var visual = ElementCompositionPreview.GetElementVisual(container);
        UpdatePressVisualCenter(container, visual);

        var duration = Math.Max(0, durationMilliseconds);
        var scale = (float)Math.Clamp(targetScale, 0.1, 1.5);
        if (duration <= 0)
        {
            visual.StopAnimation("Scale");
            visual.Scale = new Vector3(scale, scale, 1f);
            return;
        }

        var compositor = visual.Compositor;
        var animation = compositor.CreateVector3KeyFrameAnimation();
        var easing = compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.1f, 0.9f),
            new Vector2(0.2f, 1f));
        animation.InsertKeyFrame(0f, visual.Scale);
        animation.InsertKeyFrame(1f, new Vector3(scale, scale, 1f), easing);
        animation.Duration = TimeSpan.FromMilliseconds(duration);

        visual.StartAnimation("Scale", animation);
    }

    private static void UpdatePressVisualCenter(CoverflowItemContainer container, Visual visual)
    {
        var width = container.ActualWidth > 0 ? container.ActualWidth : container.Width;
        visual.CenterPoint = new Vector3((float)(width / 2), (float)container.ArtworkCenterY, 0f);
    }

    private CoverflowItemContainer? ResolveContainerAt(Point point)
    {
        if (_repeater == null)
        {
            return null;
        }

        CoverflowItemContainer? best = null;
        var bestZIndex = int.MinValue;
        var bestDistance = double.MaxValue;
        var count = GetItemCount();

        for (var i = 0; i < count; i++)
        {
            if (_repeater.TryGetElement(i) is not CoverflowItemContainer container ||
                !TryGetArtworkQuad(container, out var quad) ||
                !IsPointInConvexQuad(point, quad))
            {
                continue;
            }

            var zIndex = Canvas.GetZIndex(container);
            var distance = DistanceSquared(point, GetQuadCenter(quad));
            if (zIndex > bestZIndex || zIndex == bestZIndex && distance < bestDistance)
            {
                best = container;
                bestZIndex = zIndex;
                bestDistance = distance;
            }
        }

        return best;
    }

    private bool TryGetArtworkQuad(CoverflowItemContainer container, out Point[] quad)
    {
        quad = new Point[4];

        var width = container.ActualWidth > 0 ? container.ActualWidth : ItemWidth;
        var side = container.ArtworkSide;
        if (side <= 0)
        {
            return false;
        }

        var projection = container.Projection as PlaneProjection;
        var scale = container.RenderTransform as ScaleTransform;
        var scaleX = scale?.ScaleX ?? 1.0;
        var scaleY = scale?.ScaleY ?? 1.0;
        var halfWidth = side * scaleX / 2;
        var halfHeight = side * scaleY / 2;
        var center = new Point(width / 2, container.ArtworkCenterY);
        var transform = container.TransformToVisual(this);
        var screenCenter = transform.TransformPoint(center);

        quad[0] = ProjectArtworkPoint(-halfWidth, -halfHeight, projection, screenCenter);
        quad[1] = ProjectArtworkPoint(halfWidth, -halfHeight, projection, screenCenter);
        quad[2] = ProjectArtworkPoint(halfWidth, halfHeight, projection, screenCenter);
        quad[3] = ProjectArtworkPoint(-halfWidth, halfHeight, projection, screenCenter);
        return true;
    }

    private static Point ProjectArtworkPoint(
        double x,
        double y,
        PlaneProjection? projection,
        Point screenCenter)
    {
        var rotationX = DegreesToRadians(projection?.RotationX ?? 0);
        var rotationY = DegreesToRadians(projection?.RotationY ?? 0);
        var localOffsetZ = projection?.LocalOffsetZ ?? 0;

        var cosX = Math.Cos(rotationX);
        var sinX = Math.Sin(rotationX);
        var cosY = Math.Cos(rotationY);
        var sinY = Math.Sin(rotationY);

        var rotatedY = y * cosX;
        var zAfterX = y * sinX;
        var rotatedX = x * cosY + zAfterX * sinY;
        var rotatedZ = -x * sinY + zAfterX * cosY + localOffsetZ;

        const double perspectiveDepth = 800;
        var perspective = perspectiveDepth / Math.Max(1, perspectiveDepth - rotatedZ);
        return new Point(
            screenCenter.X + rotatedX * perspective,
            screenCenter.Y + rotatedY * perspective);
    }

    private static double DegreesToRadians(double degrees)
    {
        return degrees * Math.PI / 180.0;
    }

    private static bool IsPointInConvexQuad(Point point, Point[] quad)
    {
        const double tolerance = 1.5;
        var hasPositive = false;
        var hasNegative = false;

        for (var i = 0; i < quad.Length; i++)
        {
            var a = quad[i];
            var b = quad[(i + 1) % quad.Length];
            var cross = (b.X - a.X) * (point.Y - a.Y) - (b.Y - a.Y) * (point.X - a.X);
            if (Math.Abs(cross) <= tolerance)
            {
                continue;
            }

            hasPositive |= cross > 0;
            hasNegative |= cross < 0;
            if (hasPositive && hasNegative)
            {
                return false;
            }
        }

        return true;
    }

    private static Point GetQuadCenter(Point[] quad)
    {
        var x = 0.0;
        var y = 0.0;
        foreach (var point in quad)
        {
            x += point.X;
            y += point.Y;
        }

        return new Point(x / quad.Length, y / quad.Length);
    }

    private static double DistanceSquared(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private static CoverflowItemContainer? FindSourceContainer(object? source)
    {
        var current = source as DependencyObject;
        while (current != null)
        {
            if (current is CoverflowItemContainer container)
            {
                return container;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static bool IsPointerWithin(PointerRoutedEventArgs e, FrameworkElement element)
    {
        var position = e.GetCurrentPoint(element).Position;
        return position.X >= 0 &&
            position.Y >= 0 &&
            position.X <= element.ActualWidth &&
            position.Y <= element.ActualHeight;
    }

    private void RecomputeAllTransforms()
    {
        if (_repeater == null || _scroller == null)
        {
            return;
        }

        var count = GetItemCount();
        var step = ItemWidth + ItemSpacing;
        if (count == 0 || step <= 0)
        {
            UpdateArrowState();
            return;
        }

        var scrollerCenter = _scroller.HorizontalOffset + _scroller.ViewportWidth / 2;
        var centerIndex = FindNearestIndexToCenter(count, scrollerCenter, step);

        for (var i = 0; i < count; i++)
        {
            var element = _repeater.TryGetElement(i);
            if (element is CoverflowItemContainer container)
            {
                RecomputeTransformForContainer(container, centerIndex, scrollerCenter, step);
            }
        }

        UpdateArrowState();
    }

    private void RecomputeTransformForContainer(CoverflowItemContainer container)
    {
        if (_scroller == null || container.Index < 0)
        {
            return;
        }

        var count = GetItemCount();
        var step = ItemWidth + ItemSpacing;
        if (count == 0 || step <= 0)
        {
            return;
        }

        var scrollerCenter = _scroller.HorizontalOffset + _scroller.ViewportWidth / 2;
        var centerIndex = FindNearestIndexToCenter(count, scrollerCenter, step);
        RecomputeTransformForContainer(container, centerIndex, scrollerCenter, step);
    }

    private void RecomputeTransformForContainer(
        CoverflowItemContainer container,
        int centerIndex,
        double scrollerCenter,
        double step)
    {
        if (container.Index < 0)
        {
            return;
        }

        var itemCenter = GetItemCenterOffset(container.Index);
        var delta = (itemCenter - scrollerCenter) / step;
        var centerDistance = Math.Abs(delta);
        var visualDistance = Math.Min(centerDistance, 3.0);
        var isCenterItem = container.Index == centerIndex;

        // Side cards face TOWARD center: left card's right edge comes forward
        // (negative RotationY in PlaneProjection), right card's left edge
        // comes forward (positive RotationY). Sign matches the iPad reference.
        var maxRotationDegrees = GetSafeMaxRotationDegrees();
        var rotationY = Math.Clamp(delta * GetSafeRotationPerStep(), -maxRotationDegrees, maxRotationDegrees);
        var rotationX = 0.0;
        var minimumScale = GetSafeMinimumScale();
        var maxDimmingOpacity = GetSafeMaxDimmingOpacity();
        var scale = Math.Max(minimumScale, 1.0 - (1.0 - minimumScale) * visualDistance);
        var localOffsetZ = isCenterItem ? 0.0 : NonCenterItemOffsetZ;
        var dimmingOpacity = Math.Min(maxDimmingOpacity, maxDimmingOpacity * visualDistance);

        if (isCenterItem)
        {
            rotationX = _hoverRotationX;
            rotationY += _hoverRotationY;
        }

        var artworkCenterYRatio = container.ArtworkCenterYRatio;

        // PlaneProjection: per-card perspective with normalized rotation centers.
        // The center rotates around the album artwork. LocalOffsetZ
        // pushes the card backward in its own local space, which the projection
        // then projects with the built-in perspective.
        var projection = container.Projection as PlaneProjection;
        if (projection == null)
        {
            projection = new PlaneProjection();
            container.Projection = projection;
        }

        projection.CenterOfRotationX = 0.5;
        projection.CenterOfRotationY = artworkCenterYRatio;
        projection.RotationX = rotationX;
        projection.RotationY = rotationY;
        projection.LocalOffsetZ = localOffsetZ;

        // Scale handled separately so PlaneProjection doesn't need a scale field.
        container.RenderTransformOrigin = new Point(0.5, artworkCenterYRatio);
        var scaleTransform = container.RenderTransform as ScaleTransform;
        if (scaleTransform == null)
        {
            scaleTransform = new ScaleTransform();
            container.RenderTransform = scaleTransform;
        }

        scaleTransform.ScaleX = scale;
        scaleTransform.ScaleY = scale;
        container.Opacity = 1.0;
        container.DimmingOpacity = dimmingOpacity;

        // Depth is intentionally two-plane: the active center card sits on the
        // front plane while every side card shares one recessed plane. Draw
        // order is still sorted by raw distance from center, so nearer side
        // cards cover farther side cards where they overlap.
        var zIndex = isCenterItem
            ? 20000
            : 10000 - (int)Math.Round(centerDistance * 1000);
        Canvas.SetZIndex(container, zIndex);
    }

    private double GetSafeMaxRotationDegrees()
    {
        return Math.Clamp(MaxRotationDegrees, 0.0, 89.0);
    }

    private double GetSafeRotationPerStep()
    {
        return Math.Max(0.0, RotationPerStep);
    }

    private double GetSafeMinimumScale()
    {
        return Math.Clamp(MinimumScale, 0.1, 2.0);
    }

    private double GetSafeMaxDimmingOpacity()
    {
        return Math.Clamp(MaxDimmingOpacity, 0.0, 1.0);
    }

    private double GetSafePressCancelDistance()
    {
        return Math.Max(0.0, PressCancelDistance);
    }

    private void UpdateArrowState()
    {
        if (_leftArrow == null || _rightArrow == null)
        {
            return;
        }

        var count = GetItemCount();
        var hasOverflow = count > 1;
        var visibility = hasOverflow ? Visibility.Visible : Visibility.Collapsed;
        _leftArrow.Visibility = visibility;
        _rightArrow.Visibility = visibility;

        if (!hasOverflow)
        {
            return;
        }

        var atStart = SelectedIndex <= 0;
        var atEnd = SelectedIndex >= count - 1;
        _leftArrow.IsEnabled = !atStart;
        _leftArrow.Opacity = atStart ? 0.4 : 1.0;
        _rightArrow.IsEnabled = !atEnd;
        _rightArrow.Opacity = atEnd ? 0.4 : 1.0;
    }
}

public sealed class CoverflowItemInvokedEventArgs : EventArgs
{
    public CoverflowItemInvokedEventArgs(int index, object item)
    {
        Index = index;
        Item = item;
    }

    public int Index { get; }
    public object Item { get; }
}
