using Hypersonic.Core.Services.Lyrics;
using Hypersonic.Services;

namespace Hypersonic.Tests.Services.Lyrics;

[TestClass]
public class LyricsCacheFormatTests
{
    [TestMethod]
    public void ExtensionForFormat_MapsNewTags()
    {
        Assert.AreEqual(".qrc", LyricsService.ExtensionForFormat(QQLyricsConstants.QrcFormat));
        Assert.AreEqual(".krc", LyricsService.ExtensionForFormat(KugouLyricsConstants.KrcFormat));
        Assert.AreEqual(".yrc", LyricsService.ExtensionForFormat(NeteaseLyricsConstants.YrcFormat));
        Assert.AreEqual(".lrc", LyricsService.ExtensionForFormat(NeteaseLyricsConstants.LrcFormat));
    }

    [TestMethod]
    public void IsKnownLyricsFileName_AcceptsQrcAndKrc()
    {
        var hash = new string('a', 64);
        Assert.IsTrue(SqliteLyricsCacheStore.IsKnownLyricsFileName(hash + ".qrc"));
        Assert.IsTrue(SqliteLyricsCacheStore.IsKnownLyricsFileName(hash + ".krc"));
    }
}
