using System.Text;
using System.Text.Json;

using Hypersonic.Client.Models.Entities;
using Hypersonic.Core.Helpers;
using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Core.Services;
using Hypersonic.Services.Lyrics;

namespace Hypersonic.Tests.Services.Lyrics;

[TestClass]
public class SubsonicLyricsParsersTests
{
    private static SubsonicStructuredLyricsParser StructuredParser()
        => new(new LoggingService());

    private static SubsonicLegacyLyricsParser LegacyParser()
        => new(new LoggingService());

    private static byte[] SerializeStructured(StructuredLyrics entry)
        => JsonSerializer.SerializeToUtf8Bytes(entry, Json.CompactOptions);

    private static LyricsDocument? ParseStructured(byte[] payload)
        => StructuredParser().Parse(payload);

    private static LyricsDocument? ParseLegacy(string text)
        => LegacyParser().Parse(Encoding.UTF8.GetBytes(text));

    // ---- Structured: synced -> Line ----

    [TestMethod]
    public void Structured_SyncedWithValidStarts_ProducesLineDocument()
    {
        var entry = new StructuredLyrics
        {
            Lang = "en",
            Offset = -250,
            Synced = true,
            Line =
            [
                new LyricLine { Start = 0, Value = "first" },
                new LyricLine { Start = 1500, Value = "second" },
            ],
        };

        var doc = ParseStructured(SerializeStructured(entry));

        Assert.IsNotNull(doc);
        Assert.AreEqual(LyricsSyncKind.Line, doc!.SyncKind);
        Assert.AreEqual("en", doc.LanguageCode);
        Assert.AreEqual(-250, doc.OffsetMs);
        Assert.AreEqual("subsonic", doc.SourceProvider);
        Assert.AreEqual(2, doc.Lines.Count);
        Assert.AreEqual(0, doc.Lines[0].StartMs);
        Assert.AreEqual("first", doc.Lines[0].Text);
        Assert.AreEqual(1500, doc.Lines[1].StartMs);
        Assert.AreEqual("second", doc.Lines[1].Text);
        Assert.AreEqual(0, doc.Lines[0].Words.Count, "Subsonic structured never carries word timing.");
    }

    [TestMethod]
    public void Structured_SyncedKeepsTimedBlankLineAsGap()
    {
        var entry = new StructuredLyrics
        {
            Synced = true,
            Line =
            [
                new LyricLine { Start = 0, Value = "a" },
                new LyricLine { Start = 1000, Value = "" },
                new LyricLine { Start = 2000, Value = "b" },
            ],
        };

        var doc = ParseStructured(SerializeStructured(entry));

        Assert.AreEqual(LyricsSyncKind.Line, doc!.SyncKind);
        Assert.AreEqual(3, doc.Lines.Count);
        Assert.AreEqual("", doc.Lines[1].Text);
        Assert.AreEqual(1000, doc.Lines[1].StartMs);
    }

    // ---- Structured: synced-with-bad-timing -> Plain downgrade ----

    [TestMethod]
    public void Structured_SyncedButNullStart_DowngradesToPlain()
    {
        var entry = new StructuredLyrics
        {
            Synced = true,
            Line =
            [
                new LyricLine { Start = 0, Value = "a" },
                new LyricLine { Start = null, Value = "b" },
            ],
        };

        var doc = ParseStructured(SerializeStructured(entry));

        Assert.AreEqual(LyricsSyncKind.Plain, doc!.SyncKind);
        Assert.AreEqual(2, doc.Lines.Count);
        Assert.IsTrue(doc.Lines.All(l => l.StartMs == 0));
        Assert.AreEqual("a", doc.Lines[0].Text);
        Assert.AreEqual("b", doc.Lines[1].Text);
    }

    [TestMethod]
    public void Structured_SyncedButNonMonotonicStarts_DowngradesToPlain()
    {
        var entry = new StructuredLyrics
        {
            Synced = true,
            Line =
            [
                new LyricLine { Start = 1000, Value = "a" },
                new LyricLine { Start = 500, Value = "b" },
            ],
        };

        var doc = ParseStructured(SerializeStructured(entry));

        Assert.AreEqual(LyricsSyncKind.Plain, doc!.SyncKind);
    }

    [TestMethod]
    public void Structured_SyncedButNegativeStart_DowngradesToPlain()
    {
        var entry = new StructuredLyrics
        {
            Synced = true,
            Line = [new LyricLine { Start = -1, Value = "a" }],
        };

        var doc = ParseStructured(SerializeStructured(entry));

        Assert.AreEqual(LyricsSyncKind.Plain, doc!.SyncKind);
    }

    // ---- Structured: offset survives a Plain document (synced or downgraded) ----

    [TestMethod]
    public void Structured_DowngradedToPlain_PreservesOffset()
    {
        // A null Start forces the synced→Plain downgrade. The document-level offset is metadata that
        // the server still sent; it must survive the downgrade (design doc: offset→offset for the format).
        var entry = new StructuredLyrics
        {
            Synced = true,
            Offset = -120,
            Line =
            [
                new LyricLine { Start = 0, Value = "a" },
                new LyricLine { Start = null, Value = "b" },
            ],
        };

        var doc = ParseStructured(SerializeStructured(entry));

        Assert.AreEqual(LyricsSyncKind.Plain, doc!.SyncKind);
        Assert.AreEqual(-120, doc.OffsetMs);
    }

    [TestMethod]
    public void Structured_NotSynced_PreservesOffset()
    {
        var entry = new StructuredLyrics
        {
            Synced = false,
            Offset = 400,
            Line = [new LyricLine { Value = "a" }],
        };

        var doc = ParseStructured(SerializeStructured(entry));

        Assert.AreEqual(LyricsSyncKind.Plain, doc!.SyncKind);
        Assert.AreEqual(400, doc.OffsetMs, "offset maps to OffsetMs for plain documents too, not only synced ones.");
    }

    // ---- Structured: not synced -> Plain ----

    [TestMethod]
    public void Structured_NotSynced_ProducesPlainDroppingBlankValues()
    {
        var entry = new StructuredLyrics
        {
            Synced = false,
            Line =
            [
                new LyricLine { Value = "a" },
                new LyricLine { Value = "  " },
                new LyricLine { Value = "b" },
            ],
        };

        var doc = ParseStructured(SerializeStructured(entry));

        Assert.AreEqual(LyricsSyncKind.Plain, doc!.SyncKind);
        Assert.AreEqual(2, doc.Lines.Count);
        Assert.AreEqual("a", doc.Lines[0].Text);
        Assert.AreEqual("b", doc.Lines[1].Text);
    }

    [TestMethod]
    public void Structured_NoUsableLines_ReturnsNull()
    {
        var entry = new StructuredLyrics { Synced = false, Line = [new LyricLine { Value = " " }] };

        Assert.IsNull(ParseStructured(SerializeStructured(entry)));
    }

    [TestMethod]
    public void Structured_EmptyLineArray_ReturnsNull()
    {
        var entry = new StructuredLyrics { Synced = true, Line = [] };

        Assert.IsNull(ParseStructured(SerializeStructured(entry)));
    }

    [TestMethod]
    public void Structured_InvalidJson_ReturnsNull()
    {
        Assert.IsNull(ParseStructured(Encoding.UTF8.GetBytes("not json at all")));
    }

    [TestMethod]
    public void Structured_RoundTripsThroughSerialization()
    {
        var entry = new StructuredLyrics
        {
            Lang = "ja",
            Synced = true,
            Line =
            [
                new LyricLine { Start = 100, Value = "い" },
                new LyricLine { Start = 200, Value = "ろ" },
            ],
        };

        var doc = ParseStructured(SerializeStructured(entry));

        Assert.AreEqual(LyricsSyncKind.Line, doc!.SyncKind);
        Assert.AreEqual("ja", doc.LanguageCode);
        Assert.AreEqual("い", doc.Lines[0].Text);
        Assert.AreEqual(200, doc.Lines[1].StartMs);
    }

    // ---- Legacy: text -> Plain ----

    [TestMethod]
    public void Legacy_Text_ProducesPlainOneLinePerTextLine()
    {
        var doc = ParseLegacy("line one\r\nline two\n\nline four\n");

        Assert.IsNotNull(doc);
        Assert.AreEqual(LyricsSyncKind.Plain, doc!.SyncKind);
        Assert.AreEqual("subsonic", doc.SourceProvider);
        Assert.AreEqual(4, doc.Lines.Count);
        Assert.AreEqual("line one", doc.Lines[0].Text);
        Assert.AreEqual("line two", doc.Lines[1].Text);
        Assert.AreEqual("", doc.Lines[2].Text, "internal blank line is kept as a stanza break");
        Assert.AreEqual("line four", doc.Lines[3].Text);
        Assert.IsTrue(doc.Lines.All(l => l.StartMs == 0));
    }

    [TestMethod]
    public void Legacy_BlankText_ReturnsNull()
    {
        Assert.IsNull(ParseLegacy("   \n  \n"));
    }
}
