using Hypersonic.Models;
using Hypersonic.Services.Lyrics;
using Hypersonic.Tests.Fakes;

using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Hypersonic.Tests.Services.Lyrics;

[TestClass]
public class LyricsProviderDisplayNameTests
{
    private static readonly NullLoggingService Log = new();

    [TestMethod]
    public void Subsonic_DisplayName()
        => Assert.AreEqual("Subsonic", new SubsonicLyricsProvider(null!, null!, Log).DisplayName);

    [TestMethod]
    public void LrcLib_DisplayName()
        => Assert.AreEqual("LrcLib", new LrcLibLyricsProvider(null!, null!, Log).DisplayName);

    [TestMethod]
    public void Amll_DisplayName()
        => Assert.AreEqual("AMLL",
            new AmllTtmlLyricsProvider(null!, null!, null!, Options.Create(new AmllLyricsOptions()), Log).DisplayName);

    [TestMethod]
    public void QQ_DisplayName()
        => Assert.AreEqual("QQ Music",
            new QQMusicLyricsProvider(null!, null!, Options.Create(new QQLyricsOptions()), Log).DisplayName);

    [TestMethod]
    public void Netease_DisplayName()
        => Assert.AreEqual("Netease",
            new NeteaseLyricsProvider(null!, null!, Options.Create(new NeteaseLyricsOptions()), Log).DisplayName);

    [TestMethod]
    public void Kugou_DisplayName()
        => Assert.AreEqual("Kugou",
            new KugouLyricsProvider(null!, null!, Options.Create(new KugouLyricsOptions()), Log).DisplayName);
}
