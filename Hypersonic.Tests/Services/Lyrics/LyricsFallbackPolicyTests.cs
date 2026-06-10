using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Services.Lyrics;

namespace Hypersonic.Tests.Services.Lyrics;

[TestClass]
public class LyricsFallbackPolicyTests
{
    private static LyricsProviderResult Content(LyricsSyncKind kind, bool definitive, string? source = null)
    {
        var lines = kind == LyricsSyncKind.None
            ? Array.Empty<LyricsLine>()
            : new[] { new LyricsLine { StartMs = 0, Text = "x" } };
        var doc = new LyricsDocument { SyncKind = kind, Lines = lines, SourceProvider = source };
        return LyricsProviderResult.Content(doc, new byte[] { 1 }, "fmt", definitive);
    }

    [TestMethod]
    public void SingleDefinitiveContent_WritesPositive()
    {
        var decision = LyricsFallbackPolicy.Decide([Content(LyricsSyncKind.Plain, definitive: true)]);

        Assert.AreEqual(LyricsCacheAction.WritePositive, decision.Action);
        Assert.IsNotNull(decision.Winner);
        Assert.AreEqual(LyricsSyncKind.Plain, decision.Winner!.Document!.SyncKind);
    }

    [TestMethod]
    public void SingleNonDefinitiveContent_ShownButNotCached()
    {
        var decision = LyricsFallbackPolicy.Decide([Content(LyricsSyncKind.Plain, definitive: false)]);

        Assert.AreEqual(LyricsCacheAction.None, decision.Action);
        Assert.IsNotNull(decision.Winner, "non-definitive content is still shown");
    }

    [TestMethod]
    public void PrefersHigherSyncTier()
    {
        var decision = LyricsFallbackPolicy.Decide(
        [
            Content(LyricsSyncKind.Plain, definitive: true, source: "plain"),
            Content(LyricsSyncKind.Line, definitive: true, source: "line"),
        ]);

        Assert.AreEqual("line", decision.Winner!.Document!.SourceProvider);
    }

    [TestMethod]
    public void EqualTier_UsesRegistrationOrderTiebreak()
    {
        var decision = LyricsFallbackPolicy.Decide(
        [
            Content(LyricsSyncKind.Line, definitive: true, source: "first"),
            Content(LyricsSyncKind.Line, definitive: true, source: "second"),
        ]);

        Assert.AreEqual("first", decision.Winner!.Document!.SourceProvider);
    }

    [TestMethod]
    public void ContentWins_EvenWhenAnotherProviderWasTransient()
    {
        var decision = LyricsFallbackPolicy.Decide(
        [
            LyricsProviderResult.Transient,
            Content(LyricsSyncKind.Plain, definitive: true),
        ]);

        Assert.AreEqual(LyricsCacheAction.WritePositive, decision.Action);
    }

    [TestMethod]
    public void AllDefinitiveNoContent_WritesNegative()
    {
        var decision = LyricsFallbackPolicy.Decide(
        [
            LyricsProviderResult.DefinitiveEmpty,
            LyricsProviderResult.DefinitiveUnsupported,
        ]);

        Assert.AreEqual(LyricsCacheAction.WriteNegative, decision.Action);
        Assert.IsNull(decision.Winner);
    }

    [TestMethod]
    public void AnyTransientNoContent_BlocksNegativeWrite()
    {
        var decision = LyricsFallbackPolicy.Decide(
        [
            LyricsProviderResult.DefinitiveEmpty,
            LyricsProviderResult.Transient,
        ]);

        Assert.AreEqual(LyricsCacheAction.None, decision.Action);
        Assert.IsNull(decision.Winner);
    }

    [TestMethod]
    public void EmptyResults_None()
    {
        var decision = LyricsFallbackPolicy.Decide([]);

        Assert.AreEqual(LyricsCacheAction.None, decision.Action);
        Assert.IsNull(decision.Winner);
    }
}
