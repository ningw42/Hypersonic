using System.Net;
using System.Text;

using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Core.Services;
using Hypersonic.Core.Services.Lyrics;
using Hypersonic.Services.Lyrics;

namespace Hypersonic.Tests.Services.Lyrics;

[TestClass]
public class LrcLibLyricsProviderTests
{
    private static LrcLibLyricsProvider Provider(StubHandler handler)
    {
        var log = new LoggingService();
        var registry = new LyricsParserRegistry(
            [new LrcSyncedLyricsParser(log), new LrcPlainLyricsParser(log)], log);
        return new LrcLibLyricsProvider(new StubFactory(handler), registry, log);
    }

    private static LyricsQuery Query => new("song-1", Artist: "A", Title: "T", Album: "Alb", DurationSeconds: 200);

    private static string GetSyncedJson => """
        {"id":1,"trackName":"T","artistName":"A","albumName":"Alb","duration":200,
         "instrumental":false,"plainLyrics":"plain","syncedLyrics":"[00:01.00] sung"}
        """;

    [TestMethod]
    public async Task ProviderId_IsLrclib()
        => Assert.AreEqual("lrclib", Provider(new StubHandler()).Id);

    [TestMethod]
    public async Task Get_Synced_ReturnsLineContent()
    {
        var p = Provider(new StubHandler { GetResponse = Ok(GetSyncedJson) });
        var r = await p.FetchAsync(Query, CancellationToken.None);
        Assert.AreEqual(LyricsOutcomeKind.Content, r.Kind);
        Assert.AreEqual(LyricsSyncKind.Line, r.Document!.SyncKind);
        Assert.AreEqual("lrclib-synced", r.PayloadFormat);
        Assert.IsTrue(r.Definitive);
    }

    [TestMethod]
    public async Task Get_PlainOnly_ReturnsPlainContent()
    {
        var p = Provider(new StubHandler { GetResponse = Ok(
            """{"trackName":"T","artistName":"A","duration":200,"instrumental":false,"plainLyrics":"hello","syncedLyrics":""}""") });
        var r = await p.FetchAsync(Query, CancellationToken.None);
        Assert.AreEqual(LyricsSyncKind.Plain, r.Document!.SyncKind);
        Assert.AreEqual("lrclib-plain", r.PayloadFormat);
    }

    [TestMethod]
    public async Task Get_BothEmpty_Instrumental_DefinitiveEmpty()
    {
        var p = Provider(new StubHandler { GetResponse = Ok(
            """{"trackName":"T","artistName":"A","duration":200,"instrumental":true,"plainLyrics":"","syncedLyrics":""}""") });
        var r = await p.FetchAsync(Query, CancellationToken.None);
        Assert.AreEqual(LyricsOutcomeKind.DefinitiveEmpty, r.Kind);
    }

    [TestMethod]
    public async Task Get_404_FallsThroughToSearch_RanksByDurationProximity()
    {
        var searchArray = """
            [
              {"trackName":"T","artistName":"A","albumName":"Other","duration":260,"instrumental":false,"plainLyrics":"far","syncedLyrics":""},
              {"trackName":"T","artistName":"A","albumName":"Alb","duration":201,"instrumental":false,"plainLyrics":"near","syncedLyrics":"[00:01.00] near"}
            ]
            """;
        var p = Provider(new StubHandler
        {
            GetResponse = new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("""{"code":404,"name":"TrackNotFound","message":"x"}""", Encoding.UTF8, "application/json"),
            },
            SearchResponse = Ok(searchArray),
        });

        var r = await p.FetchAsync(Query, CancellationToken.None);
        Assert.AreEqual(LyricsOutcomeKind.Content, r.Kind);
        Assert.AreEqual(LyricsSyncKind.Line, r.Document!.SyncKind);
    }

    [TestMethod]
    public async Task Search_Empty_DefinitiveEmpty()
    {
        var p = Provider(new StubHandler { GetResponse = NotFound(), SearchResponse = Ok("[]") });
        var r = await p.FetchAsync(Query, CancellationToken.None);
        Assert.AreEqual(LyricsOutcomeKind.DefinitiveEmpty, r.Kind);
    }

    [TestMethod]
    public async Task Get_5xx_Transient_DoesNotSearch()
    {
        var handler = new StubHandler
        {
            GetResponse = new HttpResponseMessage(HttpStatusCode.InternalServerError),
            SearchResponse = Ok("[]"),
        };
        var r = await Provider(handler).FetchAsync(Query, CancellationToken.None);
        Assert.AreEqual(LyricsOutcomeKind.Transient, r.Kind);
        Assert.AreEqual(0, handler.SearchCount);
    }

    [TestMethod]
    public async Task MissingArtist_SkipsGet_UsesSearch()
    {
        var handler = new StubHandler { SearchResponse = Ok($"[{GetSyncedJson}]") };
        var r = await Provider(handler).FetchAsync(new LyricsQuery("s", Artist: null, Title: "T"), CancellationToken.None);
        Assert.AreEqual(0, handler.GetCount);
        Assert.AreEqual(1, handler.SearchCount);
        Assert.AreEqual(LyricsOutcomeKind.Content, r.Kind);
    }

    [TestMethod]
    public async Task BlankTitle_Transient_NoHttp()
    {
        var handler = new StubHandler();
        var r = await Provider(handler).FetchAsync(new LyricsQuery("s", Artist: "A", Title: " "), CancellationToken.None);
        Assert.AreEqual(LyricsOutcomeKind.Transient, r.Kind);
        Assert.AreEqual(0, handler.GetCount);
        Assert.AreEqual(0, handler.SearchCount);
    }

    [TestMethod]
    public async Task AlreadyCancelled_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await Provider(new StubHandler()).FetchAsync(Query, cts.Token));
    }

    [TestMethod]
    public async Task Get_SendsTrackArtistAlbumDuration()
    {
        var handler = new StubHandler { GetResponse = Ok(GetSyncedJson) };
        await Provider(handler).FetchAsync(Query, CancellationToken.None);
        var q = handler.LastGetQuery!;
        StringAssert.Contains(q, "track_name=T");
        StringAssert.Contains(q, "artist_name=A");
        StringAssert.Contains(q, "album_name=Alb");
        StringAssert.Contains(q, "duration=200");
    }

    [TestMethod]
    public async Task Search_SendsTrackArtist_OmitsAlbumAndDuration()
    {
        var handler = new StubHandler { GetResponse = NotFound(), SearchResponse = Ok($"[{GetSyncedJson}]") };
        await Provider(handler).FetchAsync(Query, CancellationToken.None);
        var q = handler.LastSearchQuery!;
        StringAssert.Contains(q, "track_name=T");
        StringAssert.Contains(q, "artist_name=A");
        Assert.IsFalse(q.Contains("album_name"), "search must not send album_name");
        Assert.IsFalse(q.Contains("duration"), "search must not send duration");
    }

    [TestMethod]
    public async Task Get_SyncedUnparseable_PlainEmpty_Transient()
    {
        // A present-but-unparseable lyric field is a parse failure (design: → Transient), NOT
        // "no lyrics" — returning DefinitiveEmpty here would negative-cache a track that has lyrics.
        var p = Provider(new StubHandler { GetResponse = Ok(
            """{"trackName":"T","artistName":"A","duration":200,"instrumental":false,"plainLyrics":"","syncedLyrics":"no timestamps in here"}""") });
        var r = await p.FetchAsync(Query, CancellationToken.None);
        Assert.AreEqual(LyricsOutcomeKind.Transient, r.Kind);
    }

    [TestMethod]
    public async Task Get_SyncedUnparseable_PlainValid_RescuesToPlain()
    {
        var p = Provider(new StubHandler { GetResponse = Ok(
            """{"trackName":"T","artistName":"A","duration":200,"instrumental":false,"plainLyrics":"hello world","syncedLyrics":"no timestamps in here"}""") });
        var r = await p.FetchAsync(Query, CancellationToken.None);
        Assert.AreEqual(LyricsOutcomeKind.Content, r.Kind);
        Assert.AreEqual(LyricsSyncKind.Plain, r.Document!.SyncKind);
    }

    private static HttpResponseMessage Ok(string json)
        => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private static HttpResponseMessage NotFound()
        => new(HttpStatusCode.NotFound) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };

    private sealed class StubHandler : HttpMessageHandler
    {
        public HttpResponseMessage? GetResponse { get; set; }
        public HttpResponseMessage? SearchResponse { get; set; }
        public int GetCount { get; private set; }
        public int SearchCount { get; private set; }
        public string? LastGetQuery { get; private set; }
        public string? LastSearchQuery { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/api/get", StringComparison.Ordinal))
            {
                GetCount++;
                LastGetQuery = request.RequestUri!.Query;
                return Task.FromResult(GetResponse ?? new HttpResponseMessage(HttpStatusCode.NotFound));
            }
            SearchCount++;
            LastSearchQuery = request.RequestUri!.Query;
            return Task.FromResult(SearchResponse ?? new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("[]") });
        }
    }

    private sealed class StubFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) =>
            new(_handler, disposeHandler: false) { BaseAddress = new Uri("https://lrclib.net") };
    }
}
