using System.Text.Json;
using Hypersonic.Core.Helpers;
using Hypersonic.Core.Models.Lyrics;

namespace Hypersonic.Core.Tests.Models.Lyrics;

[TestClass]
public class LyricsDocumentTests
{
    [TestMethod]
    public void Construct_StoresAllFields()
    {
        var doc = new LyricsDocument
        {
            SyncKind = LyricsSyncKind.Line,
            LanguageCode = "en",
            SourceProvider = "subsonic",
            OffsetMs = -200,
            Lines = new[]
            {
                new LyricsLine { StartMs = 0, Text = "first" },
                new LyricsLine { StartMs = 1000, Text = "second" },
            },
        };

        Assert.AreEqual(LyricsSyncKind.Line, doc.SyncKind);
        Assert.AreEqual("en", doc.LanguageCode);
        Assert.AreEqual("subsonic", doc.SourceProvider);
        Assert.AreEqual(-200, doc.OffsetMs);
        Assert.AreEqual(2, doc.Lines.Count);
    }

    [TestMethod]
    public void DefaultsAreSafe()
    {
        var doc = new LyricsDocument();

        Assert.AreEqual(LyricsSyncKind.None, doc.SyncKind);
        Assert.IsNull(doc.LanguageCode);
        Assert.IsNull(doc.SourceProvider);
        Assert.IsNull(doc.OffsetMs);
        Assert.AreEqual(0, doc.Lines.Count);
    }

    [TestMethod]
    public void Lines_ExposedAsReadOnlyList()
    {
        var doc = new LyricsDocument
        {
            Lines = new[] { new LyricsLine { StartMs = 0, Text = "x" } },
        };

        IReadOnlyList<LyricsLine> ro = doc.Lines;
        Assert.AreEqual(1, ro.Count);
    }

    [TestMethod]
    public void Lines_SnapshotsTheInputEnumerable()
    {
        var mutable = new List<LyricsLine>
        {
            new() { StartMs = 0, Text = "a" },
        };
        var doc = new LyricsDocument { Lines = mutable };

        mutable.Add(new LyricsLine { StartMs = 1000, Text = "b" });

        Assert.AreEqual(1, doc.Lines.Count);
    }

    [TestMethod]
    public void Empty_IsValidNoneDocument()
    {
        var doc = LyricsDocument.Empty(sourceProvider: "lrclib");

        Assert.AreEqual(LyricsSyncKind.None, doc.SyncKind);
        Assert.AreEqual("lrclib", doc.SourceProvider);
        Assert.AreEqual(0, doc.Lines.Count);
    }

    [TestMethod]
    public void RoundTripsThroughJson_WithSyncKindAsString()
    {
        var doc = new LyricsDocument
        {
            SyncKind = LyricsSyncKind.Word,
            LanguageCode = "ja",
            SourceProvider = "amll-ttml-db",
            OffsetMs = 0,
            Lines = new[]
            {
                new LyricsLine
                {
                    StartMs = 1000,
                    EndMs = 2000,
                    Text = "hi",
                    Words = new[] { new LyricsWord { StartMs = 1000, EndMs = 2000, Text = "hi" } },
                },
            },
        };

        var json = JsonSerializer.Serialize(doc, Json.Options);
        StringAssert.Contains(json, "\"Word\"", "SyncKind must serialize as a string for readable cache files.");

        var back = JsonSerializer.Deserialize<LyricsDocument>(json, Json.Options);
        Assert.IsNotNull(back);
        Assert.AreEqual(LyricsSyncKind.Word, back!.SyncKind);
        Assert.AreEqual("ja", back.LanguageCode);
        Assert.AreEqual("amll-ttml-db", back.SourceProvider);
        Assert.AreEqual(1, back.Lines.Count);
        Assert.AreEqual(1, back.Lines[0].Words.Count);
    }

    [TestMethod]
    public void Format_DefaultsToUnknown()
        => Assert.AreEqual(LyricsFormat.Unknown, new LyricsDocument().Format);

    [TestMethod]
    public void WithSourceProvider_SetsProvider_PreservesEverythingElse()
    {
        var original = new LyricsDocument
        {
            SyncKind = LyricsSyncKind.Line,
            Format = LyricsFormat.Lrc,
            LanguageCode = "en",
            OffsetMs = -200,
            Lines = new[] { new LyricsLine { StartMs = 0, Text = "hi" } },
        };

        var stamped = original.WithSourceProvider("lrclib");

        Assert.AreEqual("lrclib", stamped.SourceProvider);
        Assert.AreEqual(LyricsFormat.Lrc, stamped.Format);
        Assert.AreEqual(LyricsSyncKind.Line, stamped.SyncKind);
        Assert.AreEqual("en", stamped.LanguageCode);
        Assert.AreEqual(-200, stamped.OffsetMs);
        Assert.AreEqual(1, stamped.Lines.Count);
        Assert.IsNull(original.SourceProvider); // original is untouched
    }
}
