using Hypersonic.Controls.Lyrics;

namespace Hypersonic.Tests.Controls.Lyrics;

[TestClass]
public class TweenTests
{
    [TestMethod]
    public void Jump_SetsValueAndTarget()
    {
        var tween = new Tween(0) { Target = 1 };
        tween.Jump(0.5);

        Assert.AreEqual(0.5, tween.Value, 1e-9);
        Assert.AreEqual(0.5, tween.Target, 1e-9);
    }

    [TestMethod]
    public void AtTarget_DoesNotMove()
    {
        var tween = new Tween(0.4) { Target = 0.4 };
        tween.Update(0.016);
        Assert.AreEqual(0.4, tween.Value, 1e-9);
    }

    [TestMethod]
    public void Update_EasesTowardTarget_PartiallyInOneStep()
    {
        var tween = new Tween(0) { Target = 1, TimeConstant = 0.1 };

        tween.Update(0.05);

        Assert.IsTrue(tween.Value > 0, "should move toward target");
        Assert.IsTrue(tween.Value < 1, "should not snap to target in one small step");
    }

    [TestMethod]
    public void Reaches_Target_OverTime()
    {
        var tween = new Tween(0) { Target = 1, TimeConstant = 0.1 };

        for (var i = 0; i < 200; i++)
        {
            tween.Update(0.016);
        }

        Assert.AreEqual(1, tween.Value, 1e-3);
    }

    [TestMethod]
    public void NonPositiveTimestep_DoesNotMove()
    {
        var tween = new Tween(0.2) { Target = 1 };
        tween.Update(0);
        tween.Update(-0.5);
        Assert.AreEqual(0.2, tween.Value, 1e-9);
    }

    [TestMethod]
    public void EaseOutCubic_Endpoints_AndEased()
    {
        Assert.AreEqual(0, Tween.EaseOutCubic(0), 1e-9);
        Assert.AreEqual(1, Tween.EaseOutCubic(1), 1e-9);
        Assert.IsTrue(Tween.EaseOutCubic(0.5) > 0.5, "ease-out is above the linear midpoint");
    }

    [TestMethod]
    public void EaseOutCubic_ClampsOutOfRange()
    {
        Assert.AreEqual(0, Tween.EaseOutCubic(-1), 1e-9);
        Assert.AreEqual(1, Tween.EaseOutCubic(2), 1e-9);
    }

    [TestMethod]
    public void Pulse_RisesToPeakAtMidpoint_AndReturns()
    {
        Assert.AreEqual(0, Tween.Pulse(0), 1e-9);
        Assert.AreEqual(1, Tween.Pulse(0.5), 1e-9);
        Assert.AreEqual(0, Tween.Pulse(1), 1e-9);
        Assert.AreEqual(0, Tween.Pulse(1.5), 1e-9, "clamps past the end");
    }
}
