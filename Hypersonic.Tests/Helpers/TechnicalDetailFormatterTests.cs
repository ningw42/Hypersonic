using Hypersonic.Helpers;

namespace Hypersonic.Tests.Helpers;

[TestClass]
public class TechnicalDetailFormatterTests
{
    [TestMethod]
    public void Flac_BitDepth_SampleRate_BitRate_AllPresent()
    {
        var s = TechnicalDetailFormatter.Format("flac", 24, 96000, 4608);
        Assert.AreEqual("FLAC · 24-bit/96kHz · 4608 kbps", s);
    }

    [TestMethod]
    public void Flac_FractionalSampleRate()
    {
        var s = TechnicalDetailFormatter.Format("flac", 16, 44100, 940);
        Assert.AreEqual("FLAC · 16-bit/44.1kHz · 940 kbps", s);
    }

    [TestMethod]
    public void Mp3_NoBitDepth_OmitsComposite()
    {
        var s = TechnicalDetailFormatter.Format("mp3", 0, 0, 320);
        Assert.AreEqual("MP3 · 320 kbps", s);
    }

    [TestMethod]
    public void Ogg_OnlyBitRate()
    {
        var s = TechnicalDetailFormatter.Format("ogg", 0, 0, 192);
        Assert.AreEqual("OGG · 192 kbps", s);
    }

    [TestMethod]
    public void NoBitRate_KeepsComposite()
    {
        var s = TechnicalDetailFormatter.Format("flac", 24, 96000, 0);
        Assert.AreEqual("FLAC · 24-bit/96kHz", s);
    }

    [TestMethod]
    public void HalfComposite_BitDepthOnly_OmitsCompositeEntirely()
    {
        var s = TechnicalDetailFormatter.Format("flac", 24, 0, 4608);
        Assert.AreEqual("FLAC · 4608 kbps", s);
    }

    [TestMethod]
    public void HalfComposite_SampleRateOnly_OmitsCompositeEntirely()
    {
        var s = TechnicalDetailFormatter.Format("flac", 0, 96000, 4608);
        Assert.AreEqual("FLAC · 4608 kbps", s);
    }

    [TestMethod]
    public void AllNumericsZero_HidesLine()
    {
        var s = TechnicalDetailFormatter.Format("flac", 0, 0, 0);
        Assert.IsNull(s);
    }

    [TestMethod]
    public void NoSuffix_HidesLineRegardlessOfNumerics()
    {
        Assert.IsNull(TechnicalDetailFormatter.Format(null, 24, 96000, 4608));
        Assert.IsNull(TechnicalDetailFormatter.Format("", 24, 96000, 4608));
        Assert.IsNull(TechnicalDetailFormatter.Format("   ", 0, 0, 320));
    }

    [TestMethod]
    public void NegativeNumerics_TreatedAsMissing()
    {
        var s = TechnicalDetailFormatter.Format("flac", -1, -2, -3);
        Assert.IsNull(s);
    }

    [TestMethod]
    public void UppercasesCodec()
    {
        var s = TechnicalDetailFormatter.Format("Flac", 0, 0, 320);
        Assert.AreEqual("FLAC · 320 kbps", s);
    }
}
