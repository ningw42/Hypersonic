using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Services;

namespace Hypersonic.Tests.Services.Lyrics;

[TestClass]
public class LyricsCacheFormatTests
{
    [TestMethod]
    public void ExtensionForFormat_MapsNewTags()
    {
        Assert.AreEqual(".qrc", LyricsService.ExtensionForFormat(LyricsFormat.Qrc));
        Assert.AreEqual(".krc", LyricsService.ExtensionForFormat(LyricsFormat.Krc));
        Assert.AreEqual(".yrc", LyricsService.ExtensionForFormat(LyricsFormat.Yrc));
        Assert.AreEqual(".lrc", LyricsService.ExtensionForFormat(LyricsFormat.Lrc));
    }

    [TestMethod]
    public void IsKnownLyricsFileName_AcceptsQrcAndKrc()
    {
        var hash = new string('a', 64);
        Assert.IsTrue(SqliteLyricsCacheStore.IsKnownLyricsFileName(hash + ".qrc"));
        Assert.IsTrue(SqliteLyricsCacheStore.IsKnownLyricsFileName(hash + ".krc"));
    }
}
