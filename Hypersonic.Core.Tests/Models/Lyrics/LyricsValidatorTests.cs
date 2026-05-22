using Hypersonic.Core.Models.Lyrics;

namespace Hypersonic.Core.Tests.Models.Lyrics;

[TestClass]
public class LyricsValidatorTests
{
    [TestMethod]
    public void Line_NegativeStartMs_IsReportedAsIssue()
    {
        var doc = new LyricsDocument
        {
            SyncKind = LyricsSyncKind.Line,
            Lines = new[] { new LyricsLine { StartMs = -1, Text = "x" } },
        };

        var issues = LyricsValidator.Validate(doc);

        Assert.AreEqual(1, issues.Count);
        StringAssert.Contains(issues[0], "StartMs");
        StringAssert.Contains(issues[0], "negative");
    }

    [TestMethod]
    public void Line_EndBeforeStart_IsReportedAsIssue()
    {
        var doc = new LyricsDocument
        {
            SyncKind = LyricsSyncKind.Line,
            Lines = new[] { new LyricsLine { StartMs = 1000, EndMs = 500, Text = "x" } },
        };

        var issues = LyricsValidator.Validate(doc);

        Assert.AreEqual(1, issues.Count);
        StringAssert.Contains(issues[0], "EndMs");
    }

    [TestMethod]
    public void Lines_OutOfOrder_IsReportedAsIssue()
    {
        var doc = new LyricsDocument
        {
            SyncKind = LyricsSyncKind.Line,
            Lines = new[]
            {
                new LyricsLine { StartMs = 2000, Text = "second" },
                new LyricsLine { StartMs = 1000, Text = "first" },
            },
        };

        var issues = LyricsValidator.Validate(doc);

        Assert.IsTrue(issues.Any(i => i.Contains("monotonic", StringComparison.OrdinalIgnoreCase)),
            "Expected a monotonicity issue, got: " + string.Join("; ", issues));
    }

    [TestMethod]
    public void Lines_EqualStartMs_IsAllowed()
    {
        var doc = new LyricsDocument
        {
            SyncKind = LyricsSyncKind.Line,
            Lines = new[]
            {
                new LyricsLine { StartMs = 1000, Text = "a" },
                new LyricsLine { StartMs = 1000, Text = "b" },
            },
        };

        Assert.AreEqual(0, LyricsValidator.Validate(doc).Count);
    }

    [TestMethod]
    public void Word_EndBeforeStart_IsReportedAsIssue()
    {
        var doc = new LyricsDocument
        {
            SyncKind = LyricsSyncKind.Word,
            Lines = new[]
            {
                new LyricsLine
                {
                    StartMs = 1000,
                    EndMs = 2000,
                    Text = "x",
                    Words = new[] { new LyricsWord { StartMs = 1500, EndMs = 1200, Text = "x" } },
                },
            },
        };

        var issues = LyricsValidator.Validate(doc);

        Assert.IsTrue(issues.Any(i => i.Contains("Word", StringComparison.Ordinal)
                                      && i.Contains("EndMs", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Words_OutOfOrder_IsReportedAsIssue()
    {
        var doc = new LyricsDocument
        {
            SyncKind = LyricsSyncKind.Word,
            Lines = new[]
            {
                new LyricsLine
                {
                    StartMs = 1000,
                    EndMs = 3000,
                    Text = "a b",
                    Words = new[]
                    {
                        new LyricsWord { StartMs = 2000, EndMs = 2500, Text = "b" },
                        new LyricsWord { StartMs = 1000, EndMs = 1500, Text = "a" },
                    },
                },
            },
        };

        var issues = LyricsValidator.Validate(doc);

        Assert.IsTrue(issues.Any(i => i.Contains("Word", StringComparison.Ordinal)
                                      && i.Contains("monotonic", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void Word_OutsideLineBounds_IsReportedAsIssue()
    {
        var doc = new LyricsDocument
        {
            SyncKind = LyricsSyncKind.Word,
            Lines = new[]
            {
                new LyricsLine
                {
                    StartMs = 1000,
                    EndMs = 2000,
                    Text = "x",
                    Words = new[] { new LyricsWord { StartMs = 1500, EndMs = 2500, Text = "x" } },
                },
            },
        };

        var issues = LyricsValidator.Validate(doc);

        Assert.IsTrue(issues.Any(i => i.Contains("outside line bounds", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void Word_OutsideLineBounds_NotCheckedWhenLineEndMsIsNull()
    {
        var doc = new LyricsDocument
        {
            SyncKind = LyricsSyncKind.Word,
            Lines = new[]
            {
                new LyricsLine
                {
                    StartMs = 1000,
                    EndMs = null,
                    Text = "x",
                    Words = new[] { new LyricsWord { StartMs = 1500, EndMs = 9999, Text = "x" } },
                },
            },
        };

        Assert.AreEqual(0, LyricsValidator.Validate(doc).Count);
    }

    [TestMethod]
    public void SyncKindWord_LineWithoutWords_IsReportedAsIssue()
    {
        var doc = new LyricsDocument
        {
            SyncKind = LyricsSyncKind.Word,
            Lines = new[] { new LyricsLine { StartMs = 0, Text = "x" } },
        };

        var issues = LyricsValidator.Validate(doc);

        Assert.IsTrue(issues.Any(i => i.Contains("Word", StringComparison.Ordinal)
                                      && i.Contains("requires", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void SyncKindLine_LineWithWords_IsReportedAsIssue()
    {
        var doc = new LyricsDocument
        {
            SyncKind = LyricsSyncKind.Line,
            Lines = new[]
            {
                new LyricsLine
                {
                    StartMs = 0,
                    EndMs = 1000,
                    Text = "x",
                    Words = new[] { new LyricsWord { StartMs = 0, EndMs = 1000, Text = "x" } },
                },
            },
        };

        var issues = LyricsValidator.Validate(doc);

        Assert.IsTrue(issues.Any(i => i.Contains("Line", StringComparison.Ordinal)
                                      && i.Contains("must not", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void SyncKindPlain_LineWithStartMs_IsReportedAsIssue()
    {
        var doc = new LyricsDocument
        {
            SyncKind = LyricsSyncKind.Plain,
            Lines = new[] { new LyricsLine { StartMs = 1000, Text = "x" } },
        };

        var issues = LyricsValidator.Validate(doc);

        Assert.IsTrue(issues.Any(i => i.Contains("Plain", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void SyncKindPlain_LineWithWords_IsReportedAsIssue()
    {
        var doc = new LyricsDocument
        {
            SyncKind = LyricsSyncKind.Plain,
            Lines = new[]
            {
                new LyricsLine
                {
                    StartMs = 0,
                    Text = "x",
                    Words = new[] { new LyricsWord { StartMs = 0, EndMs = 0, Text = "x" } },
                },
            },
        };

        var issues = LyricsValidator.Validate(doc);

        Assert.IsTrue(issues.Any(i => i.Contains("Plain", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void SyncKindNone_AnyLine_IsReportedAsIssue()
    {
        var doc = new LyricsDocument
        {
            SyncKind = LyricsSyncKind.None,
            Lines = new[] { new LyricsLine { StartMs = 0, Text = "x" } },
        };

        var issues = LyricsValidator.Validate(doc);

        Assert.IsTrue(issues.Any(i => i.Contains("None", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void HappyPath_WordSyncedDocument_HasNoIssues()
    {
        var doc = new LyricsDocument
        {
            SyncKind = LyricsSyncKind.Word,
            LanguageCode = "en",
            SourceProvider = "amll-ttml-db",
            Lines = new[]
            {
                new LyricsLine
                {
                    StartMs = 1000, EndMs = 3000, Text = "hello world",
                    Words = new[]
                    {
                        new LyricsWord { StartMs = 1000, EndMs = 1500, Text = "hello " },
                        new LyricsWord { StartMs = 1500, EndMs = 3000, Text = "world" },
                    },
                },
            },
        };

        Assert.AreEqual(0, LyricsValidator.Validate(doc).Count);
    }

    [TestMethod]
    public void HappyPath_LineSyncedDocument_HasNoIssues()
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

        Assert.AreEqual(0, LyricsValidator.Validate(doc).Count);
    }

    [TestMethod]
    public void HappyPath_PlainDocument_HasNoIssues()
    {
        var doc = new LyricsDocument
        {
            SyncKind = LyricsSyncKind.Plain,
            Lines = new[]
            {
                new LyricsLine { Text = "line one" },
                new LyricsLine { Text = "line two" },
            },
        };

        Assert.AreEqual(0, LyricsValidator.Validate(doc).Count);
    }

    [TestMethod]
    public void HappyPath_NoneDocument_HasNoIssues()
    {
        Assert.AreEqual(0, LyricsValidator.Validate(LyricsDocument.Empty()).Count);
    }
}
