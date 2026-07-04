using System.Net;
using System.Text;

using Hypersonic.Contracts.Services;
using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Core.Services;
using Hypersonic.Core.Services.Lyrics;
using Hypersonic.Models;
using Hypersonic.Services.Lyrics;

using Microsoft.Extensions.Options;

namespace Hypersonic.Tests.Services.Lyrics;

[TestClass]
public class AmllTtmlLyricsProviderTests
{
    // Minimal AMLL-flavoured word-synced TTML the real TtmlLyricsParser accepts.
    private const string Ttml = """
        <tt xmlns="http://www.w3.org/ns/ttml"><body><div>
        <p begin="0:00.000" end="0:01.000"><span begin="0:00.000" end="0:01.000">Hi</span></p>
        </div></body></tt>
        """;

    private static AmllTtmlLyricsProvider Provider(FakeIndex index, StubHandler handler, bool enabled = true)
    {
        var log = new LoggingService();
        var registry = new LyricsParserRegistry([new TtmlLyricsParser(log)], log);
        return new AmllTtmlLyricsProvider(new StubFactory(handler), index, registry,
            Options.Create(new AmllLyricsOptions { Enabled = enabled }), log);
    }

    private static LyricsQuery Query => new("s", Artist: "A", Title: "T");

    [TestMethod]
    public void ProviderId_IsAmll()
        => Assert.AreEqual("amll-ttml-db", Provider(new FakeIndex(), new StubHandler()).Id);

    [TestMethod]
    public async Task Match_FetchesTtml_ReturnsWordContent()
    {
        var p = Provider(new FakeIndex { Ready = true, File = "idol.ttml" },
            new StubHandler { Response = Ok(Ttml) });
        var r = await p.FetchAsync(Query, CancellationToken.None);
        Assert.AreEqual(LyricsOutcomeKind.Content, r.Kind);
        Assert.AreEqual(LyricsSyncKind.Word, r.Document!.SyncKind);
        Assert.AreEqual(LyricsFormat.Ttml, r.Format);
        Assert.IsTrue(r.Definitive);
    }

    [TestMethod]
    public async Task FetchAsync_Content_StampsAmllProviderIdAndTtmlFormat()
    {
        // The point of the ownership shift: AMLL's returned document is attributed to the provider
        // (identity), not to the format. Reuses the success-path fixture from the content test above.
        var p = Provider(new FakeIndex { Ready = true, File = "idol.ttml" },
            new StubHandler { Response = Ok(Ttml) });

        var result = await p.FetchAsync(Query, CancellationToken.None);

        Assert.AreEqual(LyricsOutcomeKind.Content, result.Kind);
        Assert.AreEqual("amll-ttml-db", result.Document!.SourceProvider); // identity, not "ttml"
        Assert.AreEqual(LyricsFormat.Ttml, result.Document!.Format);
    }

    [TestMethod]
    public async Task IndexNotReady_Transient_NoHttp()
    {
        var handler = new StubHandler();
        var r = await Provider(new FakeIndex { Ready = false }, handler).FetchAsync(Query, CancellationToken.None);
        Assert.AreEqual(LyricsOutcomeKind.Transient, r.Kind);
        Assert.AreEqual(0, handler.Requests);
    }

    [TestMethod]
    public async Task NoMatch_DefinitiveEmpty_NoHttp()
    {
        var handler = new StubHandler();
        var r = await Provider(new FakeIndex { Ready = true, File = null }, handler).FetchAsync(Query, CancellationToken.None);
        Assert.AreEqual(LyricsOutcomeKind.DefinitiveEmpty, r.Kind);
        Assert.AreEqual(0, handler.Requests);
    }

    [TestMethod]
    public async Task BlankTitle_Transient_NoHttp()
    {
        var handler = new StubHandler();
        var r = await Provider(new FakeIndex { Ready = true, File = "x.ttml" }, handler)
            .FetchAsync(new LyricsQuery("s", Artist: "A", Title: " "), CancellationToken.None);
        Assert.AreEqual(LyricsOutcomeKind.Transient, r.Kind);
        Assert.AreEqual(0, handler.Requests);
    }

    [TestMethod]
    public async Task HttpFailure_Transient()
    {
        var p = Provider(new FakeIndex { Ready = true, File = "idol.ttml" },
            new StubHandler { Response = new HttpResponseMessage(HttpStatusCode.InternalServerError) });
        var r = await p.FetchAsync(Query, CancellationToken.None);
        Assert.AreEqual(LyricsOutcomeKind.Transient, r.Kind);
    }

    [TestMethod]
    public async Task ParseFailure_Transient()
    {
        var p = Provider(new FakeIndex { Ready = true, File = "idol.ttml" },
            new StubHandler { Response = Ok("<tt><body/></tt>") }); // no <p> → parser returns null
        var r = await p.FetchAsync(Query, CancellationToken.None);
        Assert.AreEqual(LyricsOutcomeKind.Transient, r.Kind);
    }

    [TestMethod]
    public async Task Disabled_DefinitiveEmpty_NoIndexNoHttp()
    {
        var handler = new StubHandler();
        var index = new FakeIndex { Ready = true, File = "idol.ttml" };
        var r = await Provider(index, handler, enabled: false).FetchAsync(Query, CancellationToken.None);
        Assert.AreEqual(LyricsOutcomeKind.DefinitiveEmpty, r.Kind); // inert, non-blocking to negative cache
        Assert.AreEqual(0, handler.Requests);
        Assert.AreEqual(0, index.ResolveCalls);
    }

    [TestMethod]
    public async Task AlreadyCancelled_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await Provider(new FakeIndex { Ready = true, File = "x.ttml" }, new StubHandler())
                .FetchAsync(Query, cts.Token));
    }

    private static HttpResponseMessage Ok(string body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/xml") };

    private sealed class FakeIndex : IAmllLyricsIndex
    {
        public bool Ready { get; set; }
        public string? File { get; set; }
        public int ResolveCalls { get; private set; }
        public bool IsReady => Ready;
        public string? TryResolve(LyricsQuery query) { ResolveCalls++; return File; }
        public Task WarmAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public HttpResponseMessage? Response { get; set; }
        public int Requests { get; private set; }
        public string? LastPath { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            LastPath = request.RequestUri!.AbsolutePath;
            return Task.FromResult(Response ?? new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class StubFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name)
            => new(_handler, disposeHandler: false) { BaseAddress = new Uri(AmllLyricsConstants.CdnBase) };
    }
}
