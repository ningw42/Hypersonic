using System.Threading;

using Hypersonic.Controls.Lyrics;

namespace Hypersonic.Tests.Controls.Lyrics;

/// <summary>
/// Covers the render-thread/UI-thread teardown contract that keeps <c>LyricsCanvasControl</c> from drawing a
/// disposed <c>CanvasTextLayout</c> when its page is navigated away: a draw and a teardown never overlap, and
/// once teardown latches every later draw is skipped.
/// </summary>
[TestClass]
public class LyricsRenderGateTests
{
    [TestMethod]
    public void TryDraw_RunsDraw_AndReportsRan_BeforeTeardown()
    {
        var gate = new LyricsRenderGate();
        var runs = 0;

        var ran = gate.TryDraw(0, _ => runs++);

        Assert.IsTrue(ran, "a draw before teardown should run");
        Assert.AreEqual(1, runs);
        Assert.IsFalse(gate.IsTornDown);
    }

    [TestMethod]
    public void TryDraw_AfterTeardown_DoesNotRun()
    {
        var gate = new LyricsRenderGate();
        gate.Teardown(0, _ => { });

        var ran = false;
        var result = gate.TryDraw(0, _ => ran = true);

        Assert.IsFalse(result, "a draw after teardown must report it was skipped");
        Assert.IsFalse(ran, "a draw after teardown must not execute — its device resources are freed");
        Assert.IsTrue(gate.IsTornDown);
    }

    [TestMethod]
    public void Teardown_DisposesOnce_EvenIfCalledTwice()
    {
        var gate = new LyricsRenderGate();
        var disposeCount = 0;

        gate.Teardown(0, _ => disposeCount++);
        gate.Teardown(0, _ => disposeCount++);

        Assert.AreEqual(1, disposeCount, "teardown must latch and dispose exactly once");
    }

    [TestMethod]
    public void Teardown_BlocksUntilInFlightDrawCompletes()
    {
        // The crux of the fix: while a frame is drawing, teardown's disposal must wait for it to finish.
        // If it did not, Unloaded (UI thread) could dispose a CanvasTextLayout that OnDraw (render thread)
        // is still drawing — the ObjectDisposedException this gate exists to prevent.
        var gate = new LyricsRenderGate();
        var drawEntered = new ManualResetEventSlim(false);
        var drawCompleted = false;
        var disposeSawDrawComplete = false;
        var drawRan = false;

        var drawThread = new Thread(() =>
        {
            drawRan = gate.TryDraw(0, _ =>
            {
                drawEntered.Set();
                Thread.Sleep(150); // hold the critical section so teardown is forced to wait
                drawCompleted = true;
            });
        });
        drawThread.Start();

        drawEntered.Wait(); // guarantee the draw is inside the gate before we tear down
        gate.Teardown(0, _ => disposeSawDrawComplete = drawCompleted);

        drawThread.Join();

        Assert.IsTrue(drawRan, "the draw started before teardown and should have run");
        Assert.IsTrue(
            disposeSawDrawComplete,
            "teardown disposal overlapped an in-flight draw — it must block until the frame finishes");
    }
}
