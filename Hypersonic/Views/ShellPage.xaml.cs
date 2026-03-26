using Hypersonic.Contracts.Services;
using Hypersonic.Helpers;
using Hypersonic.Models;
using Hypersonic.ViewModels;

using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

using System.Numerics;

using Windows.System;

namespace Hypersonic.Views;

public sealed partial class ShellPage : Page
{
    public ShellViewModel ViewModel
    {
        get;
    }

    public PlayerViewModel Player
    {
        get;
    }

    public ShellPage(ShellViewModel viewModel)
    {
        ViewModel = viewModel;
        Player = App.GetService<PlayerViewModel>();
        InitializeComponent();

        ViewModel.NavigationService.Frame = NavigationFrame;
        ViewModel.NavigationViewService.Initialize(NavigationViewControl);

        App.MainWindow.ExtendsContentIntoTitleBar = true;
        App.MainWindow.SetTitleBar(AppTitleBar);
        App.MainWindow.Activated += MainWindow_Activated;
        AppTitleBarText.Text = "AppDisplayName".GetLocalized();

        Player.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Player.IsPlaying))
            {
                PlayPauseIcon.Glyph = Player.IsPlaying ? "\uE769" : "\uE768";
            }
            else if (e.PropertyName == nameof(Player.CoverArtUrl))
            {
                PlayerCoverArt.Source = !string.IsNullOrEmpty(Player.CoverArtUrl)
                    ? new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(Player.CoverArtUrl))
                    : null;
            }
            else if (e.PropertyName == nameof(Player.IsQueueOpen))
            {
                if (Player.IsQueueOpen)
                {
                    Player.RefreshQueue();
                    OpenQueuePopup();
                }
                else if (QueuePopup.IsOpen)
                {
                    QueuePopup.IsOpen = false;
                }
            }
        };

        QueuePopup.Closed += (_, _) =>
        {
            Player.IsQueueOpen = false;
        };
    }

    private void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        TitleBarHelper.UpdateTitleBar(RequestedTheme);

        KeyboardAccelerators.Add(BuildKeyboardAccelerator(VirtualKey.Left, VirtualKeyModifiers.Menu));
        KeyboardAccelerators.Add(BuildKeyboardAccelerator(VirtualKey.GoBack));

        var searchAccelerator = new KeyboardAccelerator { Key = VirtualKey.K, Modifiers = VirtualKeyModifiers.Control };
        searchAccelerator.Invoked += (s, args) =>
        {
            TitleBarSearchBox.Focus(FocusState.Programmatic);
            args.Handled = true;
        };
        KeyboardAccelerators.Add(searchAccelerator);
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        App.AppTitlebar = AppTitleBarText as UIElement;
    }

    private void NavigationViewControl_DisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
    {
        AppTitleBar.Margin = new Thickness()
        {
            Left = sender.CompactPaneLength * (sender.DisplayMode == NavigationViewDisplayMode.Minimal ? 2 : 1),
            Top = AppTitleBar.Margin.Top,
            Right = AppTitleBar.Margin.Right,
            Bottom = AppTitleBar.Margin.Bottom
        };
    }

    private bool _isClosingPopup;
    private bool _popupAnimationInitialized;
    private bool _queuePopupAnimationInitialized;

    private void OpenQueuePopup()
    {
        if (!_queuePopupAnimationInitialized)
        {
            _queuePopupAnimationInitialized = true;
            ElementCompositionPreview.SetIsTranslationEnabled(QueuePopupBorder, true);
        }

        var transform = QueueToggleButton.TransformToVisual(this);
        var buttonPos = transform.TransformPoint(new Windows.Foundation.Point(0, 0));

        // Right-align popup to button
        QueuePopup.HorizontalOffset = buttonPos.X - 400 + QueueToggleButton.ActualWidth;

        // First open: place off-screen to measure actual height
        QueuePopup.VerticalOffset = -10000;
        QueuePopup.IsOpen = true;

        // After layout, reposition using measured height
        QueuePopupBorder.UpdateLayout();
        var actualHeight = QueuePopupBorder.ActualHeight;
        QueuePopup.VerticalOffset = -(actualHeight + 8);

        AnimateQueuePopup();

        // Auto-scroll to current track
        if (Player.EffectiveQueueIndex >= 0 && Player.EffectiveQueueIndex < Player.QueueItems.Count)
        {
            QueueListView.ScrollIntoView(Player.QueueItems[Player.EffectiveQueueIndex],
                ScrollIntoViewAlignment.Leading);
        }
    }

    private void AnimateQueuePopup()
    {
        var visual = ElementCompositionPreview.GetElementVisual(QueuePopupBorder);
        var compositor = visual.Compositor;
        var duration = TimeSpan.FromMilliseconds(200);
        var easing = compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f));

        var fadeAnimation = compositor.CreateScalarKeyFrameAnimation();
        fadeAnimation.InsertKeyFrame(0f, 0f);
        fadeAnimation.InsertKeyFrame(1f, 1f, easing);
        fadeAnimation.Duration = duration;

        var slideAnimation = compositor.CreateScalarKeyFrameAnimation();
        slideAnimation.InsertKeyFrame(0f, 8f);
        slideAnimation.InsertKeyFrame(1f, 0f, easing);
        slideAnimation.Duration = duration;

        visual.StartAnimation("Opacity", fadeAnimation);
        visual.StartAnimation("Translation.Y", slideAnimation);
    }

    private void QueueList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var element = e.OriginalSource as FrameworkElement;
        while (element != null && element.DataContext is not Models.QueueItem)
        {
            element = VisualTreeHelper.GetParent(element) as FrameworkElement;
        }

        if (element?.DataContext is Models.QueueItem item)
        {
            Player.JumpToQueueTrack(item);
        }
    }

    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs args)
    {
        if (_isClosingPopup)
        {
            return;
        }

        var text = TitleBarSearchBox.Text?.Trim();
        if (!string.IsNullOrEmpty(text) && text.Length >= 2)
        {
            await ViewModel.PerformSearchAsync(text);
            if (ViewModel.IsSearchResultsVisible)
            {
                OpenSearchPopup();
            }
            else
            {
                CloseSearchPopup();
            }
        }
        else
        {
            ViewModel.ClearSearch();
            CloseSearchPopup();
        }
    }

    private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (_isClosingPopup)
        {
            return;
        }

        var hasResults = ViewModel.HasArtistResults || ViewModel.HasAlbumResults || ViewModel.HasSongResults;
        if (hasResults)
        {
            ViewModel.IsSearchResultsVisible = true;
            OpenSearchPopup();
        }
    }

    private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (!TitleBarSearchBox.FocusState.HasFlag(FocusState.Programmatic) &&
                TitleBarSearchBox.FocusState == FocusState.Unfocused)
            {
                CloseSearchPopup();
            }
        });
    }

    private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            ViewModel.ClearSearch();
            CloseSearchPopup(() =>
            {
                TitleBarSearchBox.Text = string.Empty;
            });
        }
    }

    private void SearchResult_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SearchSuggestionItem item)
        {
            ViewModel.HandleSuggestionChosen(item);
            CloseSearchPopup(() =>
            {
                TitleBarSearchBox.Text = string.Empty;
            });
        }
    }

    private void EnsurePopupAnimationSetup()
    {
        if (_popupAnimationInitialized)
        {
            return;
        }

        _popupAnimationInitialized = true;
        ElementCompositionPreview.SetIsTranslationEnabled(SearchPopupBorder, true);
    }

    private void OpenSearchPopup()
    {
        if (!ViewModel.IsSearchResultsVisible)
        {
            return;
        }

        if (_isClosingPopup)
        {
            _isClosingPopup = false;
        }

        EnsurePopupAnimationSetup();

        var popupWidth = 800.0;
        var offset = (TitleBarSearchBox.ActualWidth - popupWidth) / 2;
        SearchResultsPopup.HorizontalOffset = offset;
        SearchResultsPopup.VerticalOffset = TitleBarSearchBox.ActualHeight + 4;

        // Reset before opening
        var visual = ElementCompositionPreview.GetElementVisual(SearchPopupBorder);
        visual.StopAnimation("Opacity");
        visual.StopAnimation("Translation.Y");
        visual.Opacity = 1f;
        visual.Properties.InsertVector3("Translation", Vector3.Zero);

        SearchResultsPopup.IsOpen = true;
        AnimatePopup(isOpening: true);
    }

    private void CloseSearchPopup(Action? afterClose = null)
    {
        if (!SearchResultsPopup.IsOpen || _isClosingPopup)
        {
            afterClose?.Invoke();
            return;
        }

        _isClosingPopup = true;
        AnimatePopup(isOpening: false, onCompleted: () =>
        {
            // Reset visual before hiding
            var visual = ElementCompositionPreview.GetElementVisual(SearchPopupBorder);
            visual.StopAnimation("Opacity");
            visual.StopAnimation("Translation.Y");
            visual.Opacity = 1f;
            visual.Properties.InsertVector3("Translation", Vector3.Zero);

            SearchResultsPopup.IsOpen = false;
            ViewModel.IsSearchResultsVisible = false;
            _isClosingPopup = false;
            afterClose?.Invoke();
        });
    }

    private void AnimatePopup(bool isOpening, Action? onCompleted = null)
    {
        var visual = ElementCompositionPreview.GetElementVisual(SearchPopupBorder);
        var compositor = visual.Compositor;

        var duration = TimeSpan.FromMilliseconds(200);
        var easing = isOpening
            ? compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f))
            : compositor.CreateCubicBezierEasingFunction(new Vector2(0.7f, 0f), new Vector2(1f, 0.5f));

        // Fade
        var fadeAnimation = compositor.CreateScalarKeyFrameAnimation();
        fadeAnimation.InsertKeyFrame(0f, isOpening ? 0f : 1f);
        fadeAnimation.InsertKeyFrame(1f, isOpening ? 1f : 0f, easing);
        fadeAnimation.Duration = duration;

        // Slide Y using Translation (additive, doesn't affect layout)
        var slideAnimation = compositor.CreateScalarKeyFrameAnimation();
        slideAnimation.InsertKeyFrame(0f, isOpening ? -8f : 0f);
        slideAnimation.InsertKeyFrame(1f, isOpening ? 0f : -8f, easing);
        slideAnimation.Duration = duration;

        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);

        visual.StartAnimation("Opacity", fadeAnimation);
        visual.StartAnimation("Translation.Y", slideAnimation);

        batch.End();

        if (onCompleted != null)
        {
            batch.Completed += (_, _) =>
            {
                DispatcherQueue.TryEnqueue(() => onCompleted());
            };
        }
    }

    private static KeyboardAccelerator BuildKeyboardAccelerator(VirtualKey key, VirtualKeyModifiers? modifiers = null)
    {
        var keyboardAccelerator = new KeyboardAccelerator() { Key = key };

        if (modifiers.HasValue)
        {
            keyboardAccelerator.Modifiers = modifiers.Value;
        }

        keyboardAccelerator.Invoked += OnKeyboardAcceleratorInvoked;

        return keyboardAccelerator;
    }

    private static void OnKeyboardAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        var navigationService = App.GetService<INavigationService>();

        var result = navigationService.GoBack();

        args.Handled = result;
    }
}
