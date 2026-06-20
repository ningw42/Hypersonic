using Hypersonic.Controls.Lyrics;

namespace Hypersonic.Tests.Controls.Lyrics;

[TestClass]
public class LyricsAlignmentTests
{
    private const double Margin = 28;
    private const double Width = 400;

    [TestMethod]
    public void PivotX_Left_AnchorsAtLeftMargin()
        => Assert.AreEqual(28, LyricsAlign.PivotX(LyricsTextAlignment.Left, Margin, Width), 1e-9);

    [TestMethod]
    public void PivotX_Center_AnchorsAtCentre()
        => Assert.AreEqual(228, LyricsAlign.PivotX(LyricsTextAlignment.Center, Margin, Width), 1e-9);

    [TestMethod]
    public void PivotX_Right_AnchorsAtRightEdge()
        => Assert.AreEqual(428, LyricsAlign.PivotX(LyricsTextAlignment.Right, Margin, Width), 1e-9);
}
