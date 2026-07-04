using System.Text;

using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Core.Services;
using Hypersonic.Core.Services.Lyrics;

namespace Hypersonic.Core.Tests.Services.Lyrics;

[TestClass]
public class NeteaseLrcLyricsParserTests
{
    private static NeteaseLrcLyricsParser Parser() => new(new LoggingService());

    [TestMethod]
    public void FormatTag_IsNeteaseLrc() => Assert.AreEqual("netease-lrc", Parser().FormatTag);

    [TestMethod]
    public void Lrc_ParsesToLineDocument_StampedNetease()
    {
        var doc = Parser().Parse(Encoding.UTF8.GetBytes("[00:01.00]first\n[00:03.50]second"));

        Assert.IsNotNull(doc);
        Assert.AreEqual(LyricsSyncKind.Line, doc!.SyncKind);
        Assert.AreEqual("netease", doc.SourceProvider);
        Assert.AreEqual(2, doc.Lines.Count);
        Assert.AreEqual(1000, doc.Lines[0].StartMs);
    }

    [TestMethod]
    public void Untimed_ReturnsNull()
        => Assert.IsNull(Parser().Parse(Encoding.UTF8.GetBytes("no timestamps here")));
}
