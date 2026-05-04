using Hypersonic.Contracts.Services;
using Hypersonic.Core.Helpers;
using Hypersonic.Core.Models;
using Hypersonic.Helpers;
using Hypersonic.Models;
using Hypersonic.ViewModels;

using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;

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

    private CoverArtAnimator? _coverArtAnimator;
    private readonly IImageService _imageService;

    /// <summary>
    /// True when the player-bar cover and a parallel translation on the
    /// metadata panel have been applied for navigation to NowPlayingPage.
    /// Used to scope the restore on back navigation.
    /// </summary>
    private bool _playerCoverHiddenForNowPlaying;

    /// <summary>
    /// Width of the cover-art slot (Width=48 + Margin.Right=12). When the
    /// container collapses, the metadata column shifts left by this much in
    /// layout — we offset the metadata's composition translation by the same
    /// amount and animate it back to zero so it visually slides into place.
    /// The same value is applied as a left margin on PlayerControlsGrid to
    /// reserve the freed space and keep the playback controls visually anchored.
    /// </summary>
    private const float PlayerCoverSlotWidth = 60f;

    public ShellPage(ShellViewModel viewModel)
    {
        ViewModel = viewModel;
        Player = App.GetService<PlayerViewModel>();
        _imageService = App.GetService<IImageService>();
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
                _coverArtAnimator?.OnIsPlayingChanged();
                PlayPauseIcon.Glyph = Player.IsPlaying ? "\uE769" : "\uE768";
            }
            else if (e.PropertyName == nameof(Player.CoverImage))
            {
                var direction = Player.LastChangeDirection;
                if (_coverArtAnimator is not null)
                {
                    _coverArtAnimator.OnCoverImageChanged(direction);
                }
                else
                {
                    UpdatePlayerCoverArtFallback();
                }
            }
            else if (e.PropertyName == nameof(Player.HasTrack))
            {
                if (!Player.HasTrack)
                {
                    ResetCoverArtHoverOverlay();
                }
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

        Player.NowPlayingMetadataChanged += (_, _) => RenderNowPlaying();

        // Hand cursor on the cover-art tap target so it looks clickable. The
        // track-title is a Hyperlink and gets the cursor + hover color shift
        // for free.
        SetHandCursorOnHover(PlayerCoverArtContainer);

        QueuePopup.Closed += (_, _) =>
        {
            Player.IsQueueOpen = false;
        };

        RenderNowPlaying();
        PlayPauseIcon.Glyph = Player.IsPlaying ? "\uE769" : "\uE768";
        UpdatePlayerCoverArtFallback();
    }

    private Brush SecondaryBrush => SecondaryBrushProvider.Foreground;
    private Brush TertiaryBrush => TertiaryBrushProvider.Foreground;
    private Brush PrimaryBrush => PrimaryBrushProvider.Foreground;

    private void RenderNowPlaying()
    {
        // Resolve through the brush provider so theme changes refresh Hyperlink.Foreground without overriding native hover styling.
        PlayerTrackTitleLink.Foreground = PrimaryBrush;
        RenderNowPlayingArtist();
        RenderNowPlayingAlbum();
    }

    private void RenderNowPlayingArtist()
    {
        NowPlayingArtistText.Blocks.Clear();

        var result = DisplayArtistRenderer.BuildSegments(
            Player.DisplayArtist, Player.Artists, Player.HasStructuredArtists);
        ToolTipService.SetToolTip(NowPlayingArtistText,
            BuildArtistTooltip(result, Player.DisplayArtist, Player.Artists));

        var paragraph = new Paragraph();
        var secondaryBrush = SecondaryBrush;
        var tertiaryBrush = TertiaryBrush;

        foreach (var segment in result.Segments)
        {
            if (segment.Kind == ArtistSegmentKind.Connector)
            {
                paragraph.Inlines.Add(new Run
                {
                    Text = segment.Text,
                    Foreground = tertiaryBrush,
                });
            }
            else
            {
                var artist = segment.Artist!;
                if (!string.IsNullOrEmpty(artist.Id))
                {
                    var link = new Hyperlink
                    {
                        UnderlineStyle = UnderlineStyle.None,
                        Foreground = secondaryBrush,
                    };
                    link.Inlines.Add(new Run { Text = segment.Text });
                    var artistId = artist.Id;
                    link.Click += (s, e) =>
                    {
                        App.GetService<INavigationService>()
                            .NavigateTo(typeof(ArtistDetailViewModel).FullName!, artistId);
                    };
                    paragraph.Inlines.Add(link);
                }
                else
                {
                    paragraph.Inlines.Add(new Run
                    {
                        Text = segment.Text,
                        Foreground = secondaryBrush,
                    });
                }
            }
        }

        NowPlayingArtistText.Blocks.Add(paragraph);
    }

    private void RenderNowPlayingAlbum()
    {
        NowPlayingAlbumText.Blocks.Clear();
        ToolTipService.SetToolTip(NowPlayingAlbumText, Player.AlbumName);

        var paragraph = new Paragraph();
        var tertiaryBrush = TertiaryBrush;

        if (!string.IsNullOrEmpty(Player.AlbumId))
        {
            var link = new Hyperlink
            {
                UnderlineStyle = UnderlineStyle.None,
                Foreground = tertiaryBrush,
            };
            link.Inlines.Add(new Run { Text = Player.AlbumName });
            var albumId = Player.AlbumId;
            link.Click += (s, e) =>
            {
                App.GetService<INavigationService>()
                    .NavigateTo(typeof(AlbumDetailViewModel).FullName!, albumId);
            };
            paragraph.Inlines.Add(link);
        }
        else
        {
            paragraph.Inlines.Add(new Run
            {
                Text = Player.AlbumName,
                Foreground = tertiaryBrush,
            });
        }

        NowPlayingAlbumText.Blocks.Add(paragraph);
    }

    private static string BuildArtistTooltip(
        BuildSegmentsResult result, string? displayArtist, List<SongArtistInfo> artists)
    {
        if (result.UsedStructuredFallback && !string.IsNullOrEmpty(displayArtist))
        {
            return displayArtist;
        }
        if (result.HasUnmatchedArtists)
        {
            return string.Join(", ", artists.Where(a => !string.IsNullOrEmpty(a.Name)).Select(a => a.Name));
        }
        return string.Concat(result.Segments.Select(s => s.Text));
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

        ActualThemeChanged += (_, _) =>
        {
            Player.NotifyThemeChanged();
            RenderNowPlaying();
        };

        // Cover-art animator. Built here (post-Loaded) so element sizes are real.
        // Player bar uses a quick crossfade for track changes; the rounded
        // wrapper Grid scales with the artwork on play/pause so the corner
        // radius stays flush with the image.
        _coverArtAnimator = new CoverArtAnimator(
            primaryWrapper: PlayerCoverArtWrapper,
            primary: PlayerCoverArt,
            outgoingWrapper: PlayerCoverArtOutgoingWrapper,
            outgoing: PlayerCoverArtOutgoing,
            player: Player,
            imageService: _imageService,
            coverArtSize: "300",
            playingScale: 1.0f,
            pausedScale: 0.88f,
            cameraOutScale: 0.88f, // unused for Crossfade
            slideDistance: () => 0.0,
            transition: CoverArtTransition.Crossfade);
        _coverArtAnimator.Initialize();
        _coverArtAnimator.OnCoverImageChanged(TrackChangeDirection.None);

        ElementCompositionPreview.SetIsTranslationEnabled(PlayerTrackInfoPanel, true);

        NavigationFrame.Navigated += NavigationFrame_Navigated;
    }

    private async void UpdatePlayerCoverArtFallback()
    {
        PlayerCoverArt.Source = await CachedImage.LoadBitmapAsync(
            _imageService,
            Player.CoverImage,
            ImageRequestOptions.ForSize("300"));
    }

    private void NavigationFrame_Navigated(object sender, NavigationEventArgs e)
    {
        // Keep NowPlayingPage off the back stack on the way OUT, the same way
        // NowPlayingTapPolicy keeps it off on the way IN. Together: Back never
        // lands on NowPlayingPage — it's an overlay that only lives at the top.
        if (e.SourcePageType != typeof(NowPlayingPage))
        {
            var backStack = NavigationFrame.BackStack
                .Select(entry => new NavEntry(entry.SourcePageType, entry.Parameter))
                .ToList();
            var current = e.SourcePageType is null
                ? (NavEntry?)null
                : new NavEntry(e.SourcePageType, e.Parameter);
            var indicesToRemove = NowPlayingBackStackPolicy.IndicesToRemove(current, backStack);
            for (var i = indicesToRemove.Count - 1; i >= 0; i--)
            {
                var index = indicesToRemove[i];
                if (index >= 0 && index < NavigationFrame.BackStack.Count)
                {
                    NavigationFrame.BackStack.RemoveAt(index);
                }
            }

            // ShellViewModel.OnNavigated runs BEFORE this handler in the
            // multicast Frame.Navigated chain (NavigationService is wired up
            // first in the ShellPage ctor; this handler in OnLoaded). It set
            // IsBackEnabled from the pre-prune stack. Resync to the post-prune
            // truth so the NavigationView's back arrow doesn't sit enabled
            // pointing at nothing.
            if (indicesToRemove.Count > 0)
            {
                ViewModel.IsBackEnabled = NavigationFrame.CanGoBack;
            }
        }

        // Back from NowPlayingPage → restore the player-bar cover and slide
        // metadata back into its native column. Order matters: make the cover
        // visible BEFORE TryStart so the connected animation has a real
        // destination element with non-zero size.
        if (e.SourcePageType != typeof(NowPlayingPage))
        {
            RestorePlayerCoverAfterNavigation();

            var anim = ConnectedAnimationService.GetForCurrentView().GetAnimation("CoverArtBack");
            if (anim is not null)
            {
                if (PlayerCoverArt.Source is not null)
                {
                    anim.TryStart(PlayerCoverArtWrapper);
                }
                else
                {
                    anim.Cancel();
                }
            }
        }
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
            Player.JumpToQueueIndex(item);
        }
    }

    private void PlayerCoverArtContainer_Tapped(object sender, TappedRoutedEventArgs e)
    {
        TryNavigateToNowPlaying();
    }

    private void PlayerTrackTitleLink_Click(Hyperlink sender, HyperlinkClickEventArgs args)
    {
        TryNavigateToNowPlaying();
    }

    private void PlayerCoverArtContainer_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        // 0.25 over a solid black/white tint reads clearly on most artwork
        // without washing out the image. The brush itself flips per theme
        // (see ShellPage.xaml ThemeDictionaries), so light/dark are
        // perceptually balanced rather than the SubtleFill brush's ~6% alpha
        // which all but disappeared in dark mode.
        AnimateCoverArtHoverOverlay(0.25f);
    }

    private void PlayerCoverArtContainer_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        AnimateCoverArtHoverOverlay(0f);
    }

    private void AnimateCoverArtHoverOverlay(float toOpacity)
    {
        var visual = ElementCompositionPreview.GetElementVisual(PlayerCoverArtHoverOverlay);
        var compositor = visual.Compositor;
        var ease = compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f));

        var animation = compositor.CreateScalarKeyFrameAnimation();
        // Pick up from the in-flight value so quick enter/exit doesn't snap.
        animation.InsertExpressionKeyFrame(0f, "this.StartingValue");
        animation.InsertKeyFrame(1f, toOpacity, ease);
        animation.Duration = TimeSpan.FromMilliseconds(120);
        visual.StartAnimation("Opacity", animation);
    }

    private void ResetCoverArtHoverOverlay()
    {
        // Stop first, otherwise an in-flight fade keeps writing past the snap.
        var visual = ElementCompositionPreview.GetElementVisual(PlayerCoverArtHoverOverlay);
        visual.StopAnimation("Opacity");
        visual.Opacity = 0f;
    }

    private static void SetHandCursorOnHover(UIElement element)
    {
        // ProtectedCursor is protected on UIElement; reach it via reflection
        // (standard WinUI 3 workaround for setting cursors on Grid/TextBlock).
        var prop = typeof(UIElement).GetProperty("ProtectedCursor",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (prop == null)
        {
            return;
        }
        var hand = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
        element.PointerEntered += (_, _) => prop.SetValue(element, hand);
    }

    private void TryNavigateToNowPlaying()
    {
        var navigationService = ViewModel.NavigationService;
        var frame = navigationService.Frame;
        if (frame == null)
        {
            return;
        }

        var currentPageType = frame.Content?.GetType();
        var backStackTypes = frame.BackStack
            .Select(entry => entry.SourcePageType)
            .ToList();

        var decision = NowPlayingTapPolicy.Decide(Player.HasTrack, currentPageType, backStackTypes);
        switch (decision.Action)
        {
            case NowPlayingTapAction.None:
                return;

            case NowPlayingTapAction.GoBack:
                // Policy guarantees BackStackIndicesToRemove is empty here, and
                // there's no outgoing connected animation to prepare — we're
                // dismissing NowPlayingPage, not navigating to it.
                navigationService.GoBack();
                return;

            case NowPlayingTapAction.Navigate:
                break;
        }

        for (var i = decision.BackStackIndicesToRemove.Count - 1; i >= 0; i--)
        {
            var index = decision.BackStackIndicesToRemove[i];
            if (index >= 0 && index < frame.BackStack.Count)
            {
                frame.BackStack.RemoveAt(index);
            }
        }

        // Clear the hover tint before the connected-animation snapshot so it
        // isn't baked into the source frame (the overlay lives inside the
        // wrapper that PrepareToAnimate snapshots). Also primes a clean state
        // for when the player-bar cover comes back on the way home — pointer
        // events stop firing once the container collapses, so PointerExited
        // would otherwise never run.
        ResetCoverArtHoverOverlay();

        // Connected animation: morph the player-bar art into the big art on NowPlayingPage.
        // We pass the rounded wrapper (not the inner Image) so the snapshot
        // includes the wrapper's CornerRadius clip. The corners scale
        // proportionally during flight, so the landed radius is larger than
        // the destination's static 8 px — accepted as Option A's tradeoff.
        if (PlayerCoverArt.Source is not null)
        {
            ConnectedAnimationService.GetForCurrentView()
                .PrepareToAnimate("CoverArtForward", PlayerCoverArtWrapper);
        }

        // Drop the player-bar cover; let the metadata slide left to fill the
        // freed space. The two animations run concurrently with the connected
        // animation, telling a single visual story: art lifts up to NowPlaying,
        // metadata fills in.
        HidePlayerCoverForNavigation();

        navigationService.NavigateTo(typeof(NowPlayingViewModel).FullName!);
    }

    private void HidePlayerCoverForNavigation()
    {
        // Snapshot whether we actually hid the cover so the back-navigation
        // restore is symmetric with what we did on the way out.
        if (PlayerCoverArtContainer.Visibility != Visibility.Visible)
        {
            return;
        }

        // Defensive: any caller that didn't already reset the overlay (and
        // therefore wouldn't snapshot it via PrepareToAnimate either) still
        // needs a clean state when the cover comes back, since PointerExited
        // can't fire on a collapsed container.
        ResetCoverArtHoverOverlay();

        PlayerCoverArtContainer.Visibility = Visibility.Collapsed;
        SetLeftMargin(PlayerControlsGrid, PlayerCoverSlotWidth);
        AnimateMetadataSlide(fromX: PlayerCoverSlotWidth, toX: 0f);
        _playerCoverHiddenForNowPlaying = true;
    }

    private void RestorePlayerCoverAfterNavigation()
    {
        if (!_playerCoverHiddenForNowPlaying)
        {
            return;
        }
        _playerCoverHiddenForNowPlaying = false;

        // Make the cover visible first (layout shifts metadata to its native
        // position), then immediately offset metadata by -slotWidth and animate
        // back to zero so it visually slides right to its original spot.
        PlayerCoverArtContainer.Visibility = Visibility.Visible;
        SetLeftMargin(PlayerControlsGrid, 0);
        AnimateMetadataSlide(fromX: -PlayerCoverSlotWidth, toX: 0f);
    }

    private static void SetLeftMargin(FrameworkElement element, double left)
    {
        var m = element.Margin;
        element.Margin = new Thickness(left, m.Top, m.Right, m.Bottom);
    }

    private void AnimateMetadataSlide(float fromX, float toX)
    {
        var visual = ElementCompositionPreview.GetElementVisual(PlayerTrackInfoPanel);
        var compositor = visual.Compositor;

        // Park at the start offset before animating so the first frame doesn't flash.
        visual.Properties.InsertVector3("Translation", new Vector3(fromX, 0f, 0f));

        var animation = compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(0f, fromX);
        animation.InsertKeyFrame(1f, toX,
            compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f)));
        animation.Duration = TimeSpan.FromMilliseconds(280);
        visual.StartAnimation("Translation.X", animation);
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
            // Window may be tearing down by the time this Low-priority callback
            // runs; touching any XAML proxy (including IsLoaded) on a released
            // element throws E_UNEXPECTED, so the IsLoaded check lives inside the try.
            try
            {
                if (!IsLoaded)
                {
                    return;
                }

                var focusState = TitleBarSearchBox.FocusState;
                if (focusState == FocusState.Unfocused)
                {
                    CloseSearchPopup();
                }
            }
            catch (System.Runtime.InteropServices.COMException ex)
                when ((uint)ex.HResult == 0x8000FFFF)
            {
                // Element released between scheduling and callback execution.
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
            try
            {
                if (!IsLoaded)
                {
                    // Don't invoke afterClose during teardown — current callers
                    // (e.g. SearchBox_KeyDown) touch TitleBarSearchBox.Text and would
                    // hit the same released-XAML-proxy crash.
                    _isClosingPopup = false;
                    return;
                }

                // Reset visual before hiding
                var visual = ElementCompositionPreview.GetElementVisual(SearchPopupBorder);
                visual.StopAnimation("Opacity");
                visual.StopAnimation("Translation.Y");
                visual.Opacity = 1f;
                visual.Properties.InsertVector3("Translation", Vector3.Zero);

                SearchResultsPopup.IsOpen = false;
                ViewModel.IsSearchResultsVisible = false;
            }
            catch (System.Runtime.InteropServices.COMException ex)
                when ((uint)ex.HResult == 0x8000FFFF)
            {
                // Page torn down mid-animation; visuals are gone.
                _isClosingPopup = false;
                return;
            }

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
