using System.Text;

using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Core.Services;
using Hypersonic.Core.Services.Lyrics;

namespace Hypersonic.Core.Tests.Services.Lyrics;

[TestClass]
public class LrcPlainLyricsParserTests
{
    private static LrcPlainLyricsParser Parser() => new(new LoggingService());
    private static LyricsDocument? Parse(string text) => Parser().Parse(Encoding.UTF8.GetBytes(text));

    [TestMethod]
    public void FormatTag_IsLrclibPlain()
        => Assert.AreEqual("lrclib-plain", Parser().FormatTag);

    [TestMethod]
    public void Text_ProducesPlainDocument_OneLinePerTextLine()
    {
        var doc = Parse("line one\nline two\n");
        Assert.IsNotNull(doc);
        Assert.AreEqual(LyricsSyncKind.Plain, doc!.SyncKind);
        Assert.AreEqual("lrclib", doc.SourceProvider);
        Assert.AreEqual(2, doc.Lines.Count);
        Assert.AreEqual(0, doc.Lines[0].StartMs);
        Assert.AreEqual("line one", doc.Lines[0].Text);
    }

    [TestMethod]
    public void PreservesInternalBlanks_TrimsTrailing()
    {
        var doc = Parse("a\n\nb\n\n\n");
        Assert.AreEqual(3, doc!.Lines.Count); // a, blank, b
        Assert.AreEqual(string.Empty, doc.Lines[1].Text);
    }

    [TestMethod]
    public void Blank_ReturnsNull()
        => Assert.IsNull(Parse("   \n  "));
}
