using Hypersonic.Controls.Lyrics;

namespace Hypersonic.Tests.Controls.Lyrics;

/// <summary>
/// Covers the pure run-vs-pause decision that lets <c>LyricsCanvasControl</c> stop its
/// <c>CanvasAnimatedControl</c> game loop instead of redrawing 60×/s when nothing is visible or nothing is
/// moving. The control owns the <c>Paused</c> toggle; this logic decides whether a frame is even needed.
/// </summary>
[TestClass]
public class LyricsRenderActivityTests
{
    // --- ShouldRender: the headline gate ---

    [TestMethod]
    public void ShouldRender_WhenNotActive_IsFalse_RegardlessOfPlayingOrAnimating()
    {
        // Off-tab / minimised / no lyrics: never run, even mid-playback or mid-animation.
        Assert.IsFalse(LyricsRenderActivity.ShouldRender(renderActive: false, isPlaying: true, sceneAnimating: true));
        Assert.IsFalse(LyricsRenderActivity.ShouldRender(renderActive: false, isPlaying: true, sceneAnimating: false));
        Assert.IsFalse(LyricsRenderActivity.ShouldRender(renderActive: false, isPlaying: false, sceneAnimating: true));
        Assert.IsFalse(LyricsRenderActivity.ShouldRender(renderActive: false, isPlaying: false, sceneAnimating: false));
    }

    [TestMethod]
    public void ShouldRender_WhenActiveAndPlaying_IsTrue_EvenIfSettled()
    {
        // Playback advances the wipe every frame, so a visible+playing surface always needs frames.
        Assert.IsTrue(LyricsRenderActivity.ShouldRender(renderActive: true, isPlaying: true, sceneAnimating: false));
    }

    [TestMethod]
    public void ShouldRender_WhenActivePausedButAnimating_IsTrue()
    {
        // Paused playback but the scroll spring / emphasis tweens are still easing to rest → keep drawing.
        Assert.IsTrue(LyricsRenderActivity.ShouldRender(renderActive: true, isPlaying: false, sceneAnimating: true));
    }

    [TestMethod]
    public void ShouldRender_WhenActivePausedAndSettled_IsFalse()
    {
        // The idle case the user reported: visible, paused, everything at rest → pause the loop (~0% GPU).
        Assert.IsFalse(LyricsRenderActivity.ShouldRender(renderActive: true, isPlaying: false, sceneAnimating: false));
    }

    // --- IsScrollSettled: scroll spring at rest ---

    [TestMethod]
    public void IsScrollSettled_AtTargetWithZeroVelocity_IsTrue()
    {
        Assert.IsTrue(LyricsRenderActivity.IsScrollSettled(value: 100.0, target: 100.0, velocity: 0.0));
    }

    [TestMethod]
    public void IsScrollSettled_OffTarget_IsFalse()
    {
        Assert.IsFalse(LyricsRenderActivity.IsScrollSettled(value: 100.0, target: 140.0, velocity: 0.0));
    }

    [TestMethod]
    public void IsScrollSettled_AtTargetButStillMoving_IsFalse()
    {
        // Position can momentarily equal the target while velocity carries it through — not settled.
        Assert.IsFalse(LyricsRenderActivity.IsScrollSettled(value: 100.0, target: 100.0, velocity: 25.0));
    }

    [TestMethod]
    public void IsScrollSettled_WithinTolerance_IsTrue()
    {
        Assert.IsTrue(LyricsRenderActivity.IsScrollSettled(value: 100.2, target: 100.0, velocity: 0.3));
    }

    // --- IsEmphasisSettled: per-line opacity/scale/blur tween at rest ---

    [TestMethod]
    public void IsEmphasisSettled_AtTarget_IsTrue()
    {
        Assert.IsTrue(LyricsRenderActivity.IsEmphasisSettled(value: 0.5, target: 0.5));
    }

    [TestMethod]
    public void IsEmphasisSettled_MidTransition_IsFalse()
    {
        Assert.IsFalse(LyricsRenderActivity.IsEmphasisSettled(value: 0.5, target: 1.0));
    }

    [TestMethod]
    public void IsEmphasisSettled_WithinTolerance_IsTrue()
    {
        Assert.IsTrue(LyricsRenderActivity.IsEmphasisSettled(value: 0.999, target: 1.0));
    }
}
