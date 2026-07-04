using System.Text;

using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Core.Services;
using Hypersonic.Core.Services.Lyrics;
using Hypersonic.Models;
using Hypersonic.Services.Lyrics;

using Lyricify.Lyrics.Models;

using Microsoft.Extensions.Options;

namespace Hypersonic.Tests.Services.Lyrics;

[TestClass]
public class LyricifyLyricsProviderTests
{
    private sealed class FakeClient : ILyricifyLyricsClient
    {
        public LyricifyLyricsResult? Result;
        public Exception? Throw;
        public Task<LyricifyLyricsResult?> FetchAsync(LyricifyPlatform platform, LyricsQuery query, CancellationToken ct)
            => Throw is not null ? Task.FromException<LyricifyLyricsResult?>(Throw) : Task.FromResult(Result);
    }

    private static LyricsQuery Query => new("s1", Artist: "The Weeknd", Title: "Blinding Lights",
        Album: "After Hours", DurationSeconds: 200);

    private static (QQMusicLyricsProvider Provider, FakeClient Client) NewProvider(int threshold = 55, bool enabled = true)
    {
        var log = new LoggingService();
        var registry = new LyricsParserRegistry(
            [new LyricifyWordLyricsParser(QQLyricsConstants.QrcFormat, QQLyricsConstants.ProviderId, LyricsRawTypes.Qrc, log)],
            log);
        var client = new FakeClient();
        var options = Options.Create(new QQLyricsOptions { Enabled = enabled, MatchThreshold = threshold });
        return (new QQMusicLyricsProvider(client, registry, options, log), client);
    }

    private const string QrcSample = "[0,1000]Hello(0,500)World(500,500)"; // as in Task 4

    [TestMethod]
    public void ProviderId_IsQq() => Assert.AreEqual("qq", NewProvider().Provider.Id);

    [TestMethod]
    public async Task Disabled_ReturnsDefinitiveEmpty()
    {
        var (provider, _) = NewProvider(enabled: false);
        var r = await provider.FetchAsync(Query, CancellationToken.None);
        Assert.AreEqual(LyricsOutcomeKind.DefinitiveEmpty, r.Kind);
    }

    [TestMethod]
    public async Task NoCandidate_ReturnsDefinitiveEmpty()
    {
        var (provider, client) = NewProvider();
        client.Result = null;
        var r = await provider.FetchAsync(Query, CancellationToken.None);
        Assert.AreEqual(LyricsOutcomeKind.DefinitiveEmpty, r.Kind);
    }

    [TestMethod]
    public async Task BelowThreshold_ReturnsDefinitiveEmpty()
    {
        var (provider, client) = NewProvider(threshold: 90);
        client.Result = new LyricifyLyricsResult(QrcSample, "qrc",
            new LyricsCandidate("Totally Different Song", "Other Artist", "Other", 12));
        var r = await provider.FetchAsync(Query, CancellationToken.None);
        Assert.AreEqual(LyricsOutcomeKind.DefinitiveEmpty, r.Kind);
    }

    [TestMethod]
    public async Task GoodMatch_ReturnsWordContent()
    {
        var (provider, client) = NewProvider(threshold: 55);
        client.Result = new LyricifyLyricsResult(QrcSample, "qrc",
            new LyricsCandidate("Blinding Lights", "The Weeknd", "After Hours", 200));
        var r = await provider.FetchAsync(Query, CancellationToken.None);
        Assert.AreEqual(LyricsOutcomeKind.Content, r.Kind);
        Assert.AreEqual(LyricsSyncKind.Word, r.Document!.SyncKind);
        Assert.AreEqual("qrc", r.PayloadFormat);
        Assert.IsTrue(r.Definitive);
    }

    [TestMethod]
    public async Task LibraryThrows_ReturnsTransient()
    {
        var (provider, client) = NewProvider();
        client.Throw = new HttpRequestException("boom");
        var r = await provider.FetchAsync(Query, CancellationToken.None);
        Assert.AreEqual(LyricsOutcomeKind.Transient, r.Kind);
    }
}
