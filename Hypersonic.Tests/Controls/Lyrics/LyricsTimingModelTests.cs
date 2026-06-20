using Hypersonic.Controls.Lyrics;
using Hypersonic.Core.Models.Lyrics;

namespace Hypersonic.Tests.Controls.Lyrics;

[TestClass]
public class LyricsTimingModelTests
{
    private static LyricsDocument WordDoc(int? offset = null, params (int start, int? end, string text, (int s, int e, string t)[] words)[] lines)
    {
        var built = new List<LyricsLine>();
        foreach (var (start, end, text, words) in lines)
        {
            built.Add(new LyricsLine
            {
                StartMs = start,
                EndMs = end,
                Text = text,
                Words = words.Select(w => new LyricsWord { StartMs = w.s, EndMs = w.e, Text = w.t }).ToList(),
            });
        }

        return new LyricsDocument { SyncKind = LyricsSyncKind.Word, Lines = built, OffsetMs = offset };
    }

    [TestMethod]
    public void WordDoc_KeepsWords_AndIsNotStatic()
    {
        var doc = WordDoc(null,
            (0, 2000, "hi there", [(0, 1000, "hi"), (1000, 2000, " there")]));

        var model = LyricsTimingModel.Build(doc, trackDurationMs: null);

        Assert.IsFalse(model.IsStatic);
        Assert.AreEqual(1, model.Lines.Count);
        Assert.AreEqual(2, model.Lines[0].Words.Count);
        Assert.AreEqual(2000, model.Lines[0].EndMs);
    }

    [TestMethod]
    public void Offset_IsFoldedIntoLineAndWordTimings()
    {
        var doc = WordDoc(offset: 250,
            (1000, 2000, "a", [(1000, 2000, "a")]));

        var model = LyricsTimingModel.Build(doc, trackDurationMs: null);

        Assert.AreEqual(1250, model.Lines[0].StartMs);
        Assert.AreEqual(2250, model.Lines[0].EndMs);
        Assert.AreEqual(1250, model.Lines[0].Words[0].StartMs);
        Assert.AreEqual(2250, model.Lines[0].Words[0].EndMs);
    }

    [TestMethod]
    public void NegativeOffset_ShiftsTimingsEarlier()
    {
        var doc = WordDoc(offset: -300,
            (1000, 2000, "a", [(1000, 2000, "a")]));

        var model = LyricsTimingModel.Build(doc, trackDurationMs: null);

        Assert.AreEqual(700, model.Lines[0].StartMs);
    }

    [TestMethod]
    public void MiddleLineEnd_FallsBackToNextLineStart_WhenNoEndMs()
    {
        var doc = WordDoc(null,
            (0, null, "first", [(0, 900, "first")]),
            (1500, 3000, "second", [(1500, 3000, "second")]));

        var model = LyricsTimingModel.Build(doc, trackDurationMs: null);

        Assert.AreEqual(1500, model.Lines[0].EndMs, "a missing end resolves to the next line's start");
    }

    [TestMethod]
    public void FinalLineEnd_UsesTrackDuration_WhenNoEndMs()
    {
        var doc = WordDoc(null,
            (1000, null, "only", [(1000, 2000, "only")]));

        var model = LyricsTimingModel.Build(doc, trackDurationMs: 9000);

        Assert.AreEqual(9000, model.Lines[0].EndMs);
    }

    [TestMethod]
    public void FinalLineEnd_UsesTail_WhenNoEndMsAndNoDuration()
    {
        var doc = WordDoc(null,
            (1000, null, "only", [(1000, 2000, "only")]));

        var model = LyricsTimingModel.Build(doc, trackDurationMs: null, defaultTailMs: 4000);

        Assert.AreEqual(5000, model.Lines[0].EndMs, "final line tail = start + default tail");
    }

    [TestMethod]
    public void LineOnly_SynthesizesOneWordSpanningTheLine()
    {
        var doc = new LyricsDocument
        {
            SyncKind = LyricsSyncKind.Line,
            Lines = new[]
            {
                new LyricsLine { StartMs = 0, Text = "first" },
                new LyricsLine { StartMs = 2000, Text = "second" },
            },
        };

        var model = LyricsTimingModel.Build(doc, trackDurationMs: 5000);

        Assert.IsFalse(model.IsStatic);
        Assert.AreEqual(1, model.Lines[0].Words.Count);
        Assert.AreEqual(0, model.Lines[0].Words[0].StartMs);
        Assert.AreEqual(2000, model.Lines[0].Words[0].EndMs, "synthetic word spans to the resolved line end");
        Assert.AreEqual("first", model.Lines[0].Words[0].Text);
    }

    [TestMethod]
    public void Plain_IsStatic()
    {
        var doc = new LyricsDocument
        {
            SyncKind = LyricsSyncKind.Plain,
            Lines = new[] { new LyricsLine { StartMs = 0, Text = "line one" } },
        };

        var model = LyricsTimingModel.Build(doc, trackDurationMs: null);

        Assert.IsTrue(model.IsStatic);
        Assert.AreEqual(1, model.Lines.Count);
        Assert.AreEqual("line one", model.Lines[0].Text);
    }

    [TestMethod]
    public void None_OrEmpty_ProducesNoLines()
    {
        var doc = LyricsDocument.Empty();
        var model = LyricsTimingModel.Build(doc, trackDurationMs: null);
        Assert.AreEqual(0, model.Lines.Count);
    }
}
