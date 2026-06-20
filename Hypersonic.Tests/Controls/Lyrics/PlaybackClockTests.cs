using Hypersonic.Controls.Lyrics;

namespace Hypersonic.Tests.Controls.Lyrics;

[TestClass]
public class PlaybackClockTests
{
    private double _now;
    private PlaybackClock NewClock() => new(() => _now);

    [TestMethod]
    public void WhilePlaying_EstimateAdvancesWithWallClock()
    {
        _now = 0;
        var clock = NewClock();
        clock.SetPlaying(true);
        clock.SetPosition(1000);

        _now = 500;

        Assert.AreEqual(1500, clock.Estimate(), 1e-6, "playing clock advances 1:1 with wall time");
    }

    [TestMethod]
    public void WhilePaused_EstimateIsFrozen()
    {
        _now = 0;
        var clock = NewClock();
        clock.SetPlaying(false);
        clock.SetPosition(1000);

        _now = 500;

        Assert.AreEqual(1000, clock.Estimate(), 1e-6, "paused clock does not advance");
    }

    [TestMethod]
    public void LargeDelta_IsTreatedAsSeek_SnapsImmediately()
    {
        _now = 0;
        var clock = NewClock();
        clock.SetPlaying(true);
        clock.SetPosition(1000);
        _now = 500; // estimate ~1500

        clock.SetPosition(5000); // |5000 - 1500| = 3500 > threshold → seek

        Assert.AreEqual(5000, clock.Estimate(), 1e-6, "a seek snaps the anchor with no smoothing");
    }

    [TestMethod]
    public void SmallDelta_Converges_WithoutAVisibleJump()
    {
        _now = 0;
        var clock = NewClock();
        clock.SetPlaying(true);
        clock.SetPosition(1000);
        _now = 500;
        var predicted = clock.Estimate(); // ~1500

        clock.SetPosition(1480); // delta -20, within threshold → smooth converge

        // No instantaneous jump: the estimate right now still equals what we were showing.
        Assert.AreEqual(predicted, clock.Estimate(), 1e-6, "a small correction must not jump the displayed position");
    }

    [TestMethod]
    public void SmallDelta_EventuallyConvergesTowardAuthoritativeTimeline()
    {
        _now = 0;
        var clock = NewClock();
        clock.SetPlaying(true);
        clock.SetPosition(1000);
        _now = 500;
        clock.Estimate(); // predicted ~1500
        clock.SetPosition(1480); // residual correction of +20 to be absorbed

        _now = 2000; // 1.5s later — plenty of time for the residual to decay
        var authoritativeRaw = 1480 + (2000 - 500); // anchor 1480 at wall 500, playing

        Assert.AreEqual(authoritativeRaw, clock.Estimate(), 1.0, "the residual decays and the estimate rejoins the authoritative timeline");
    }

    [TestMethod]
    public void ResumeAfterPause_ContinuesWithoutJumping()
    {
        _now = 0;
        var clock = NewClock();
        clock.SetPlaying(true);
        clock.SetPosition(1000);
        _now = 500; // estimate 1500

        clock.SetPlaying(false);
        _now = 3000; // long pause — frozen
        Assert.AreEqual(1500, clock.Estimate(), 1e-6, "frozen at the pause point");

        clock.SetPlaying(true);
        _now = 3200; // 200ms after resume
        Assert.AreEqual(1700, clock.Estimate(), 1e-6, "resumes from where it paused, no jump");
    }
}
