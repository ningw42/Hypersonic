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
using Microsoft.UI.Xaml.Hosting;
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

    // Single source of truth for the NowPlaying paused/playing scales. Used
    // both by CoverArtAnimator (for the breathing pulse) and by the
    // connected-animation pin/restore helpers — keep these in lockstep.
    private const float LargeCoverPlayingScale = 1.0f;
    private const float LargeCoverPausedScale = 0.92f;

    // Set true only between Prepare…() and Restore…() — guards layout
    // restore against a spurious second Completed callback.
    private bool _connectedAnimationDestinationPinned;

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
        EnsureCoverArtAnimator();
    }

    private void EnsureCoverArtAnimator()
    {
        if (_coverArtAnimator is not null) return;

        _coverArtAnimator = new CoverArtAnimator(
            primaryWrapper: LargeCoverArtWrapper,
            primary: LargeCoverArt,
            outgoingWrapper: LargeCoverArtOutgoingWrapper,
            outgoing: LargeCoverArtOutgoing,
            player: Player,
            playingScale: LargeCoverPlayingScale,
            pausedScale: LargeCoverPausedScale,
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

    // Dynamically caps the artwork's MaxHeight so the artwork+metadata block
    // shrinks vertically with the window. The parent StackPanel passes
    // Infinity along its stack axis, so without this the SquarePanel ignores
    // available height. We read the root Grid's height (not the left
    // StackPanel's, which has VerticalAlignment=Center and so reports its own
    // desired height — circular).
    private const double MaxArtworkSide = 600.0;
    private const double LeftStackSpacing = 16.0;
    private const double RootVerticalPadding = 32.0;

    private void RootContentGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        => UpdateArtworkMaxHeight();

    private void MetadataStack_SizeChanged(object sender, SizeChangedEventArgs e)
        => UpdateArtworkMaxHeight();

    private void UpdateArtworkMaxHeight()
    {
        var cellHeight = RootContentGrid.ActualHeight - RootVerticalPadding;
        if (cellHeight <= 0) return;

        var available = cellHeight - MetadataStack.ActualHeight - LeftStackSpacing;
        var target = Math.Max(0.0, Math.Min(MaxArtworkSide, available));
        if (Math.Abs(LargeCoverArtSquarePanel.MaxHeight - target) > 0.5)
        {
            LargeCoverArtSquarePanel.MaxHeight = target;
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

        // Build/init the animator BEFORE the connected animation starts so
        // it's ready to take over the wrapper's resting Scale once the
        // flight ends (Restore…() snaps Scale into the value the animator
        // would otherwise have set on its own). Also wires the wrapper's
        // CenterPoint via SizeChanged.
        EnsureCoverArtAnimator();

        var anim = ConnectedAnimationService.GetForCurrentView().GetAnimation("CoverArtForward");
        if (anim is null) return;

        // Two gates must close before TryStart:
        //   (a) layout has run — SquarePanel.ActualWidth > 0 — so
        //       PrepareConnectedAnimationDestination can pin the wrapper to
        //       a visually-correct size. OnNavigatedTo runs BEFORE the
        //       page's first layout pass, so this is 0 here on every
        //       navigation (verified by logging).
        //   (b) image is set — LargeCoverArt.Source != null — so the
        //       destination has pixels to land into.
        // Both must close before we touch the animation; otherwise the pin
        // sees a 0 width and silently skips, reproducing the original
        // "land big, snap small" behavior.
        var started = false;

        void TryStartIfReady()
        {
            if (started) return;
            if (LargeCoverArtSquarePanel.ActualWidth <= 0) return;
            if (LargeCoverArt.Source is null) return;
            started = true;
            LargeCoverArtSquarePanel.SizeChanged -= OnSquarePanelSized;
            LargeCoverArt.ImageOpened -= OnImageOpened;
            StartCoverArtForwardAnimation(anim);
        }

        void OnSquarePanelSized(object sender, SizeChangedEventArgs args) => TryStartIfReady();
        void OnImageOpened(object? sender, RoutedEventArgs args) => TryStartIfReady();

        if (LargeCoverArtSquarePanel.ActualWidth <= 0)
        {
            LargeCoverArtSquarePanel.SizeChanged += OnSquarePanelSized;
        }
        if (LargeCoverArt.Source is null)
        {
            LargeCoverArt.ImageOpened += OnImageOpened;
        }
        // Both gates may already be closed on a re-navigation where layout
        // happened to be cached and the image is already set; cover that
        // path without waiting for either event to fire.
        TryStartIfReady();

        // Safety: if either gate never closes (e.g. the image fails to
        // load), drop the animation and the handlers so we don't sit on
        // stale subscriptions forever.
        //
        // Residual risk if the user navigates away within this 800ms
        // window: the closure keeps the page alive until the timeout
        // fires (bounded, ~800ms — not a leak). The subscriptions sit on
        // detached elements that no longer raise events, so the handlers
        // never fire. The captured `anim` is either already Completed
        // (Cancel is a no-op) or has been superseded by the next
        // navigation's PrepareToAnimate (which creates a fresh instance —
        // Cancel here can't touch it). Untidy, not a correctness bug;
        // tightening would mean hooking Unloaded to detach+cancel
        // synchronously, which is more code than the symptom warrants.
        var dispatcher = _pageDispatcher;
        _ = Task.Delay(800).ContinueWith(_ =>
        {
            dispatcher.TryEnqueue(() =>
            {
                if (started) return;
                LargeCoverArtSquarePanel.SizeChanged -= OnSquarePanelSized;
                LargeCoverArt.ImageOpened -= OnImageOpened;
                anim?.Cancel();
            });
        });
    }

    private void PrepareConnectedAnimationDestination()
    {
        // SquarePanel is the actual visible artwork bounds (capped by
        // MaxWidth=600); the parent clip can be wider than that on wide
        // windows, which would over-pin the destination.
        var targetScale = Player.IsPlaying ? LargeCoverPlayingScale : LargeCoverPausedScale;
        var pinnedSide = LargeCoverArtSquarePanel.ActualWidth * targetScale;
        if (pinnedSide <= 0) return; // Caller's gate should have prevented this.

        LargeCoverArtWrapper.Width = pinnedSide;
        LargeCoverArtWrapper.Height = pinnedSide;
        // A smaller explicit size inside LargeCoverArtContainer needs explicit
        // centering — Stretch is the wrong arrange when Width/Height are set.
        LargeCoverArtWrapper.HorizontalAlignment = HorizontalAlignment.Center;
        LargeCoverArtWrapper.VerticalAlignment = VerticalAlignment.Center;
        // Composition Scale must be 1.0 for the duration of the flight: layout
        // already encodes the visual size, so any extra scale would double-apply.
        var visual = ElementCompositionPreview.GetElementVisual(LargeCoverArtWrapper);
        visual.Scale = new System.Numerics.Vector3(1f, 1f, 1f);

        // TryStart reads the layout rect at call time. Width/Height changes
        // invalidate layout but don't run measure/arrange synchronously.
        LargeCoverArtWrapper.UpdateLayout();

        _connectedAnimationDestinationPinned = true;
    }

    private void RestoreAfterConnectedAnimation()
    {
        // Layout restore is gated on whether we actually pinned — idempotent,
        // and skips work when Prepare…() bailed on a zero-width clip.
        if (_connectedAnimationDestinationPinned)
        {
            _connectedAnimationDestinationPinned = false;
            LargeCoverArtWrapper.Width = double.NaN;
            LargeCoverArtWrapper.Height = double.NaN;
            LargeCoverArtWrapper.HorizontalAlignment = HorizontalAlignment.Stretch;
            LargeCoverArtWrapper.VerticalAlignment = VerticalAlignment.Stretch;
        }

        // Scale restore ALWAYS runs so the play/pause pulse starts from the
        // right value. Snap directly; do NOT call OnIsPlayingChanged(),
        // which animates 1.0 → 0.92 and reproduces the snap-as-shrink the
        // pin was supposed to eliminate.
        var targetScale = Player.IsPlaying ? LargeCoverPlayingScale : LargeCoverPausedScale;
        ElementCompositionPreview.GetElementVisual(LargeCoverArtWrapper).Scale =
            new System.Numerics.Vector3(targetScale, targetScale, 1f);
    }

    private void StartCoverArtForwardAnimation(ConnectedAnimation anim)
    {
        PrepareConnectedAnimationDestination();

        // Named local so we can unsubscribe — keeps a stale subscription
        // from outliving its purpose if TryStart returned false.
        void Completed(ConnectedAnimation s, object? a)
        {
            s.Completed -= Completed;
            RestoreAfterConnectedAnimation();
        }
        anim.Completed += Completed;

        if (!anim.TryStart(LargeCoverArtWrapper))
        {
            anim.Completed -= Completed;
            RestoreAfterConnectedAnimation();
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
    private Brush PrimaryBrush => PrimaryBrushProvider.Foreground;

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
        // Resolve through the brush provider so theme changes refresh Hyperlink.Foreground without overriding native hover styling.
        MetaTrackTitleLink.Foreground = PrimaryBrush;
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

    private void MetaTrackTitleLink_Click(Hyperlink sender, HyperlinkClickEventArgs args)
    {
        // Page-local intent: tapping the title here means "close me." We skip
        // NowPlayingTapPolicy because the page already knows it's the one
        // being dismissed — consulting the shell-side policy would just
        // round-trip back to GoBack.
        App.GetService<INavigationService>().GoBack();
    }
}
