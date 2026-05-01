using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Helpers;
using Hypersonic.Core.Models;
using Hypersonic.Helpers;
using Hypersonic.Models;
using Hypersonic.ViewModels;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;

namespace Hypersonic.Views;

public sealed partial class NowPlayingPage : Page
{
    public NowPlayingViewModel ViewModel { get; }
    public PlayerViewModel Player { get; }

    private readonly IPlaybackService _playbackService;
    private readonly DispatcherQueue _pageDispatcher;
    private readonly QueueAutoScrollCoordinator _scrollCoordinator;
    private readonly QueueTabSubscriptionGate _queueTabGate;
    private bool _pageActive;
    private bool _initialized;
    private CoverArtAnimator? _coverArtAnimator;

    public NowPlayingPage()
    {
        ViewModel = App.GetService<NowPlayingViewModel>();
        Player = App.GetService<PlayerViewModel>();
        _playbackService = App.GetService<IPlaybackService>();

        // Construct the queue-tab plumbing BEFORE InitializeComponent() so the
        // SelectorBar's XAML-wired SelectionChanged handler (which can fire on
        // initial IsSelected="True" application during component init) never
        // touches null fields. _initialized is the second line of defense:
        // even with the fields populated, we don't want any side effects of a
        // synthetic SelectionChanged-during-init to mutate state.
        _scrollCoordinator = new QueueAutoScrollCoordinator(MarshalAndScroll);
        _queueTabGate = new QueueTabSubscriptionGate(
            attachAll: () =>
            {
                Player.NowPlayingMetadataChanged += OnQueueTabMetadataChanged;
                _playbackService.QueueChanged += OnQueueTabQueueChanged;
            },
            detachAll: () =>
            {
                Player.NowPlayingMetadataChanged -= OnQueueTabMetadataChanged;
                _playbackService.QueueChanged -= OnQueueTabQueueChanged;
            });

        InitializeComponent();

        _pageDispatcher = DispatcherQueue.GetForCurrentThread();
        _pageActive = true;

        Player.NowPlayingMetadataChanged += OnPlayerMetadataChanged;
        Player.PropertyChanged += OnPlayerPropertyChanged;
        ActualThemeChanged += OnActualThemeChanged;
        Loaded += OnPageLoaded;
        RenderNowPlaying();

        // Now that all subscriptions and helpers are wired, allow the
        // SelectionChanged handler to do real work.
        _initialized = true;

        Unloaded += (_, _) =>
        {
            _pageActive = false;
            _queueTabGate.Detach();
            _scrollCoordinator.OnTabSelected(NowPlayingTab.Lyrics, -1);
            Player.NowPlayingMetadataChanged -= OnPlayerMetadataChanged;
            Player.PropertyChanged -= OnPlayerPropertyChanged;
            ActualThemeChanged -= OnActualThemeChanged;
            _coverArtAnimator?.Dispose();
        };
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        if (_coverArtAnimator is not null) return;

        _coverArtAnimator = new CoverArtAnimator(
            primaryWrapper: LargeCoverArtWrapper,
            primary: LargeCoverArt,
            outgoingWrapper: LargeCoverArtOutgoingWrapper,
            outgoing: LargeCoverArtOutgoing,
            player: Player,
            playingScale: 1.0f,
            pausedScale: 0.92f,
            cameraOutScale: 0.72f,
            // Slide distance = wrapper visible width (cameraOutScale=0.72) + gap.
            // The wrappers stay centered relative to each other in flight, so
            // their edge-to-edge gap during phase 2 equals (slideDistance -
            // cameraOutScale * containerWidth). 0.85 here gives a ~13% gap.
            slideDistance: () => LargeCoverArtClip.ActualWidth * 0.85,
            transition: CoverArtTransition.CameraSlide);
        _coverArtAnimator.Initialize();
    }

    private void LargeCoverArtClip_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var w = e.NewSize.Width;
        var h = e.NewSize.Height;
        if (w > 0 && h > 0)
        {
            LargeCoverArtClip.Clip = new Microsoft.UI.Xaml.Media.RectangleGeometry
            {
                Rect = new Windows.Foundation.Rect(0, 0, w, h),
            };
        }
    }

    private void OnPlayerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Player.IsPlaying))
        {
            _pageDispatcher.TryEnqueue(() =>
            {
                if (!_pageActive) return;
                _coverArtAnimator?.OnIsPlayingChanged();
            });
        }
        else if (e.PropertyName == nameof(Player.CoverArtUrl))
        {
            // Capture direction synchronously here. The PropertyChanged event
            // fires inside PlayerViewModel.OnTrackChanged, which resets
            // LastChangeDirection back to None at its end. Reading it inside
            // the queued callback below would race and lose the direction.
            var direction = Player.LastChangeDirection;
            _pageDispatcher.TryEnqueue(() =>
            {
                if (!_pageActive) return;
                if (_coverArtAnimator is not null)
                {
                    _coverArtAnimator.OnCoverArtUrlChanged(direction);
                }
                else
                {
                    UpdateCoverArtFallback();
                }
            });
        }
    }


    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        var anim = ConnectedAnimationService.GetForCurrentView().GetAnimation("CoverArtForward");
        if (anim is null) return;

        // Pass the rounded wrapper (not the inner Image) so the snapshot's
        // shape matches the player-bar wrapper's rounded corners. See the
        // matching comment in ShellPage.TryNavigateToNowPlaying.
        if (LargeCoverArt.Source is null)
        {
            // Wait until the cover art is set before starting (renderer also runs
            // RenderNowPlaying on first paint via the metadata path).
            void Started(object? s, RoutedEventArgs args)
            {
                LargeCoverArt.ImageOpened -= Started;
                anim.TryStart(LargeCoverArtWrapper);
            }
            LargeCoverArt.ImageOpened += Started;

            // Safety: if no image loads in a short window, drop the animation
            // so it doesn't sit forever waiting.
            var dispatcher = _pageDispatcher;
            _ = Task.Delay(800).ContinueWith(_ =>
            {
                dispatcher.TryEnqueue(() =>
                {
                    LargeCoverArt.ImageOpened -= Started;
                    anim?.Cancel();
                });
            });
        }
        else
        {
            anim.TryStart(LargeCoverArtWrapper);
        }
    }

    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        base.OnNavigatingFrom(e);

        if (LargeCoverArt.Source is not null)
        {
            ConnectedAnimationService.GetForCurrentView()
                .PrepareToAnimate("CoverArtBack", LargeCoverArtWrapper);
        }
    }

    private Brush SecondaryBrush => SecondaryBrushProvider.Foreground;
    private Brush TertiaryBrush => TertiaryBrushProvider.Foreground;

    private void OnPlayerMetadataChanged(object? sender, EventArgs e)
    {
        _pageDispatcher.TryEnqueue(() =>
        {
            if (!_pageActive) return;
            RenderNowPlaying();
        });
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        RenderNowPlaying();
    }

    private void OnQueueTabMetadataChanged(object? sender, EventArgs e)
    {
        _scrollCoordinator.OnQueueRefreshed(Player.EffectiveQueueIndex);
    }

    private void OnQueueTabQueueChanged(object? sender, EventArgs e)
    {
        _scrollCoordinator.OnQueueRefreshed(Player.EffectiveQueueIndex);
    }

    private void RenderNowPlaying()
    {
        RenderArtist();
        RenderAlbum();
        // Cover art swap is driven by Player.PropertyChanged (CoverArtUrl), so
        // it's not invoked here — that channel uses the animator and avoids a
        // double source-set when both events fire for the same change.
        if (_coverArtAnimator is null)
        {
            UpdateCoverArtFallback();
        }
    }

    private void UpdateCoverArtFallback()
    {
        LargeCoverArt.Source = !string.IsNullOrEmpty(Player.CoverArtUrl)
            ? new BitmapImage(new Uri(Player.CoverArtUrl))
            : null;
    }

    private void RenderArtist()
    {
        MetaArtistText.Blocks.Clear();
        var result = DisplayArtistRenderer.BuildSegments(
            Player.DisplayArtist, Player.Artists, Player.HasStructuredArtists);

        var paragraph = new Paragraph();
        var secondaryBrush = SecondaryBrush;
        var tertiaryBrush = TertiaryBrush;

        foreach (var segment in result.Segments)
        {
            if (segment.Kind == ArtistSegmentKind.Connector)
            {
                paragraph.Inlines.Add(new Run { Text = segment.Text, Foreground = tertiaryBrush });
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
                    link.Click += (_, _) =>
                    {
                        App.GetService<INavigationService>()
                            .NavigateTo(typeof(ArtistDetailViewModel).FullName!, artistId);
                    };
                    paragraph.Inlines.Add(link);
                }
                else
                {
                    paragraph.Inlines.Add(new Run { Text = segment.Text, Foreground = secondaryBrush });
                }
            }
        }

        MetaArtistText.Blocks.Add(paragraph);
    }

    private void RenderAlbum()
    {
        MetaAlbumText.Blocks.Clear();
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
            link.Click += (_, _) =>
            {
                App.GetService<INavigationService>()
                    .NavigateTo(typeof(AlbumDetailViewModel).FullName!, albumId);
            };
            paragraph.Inlines.Add(link);
        }
        else
        {
            paragraph.Inlines.Add(new Run { Text = Player.AlbumName, Foreground = tertiaryBrush });
        }

        MetaAlbumText.Blocks.Add(paragraph);
    }

    private void TabSelectorBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        // Guard against the handler firing during InitializeComponent() (when
        // SelectorBarItem.IsSelected="True" is being applied to LyricsTab) and
        // before the page constructor finishes wiring _pageDispatcher,
        // subscriptions, etc. The constructor sets _initialized = true at the
        // very end. Pre-init firings would also be no-ops in practice (Lyrics
        // is the default selection so the branch matches the initial state),
        // but skipping them outright is cheaper and removes the dependency on
        // SelectorBar's exact initial-event behavior.
        if (!_initialized) return;

        var isQueue = sender.SelectedItem == QueueTab;
        ViewModel.SelectedTab = isQueue ? NowPlayingTab.Queue : NowPlayingTab.Lyrics;
        LyricsSurface.Visibility = isQueue ? Visibility.Collapsed : Visibility.Visible;
        QueueSurface.Visibility = isQueue ? Visibility.Visible : Visibility.Collapsed;

        if (isQueue)
        {
            _queueTabGate.Attach();
            _scrollCoordinator.OnTabSelected(NowPlayingTab.Queue, Player.EffectiveQueueIndex);
        }
        else
        {
            _queueTabGate.Detach();
            _scrollCoordinator.OnTabSelected(NowPlayingTab.Lyrics, -1);
        }
    }

    private void MarshalAndScroll(int effectiveIndex)
    {
        _pageDispatcher.TryEnqueue(() =>
        {
            if (!_pageActive) return;
            ScrollQueueToIndex(effectiveIndex);
        });
    }

    private void ScrollQueueToIndex(int effectiveIndex)
    {
        var target = QueueScrollResolver.ResolveScrollTarget(Player.QueueItems, effectiveIndex);
        if (target is null)
        {
            return;
        }
        // Default alignment: scroll only the minimum amount needed. If the
        // target row is already visible, this is a no-op — preserving the
        // user's scroll position when navigating prev/next within the
        // currently-visible window of the queue.
        QueueListView.ScrollIntoView(target, ScrollIntoViewAlignment.Default);
    }

    private void QueueList_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        var element = e.OriginalSource as FrameworkElement;
        while (element != null && element.DataContext is not QueueItem)
        {
            element = VisualTreeHelper.GetParent(element) as FrameworkElement;
        }

        if (element?.DataContext is QueueItem item)
        {
            Player.JumpToQueueIndex(item);
        }
    }
}
