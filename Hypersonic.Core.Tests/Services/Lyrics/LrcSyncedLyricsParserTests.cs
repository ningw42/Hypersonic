using System.Text;

using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Core.Services;
using Hypersonic.Core.Services.Lyrics;

namespace Hypersonic.Core.Tests.Services.Lyrics;

[TestClass]
public class LrcSyncedLyricsParserTests
{
    private static LrcSyncedLyricsParser Parser() => new(new LoggingService());
    private static LyricsDocument? Parse(string lrc) => Parser().Parse(Encoding.UTF8.GetBytes(lrc));

    [TestMethod]
    public void FormatTag_IsLrclibSynced()
        => Assert.AreEqual("lrclib-synced", Parser().FormatTag);

    [TestMethod]
    public void Synced_ProducesLineDocument_WithSourceAndNullLanguage()
    {
        var doc = Parse("[00:17.12] first\n[00:20.00] second");
        Assert.IsNotNull(doc);
        Assert.AreEqual(LyricsSyncKind.Line, doc!.SyncKind);
        Assert.AreEqual("lrclib", doc.SourceProvider);
        Assert.IsNull(doc.LanguageCode);
        Assert.AreEqual(2, doc.Lines.Count);
        Assert.AreEqual(17_120, doc.Lines[0].StartMs);
        Assert.AreEqual("first", doc.Lines[0].Text);
    }

    [TestMethod]
    public void HonorsOffset_DirectSign()
    {
        var doc = Parse("[offset:-400]\n[00:01.00]a");
        Assert.AreEqual(-400, doc!.OffsetMs);
    }

    [TestMethod]
    public void NoTimestamps_ReturnsNull()
        => Assert.IsNull(Parse("just plain text\nno timing"));

    [TestMethod]
    public void RoundTrip_RawToParse_IsStable()
    {
        const string lrc = "[00:01.00]a\n[00:02.00]b";
        var doc = Parse(lrc);
        Assert.AreEqual(2, doc!.Lines.Count);
        var again = Parse(lrc);
        Assert.AreEqual(doc.Lines[1].StartMs, again!.Lines[1].StartMs);
    }
}
