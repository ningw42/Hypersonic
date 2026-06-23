using System.Threading;

using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Core.Services;

using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using Windows.UI;

namespace Hypersonic.Controls.Lyrics;

/// <summary>
/// The Phase 2 public surface: a Win2D word-synced lyrics renderer. <b>Pure presentation</b> — it takes
/// a <see cref="LyricsDocument"/> plus position / play-state / duration / colours from outside and owns
/// no service, ViewModel, or <c>MediaPlayer</c> reach-in. It hosts a single
/// <see cref="CanvasAnimatedControl"/> and drives the clock → synchronizer → choreographer → renderer
/// pipeline each frame. A new <see cref="Lyrics"/> rebuilds the model; a size change re-measures.
/// </summary>
/// <remarks>
/// The canvas runs its game loop on a dedicated render thread, so dependency-property changes (UI thread)
/// are mirrored into plain fields under a lock and consumed in <c>OnDraw</c> (render thread). The clock,
/// synchronizer, choreographer and visual model all live on the render thread.
/// </remarks>
public sealed partial class LyricsCanvasControl : UserControl
{
    private const double HorizontalMargin = 28;

    private readonly object _gate = new();
    // Serialises the render-thread draw against UI-thread teardown so a frame never touches a disposed
    // CanvasTextLayout / CanvasTextFormat (an in-flight Draw can outlive Unloaded — RemoveFromVisualTree
    // does not join the game-loop thread). Separate from _gate (the cheap UI→render mirror lock) so a
    // frame-long hold never stalls the frequent DP-change handlers.
    private readonly LyricsRenderGate _renderGate = new();
    private readonly PlaybackClock _clock;
    private readonly LyricsSynchronizer _synchronizer = new();
    private readonly LyricsChoreographer _choreographer = new();
    private readonly LyricsSceneRenderer _renderer = new();

    // --- render-thread state ---
    private LyricsTimingModel _timingModel = LyricsTimingModel.Empty;
    private LyricsVisualModel? _visualModel;
    private CanvasTextFormat? _format;
    private double _lastWidth = -1;
    private bool _visualDirty = true;
    private double _frameNowMs;

    // --- pause controller: stop the CanvasAnimatedControl game loop instead of redrawing 60×/s when the
    // surface is hidden (off-tab / minimised / no lyrics) or idle (paused playback + scene settled). The
    // UI thread drives the "visible/active" gate directly; the render thread requests an idle pause once
    // the non-playing scene is fully at rest, guarded by a wake generation so a request that races a fresh
    // UI-thread wake is ignored. See LyricsRenderActivity. ---
    private readonly DispatcherQueue _dispatcher;
    private bool _isRenderActive = true;     // UI thread: mirrors IsRenderActive (tab + window visibility, set by the driver)
    private long _wakeGeneration;            // bumped on every UI-thread wake; the render thread snapshots it to guard its pause request
    private long _renderSeenGeneration;      // render thread only: detects a wake since the last frame to re-arm the request
    private bool _idlePauseRequested;        // render thread only: dedupes TryEnqueue while the pause is in flight

    // --- mirrors written on the UI thread, read under _gate on the render thread ---
    private LyricsTimingModel? _pendingTimingModel;
    private bool _timingDirty;
    private double _mirrorPositionMs;
    private bool _positionDirty;
    private bool _mirrorIsPlaying;
    private Color _mirrorPlayed;
    private Color _mirrorUnplayed;
    private string _mirrorFontFamily;
    private double _mirrorFontSize;
    private LyricsTextAlignment _mirrorAlignment;
    private bool _formatDirty;

    public LyricsCanvasControl()
    {
        InitializeComponent();

        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _clock = new PlaybackClock(() => _frameNowMs);
        _renderer.BloomDuration = _choreographer.BloomDurationMs;

        _mirrorPlayed = PlayedColor;
        _mirrorUnplayed = UnplayedColor;
        _mirrorFontFamily = FontFamilyName;
        _mirrorFontSize = LyricsFontSize;
        _mirrorAlignment = Alignment;
        _isRenderActive = IsRenderActive;

        // Pause the loop when this control is hidden by its own Visibility (the LyricsLoadState binding
        // collapses it when there are no lyrics) or resized while idle-paused; evaluate once on load.
        RegisterPropertyChangedCallback(VisibilityProperty, OnSelfVisibilityChanged);
        Loaded += OnLoaded;
        SizeChanged += OnControlSizeChanged;
        Unloaded += OnUnloaded;
    }

    #region Dependency properties

    public static readonly DependencyProperty LyricsProperty = DependencyProperty.Register(
        nameof(Lyrics), typeof(LyricsDocument), typeof(LyricsCanvasControl), new PropertyMetadata(null, OnLyricsChanged));

    /// <summary>The lines to render plus sync kind and offset. <c>null</c> / empty → blank.</summary>
    public LyricsDocument? Lyrics
    {
        get => (LyricsDocument?)GetValue(LyricsProperty);
        set => SetValue(LyricsProperty, value);
    }

    public static readonly DependencyProperty PositionMsProperty = DependencyProperty.Register(
        nameof(PositionMs), typeof(double), typeof(LyricsCanvasControl), new PropertyMetadata(0.0, OnPositionChanged));

    /// <summary>Authoritative playback position (ms) — treated as a clock anchor, not per-frame truth.</summary>
    public double PositionMs
    {
        get => (double)GetValue(PositionMsProperty);
        set => SetValue(PositionMsProperty, value);
    }

    public static readonly DependencyProperty IsPlayingProperty = DependencyProperty.Register(
        nameof(IsPlaying), typeof(bool), typeof(LyricsCanvasControl), new PropertyMetadata(false, OnIsPlayingChanged));

    /// <summary>Drives the clock — advance vs freeze.</summary>
    public bool IsPlaying
    {
        get => (bool)GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    public static readonly DependencyProperty TrackDurationMsProperty = DependencyProperty.Register(
        nameof(TrackDurationMs), typeof(double?), typeof(LyricsCanvasControl), new PropertyMetadata(null, OnLyricsChanged));

    /// <summary>Track length (ms) — bounds the final line's end. <c>null</c> when unknown.</summary>
    public double? TrackDurationMs
    {
        get => (double?)GetValue(TrackDurationMsProperty);
        set => SetValue(TrackDurationMsProperty, value);
    }

    public static readonly DependencyProperty PlayedColorProperty = DependencyProperty.Register(
        nameof(PlayedColor), typeof(Color), typeof(LyricsCanvasControl),
        new PropertyMetadata(Microsoft.UI.Colors.White, OnColorChanged));

    /// <summary>The wipe's played colour — the driver supplies the theme accent.</summary>
    public Color PlayedColor
    {
        get => (Color)GetValue(PlayedColorProperty);
        set => SetValue(PlayedColorProperty, value);
    }

    public static readonly DependencyProperty UnplayedColorProperty = DependencyProperty.Register(
        nameof(UnplayedColor), typeof(Color), typeof(LyricsCanvasControl),
        new PropertyMetadata(Color.FromArgb(110, 255, 255, 255), OnColorChanged));

    /// <summary>The wipe's unplayed colour — the driver supplies a dimmed foreground.</summary>
    public Color UnplayedColor
    {
        get => (Color)GetValue(UnplayedColorProperty);
        set => SetValue(UnplayedColorProperty, value);
    }

    public static readonly DependencyProperty SurfaceColorProperty = DependencyProperty.Register(
        nameof(SurfaceColor), typeof(Color), typeof(LyricsCanvasControl),
        new PropertyMetadata(Microsoft.UI.Colors.Transparent, OnSurfaceColorChanged));

    /// <summary>
    /// The canvas clear (background) colour. The driver pushes the page's effective surface colour so the
    /// canvas matches the rest of the page. The lyrics surface sits in the Fluent "content layer", so the
    /// driver supplies that layer's colour: a <i>translucent</i> layer colour under a Mica/Acrylic backdrop
    /// (the swap chain blends it over the live material), and an opaque colour (the layer composited over
    /// the black/white root) when there is no backdrop, where a transparent swap-chain clear would
    /// otherwise render white over the opaque window.
    /// </summary>
    public Color SurfaceColor
    {
        get => (Color)GetValue(SurfaceColorProperty);
        set => SetValue(SurfaceColorProperty, value);
    }

    private static void OnSurfaceColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (LyricsCanvasControl)d;
        if (control.Canvas is not null)
        {
            // ClearColor is a UI-thread Win2D property; the animated control picks it up on the next frame.
            control.Canvas.ClearColor = (Color)e.NewValue;
        }
    }

    public static readonly DependencyProperty FontFamilyNameProperty = DependencyProperty.Register(
        nameof(FontFamilyName), typeof(string), typeof(LyricsCanvasControl),
        new PropertyMetadata("Segoe UI", OnFontChanged));

    public string FontFamilyName
    {
        get => (string)GetValue(FontFamilyNameProperty);
        set => SetValue(FontFamilyNameProperty, value);
    }

    public static readonly DependencyProperty LyricsFontSizeProperty = DependencyProperty.Register(
        nameof(LyricsFontSize), typeof(double), typeof(LyricsCanvasControl),
        new PropertyMetadata(34.0, OnFontChanged));

    /// <summary>Base font size for the lyric text. (Distinct from the inherited <see cref="Control.FontSize"/>.)</summary>
    public double LyricsFontSize
    {
        get => (double)GetValue(LyricsFontSizeProperty);
        set => SetValue(LyricsFontSizeProperty, value);
    }

    /// <summary>Horizontal alignment of the lyric text. Also anchors the active-line scale pivot.</summary>
    public static readonly DependencyProperty AlignmentProperty = DependencyProperty.Register(
        nameof(Alignment), typeof(LyricsTextAlignment), typeof(LyricsCanvasControl),
        new PropertyMetadata(LyricsTextAlignment.Left, OnAlignmentChanged));

    public LyricsTextAlignment Alignment
    {
        get => (LyricsTextAlignment)GetValue(AlignmentProperty);
        set => SetValue(AlignmentProperty, value);
    }

    /// <summary>Per-word pop/float on the active line. Exposed so the harness can A/B it in the smoke test.</summary>
    public static readonly DependencyProperty EnableBloomProperty = DependencyProperty.Register(
        nameof(EnableBloom), typeof(bool), typeof(LyricsCanvasControl), new PropertyMetadata(true, OnEnableBloomChanged));

    public bool EnableBloom
    {
        get => (bool)GetValue(EnableBloomProperty);
        set => SetValue(EnableBloomProperty, value);
    }

    /// <summary>Gaussian blur on neighbour lines. Off by default (perf); exposed for the smoke test.</summary>
    public static readonly DependencyProperty EnableNeighborBlurProperty = DependencyProperty.Register(
        nameof(EnableNeighborBlur), typeof(bool), typeof(LyricsCanvasControl), new PropertyMetadata(false, OnEnableNeighborBlurChanged));

    public bool EnableNeighborBlur
    {
        get => (bool)GetValue(EnableNeighborBlurProperty);
        set => SetValue(EnableNeighborBlurProperty, value);
    }

    /// <summary>Karaoke gradient sweep on the active line (default), vs a discrete solid highlight when false.</summary>
    public static readonly DependencyProperty EnableKaraokeWipeProperty = DependencyProperty.Register(
        nameof(EnableKaraokeWipe), typeof(bool), typeof(LyricsCanvasControl), new PropertyMetadata(true, OnEnableKaraokeWipeChanged));

    public bool EnableKaraokeWipe
    {
        get => (bool)GetValue(EnableKaraokeWipeProperty);
        set => SetValue(EnableKaraokeWipeProperty, value);
    }

    private static void OnEnableBloomChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((LyricsCanvasControl)d)._renderer.EnableBloom = (bool)e.NewValue;

    private static void OnEnableNeighborBlurChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((LyricsCanvasControl)d)._renderer.EnableNeighborBlur = (bool)e.NewValue;

    private static void OnEnableKaraokeWipeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((LyricsCanvasControl)d)._renderer.EnableKaraokeWipe = (bool)e.NewValue;

    private static void OnAlignmentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (LyricsCanvasControl)d;
        lock (control._gate)
        {
            control._mirrorAlignment = (LyricsTextAlignment)e.NewValue;
            control._formatDirty = true; // alignment lives on the text format → re-lay-out
        }
        control.Wake();
    }

    /// <summary>
    /// Whether the surface is actually on screen — the driver sets this to <c>false</c> when the lyrics
    /// surface is not the selected tab or the window is minimised/hidden, so the canvas pauses its game
    /// loop instead of rendering pixels nobody sees. Combined with the control's own
    /// <see cref="UIElement.Visibility"/> (collapsed when there are no lyrics). Default <c>true</c>.
    /// </summary>
    public static readonly DependencyProperty IsRenderActiveProperty = DependencyProperty.Register(
        nameof(IsRenderActive), typeof(bool), typeof(LyricsCanvasControl), new PropertyMetadata(true, OnIsRenderActiveChanged));

    public bool IsRenderActive
    {
        get => (bool)GetValue(IsRenderActiveProperty);
        set => SetValue(IsRenderActiveProperty, value);
    }

    private static void OnIsRenderActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (LyricsCanvasControl)d;
        control._isRenderActive = (bool)e.NewValue;
        control.UpdateRenderActivity();
    }

    #endregion

    private static void OnLyricsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((LyricsCanvasControl)d).RebuildTimingModel();

    private static void OnPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (LyricsCanvasControl)d;
        lock (control._gate)
        {
            control._mirrorPositionMs = (double)e.NewValue;
            control._positionDirty = true;
        }
        control.Wake();
    }

    private static void OnIsPlayingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (LyricsCanvasControl)d;
        lock (control._gate)
        {
            control._mirrorIsPlaying = (bool)e.NewValue;
        }
        control.Wake();
    }

    private static void OnColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (LyricsCanvasControl)d;
        lock (control._gate)
        {
            control._mirrorPlayed = control.PlayedColor;
            control._mirrorUnplayed = control.UnplayedColor;
        }
        control.Wake();
    }

    private static void OnFontChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (LyricsCanvasControl)d;
        lock (control._gate)
        {
            control._mirrorFontFamily = control.FontFamilyName;
            control._mirrorFontSize = control.LyricsFontSize;
            control._formatDirty = true;
        }
        control.Wake();
    }

    private void RebuildTimingModel()
    {
        // Pure (no device) — safe to build on the UI thread; the render thread swaps it in.
        var model = LyricsTimingModel.Build(Lyrics, TrackDurationMs);
        lock (_gate)
        {
            _pendingTimingModel = model;
            _timingDirty = true;
        }
        Wake();
    }

    private void OnCreateResources(CanvasAnimatedControl sender, CanvasCreateResourcesEventArgs args)
    {
        // The device was (re)created — text layouts are device-bound, so force a rebuild.
        _visualDirty = true;
    }

    // Draw runs on the render thread; route it through the gate so it can't race teardown (see LyricsRenderGate).
    // State is passed positionally to a static lambda to keep this per-frame hop allocation-free.
    private void OnDraw(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args)
        => _renderGate.TryDraw((self: this, sender, args), static s => s.self.DrawFrame(s.sender, s.args));

    private void DrawFrame(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args)
    {
        _frameNowMs = args.Timing.TotalTime.TotalMilliseconds;
        var ds = args.DrawingSession;

        LyricsTimingModel? newTiming;
        bool positionDirty;
        double positionMs;
        bool isPlaying;
        Color played;
        Color unplayed;
        string fontFamily;
        double fontSize;
        LyricsTextAlignment alignment;
        bool formatDirty;

        lock (_gate)
        {
            newTiming = _timingDirty ? _pendingTimingModel : null;
            _timingDirty = false;
            positionDirty = _positionDirty;
            _positionDirty = false;
            positionMs = _mirrorPositionMs;
            isPlaying = _mirrorIsPlaying;
            played = _mirrorPlayed;
            unplayed = _mirrorUnplayed;
            fontFamily = _mirrorFontFamily;
            fontSize = _mirrorFontSize;
            alignment = _mirrorAlignment;
            formatDirty = _formatDirty;
            _formatDirty = false;
        }

        var width = sender.Size.Width;
        var height = sender.Size.Height;

        if (_format is null || formatDirty)
        {
            _format?.Dispose();
            _format = new CanvasTextFormat
            {
                FontFamily = string.IsNullOrWhiteSpace(fontFamily) ? "Segoe UI" : fontFamily,
                FontSize = (float)Math.Max(1, fontSize),
                HorizontalAlignment = LyricsAlign.ToCanvas(alignment),
                WordWrapping = CanvasWordWrapping.Wrap,
            };
            _visualDirty = true;
        }

        var trackChanged = newTiming is not null;
        if (trackChanged)
        {
            _timingModel = newTiming!;
            _visualDirty = true;
            _synchronizer.Reset();
            _choreographer.Reset();
        }

        if (_visualDirty || Math.Abs(width - _lastWidth) > 0.5)
        {
            var previousModel = _visualModel;
            _visualModel = LyricsLayout.Build(_timingModel, ds, _format, Math.Max(1, width - 2 * HorizontalMargin));

            // Same track, re-layout only (a live width / font / device change): carry each line's emphasis
            // tweens + bloom onsets across so they don't reset to defaults every frame — that reset is what
            // made neighbour blur drop and inactive lines jitter horizontally during a resize. The scroll
            // spring is likewise left intact (no Reset) so it glides to the re-wrapped target, not to 0.
            if (!trackChanged && previousModel is not null)
            {
                TransferAnimationState(previousModel, _visualModel);
            }

            previousModel?.Dispose();
            _lastWidth = width;
            _visualDirty = false;
        }

        var model = _visualModel ?? LyricsVisualModel.Empty;

        _clock.SetPlaying(isPlaying);
        if (positionDirty)
        {
            _clock.SetPosition(positionMs);
        }

        var estimated = _clock.Estimate();
        // Resolve against the timing model — its lines are parallel to the visual model's, so the index
        // maps 1:1. (When the visual model is empty, e.g. zero width, the choreographer/renderer no-op.)
        var active = _synchronizer.Resolve(_timingModel.Lines, estimated);
        _choreographer.Update(model, active, estimated, height, args.Timing.ElapsedTime.TotalSeconds);

        _renderer.Draw(ds, model, active, estimated, _choreographer.ScrollY, width, height, HorizontalMargin, alignment, played, unplayed);

        RequestIdlePauseIfSettled(isPlaying, model);
    }

    #region Pause controller

    private void OnLoaded(object sender, RoutedEventArgs e) => UpdateRenderActivity();

    private void OnControlSizeChanged(object sender, SizeChangedEventArgs e) => Wake();

    private void OnSelfVisibilityChanged(DependencyObject sender, DependencyProperty dp) => UpdateRenderActivity();

    /// <summary>Whether the surface is allowed to render at all — the driver-supplied gate AND not hidden
    /// by the control's own collapsed Visibility (the no-lyrics state).</summary>
    private bool IsActiveAndVisible => _isRenderActive && Visibility == Visibility.Visible;

    /// <summary>
    /// Re-evaluates the visible/active gate (UI thread). When the surface can't be seen the loop is paused
    /// outright; when it can, it is resumed — the render thread re-pauses it once a non-playing scene settles.
    /// </summary>
    private void UpdateRenderActivity()
    {
        if (_renderGate.IsTornDown || Canvas is null)
        {
            return;
        }

        if (IsActiveAndVisible)
        {
            Wake();
        }
        else
        {
            Canvas.Paused = true;
        }
    }

    /// <summary>
    /// Resumes the loop (UI thread) and bumps the wake generation so any idle-pause request the render
    /// thread has in flight is discarded. A no-op when the surface is not visible/active (e.g. a position
    /// tick arriving while off-tab), and harmless when already running.
    /// </summary>
    private void Wake()
    {
        Interlocked.Increment(ref _wakeGeneration);
        if (_renderGate.IsTornDown || Canvas is null)
        {
            return;
        }

        if (IsActiveAndVisible)
        {
            Canvas.Paused = false;
        }
    }

    /// <summary>
    /// Render thread: once playback is stopped and the scene is fully at rest, ask the UI thread to pause
    /// the loop. The request carries the wake generation it was made under; the UI-thread handler honours it
    /// only if no wake has happened since, so a seek/play/resize that lands first always wins.
    /// </summary>
    private void RequestIdlePauseIfSettled(bool isPlaying, LyricsVisualModel model)
    {
        var generation = Interlocked.Read(ref _wakeGeneration);
        if (generation != _renderSeenGeneration)
        {
            _renderSeenGeneration = generation; // a wake happened → re-arm so a fresh settle re-requests
            _idlePauseRequested = false;
        }

        // We are active here (the loop only runs when active), so "should not render" means the scene is a
        // static, non-playing image — safe to pause until the next wake.
        var sceneAnimating = !_choreographer.IsAtRest(model);
        var canIdle = !LyricsRenderActivity.ShouldRender(renderActive: true, isPlaying, sceneAnimating);
        if (!canIdle)
        {
            _idlePauseRequested = false;
            return;
        }

        if (_idlePauseRequested)
        {
            return; // already asked; waiting for the UI thread to land the pause
        }

        _idlePauseRequested = true;
        _dispatcher.TryEnqueue(() =>
        {
            if (Interlocked.Read(ref _wakeGeneration) != generation || _renderGate.IsTornDown || Canvas is null)
            {
                return; // a wake superseded this request, or we tore down — leave the loop running/stopped as is
            }

            if (IsActiveAndVisible)
            {
                Canvas.Paused = true;
            }
        });
    }

    #endregion

    /// <summary>
    /// Copies per-line emphasis (opacity / scale / blur) and per-word bloom onsets from a prior visual
    /// model to its freshly re-laid-out replacement. The two are parallel (same timing model → same line
    /// and word order), so transfer is by index. This keeps animation continuous across a re-layout, so a
    /// live resize re-wraps smoothly instead of resetting every effect to its default each frame.
    /// </summary>
    private static void TransferAnimationState(LyricsVisualModel from, LyricsVisualModel to)
    {
        var lineCount = Math.Min(from.Lines.Count, to.Lines.Count);
        for (var i = 0; i < lineCount; i++)
        {
            var source = from.Lines[i];
            var destination = to.Lines[i];

            destination.Opacity.Jump(source.Opacity.Value);
            destination.Opacity.Target = source.Opacity.Target;
            destination.Scale.Jump(source.Scale.Value);
            destination.Scale.Target = source.Scale.Target;
            destination.Blur.Jump(source.Blur.Value);
            destination.Blur.Target = source.Blur.Target;

            var wordCount = Math.Min(source.Words.Count, destination.Words.Count);
            for (var j = 0; j < wordCount; j++)
            {
                destination.Words[j].OnsetMs = source.Words[j].OnsetMs;
            }
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Win2D guidance: detach the animated control and free device resources on unload. RemoveFromVisualTree
        // only asks the render loop to stop (it does not join it), so dispose through the gate: it latches and
        // waits for any in-flight frame, after which OnDraw no-ops instead of touching the freed layouts/format.
        Canvas.RemoveFromVisualTree();
        _renderGate.Teardown(this, static self =>
        {
            self._visualModel?.Dispose();
            self._visualModel = null;
            self._format?.Dispose();
            self._format = null;
        });
    }
}
