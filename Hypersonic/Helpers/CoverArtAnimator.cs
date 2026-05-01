using System.Numerics;

using Hypersonic.Core.Models;
using Hypersonic.ViewModels;

using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Hypersonic.Helpers;

/// <summary>
/// Strategy for the cover-art track-change transition.
/// </summary>
internal enum CoverArtTransition
{
    /// <summary>Quick cross-fade. Used by the small player bar.</summary>
    Crossfade,

    /// <summary>
    /// Three-phase camera metaphor: zoom out the current artwork, slide both
    /// horizontally, zoom back in on the new artwork. Used by the large
    /// now-playing surface.
    /// </summary>
    CameraSlide,
}

/// <summary>
/// Drives the breathing (pulse) and track-change animations for a cover-art
/// surface. One instance per surface.
///
/// Sizing model: the *wrapper Grids* are the scale targets. They carry the
/// <c>CornerRadius</c>, so the rounded boundary scales with the artwork — no
/// gap appears between the image and its frame. The placeholder Border below
/// stays at full size and shows through when there is no track.
///
/// Owner page is responsible for:
/// - Calling <see cref="Initialize"/> once after Loaded.
/// - Forwarding <c>PlayerViewModel</c> property changes:
///   <see cref="OnIsPlayingChanged"/> for IsPlaying,
///   <see cref="OnCoverArtUrlChanged"/> for CoverArtUrl.
/// - Calling <see cref="Dispose"/> on Unloaded.
/// </summary>
internal sealed class CoverArtAnimator
{
    private readonly Grid _primaryWrapper;
    private readonly Image _primary;
    private readonly Grid _outgoingWrapper;
    private readonly Image _outgoing;
    private readonly PlayerViewModel _player;
    private readonly float _playingScale;
    private readonly float _pausedScale;
    private readonly float _cameraOutScale;
    private readonly Func<double> _slideDistance;
    private readonly CoverArtTransition _transition;

    private Visual? _primaryVisual;
    private Visual? _outgoingVisual;
    private Compositor? _compositor;

    private CompositionScopedBatch? _phase1Batch;
    private CompositionScopedBatch? _phase2Batch;
    private CompositionScopedBatch? _phase3Batch;
    private BitmapImage? _pendingIncomingSource;
    /// <summary>
    /// True between <see cref="RunCameraSlide"/> and the start of phase 2,
    /// when <see cref="_pendingIncomingSource"/> is the source that should
    /// land on the primary if the transition is snapped early. Distinct
    /// from <c>_pendingIncomingSource != null</c> because the pending source
    /// can legitimately be null (next track has no artwork).
    /// </summary>
    private bool _hasPendingIncoming;
    private TrackChangeDirection _pendingDirection;
    private bool _transitionInFlight;
    private bool _initialized;

    public CoverArtAnimator(
        Grid primaryWrapper,
        Image primary,
        Grid outgoingWrapper,
        Image outgoing,
        PlayerViewModel player,
        float playingScale,
        float pausedScale,
        float cameraOutScale,
        Func<double> slideDistance,
        CoverArtTransition transition)
    {
        _primaryWrapper = primaryWrapper;
        _primary = primary;
        _outgoingWrapper = outgoingWrapper;
        _outgoing = outgoing;
        _player = player;
        _playingScale = playingScale;
        _pausedScale = pausedScale;
        _cameraOutScale = cameraOutScale;
        _slideDistance = slideDistance;
        _transition = transition;
    }

    public void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        ElementCompositionPreview.SetIsTranslationEnabled(_primaryWrapper, true);
        ElementCompositionPreview.SetIsTranslationEnabled(_outgoingWrapper, true);

        _primaryVisual = ElementCompositionPreview.GetElementVisual(_primaryWrapper);
        _outgoingVisual = ElementCompositionPreview.GetElementVisual(_outgoingWrapper);
        _compositor = _primaryVisual.Compositor;

        UpdateCenterPoint(_primaryWrapper, _primaryVisual);
        UpdateCenterPoint(_outgoingWrapper, _outgoingVisual);
        _primaryWrapper.SizeChanged += (_, _) => UpdateCenterPoint(_primaryWrapper, _primaryVisual);
        _outgoingWrapper.SizeChanged += (_, _) => UpdateCenterPoint(_outgoingWrapper, _outgoingVisual);

        var initialScale = HasTrack() && _player.IsPlaying ? _playingScale : _pausedScale;
        _primaryVisual.Scale = new Vector3(initialScale, initialScale, 1f);
        _outgoingVisual.Scale = new Vector3(_pausedScale, _pausedScale, 1f);
        _outgoingVisual.Opacity = 0f;
    }

    public void OnIsPlayingChanged()
    {
        if (!_initialized || _primaryVisual is null || _compositor is null) return;
        if (_transitionInFlight) return; // Transition will re-apply on completion.

        var target = HasTrack() && _player.IsPlaying ? _playingScale : _pausedScale;
        AnimateScale(_primaryVisual, target);
    }

    public void OnCoverArtUrlChanged(TrackChangeDirection direction)
    {
        var newSource = BuildSource(_player.CoverArtUrl);

        if (!_initialized || _primaryVisual is null || _outgoingVisual is null || _compositor is null)
        {
            _primary.Source = newSource;
            return;
        }

        // First paint or no meaningful direction (e.g. external/auto restore): just swap.
        if (_primary.Source is null || direction == TrackChangeDirection.None)
        {
            _primary.Source = newSource;
            var target = HasTrack() && _player.IsPlaying ? _playingScale : _pausedScale;
            _primaryVisual.Scale = new Vector3(target, target, 1f);
            return;
        }

        if (_transition == CoverArtTransition.Crossfade)
        {
            RunCrossfade(newSource);
        }
        else
        {
            RunCameraSlide(newSource, direction);
        }
    }

    public void Dispose()
    {
        SnapTransitionToEnd();
    }

    // ── Crossfade (player bar) ────────────────────────────────────────────────

    private void RunCrossfade(BitmapImage? newSource)
    {
        if (_compositor is null || _primaryVisual is null || _outgoingVisual is null) return;

        SnapTransitionToEnd();
        _transitionInFlight = true;

        // Park the current image in the outgoing wrapper at the same scale.
        _outgoing.Source = _primary.Source;
        _outgoingVisual.Scale = _primaryVisual.Scale;
        _outgoingVisual.Properties.InsertVector3("Translation", Vector3.Zero);
        _outgoingVisual.Opacity = 1f;

        // New image enters at the resting scale (don't change size during a crossfade
        // — that would feel like a pulse, not a swap).
        _primary.Source = newSource;
        _primaryVisual.Opacity = 0f;

        var duration = TimeSpan.FromMilliseconds(220);
        var ease = _compositor.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0f), new Vector2(0.2f, 1f));

        var batch = _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        _phase1Batch = batch;

        var inOpacity = _compositor.CreateScalarKeyFrameAnimation();
        inOpacity.InsertKeyFrame(0f, 0f);
        inOpacity.InsertKeyFrame(1f, 1f, ease);
        inOpacity.Duration = duration;
        _primaryVisual.StartAnimation("Opacity", inOpacity);

        var outOpacity = _compositor.CreateScalarKeyFrameAnimation();
        outOpacity.InsertKeyFrame(0f, 1f);
        outOpacity.InsertKeyFrame(1f, 0f, ease);
        outOpacity.Duration = duration;
        _outgoingVisual.StartAnimation("Opacity", outOpacity);

        batch.End();
        batch.Completed += (_, _) =>
        {
            _primary.DispatcherQueue.TryEnqueue(() =>
            {
                if (!ReferenceEquals(_phase1Batch, batch)) return;
                _phase1Batch = null;
                _transitionInFlight = false;
                FinalizeCrossfade();
            });
        };
    }

    private void FinalizeCrossfade()
    {
        if (_outgoingVisual is null || _primaryVisual is null) return;

        _outgoing.Source = null;
        _outgoingVisual.Opacity = 0f;
        _outgoingVisual.Scale = new Vector3(_pausedScale, _pausedScale, 1f);
        _primaryVisual.Opacity = 1f;

        // Re-sync to live IsPlaying — the user may have toggled mid-fade.
        var target = HasTrack() && _player.IsPlaying ? _playingScale : _pausedScale;
        _primaryVisual.Scale = new Vector3(target, target, 1f);
    }

    // ── Camera slide (now playing) ────────────────────────────────────────────

    private void RunCameraSlide(BitmapImage? newSource, TrackChangeDirection direction)
    {
        if (_compositor is null || _primaryVisual is null || _outgoingVisual is null) return;

        SnapTransitionToEnd();
        _transitionInFlight = true;
        _pendingIncomingSource = newSource;
        _hasPendingIncoming = true;
        _pendingDirection = direction;

        // Phase 1: zoom OUT the current artwork. Outgoing/incoming are NOT yet visible
        // — only the current primary scales down. (No peek of next/prev art.)
        _outgoing.Source = null;
        _outgoingVisual.Opacity = 0f;
        _outgoingVisual.Properties.InsertVector3("Translation", Vector3.Zero);

        var duration = TimeSpan.FromMilliseconds(200);
        var ease = _compositor.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0f), new Vector2(0.6f, 1f));

        var batch = _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        _phase1Batch = batch;

        var scale = _compositor.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(0f, _primaryVisual.Scale);
        scale.InsertKeyFrame(1f, new Vector3(_cameraOutScale, _cameraOutScale, 1f), ease);
        scale.Duration = duration;
        _primaryVisual.StartAnimation("Scale", scale);

        batch.End();
        batch.Completed += (_, _) =>
        {
            _primary.DispatcherQueue.TryEnqueue(() =>
            {
                if (!ReferenceEquals(_phase1Batch, batch)) return;
                _phase1Batch = null;
                StartCameraSlidePhase2();
            });
        };
    }

    private void StartCameraSlidePhase2()
    {
        if (_compositor is null || _primaryVisual is null || _outgoingVisual is null) return;

        // Move the now-shrunken current art into the OUTGOING wrapper, free the
        // PRIMARY wrapper for the incoming art entering from the side.
        _outgoing.Source = _primary.Source;
        _outgoingVisual.Scale = new Vector3(_cameraOutScale, _cameraOutScale, 1f);
        _outgoingVisual.Opacity = 1f;
        _outgoingVisual.Properties.InsertVector3("Translation", Vector3.Zero);

        _primary.Source = _pendingIncomingSource;
        _pendingIncomingSource = null;
        _hasPendingIncoming = false;
        _primaryVisual.Scale = new Vector3(_cameraOutScale, _cameraOutScale, 1f);
        _primaryVisual.Opacity = 1f;

        var distance = (float)_slideDistance();
        // Next: outgoing slides LEFT, incoming enters from RIGHT.
        // Previous: mirrored.
        // Jump: fall back to a still cross-fade (no slide).
        if (_pendingDirection == TrackChangeDirection.Jump)
        {
            _primaryVisual.Properties.InsertVector3("Translation", Vector3.Zero);
            distance = 0f;
        }
        else
        {
            var sign = _pendingDirection == TrackChangeDirection.Next ? +1f : -1f;
            _primaryVisual.Properties.InsertVector3("Translation", new Vector3(sign * distance, 0f, 0f));
        }

        var duration = TimeSpan.FromMilliseconds(280);
        var ease = _compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0f), new Vector2(0.2f, 1f));

        var batch = _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        _phase2Batch = batch;

        if (distance > 0f)
        {
            var sign = _pendingDirection == TrackChangeDirection.Next ? -1f : +1f;
            var outTranslate = _compositor.CreateScalarKeyFrameAnimation();
            outTranslate.InsertKeyFrame(0f, 0f);
            outTranslate.InsertKeyFrame(1f, sign * distance, ease);
            outTranslate.Duration = duration;
            _outgoingVisual.StartAnimation("Translation.X", outTranslate);

            var inTranslate = _compositor.CreateScalarKeyFrameAnimation();
            inTranslate.InsertKeyFrame(0f, -sign * distance);
            inTranslate.InsertKeyFrame(1f, 0f, ease);
            inTranslate.Duration = duration;
            _primaryVisual.StartAnimation("Translation.X", inTranslate);
        }
        else
        {
            // Jump: fade between stills.
            var fadeOut = _compositor.CreateScalarKeyFrameAnimation();
            fadeOut.InsertKeyFrame(0f, 1f);
            fadeOut.InsertKeyFrame(1f, 0f, ease);
            fadeOut.Duration = duration;
            _outgoingVisual.StartAnimation("Opacity", fadeOut);

            _primaryVisual.Opacity = 1f;
        }

        batch.End();
        batch.Completed += (_, _) =>
        {
            _primary.DispatcherQueue.TryEnqueue(() =>
            {
                if (!ReferenceEquals(_phase2Batch, batch)) return;
                _phase2Batch = null;
                StartCameraSlidePhase3();
            });
        };
    }

    private void StartCameraSlidePhase3()
    {
        if (_compositor is null || _primaryVisual is null || _outgoingVisual is null) return;

        // Outgoing departs the stage entirely.
        _outgoing.Source = null;
        _outgoingVisual.Opacity = 0f;
        _outgoingVisual.Properties.InsertVector3("Translation", Vector3.Zero);
        _outgoingVisual.Scale = new Vector3(_pausedScale, _pausedScale, 1f);

        // Reset incoming translation just in case.
        _primaryVisual.Properties.InsertVector3("Translation", Vector3.Zero);

        var target = HasTrack() && _player.IsPlaying ? _playingScale : _pausedScale;
        var duration = TimeSpan.FromMilliseconds(220);
        var ease = _compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f));

        var batch = _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        _phase3Batch = batch;

        var scale = _compositor.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(0f, _primaryVisual.Scale);
        scale.InsertKeyFrame(1f, new Vector3(target, target, 1f), ease);
        scale.Duration = duration;
        _primaryVisual.StartAnimation("Scale", scale);

        batch.End();
        batch.Completed += (_, _) =>
        {
            _primary.DispatcherQueue.TryEnqueue(() =>
            {
                if (!ReferenceEquals(_phase3Batch, batch)) return;
                _phase3Batch = null;
                _transitionInFlight = false;
                _pendingIncomingSource = null;
                _hasPendingIncoming = false;
            });
        };
    }

    // ── Shared ────────────────────────────────────────────────────────────────

    private void SnapTransitionToEnd()
    {
        if (!_transitionInFlight) return;
        if (_primaryVisual is null || _outgoingVisual is null) return;

        _primaryVisual.StopAnimation("Opacity");
        _primaryVisual.StopAnimation("Translation.X");
        _primaryVisual.StopAnimation("Scale");
        _outgoingVisual.StopAnimation("Opacity");
        _outgoingVisual.StopAnimation("Translation.X");
        _outgoingVisual.StopAnimation("Scale");

        _phase1Batch = null;
        _phase2Batch = null;
        _phase3Batch = null;

        if (_hasPendingIncoming)
        {
            // Apply even if null — the destination track may genuinely have
            // no artwork, and leaving the previous track's source in _primary
            // would diverge from Player.CoverArtUrl.
            _primary.Source = _pendingIncomingSource;
            _pendingIncomingSource = null;
            _hasPendingIncoming = false;
        }

        _outgoing.Source = null;
        _outgoingVisual.Opacity = 0f;
        _outgoingVisual.Properties.InsertVector3("Translation", Vector3.Zero);
        _outgoingVisual.Scale = new Vector3(_pausedScale, _pausedScale, 1f);

        _primaryVisual.Opacity = 1f;
        _primaryVisual.Properties.InsertVector3("Translation", Vector3.Zero);
        var target = HasTrack() && _player.IsPlaying ? _playingScale : _pausedScale;
        _primaryVisual.Scale = new Vector3(target, target, 1f);

        _transitionInFlight = false;
    }

    private void AnimateScale(Visual visual, float target)
    {
        if (_compositor is null) return;

        var growing = target > visual.Scale.X;
        var duration = TimeSpan.FromMilliseconds(growing ? 250 : 200);
        var ease = _compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f));

        var animation = _compositor.CreateVector3KeyFrameAnimation();
        animation.InsertKeyFrame(0f, visual.Scale);
        animation.InsertKeyFrame(1f, new Vector3(target, target, 1f), ease);
        animation.Duration = duration;
        visual.StartAnimation("Scale", animation);
    }

    private bool HasTrack() => !string.IsNullOrEmpty(_player.CoverArtUrl);

    private static void UpdateCenterPoint(FrameworkElement element, Visual? visual)
    {
        if (visual is null) return;
        var w = element.ActualWidth > 0 ? element.ActualWidth : element.Width;
        var h = element.ActualHeight > 0 ? element.ActualHeight : element.Height;
        if (w > 0 && h > 0)
        {
            visual.CenterPoint = new Vector3((float)(w / 2), (float)(h / 2), 0f);
        }
    }

    private static BitmapImage? BuildSource(string? url)
    {
        return string.IsNullOrEmpty(url) ? null : new BitmapImage(new Uri(url));
    }
}
