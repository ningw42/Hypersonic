using Hypersonic.Core.Models.Lyrics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Hypersonic.Core.Tests.Models.Lyrics;

[TestClass]
public class LyricsFormatTests
{
    [TestMethod]
    public void Default_IsUnknown()
        => Assert.AreEqual(0, System.Convert.ToInt32(LyricsFormat.Unknown));

    [TestMethod]
    public void DefinesEveryWireFormat()
    {
        // One member per genuinely-distinct wire format the providers emit.
        CollectionAssert.AreEquivalent(
            new[] { "Unknown", "PlainText", "Lrc", "Ttml", "Qrc", "Krc", "Yrc", "SubsonicStructured" },
            System.Enum.GetNames<LyricsFormat>());
    }
}
