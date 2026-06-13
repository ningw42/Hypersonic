using Hypersonic.Core.Services.Lyrics;

namespace Hypersonic.Core.Tests.Services.Lyrics;

[TestClass]
public class LrcLineParserTests
{
    [TestMethod]
    public void Parses_TwoDigitHundredths()
    {
        var r = LrcLineParser.Parse("[00:17.12] hello");
        Assert.AreEqual(1, r.Lines.Count);
        Assert.AreEqual(17_120, r.Lines[0].TimeMs);
        Assert.AreEqual("hello", r.Lines[0].Text);
    }

    [TestMethod]
    public void Parses_Milliseconds_ThreeDigits()
    {
        var r = LrcLineParser.Parse("[01:02.345]word");
        Assert.AreEqual(62_345, r.Lines[0].TimeMs);
    }

    [TestMethod]
    public void Parses_NoFraction()
    {
        var r = LrcLineParser.Parse("[00:05]x");
        Assert.AreEqual(5_000, r.Lines[0].TimeMs);
    }

    [TestMethod]
    public void MultipleTimestamps_EmitOneLineEach_SortedByTime()
    {
        var r = LrcLineParser.Parse("[01:24.00][00:12.00]Repeating chorus");
        Assert.AreEqual(2, r.Lines.Count);
        Assert.AreEqual(12_000, r.Lines[0].TimeMs);
        Assert.AreEqual(84_000, r.Lines[1].TimeMs);
        Assert.AreEqual("Repeating chorus", r.Lines[1].Text);
    }

    [TestMethod]
    public void HonorsOffsetTag_DirectSign()
    {
        var r = LrcLineParser.Parse("[offset:+250]\n[00:01.00]a");
        Assert.AreEqual(250, r.OffsetMs);
        var r2 = LrcLineParser.Parse("[offset:-250]\n[00:01.00]a");
        Assert.AreEqual(-250, r2.OffsetMs);
    }

    [TestMethod]
    public void IgnoresInformationalTags()
    {
        var r = LrcLineParser.Parse("[ar:Artist]\n[ti:Title]\n[al:Album]\n[by:x]\n[00:01.00]only line");
        Assert.AreEqual(1, r.Lines.Count);
        Assert.AreEqual("only line", r.Lines[0].Text);
        Assert.IsNull(r.OffsetMs);
    }

    [TestMethod]
    public void DropsUntimedLines_AndBlankInput()
    {
        var r = LrcLineParser.Parse("no timestamp here\n[00:01.00]kept");
        Assert.AreEqual(1, r.Lines.Count);
        Assert.AreEqual("kept", r.Lines[0].Text);
        Assert.AreEqual(0, LrcLineParser.Parse("   ").Lines.Count);
    }

    [TestMethod]
    public void EmptyTextLine_IsKept_AsTimedBlank()
    {
        var r = LrcLineParser.Parse("[00:30.00]");
        Assert.AreEqual(1, r.Lines.Count);
        Assert.AreEqual(string.Empty, r.Lines[0].Text);
    }

    [TestMethod]
    public void Parses_SingleDigitTenths()
    {
        var r = LrcLineParser.Parse("[00:01.5]half");
        Assert.AreEqual(1_500, r.Lines[0].TimeMs);
    }

    [TestMethod]
    public void LastOffsetTag_Wins()
    {
        var r = LrcLineParser.Parse("[offset:100]\n[offset:-50]\n[00:01.00]a");
        Assert.AreEqual(-50, r.OffsetMs);
    }

    [TestMethod]
    public void SameTimestamp_PreservesSourceOrder()
    {
        var r = LrcLineParser.Parse("[00:10.00]A\n[00:10.00]B");
        Assert.AreEqual(2, r.Lines.Count);
        Assert.AreEqual("A", r.Lines[0].Text);
        Assert.AreEqual("B", r.Lines[1].Text);
    }
}
