using Hypersonic.Helpers;

namespace Hypersonic.Tests.Helpers;

[TestClass]
public class QueueTabSubscriptionGateTests
{
    [TestMethod]
    public void RepeatedAttach_AttachesOnce()
    {
        var attachCount = 0;
        var detachCount = 0;
        var gate = new QueueTabSubscriptionGate(
            attachAll: () => attachCount++,
            detachAll: () => detachCount++);

        gate.Attach();
        gate.Attach();
        gate.Attach();

        Assert.AreEqual(1, attachCount);
        Assert.AreEqual(0, detachCount);
        Assert.IsTrue(gate.IsSubscribed);
    }

    [TestMethod]
    public void RepeatedDetach_DetachesAtMostOnce()
    {
        var attachCount = 0;
        var detachCount = 0;
        var gate = new QueueTabSubscriptionGate(
            attachAll: () => attachCount++,
            detachAll: () => detachCount++);

        gate.Detach();
        gate.Detach();

        Assert.AreEqual(0, attachCount);
        Assert.AreEqual(0, detachCount);
        Assert.IsFalse(gate.IsSubscribed);
    }

    [TestMethod]
    public void AttachDetachCycle()
    {
        var attachCount = 0;
        var detachCount = 0;
        var gate = new QueueTabSubscriptionGate(
            attachAll: () => attachCount++,
            detachAll: () => detachCount++);

        gate.Attach();
        gate.Detach();
        gate.Attach();
        gate.Detach();

        Assert.AreEqual(2, attachCount);
        Assert.AreEqual(2, detachCount);
        Assert.IsFalse(gate.IsSubscribed);
    }

    [TestMethod]
    public void Atomicity_BothCallbacksFireTogether()
    {
        var fakeEventA = new FakeEvent();
        var fakeEventB = new FakeEvent();
        var aCount = 0;
        var bCount = 0;

        var gate = new QueueTabSubscriptionGate(
            attachAll: () =>
            {
                fakeEventA.Subscribe(OnA);
                fakeEventB.Subscribe(OnB);
            },
            detachAll: () =>
            {
                fakeEventA.Unsubscribe(OnA);
                fakeEventB.Unsubscribe(OnB);
            });

        void OnA() => aCount++;
        void OnB() => bCount++;

        gate.Attach();
        gate.Attach();   // idempotent

        fakeEventA.Raise();
        fakeEventB.Raise();

        Assert.AreEqual(1, aCount);
        Assert.AreEqual(1, bCount);

        gate.Detach();
        fakeEventA.Raise();
        fakeEventB.Raise();
        Assert.AreEqual(1, aCount);
        Assert.AreEqual(1, bCount);
    }

    private sealed class FakeEvent
    {
        private Action? _handlers;
        public void Subscribe(Action h) => _handlers += h;
        public void Unsubscribe(Action h) => _handlers -= h;
        public void Raise() => _handlers?.Invoke();
    }
}
