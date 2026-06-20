using Hypersonic.Controls.Lyrics;

namespace Hypersonic.Tests.Controls.Lyrics;

[TestClass]
public class SpringTests
{
    [TestMethod]
    public void Jump_SetsValueAndTarget_ZeroesVelocity()
    {
        var spring = new Spring(0);
        spring.Target = 100;
        spring.Update(0.1);

        spring.Jump(42);

        Assert.AreEqual(42, spring.Value, 1e-9);
        Assert.AreEqual(0, spring.Velocity, 1e-9);
    }

    [TestMethod]
    public void AtRest_AtTarget_DoesNotMove()
    {
        var spring = new Spring(50);
        spring.Target = 50;

        spring.Update(0.016);

        Assert.AreEqual(50, spring.Value, 1e-9);
        Assert.AreEqual(0, spring.Velocity, 1e-9);
    }

    [TestMethod]
    public void Update_MovesValueTowardTarget()
    {
        var spring = new Spring(0) { Target = 100 };

        var before = spring.Value;
        spring.Update(0.05);

        Assert.IsTrue(spring.Value > before, "value should advance toward the target");
        Assert.IsTrue(spring.Value < 100, "one step should not reach the target");
    }

    [TestMethod]
    public void Settles_ToTarget_OverTime()
    {
        var spring = new Spring(0) { Target = 100 };

        for (var i = 0; i < 200; i++)
        {
            spring.Update(0.016);
        }

        Assert.AreEqual(100, spring.Value, 0.5, "critically-damped spring should settle on the target");
    }

    [TestMethod]
    public void CriticallyDamped_DoesNotOvershoot()
    {
        var spring = new Spring(0) { Target = 100 };

        var max = 0.0;
        for (var i = 0; i < 500; i++)
        {
            spring.Update(0.016);
            max = Math.Max(max, spring.Value);
        }

        Assert.IsTrue(max <= 100.0 + 1e-6, $"critically-damped spring must not overshoot, but peaked at {max}");
    }

    [TestMethod]
    public void NonPositiveTimestep_DoesNotMove()
    {
        var spring = new Spring(10) { Target = 100 };

        spring.Update(0);
        spring.Update(-0.1);

        Assert.AreEqual(10, spring.Value, 1e-9);
    }
}
