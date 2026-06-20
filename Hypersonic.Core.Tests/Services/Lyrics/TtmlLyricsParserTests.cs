using System.Text;

using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Core.Services;
using Hypersonic.Core.Services.Lyrics;

namespace Hypersonic.Core.Tests.Services.Lyrics;

[TestClass]
public class TtmlLyricsParserTests
{
    private static TtmlLyricsParser Parser() => new(new LoggingService());
    private static LyricsDocument? Parse(string ttml) => Parser().Parse(Encoding.UTF8.GetBytes(ttml));

    private const string TwoLineWordSynced =
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <tt xmlns="http://www.w3.org/ns/ttml" xmlns:ttm="http://www.w3.org/ns/ttml#metadata">
          <body>
            <div>
              <p begin="00:00.500" end="00:03.000">
                <span begin="00:00.500" end="00:01.000">Hello</span>
                <span begin="00:01.000" end="00:02.000"> world</span>
              </p>
              <p begin="00:03.000" end="00:06.000">
                <span begin="00:03.000" end="00:04.000">Second</span>
                <span begin="00:04.000" end="00:06.000"> line</span>
              </p>
            </div>
          </body>
        </tt>
        """;

    [TestMethod]
    public void FormatTag_IsTtml()
        => Assert.AreEqual("ttml", Parser().FormatTag);

    [TestMethod]
    public void WordSynced_ProducesWordDocument_WithLinesAndWords()
    {
        var doc = Parse(TwoLineWordSynced);

        Assert.IsNotNull(doc);
        Assert.AreEqual(LyricsSyncKind.Word, doc!.SyncKind);
        Assert.AreEqual(2, doc.Lines.Count);

        var first = doc.Lines[0];
        Assert.AreEqual(500, first.StartMs);
        Assert.AreEqual(3000, first.EndMs);
        Assert.AreEqual(2, first.Words.Count);
        Assert.AreEqual("Hello", first.Words[0].Text);
        Assert.AreEqual(500, first.Words[0].StartMs);
        Assert.AreEqual(1000, first.Words[0].EndMs);
        Assert.AreEqual(" world", first.Words[1].Text);
        Assert.AreEqual(2000, first.Words[1].EndMs);
    }

    [TestMethod]
    public void LineText_ConcatenatesWordSpans_PreservingInterWordSpaces()
    {
        var doc = Parse(TwoLineWordSynced);
        Assert.AreEqual("Hello world", doc!.Lines[0].Text);
        Assert.AreEqual("Second line", doc.Lines[1].Text);
    }

    [TestMethod]
    public void SourceProvider_IsTtml()
        => Assert.AreEqual("ttml", Parse(TwoLineWordSynced)!.SourceProvider);

    [TestMethod]
    public void TimeFormat_HoursMinutesSeconds_Parsed()
    {
        var doc = Parse(
            """
            <tt xmlns="http://www.w3.org/ns/ttml">
              <body><div>
                <p begin="01:02:03.250" end="01:02:05.000">
                  <span begin="01:02:03.250" end="01:02:05.000">x</span>
                </p>
              </div></body>
            </tt>
            """);

        // 1h 2m 3.25s = 3_600_000 + 120_000 + 3_250 = 3_723_250
        Assert.AreEqual(3_723_250, doc!.Lines[0].StartMs);
    }

    [TestMethod]
    public void AuxiliarySpans_TranslationRomanBackground_AreExcludedFromPrimary()
    {
        var doc = Parse(
            """
            <tt xmlns="http://www.w3.org/ns/ttml" xmlns:ttm="http://www.w3.org/ns/ttml#metadata">
              <body><div>
                <p begin="00:00.000" end="00:02.000">
                  <span begin="00:00.000" end="00:01.000">main</span>
                  <span begin="00:01.000" end="00:02.000"> word</span>
                  <span ttm:role="x-bg" begin="00:00.000" end="00:02.000">(background)</span>
                  <span ttm:role="x-translation">translated</span>
                  <span ttm:role="x-roman">romanized</span>
                </p>
              </div></body>
            </tt>
            """);

        Assert.AreEqual(1, doc!.Lines.Count);
        Assert.AreEqual(2, doc.Lines[0].Words.Count);
        Assert.AreEqual("main word", doc.Lines[0].Text);
    }

    [TestMethod]
    public void LineWithTextButNoTimedSpans_SynthesizesOneWord_SpanningLine()
    {
        var doc = Parse(
            """
            <tt xmlns="http://www.w3.org/ns/ttml">
              <body><div>
                <p begin="00:01.000" end="00:04.000">plain timed line</p>
              </div></body>
            </tt>
            """);

        Assert.IsNotNull(doc);
        Assert.AreEqual(LyricsSyncKind.Word, doc!.SyncKind);
        Assert.AreEqual(1, doc.Lines.Count);
        Assert.AreEqual("plain timed line", doc.Lines[0].Text);
        Assert.AreEqual(1, doc.Lines[0].Words.Count, "a line without word spans gets one synthetic word covering it");
        Assert.AreEqual(1000, doc.Lines[0].Words[0].StartMs);
        Assert.AreEqual(4000, doc.Lines[0].Words[0].EndMs);
    }

    [TestMethod]
    public void NonMonotonicWordStart_IsNormalized_NotRejected()
    {
        // Real AMLL community files carry small word-start wobbles (tens of ms) and zero-duration
        // syllables. The parser must normalize them into a clean monotonic document rather than discard
        // the whole song. This reproduces the Blinding Lights line 6: "can" begins 59 ms before the
        // zero-duration "You".
        var doc = Parse(
            """
            <tt xmlns="http://www.w3.org/ns/ttml">
              <body><div>
                <p begin="44.162" end="48.303">
                  <span begin="44.162" end="44.162">You</span>
                  <span begin="44.103" end="44.345">can</span>
                  <span begin="44.345" end="44.700">turn</span>
                </p>
              </div></body>
            </tt>
            """);

        Assert.IsNotNull(doc, "a small non-monotonic word start must be normalized, not rejected");
        Assert.AreEqual(LyricsSyncKind.Word, doc!.SyncKind);

        var words = doc.Lines[0].Words;
        Assert.AreEqual(3, words.Count);
        Assert.AreEqual("can", words[1].Text);
        Assert.IsTrue(words[1].StartMs >= words[0].StartMs, "word starts are monotonic after normalization");
        Assert.IsTrue(doc.Lines[0].Words[1].StartMs >= doc.Lines[0].StartMs, "every word stays within the line bounds");
    }

    [TestMethod]
    public void NoBody_ReturnsNull()
        => Assert.IsNull(Parse("""<tt xmlns="http://www.w3.org/ns/ttml"><head/></tt>"""));

    [TestMethod]
    public void NoTimedLines_ReturnsNull()
        => Assert.IsNull(Parse(
            """
            <tt xmlns="http://www.w3.org/ns/ttml"><body><div></div></body></tt>
            """));

    [TestMethod]
    public void Garbage_ReturnsNull()
        => Assert.IsNull(Parse("this is not xml at all <<<"));

    [TestMethod]
    public void Empty_ReturnsNull()
        => Assert.IsNull(Parse(string.Empty));
}
