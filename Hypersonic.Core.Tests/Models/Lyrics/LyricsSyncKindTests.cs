using System.Text.Json;
using Hypersonic.Core.Helpers;
using Hypersonic.Core.Models.Lyrics;

namespace Hypersonic.Core.Tests.Models.Lyrics;

[TestClass]
public class LyricsSyncKindTests
{
    [TestMethod]
    public void AllFourMembers_ArePresent()
    {
        var values = Enum.GetValues<LyricsSyncKind>();
        CollectionAssert.AreEquivalent(
            new[] { LyricsSyncKind.Word, LyricsSyncKind.Line, LyricsSyncKind.Plain, LyricsSyncKind.None },
            values);
    }

    [TestMethod]
    public void RoundTripsAsStringViaGlobalJsonOptions()
    {
        var json = JsonSerializer.Serialize(LyricsSyncKind.Word, Json.Options);
        Assert.AreEqual("\"Word\"", json);

        var back = JsonSerializer.Deserialize<LyricsSyncKind>(json, Json.Options);
        Assert.AreEqual(LyricsSyncKind.Word, back);
    }
}
