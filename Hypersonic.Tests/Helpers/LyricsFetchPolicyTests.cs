using Hypersonic.Helpers;

namespace Hypersonic.Tests.Helpers;

[TestClass]
public class LyricsFetchPolicyTests
{
    [TestMethod]
    public void StructuredContent_ShortCircuits()
    {
        // Legacy isn't called in production when structured returns content; we
        // simulate by passing whatever for legacy.
        var r = LyricsFetchPolicy.Combine(
            LyricsOutcome.Content("hello"), LyricsOutcome.Transient);
        Assert.AreEqual("hello", r.Text);
        Assert.IsTrue(r.ShouldCache);
    }

    [TestMethod]
    public void StructuredEmpty_LegacyContent_CachesLegacyText()
    {
        var r = LyricsFetchPolicy.Combine(
            LyricsOutcome.DefinitiveEmpty, LyricsOutcome.Content("legacy"));
        Assert.AreEqual("legacy", r.Text);
        Assert.IsTrue(r.ShouldCache);
    }

    [TestMethod]
    public void StructuredEmpty_LegacyEmpty_CachesNull()
    {
        var r = LyricsFetchPolicy.Combine(LyricsOutcome.DefinitiveEmpty, LyricsOutcome.DefinitiveEmpty);
        Assert.IsNull(r.Text);
        Assert.IsTrue(r.ShouldCache);
    }

    [TestMethod]
    public void StructuredEmpty_LegacyUnsupported_CachesNull()
    {
        var r = LyricsFetchPolicy.Combine(LyricsOutcome.DefinitiveEmpty, LyricsOutcome.DefinitiveUnsupported);
        Assert.IsNull(r.Text);
        Assert.IsTrue(r.ShouldCache);
    }

    [TestMethod]
    public void StructuredEmpty_LegacyTransient_DoesNotCache()
    {
        var r = LyricsFetchPolicy.Combine(LyricsOutcome.DefinitiveEmpty, LyricsOutcome.Transient);
        Assert.IsNull(r.Text);
        Assert.IsFalse(r.ShouldCache);
    }

    [TestMethod]
    public void StructuredUnsupported_LegacyContent_CachesLegacy()
    {
        var r = LyricsFetchPolicy.Combine(LyricsOutcome.DefinitiveUnsupported, LyricsOutcome.Content("legacy"));
        Assert.AreEqual("legacy", r.Text);
        Assert.IsTrue(r.ShouldCache);
    }

    [TestMethod]
    public void StructuredUnsupported_LegacyTransient_DoesNotCache()
    {
        var r = LyricsFetchPolicy.Combine(LyricsOutcome.DefinitiveUnsupported, LyricsOutcome.Transient);
        Assert.IsFalse(r.ShouldCache);
    }

    [TestMethod]
    public void StructuredTransient_AnyLegacy_DoesNotCache()
    {
        Assert.IsFalse(LyricsFetchPolicy.Combine(LyricsOutcome.Transient, LyricsOutcome.Content("x")).ShouldCache);
        Assert.IsFalse(LyricsFetchPolicy.Combine(LyricsOutcome.Transient, LyricsOutcome.DefinitiveEmpty).ShouldCache);
        Assert.IsFalse(LyricsFetchPolicy.Combine(LyricsOutcome.Transient, LyricsOutcome.DefinitiveUnsupported).ShouldCache);
        Assert.IsFalse(LyricsFetchPolicy.Combine(LyricsOutcome.Transient, LyricsOutcome.Transient).ShouldCache);
    }

    [TestMethod]
    public void DefinitiveErrorCodes()
    {
        Assert.IsTrue(LyricsFetchPolicy.IsDefinitiveErrorCode(20));
        Assert.IsTrue(LyricsFetchPolicy.IsDefinitiveErrorCode(30));
        Assert.IsTrue(LyricsFetchPolicy.IsDefinitiveErrorCode(70));
        Assert.IsFalse(LyricsFetchPolicy.IsDefinitiveErrorCode(0));
        Assert.IsFalse(LyricsFetchPolicy.IsDefinitiveErrorCode(10));
    }

    [TestMethod]
    public void EmptyClassificationCode_OnlyForCode70()
    {
        Assert.IsTrue(LyricsFetchPolicy.IsEmptyClassificationCode(70));
        Assert.IsFalse(LyricsFetchPolicy.IsEmptyClassificationCode(20));
        Assert.IsFalse(LyricsFetchPolicy.IsEmptyClassificationCode(30));
    }

    [TestMethod]
    public void ClassifyFailedStatus_Code70_DefinitiveEmpty()
    {
        // Code 70 = "data not found": server is reachable, endpoint works,
        // it just has no lyrics for this track.
        var outcome = LyricsFetchPolicy.ClassifyFailedStatus(70);
        Assert.AreEqual(LyricsOutcomeKind.DefinitiveEmpty, outcome.Kind);
        Assert.IsNull(outcome.Text);
    }

    [TestMethod]
    public void ClassifyFailedStatus_Code30_DefinitiveUnsupported()
    {
        // Code 30 = "incompatible client": the structured endpoint isn't
        // available on this server.
        var outcome = LyricsFetchPolicy.ClassifyFailedStatus(30);
        Assert.AreEqual(LyricsOutcomeKind.DefinitiveUnsupported, outcome.Kind);
    }

    [TestMethod]
    public void ClassifyFailedStatus_Code20_DefinitiveUnsupported()
    {
        // Code 20 = "protocol mismatch": same family as 30 — endpoint not available.
        var outcome = LyricsFetchPolicy.ClassifyFailedStatus(20);
        Assert.AreEqual(LyricsOutcomeKind.DefinitiveUnsupported, outcome.Kind);
    }

    [TestMethod]
    public void ClassifyFailedStatus_GenericCodes_Transient()
    {
        Assert.AreEqual(LyricsOutcomeKind.Transient, LyricsFetchPolicy.ClassifyFailedStatus(0).Kind);
        Assert.AreEqual(LyricsOutcomeKind.Transient, LyricsFetchPolicy.ClassifyFailedStatus(10).Kind);
        Assert.AreEqual(LyricsOutcomeKind.Transient, LyricsFetchPolicy.ClassifyFailedStatus(40).Kind);
        Assert.AreEqual(LyricsOutcomeKind.Transient, LyricsFetchPolicy.ClassifyFailedStatus(50).Kind);
        Assert.AreEqual(LyricsOutcomeKind.Transient, LyricsFetchPolicy.ClassifyFailedStatus(60).Kind);
    }

    [TestMethod]
    public void Flatten_NullOrEmpty_ReturnsNull()
    {
        Assert.IsNull(LyricsFetchPolicy.FlattenStructured(null));
        Assert.IsNull(LyricsFetchPolicy.FlattenStructured(Array.Empty<Hypersonic.Client.Models.Entities.StructuredLyrics>()));
    }

    [TestMethod]
    public void Flatten_AllNullOrBlank_ReturnsNull()
    {
        var entries = new[]
        {
            new Hypersonic.Client.Models.Entities.StructuredLyrics
            {
                Line = new[]
                {
                    new Hypersonic.Client.Models.Entities.LyricLine { Value = null },
                    new Hypersonic.Client.Models.Entities.LyricLine { Value = "  " },
                }
            }
        };
        Assert.IsNull(LyricsFetchPolicy.FlattenStructured(entries));
    }

    [TestMethod]
    public void Flatten_FirstUsableEntryWins()
    {
        var entries = new[]
        {
            new Hypersonic.Client.Models.Entities.StructuredLyrics { Line = null },
            new Hypersonic.Client.Models.Entities.StructuredLyrics
            {
                Line = new[]
                {
                    new Hypersonic.Client.Models.Entities.LyricLine { Value = "first" },
                    new Hypersonic.Client.Models.Entities.LyricLine { Value = "" },
                    new Hypersonic.Client.Models.Entities.LyricLine { Value = "third" },
                }
            },
            new Hypersonic.Client.Models.Entities.StructuredLyrics
            {
                Line = new[] { new Hypersonic.Client.Models.Entities.LyricLine { Value = "skipped" } }
            }
        };
        Assert.AreEqual("first\nthird", LyricsFetchPolicy.FlattenStructured(entries));
    }
}
