using System.Text;

using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Core.Services;
using Hypersonic.Core.Services.Lyrics;
using Hypersonic.Services.Lyrics;

using Lyricify.Lyrics.Models;

namespace Hypersonic.Tests.Services.Lyrics;

[TestClass]
public class LyricifyWordLyricsParserTests
{
    // Minimal QRC: [lineStart,lineDuration]word(wordStart,wordDuration)...
    // If this literal does not parse under the library, replace it with a captured sample from the
    // library's Lyricify.Lyrics.Demo/RawLyrics/QrcDemo.txt — the test's red→green loop is the check.
    private const string QrcSample =
        "[0,1000]Hello(0,500)World(500,500)\n[1000,1000]Second(1000,500)Line(1500,500)";

    private static LyricifyWordLyricsParser QrcParser()
        => new(LyricsFormat.Qrc, LyricsRawTypes.Qrc, new LoggingService());

    [TestMethod]
    public void FormatTag_IsQrc()
        => Assert.AreEqual(LyricsFormat.Qrc, QrcParser().Format);

    [TestMethod]
    public void Qrc_ParsesToWordDocument()
    {
        var doc = QrcParser().Parse(Encoding.UTF8.GetBytes(QrcSample));

        Assert.IsNotNull(doc);
        Assert.AreEqual(LyricsSyncKind.Word, doc!.SyncKind);
        Assert.AreEqual(LyricsFormat.Qrc, doc.Format);
        Assert.AreEqual(2, doc.Lines.Count);
        Assert.IsTrue(doc.Lines[0].Words.Count >= 2, "first line should carry word timings");
        // Validator invariants hold (word bounds within line, monotonic) — a parsed doc is already validated.
        Assert.AreEqual(0, LyricsValidator.Validate(doc).Count);
    }

    [TestMethod]
    public void Garbage_ReturnsNull()
        => Assert.IsNull(QrcParser().Parse(Encoding.UTF8.GetBytes("not a lyric at all")));
}
