using Hypersonic.Controls.Lyrics;

namespace Hypersonic.Tests.Controls.Lyrics;

[TestClass]
public class LyricsSynchronizerTests
{
    private static IReadOnlyList<LyricsTimingLine> Lines(params (int start, int end)[] spans)
        => spans.Select(s => new LyricsTimingLine
        {
            StartMs = s.start,
            EndMs = s.end,
            Text = "x",
            Words = new[] { new LyricsTimingWord { StartMs = s.start, EndMs = s.end, Text = "x" } },
        }).ToList();

    [TestMethod]
    public void BeforeFirstLine_ReturnsMinusOne()
    {
        var sync = new LyricsSynchronizer();
        var lines = Lines((1000, 2000), (2000, 3000));

        Assert.AreEqual(-1, sync.Resolve(lines, 500));
    }

    [TestMethod]
    public void AtAndAfterLineStart_ReturnsThatLine()
    {
        var sync = new LyricsSynchronizer();
        var lines = Lines((1000, 2000), (2000, 3000));

        Assert.AreEqual(0, sync.Resolve(lines, 1000));
        Assert.AreEqual(0, sync.Resolve(lines, 1999));
        Assert.AreEqual(1, sync.Resolve(lines, 2000));
    }

    [TestMethod]
    public void PastLastLine_ReturnsLastIndex()
    {
        var sync = new LyricsSynchronizer();
        var lines = Lines((0, 1000), (1000, 2000));

        Assert.AreEqual(1, sync.Resolve(lines, 999_999));
    }

    [TestMethod]
    public void ForwardProgression_TracksActiveLine()
    {
        var sync = new LyricsSynchronizer();
        var lines = Lines((0, 1000), (1000, 2000), (2000, 3000));

        Assert.AreEqual(0, sync.Resolve(lines, 500));
        Assert.AreEqual(1, sync.Resolve(lines, 1500));
        Assert.AreEqual(2, sync.Resolve(lines, 2500));
    }

    [TestMethod]
    public void BackwardSeek_ResolvesCorrectly_AfterForwardScan()
    {
        var sync = new LyricsSynchronizer();
        var lines = Lines((0, 1000), (1000, 2000), (2000, 3000));

        sync.Resolve(lines, 2500); // cache points at index 2
        Assert.AreEqual(0, sync.Resolve(lines, 500), "a backward seek must re-resolve, not trust the cache");
    }

    [TestMethod]
    public void EmptyLines_ReturnsMinusOne()
    {
        var sync = new LyricsSynchronizer();
        Assert.AreEqual(-1, sync.Resolve(System.Array.Empty<LyricsTimingLine>(), 1000));
    }
}
