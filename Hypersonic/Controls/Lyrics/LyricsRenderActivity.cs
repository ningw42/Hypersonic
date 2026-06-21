namespace Hypersonic.Controls.Lyrics;

/// <summary>
/// Pure decision for whether the lyrics canvas's animation loop needs to keep running this frame, or can
/// be paused so it stops burning GPU. A <c>CanvasAnimatedControl</c> redraws ~60×/s from load until it
/// leaves the visual tree — pointless when nothing is visible (off-tab, minimised, no lyrics) or nothing
/// is moving (paused playback, scene settled). This logic is stateless and Win2D-free so it is unit-tested
/// directly (mirrors <see cref="LyricsRenderGate"/> / <see cref="LyricsWipe"/> / <see cref="LyricsSynchronizer"/>);
/// the control composes it, owning the <c>Paused</c> toggle and feeding in the live flags + animation scalars.
/// </summary>
internal static class LyricsRenderActivity
{
    /// <summary>Scroll-spring rest tolerance — distance to target, in px.</summary>
    public const double ScrollRestPx = 0.5;

    /// <summary>Scroll-spring rest tolerance — residual speed, in px/s.</summary>
    public const double ScrollRestVelocity = 1.0;

    /// <summary>Per-line emphasis tween rest tolerance. Unit-agnostic small epsilon — opacity is 0..1,
    /// scale ~1, blur 0..4, so a single tight bound settles all three without freezing a visible transition.</summary>
    public const double EmphasisRest = 0.01;

    /// <summary>True when a critically-damped scroll value has effectively reached its target (position and
    /// velocity both within tolerance — position alone can momentarily coincide while momentum carries through).</summary>
    public static bool IsScrollSettled(double value, double target, double velocity)
        => Math.Abs(value - target) <= ScrollRestPx && Math.Abs(velocity) <= ScrollRestVelocity;

    /// <summary>True when an eased emphasis value (opacity / scale / blur) has effectively reached its target.</summary>
    public static bool IsEmphasisSettled(double value, double target)
        => Math.Abs(value - target) <= EmphasisRest;

    /// <summary>
    /// The headline gate. Run the loop only when the surface is actually visible/active <b>and</b> there is
    /// something to animate — playback is advancing the wipe, or the scene is still easing to rest. When not
    /// active (off-tab, minimised, no lyrics) it never runs; when active but idle it stops once settled.
    /// </summary>
    public static bool ShouldRender(bool renderActive, bool isPlaying, bool sceneAnimating)
        => renderActive && (isPlaying || sceneAnimating);
}
