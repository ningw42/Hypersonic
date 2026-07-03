using System.Net;
using System.Text;
using System.Text.Json;

using Hypersonic.Contracts.Services;
using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Core.Services;
using Hypersonic.Models;
using Hypersonic.Services.Lyrics;
using Hypersonic.Tests.Fakes;

using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hypersonic.Tests.Services.Lyrics;

[TestClass]
public class AmllLyricsIndexTests
{
    private string _dir = null!;
    private FakeTimeProvider _time = null!;

    private const string Jsonl =
        """{"metadata":[["artists",["YOASOBI"]],["musicName",["Idol"]]],"rawLyricFile":"idol.ttml"}""";

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "amll-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _time = new FakeTimeProvider(new DateTimeOffset(2026, 7, 3, 0, 0, 0, TimeSpan.Zero));
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private AmllLyricsIndex Index(StubHandler handler, bool enabled = true)
        => new(new StubFactory(handler), new FakeFolderLocator(_dir),
            Options.Create(new AmllLyricsOptions { Enabled = enabled }), _time, new NullLoggingService());

    private static LyricsQuery Idol => new("s", Artist: "YOASOBI", Title: "Idol");

    [TestMethod]
    public void BeforeWarm_NotReady_ResolveNull()
    {
        var index = Index(new StubHandler());
        Assert.IsFalse(index.IsReady);
        Assert.IsNull(index.TryResolve(Idol));
    }

    [TestMethod]
    public async Task Warm_FreshDiskIndex_LoadsWithoutNetwork()
    {
        // Seed a fresh disk index + sidecar stamped "now".
        var sub = Path.Combine(_dir, AmllLyricsConstants.CacheSubfolder);
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, AmllLyricsConstants.IndexFileName), Jsonl);
        File.WriteAllText(Path.Combine(sub, AmllLyricsConstants.MetaFileName),
            JsonSerializer.Serialize(new { ETag = "\"e1\"", FetchedUtc = _time.GetUtcNow() }));

        var handler = new StubHandler();
        var index = Index(handler);
        await index.WarmAsync();

        Assert.IsTrue(index.IsReady);
        Assert.AreEqual("idol.ttml", index.TryResolve(Idol));
        Assert.AreEqual(0, handler.Requests); // fresh → no network
    }

    [TestMethod]
    public async Task Warm_EmptyDir_Downloads_BecomesReady_PersistsToDisk()
    {
        var handler = new StubHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Jsonl, Encoding.UTF8, "application/json"),
                Headers = { ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"v1\"") },
            },
        };
        var index = Index(handler);
        await index.WarmAsync();

        Assert.IsTrue(index.IsReady);
        Assert.AreEqual("idol.ttml", index.TryResolve(Idol));
        Assert.AreEqual(1, handler.Requests);
        Assert.IsTrue(File.Exists(Path.Combine(_dir, AmllLyricsConstants.CacheSubfolder, AmllLyricsConstants.IndexFileName)));
    }

    [TestMethod]
    public async Task Warm_StaleDisk_304_KeepsDiskData()
    {
        var sub = Path.Combine(_dir, AmllLyricsConstants.CacheSubfolder);
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, AmllLyricsConstants.IndexFileName), Jsonl);
        // Stamp the sidecar 30 days ago → stale → conditional GET.
        var old = _time.GetUtcNow().AddDays(-30);
        File.WriteAllText(Path.Combine(sub, AmllLyricsConstants.MetaFileName),
            JsonSerializer.Serialize(new { ETag = "\"e1\"", FetchedUtc = old }));

        var handler = new StubHandler { Response = new HttpResponseMessage(HttpStatusCode.NotModified) };
        var index = Index(handler);
        await index.WarmAsync();

        Assert.IsTrue(index.IsReady);
        Assert.AreEqual("idol.ttml", index.TryResolve(Idol));
        Assert.AreEqual(1, handler.Requests);
        Assert.AreEqual("\"e1\"", handler.LastIfNoneMatch); // sent the stored ETag
    }

    [TestMethod]
    public async Task Warm_Disabled_DoesNothing()
    {
        var handler = new StubHandler();
        var index = Index(handler, enabled: false);
        await index.WarmAsync();

        Assert.IsFalse(index.IsReady);
        Assert.AreEqual(0, handler.Requests);
    }

    // ---- shared stub HTTP harness (reused by Task 3) ----

    internal sealed class StubHandler : HttpMessageHandler
    {
        public HttpResponseMessage? Response { get; set; }
        public int Requests { get; private set; }
        public string? LastIfNoneMatch { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            LastIfNoneMatch = request.Headers.IfNoneMatch.FirstOrDefault()?.Tag;
            return Task.FromResult(Response ?? new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(Jsonl, Encoding.UTF8, "application/json") });
        }
    }

    internal sealed class StubFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name)
            => new(_handler, disposeHandler: false) { BaseAddress = new Uri(AmllLyricsConstants.CdnBase) };
    }

    internal sealed class FakeFolderLocator : IAppFolderLocator
    {
        private readonly string _cache;
        public FakeFolderLocator(string cache) => _cache = cache;
        public string GetCacheFolder() => _cache;
        public string GetLyricsOverrideFolder() => Path.Combine(_cache, "LyricsOverride");
    }
}
