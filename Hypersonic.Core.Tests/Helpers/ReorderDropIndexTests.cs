using Hypersonic.Core.Helpers;

namespace Hypersonic.Core.Tests.Helpers;

[TestClass]
public class ReorderDropIndexTests
{
    // One row: three 100-wide, 20-tall chips at x=0,100,200 (top=0). Midpoints x=50,150,250; row center y=10.
    private static readonly (double Left, double Top, double Width, double Height)[] OneRow =
        [(0, 0, 100, 20), (100, 0, 100, 20), (200, 0, 100, 20)];

    // Two rows: [A,B] on row 0 (top=0), [C] on row 1 (top=30). Row centers y=10 and y=40.
    private static readonly (double Left, double Top, double Width, double Height)[] TwoRows =
        [(0, 0, 100, 20), (100, 0, 100, 20), (0, 30, 100, 20)];

    [TestMethod]
    public void Empty_ReturnsZero()
        => Assert.AreEqual(0, ReorderDropIndex.Resolve(123, 45, []));

    [TestMethod]
    public void SingleRow_BeforeFirstMidpoint_ReturnsZero()
        => Assert.AreEqual(0, ReorderDropIndex.Resolve(40, 10, OneRow));

    [TestMethod]
    public void SingleRow_PastFirstMidpoint_ReturnsOne()
        => Assert.AreEqual(1, ReorderDropIndex.Resolve(60, 10, OneRow));

    [TestMethod]
    public void SingleRow_PastLastMidpoint_ReturnsCount()
        => Assert.AreEqual(3, ReorderDropIndex.Resolve(9999, 10, OneRow));

    [TestMethod]
    public void SingleRow_ExactlyAtMidpoint_CountsAsAtOrLeft()
        => Assert.AreEqual(1, ReorderDropIndex.Resolve(50, 10, OneRow));

    [TestMethod]
    public void SecondRow_BeforeItsChip_ReturnsCountOfRowOne()
        => Assert.AreEqual(2, ReorderDropIndex.Resolve(40, 35, TwoRows)); // both row-0 chips precede; row-1 chip does not

    [TestMethod]
    public void SecondRow_PastItsChip_ReturnsAll()
        => Assert.AreEqual(3, ReorderDropIndex.Resolve(60, 35, TwoRows));

    [TestMethod]
    public void SingleChip_LeftAndRight()
    {
        var one = new[] { (0d, 0d, 100d, 20d) };
        Assert.AreEqual(0, ReorderDropIndex.Resolve(40, 10, one));
        Assert.AreEqual(1, ReorderDropIndex.Resolve(60, 10, one));
    }
}
