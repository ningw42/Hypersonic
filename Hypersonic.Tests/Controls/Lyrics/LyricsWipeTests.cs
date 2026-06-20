using Hypersonic.Controls.Lyrics;

namespace Hypersonic.Tests.Controls.Lyrics;

[TestClass]
public class LyricsWipeTests
{
    // Three words, each 1s long, 10px / 20px / 30px wide (total 60px).
    private static readonly (int StartMs, int EndMs, double WidthPx)[] Words =
    [
        (0, 1000, 10),
        (1000, 2000, 20),
        (2000, 3000, 30),
    ];

    [TestMethod]
    public void PlayedHead_BeforeStart_IsZero()
        => Assert.AreEqual(0, LyricsWipe.PlayedHeadPx(Words, -100), 1e-9);

    [TestMethod]
    public void PlayedHead_AfterEnd_IsTotalWidth()
        => Assert.AreEqual(60, LyricsWipe.PlayedHeadPx(Words, 10_000), 1e-9);

    [TestMethod]
    public void PlayedHead_AtWordBoundary_IsCumulativeWidth()
        => Assert.AreEqual(10, LyricsWipe.PlayedHeadPx(Words, 1000), 1e-9);

    [TestMethod]
    public void PlayedHead_MidWord_InterpolatesWithinThatWord()
    {
        // Halfway through the 2nd word (t=1500): 10 (first word) + 0.5 * 20 = 20.
        Assert.AreEqual(20, LyricsWipe.PlayedHeadPx(Words, 1500), 1e-9);
    }

    [TestMethod]
    public void RowFraction_HeadBeforeRow_IsZero()
        => Assert.AreEqual(0, LyricsWipe.RowFraction(headPx: 5, rowStartPx: 10, rowEndPx: 30), 1e-9);

    [TestMethod]
    public void RowFraction_HeadPastRow_IsOne()
        => Assert.AreEqual(1, LyricsWipe.RowFraction(headPx: 40, rowStartPx: 10, rowEndPx: 30), 1e-9);

    [TestMethod]
    public void RowFraction_HeadInsideRow_IsLocalFraction()
    {
        // Head at 20 within row [10,30] → (20-10)/(30-10) = 0.5.
        Assert.AreEqual(0.5, LyricsWipe.RowFraction(headPx: 20, rowStartPx: 10, rowEndPx: 30), 1e-9);
    }

    [TestMethod]
    public void RowFraction_ZeroWidthRow_IsFullyPlayedWhenReached()
        => Assert.AreEqual(1, LyricsWipe.RowFraction(headPx: 10, rowStartPx: 10, rowEndPx: 10), 1e-9);

    [TestMethod]
    public void PlayedHead_ZeroDurationWord_DoesNotDivideByZero()
    {
        (int, int, double)[] words = [(1000, 1000, 15)];
        // Before the instant → 0; at/after → full width.
        Assert.AreEqual(0, LyricsWipe.PlayedHeadPx(words, 999), 1e-9);
        Assert.AreEqual(15, LyricsWipe.PlayedHeadPx(words, 1000), 1e-9);
    }
}
