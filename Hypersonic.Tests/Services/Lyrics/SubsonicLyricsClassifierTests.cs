using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Services.Lyrics;

namespace Hypersonic.Tests.Services.Lyrics;

[TestClass]
public class SubsonicLyricsClassifierTests
{
    [TestMethod]
    public void IsDefinitiveErrorCode_TrueOnlyFor20_30_70()
    {
        Assert.IsTrue(SubsonicLyricsClassifier.IsDefinitiveErrorCode(20));
        Assert.IsTrue(SubsonicLyricsClassifier.IsDefinitiveErrorCode(30));
        Assert.IsTrue(SubsonicLyricsClassifier.IsDefinitiveErrorCode(70));
        Assert.IsFalse(SubsonicLyricsClassifier.IsDefinitiveErrorCode(0));
        Assert.IsFalse(SubsonicLyricsClassifier.IsDefinitiveErrorCode(10));
    }

    [TestMethod]
    public void IsEmptyClassificationCode_OnlyForCode70()
    {
        Assert.IsTrue(SubsonicLyricsClassifier.IsEmptyClassificationCode(70));
        Assert.IsFalse(SubsonicLyricsClassifier.IsEmptyClassificationCode(20));
        Assert.IsFalse(SubsonicLyricsClassifier.IsEmptyClassificationCode(30));
    }

    [TestMethod]
    public void ClassifyFailedStatus_Code70_DefinitiveEmpty()
    {
        Assert.AreEqual(LyricsOutcomeKind.DefinitiveEmpty, SubsonicLyricsClassifier.ClassifyFailedStatus(70));
    }

    [TestMethod]
    public void ClassifyFailedStatus_Codes20And30_DefinitiveUnsupported()
    {
        Assert.AreEqual(LyricsOutcomeKind.DefinitiveUnsupported, SubsonicLyricsClassifier.ClassifyFailedStatus(20));
        Assert.AreEqual(LyricsOutcomeKind.DefinitiveUnsupported, SubsonicLyricsClassifier.ClassifyFailedStatus(30));
    }

    [TestMethod]
    public void ClassifyFailedStatus_GenericCodes_Transient()
    {
        foreach (var code in new[] { 0, 10, 40, 50, 60 })
        {
            Assert.AreEqual(LyricsOutcomeKind.Transient, SubsonicLyricsClassifier.ClassifyFailedStatus(code));
        }
    }
}
