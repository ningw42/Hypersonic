using System.Collections.Concurrent;
using System.Text;

using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Lyrics;
using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Core.Services;
using Hypersonic.Core.Services.Lyrics;
using Hypersonic.Models;
using Hypersonic.Services;
using Hypersonic.Services.Cache;
using Hypersonic.Services.Lyrics;
using Hypersonic.Tests.Fakes;

using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hypersonic.Tests.Services.Lyrics;

[TestClass]
public class LyricsServiceTests
{
    private FakeTimeProvider _time = null!;
    private FakeLyricsCacheStore _store = null!;
    private FakeSubsonicClientService _clientService = null!;
    private LyricsParserRegistry _registry = null!;
    private SubsonicLegacyLyricsParser _legacyParser = null!;

    [TestInitialize]
    public void Setup()
    {
        _time = new FakeTimeProvider(new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero));
        _store = new FakeLyricsCacheStore();
        _clientService = new FakeSubsonicClientService
        {
            Client = new RecordingSubsonicClient(),
            ActiveUserIdentity = "alice",
        };
        var log = new LoggingService();
        _legacyParser = new SubsonicLegacyLyricsParser(log);
        _registry = new LyricsParserRegistry([new SubsonicStructuredLyricsParser(log), _legacyParser], log);
    }

    private LyricsService CreateService(LyricsCacheOptions? options, params ILyricsProvider[] providers)
        => CreateService(options, null, providers);

    private LyricsService CreateService(LyricsCacheOptions? cacheOptions, LyricsOrchestrationOptions? orchestration, params ILyricsProvider[] providers)
    {
        cacheOptions ??= new LyricsCacheOptions();
        orchestration ??= new LyricsOrchestrationOptions();
        return new LyricsService(
            _clientService,
            providers,
            _store,
            _registry,
            Options.Create(cacheOptions),
            Options.Create(orchestration),
            _time,
            new NullLoggingService());
    }

    private LyricsProviderResult LegacyContent(string text, bool definitive = true)
    {
        var payload = Encoding.UTF8.GetBytes(text);
        var doc = _legacyParser.Parse(payload)!;
        return LyricsProviderResult.Content(doc, payload, SubsonicLyricsConstants.LegacyFormat, definitive);
    }

    private static LyricsQuery Query => new("song-1", Artist: "A", Title: "T");

    [TestMethod]
    public async Task Miss_FetchesContent_WritesPositive_ReturnsDocument()
    {
        var provider = new FakeLyricsProvider(_ => LegacyContent("hello\nworld"));
        var service = CreateService(null, provider);

        var doc = await service.GetLyricsAsync(Query);

        Assert.IsNotNull(doc);
        Assert.AreEqual("hello", doc!.Lines[0].Text);
        Assert.AreEqual(1, provider.CallCount);
        Assert.AreEqual(1, _store.WritePositiveCount);
    }

    [TestMethod]
    public async Task SecondCall_HitsCache_DoesNotRefetch()
    {
        var provider = new FakeLyricsProvider(_ => LegacyContent("cached lyrics"));
        var service = CreateService(null, provider);

        await service.GetLyricsAsync(Query);
        var second = await service.GetLyricsAsync(Query);

        Assert.AreEqual("cached lyrics", second!.Lines[0].Text);
        Assert.AreEqual(1, provider.CallCount, "second call must be served from cache");
    }

    [TestMethod]
    public async Task NegativeOutcome_WritesNegative_SubsequentReturnsEmpty_NoRefetch()
    {
        var provider = new FakeLyricsProvider(_ => LyricsProviderResult.DefinitiveEmpty);
        var service = CreateService(null, provider);

        var first = await service.GetLyricsAsync(Query);
        Assert.IsNotNull(first);
        Assert.AreEqual(LyricsSyncKind.None, first!.SyncKind);
        Assert.AreEqual(1, _store.WriteNegativeCount);

        var second = await service.GetLyricsAsync(Query);
        Assert.AreEqual(LyricsSyncKind.None, second!.SyncKind);
        Assert.AreEqual(1, provider.CallCount, "negative cache hit must not refetch");
    }

    [TestMethod]
    public async Task NonDefinitiveContent_ReturnsDocument_DoesNotWritePositive()
    {
        var provider = new FakeLyricsProvider(_ => LegacyContent("show me", definitive: false));
        var service = CreateService(null, provider);

        var doc = await service.GetLyricsAsync(Query);

        Assert.AreEqual("show me", doc!.Lines[0].Text);
        Assert.AreEqual(0, _store.WritePositiveCount, "non-definitive content is shown but not cached");
    }

    [TestMethod]
    public async Task Transient_ReturnsNull_DoesNotWrite_RefetchesNextTime()
    {
        var provider = new FakeLyricsProvider(_ => LyricsProviderResult.Transient);
        var service = CreateService(null, provider);

        var first = await service.GetLyricsAsync(Query);
        Assert.IsNull(first);
        Assert.AreEqual(0, _store.WritePositiveCount);
        Assert.AreEqual(0, _store.WriteNegativeCount);

        await service.GetLyricsAsync(Query);
        Assert.AreEqual(2, provider.CallCount, "transient was not cached, so it refetches");
    }

    [TestMethod]
    public async Task Transient_WithStalePositive_ReturnsStaleDocument()
    {
        var options = new LyricsCacheOptions { PositiveTimeToLiveDays = 1 };
        var calls = 0;
        var provider = new FakeLyricsProvider(_ =>
            ++calls == 1 ? LegacyContent("original") : LyricsProviderResult.Transient);
        var service = CreateService(options, provider);

        var first = await service.GetLyricsAsync(Query);
        Assert.AreEqual("original", first!.Lines[0].Text);

        _time.Advance(TimeSpan.FromDays(2)); // positive entry now stale

        var stale = await service.GetLyricsAsync(Query);
        Assert.AreEqual("original", stale!.Lines[0].Text, "transient falls back to the stale positive entry");
    }

    [TestMethod]
    public async Task ExpiredPositive_Refetches()
    {
        var options = new LyricsCacheOptions { PositiveTimeToLiveDays = 1 };
        var calls = 0;
        var provider = new FakeLyricsProvider(_ => LegacyContent(++calls == 1 ? "first" : "second"));
        var service = CreateService(options, provider);

        await service.GetLyricsAsync(Query);
        _time.Advance(TimeSpan.FromDays(2));
        var second = await service.GetLyricsAsync(Query);

        Assert.AreEqual("second", second!.Lines[0].Text);
        Assert.AreEqual(2, provider.CallCount);
    }

    [TestMethod]
    public async Task NegativeTtl_ShorterThanPositive_AllowsRecheck()
    {
        var options = new LyricsCacheOptions { NegativeTimeToLiveDays = 7 };
        var calls = 0;
        var provider = new FakeLyricsProvider(_ =>
            ++calls == 1 ? LyricsProviderResult.DefinitiveEmpty : LegacyContent("now has lyrics"));
        var service = CreateService(options, provider);

        await service.GetLyricsAsync(Query); // caches negative
        _time.Advance(TimeSpan.FromDays(8)); // negative expired

        var doc = await service.GetLyricsAsync(Query);
        Assert.AreEqual("now has lyrics", doc!.Lines[0].Text);
        Assert.AreEqual(2, provider.CallCount);
    }

    [TestMethod]
    public async Task NoUserIdentity_ReturnsNull_NoFetch()
    {
        _clientService.ActiveUserIdentity = null;
        var provider = new FakeLyricsProvider(_ => LegacyContent("x"));
        var service = CreateService(null, provider);

        var doc = await service.GetLyricsAsync(Query);

        Assert.IsNull(doc);
        Assert.AreEqual(0, provider.CallCount);
    }

    [TestMethod]
    public async Task InFlightDedup_ConcurrentCallsForSameTrack_FetchOnce()
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeLyricsProvider(_ => LegacyContent("deduped"))
        {
            Gate = gate,
        };
        var service = CreateService(null, provider);

        var a = service.GetLyricsAsync(Query);
        var b = service.GetLyricsAsync(Query);
        gate.SetResult(true);

        var docs = await Task.WhenAll(a, b);

        Assert.AreEqual("deduped", docs[0]!.Lines[0].Text);
        Assert.AreEqual("deduped", docs[1]!.Lines[0].Text);
        Assert.AreEqual(1, provider.CallCount, "the two concurrent calls share one fetch");
    }

    [TestMethod]
    public async Task CacheRead_OverlaysRowMetadata_WhenParsedDocumentLacksIt()
    {
        // A future metadata-poor format: the parser yields a document with no source/lang/offset, but
        // the index row carries them. The read path restores them from the row (no contract churn).
        SubsonicCacheScope.TryBuildSongKeyHash(_clientService, "song-1", out var keyHash);
        var now = _time.GetUtcNow();
        _store.Seed(keyHash, new LyricsCacheEntry
        {
            FileName = keyHash + ".x",
            Length = 1,
            SourceProvider = "lrclib",
            PayloadFormat = "metadata-poor",
            SyncKind = LyricsSyncKind.Plain,
            LanguageCode = "ja",
            OffsetMs = 500,
            CreatedAtUtc = now,
            LastAccessUtc = now,
            ExpiresAtUtc = now.AddDays(30),
        }, [1]);

        var registry = new StubRegistry(new LyricsDocument
        {
            SyncKind = LyricsSyncKind.Plain,
            Lines = new[] { new LyricsLine { StartMs = 0, Text = "hi" } },
            // SourceProvider / LanguageCode / OffsetMs intentionally null
        });
        var service = new LyricsService(
            _clientService, Array.Empty<ILyricsProvider>(), _store, registry,
            Options.Create(new LyricsCacheOptions()), Options.Create(new LyricsOrchestrationOptions()), _time, new NullLoggingService());

        var doc = await service.GetLyricsAsync(Query);

        Assert.IsNotNull(doc);
        Assert.AreEqual("lrclib", doc!.SourceProvider);
        Assert.AreEqual("ja", doc.LanguageCode);
        Assert.AreEqual(500, doc.OffsetMs);
        Assert.AreEqual("hi", doc.Lines[0].Text);
    }

    [TestMethod]
    public async Task CacheRead_PrefersParsedMetadata_OverRow()
    {
        // When the parser DOES yield metadata, it wins — the row is only a fallback.
        SubsonicCacheScope.TryBuildSongKeyHash(_clientService, "song-1", out var keyHash);
        var now = _time.GetUtcNow();
        _store.Seed(keyHash, new LyricsCacheEntry
        {
            FileName = keyHash + ".x",
            Length = 1,
            SourceProvider = "row-src",
            PayloadFormat = "x",
            SyncKind = LyricsSyncKind.Plain,
            LanguageCode = "row-lang",
            OffsetMs = 111,
            CreatedAtUtc = now,
            LastAccessUtc = now,
            ExpiresAtUtc = now.AddDays(30),
        }, [1]);

        var registry = new StubRegistry(new LyricsDocument
        {
            SyncKind = LyricsSyncKind.Plain,
            Lines = new[] { new LyricsLine { StartMs = 0, Text = "hi" } },
            SourceProvider = "parsed-src",
            LanguageCode = "parsed-lang",
            OffsetMs = 222,
        });
        var service = new LyricsService(
            _clientService, Array.Empty<ILyricsProvider>(), _store, registry,
            Options.Create(new LyricsCacheOptions()), Options.Create(new LyricsOrchestrationOptions()), _time, new NullLoggingService());

        var doc = await service.GetLyricsAsync(Query);

        Assert.AreEqual("parsed-src", doc!.SourceProvider);
        Assert.AreEqual("parsed-lang", doc.LanguageCode);
        Assert.AreEqual(222, doc.OffsetMs);
    }

    // ---- Fan-out (Phase 1.5) ----

    private static LyricsProviderResult LineContent(string source)
    {
        var doc = new LyricsDocument
        {
            SyncKind = LyricsSyncKind.Line,
            Lines = new[] { new LyricsLine { StartMs = 0, Text = "x" } },
            SourceProvider = source,
        };
        return LyricsProviderResult.Content(doc, new byte[] { 1 }, LrcLibLyricsConstants.SyncedFormat, definitive: true);
    }

    private static LyricsProviderResult PlainContent(string source)
    {
        var doc = new LyricsDocument
        {
            SyncKind = LyricsSyncKind.Plain,
            Lines = new[] { new LyricsLine { StartMs = 0, Text = "x" } },
            SourceProvider = source,
        };
        return LyricsProviderResult.Content(doc, new byte[] { 1 }, SubsonicLyricsConstants.LegacyFormat, definitive: true);
    }

    /// <summary>
    /// Drives a budget-bounded fetch to completion under the fake clock, deterministically and with no
    /// wall-clock waits. A <c>Task.Delay</c> on a <see cref="FakeTimeProvider"/> only fires for advances
    /// that happen AFTER it is armed, and the fan-out arms its budget timer on a pool thread whose timing
    /// relative to this method is unknowable (and, in a cold/low-thread test process, easy to starve with
    /// a tight advance loop). So rather than guess when to advance, we run a CONCURRENT advancer that
    /// keeps pushing the clock forward by <paramref name="advance"/> (&gt; budget) until the fetch
    /// completes: whenever the fan-out finally arms its timer, a later advance is guaranteed to fire it.
    /// The advancer yields each turn (no wall-clock waits) and stops the instant the fetch completes.
    /// </summary>
    private async Task<LyricsDocument?> CompleteUnderBudget(Task<LyricsDocument?> task, TimeSpan advance)
    {
        var advancer = Task.Run(async () =>
        {
            // Cap iterations so a regression that never completes the fetch fails the assert below
            // instead of spinning forever. The advance is fake-time only — no wall-clock waits.
            for (var i = 0; i < 100_000 && !task.IsCompleted; i++)
            {
                _time.Advance(advance);
                await Task.Yield();
            }
        });

        await advancer.ConfigureAwait(false);
        Assert.IsTrue(task.IsCompleted, "budget-bounded fetch did not complete under the fake clock");
        return await task.ConfigureAwait(false);
    }

    [TestMethod]
    public async Task FanOut_HigherTierWinsAcrossProviders()
    {
        var subsonic = new FakeLyricsProvider(_ => PlainContent("subsonic")) { Id = "subsonic" };
        var lrclib = new FakeLyricsProvider(_ => LineContent("lrclib")) { Id = "lrclib" };
        var service = CreateService(null, subsonic, lrclib);

        var doc = await service.GetLyricsAsync(Query);

        Assert.AreEqual(LyricsSyncKind.Line, doc!.SyncKind);
        Assert.AreEqual("lrclib", doc.SourceProvider);
    }

    [TestMethod]
    public async Task FanOut_EqualTier_KeepsEarliestRegisteredProvider()
    {
        var subsonic = new FakeLyricsProvider(_ => LineContent("subsonic")) { Id = "subsonic" };
        var lrclib = new FakeLyricsProvider(_ => LineContent("lrclib")) { Id = "lrclib" };
        var service = CreateService(null, subsonic, lrclib); // registration order: subsonic first

        var doc = await service.GetLyricsAsync(Query);

        Assert.AreEqual("subsonic", doc!.SourceProvider, "equal tier keeps the earliest-registered provider");
    }

    [TestMethod]
    public async Task FanOut_AllComplete_DecidesWithoutAdvancingClock()
    {
        var subsonic = new FakeLyricsProvider(_ => PlainContent("subsonic")) { Id = "subsonic" };
        var lrclib = new FakeLyricsProvider(_ => LineContent("lrclib")) { Id = "lrclib" };
        var service = CreateService(null, subsonic, lrclib);

        // Both providers complete synchronously → all-complete early-exit; no clock advance needed.
        var doc = await service.GetLyricsAsync(Query);

        Assert.AreEqual(LyricsSyncKind.Line, doc!.SyncKind);
    }

    [TestMethod]
    public async Task FanOut_BudgetTruncation_SuppressesNegativeWrite()
    {
        var fast = new FakeLyricsProvider(_ => LyricsProviderResult.DefinitiveEmpty) { Id = "subsonic" };
        var slowGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var slow = new FakeLyricsProvider(_ => LyricsProviderResult.DefinitiveEmpty) { Id = "lrclib", Gate = slowGate };
        var options = new LyricsOrchestrationOptions { DefaultFanOutBudgetMs = 700 };
        var service = CreateService(null, options, fast, slow);

        var task = service.GetLyricsAsync(Query);
        var doc = await CompleteUnderBudget(task, TimeSpan.FromMilliseconds(750));

        Assert.AreEqual(LyricsSyncKind.None, doc!.SyncKind, "no winner → empty this attempt");
        Assert.AreEqual(0, _store.WriteNegativeCount, "negative suppressed because a peer was truncated");
    }

    [TestMethod]
    public async Task FanOut_BudgetTruncation_EagerPinsPresentWinner()
    {
        var fast = new FakeLyricsProvider(_ => PlainContent("subsonic")) { Id = "subsonic" };
        var slowGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var slow = new FakeLyricsProvider(_ => LineContent("lrclib")) { Id = "lrclib", Gate = slowGate };
        var options = new LyricsOrchestrationOptions { DefaultFanOutBudgetMs = 700 };
        var service = CreateService(null, options, fast, slow);

        var task = service.GetLyricsAsync(Query);
        var doc = await CompleteUnderBudget(task, TimeSpan.FromMilliseconds(750));

        Assert.AreEqual(LyricsSyncKind.Plain, doc!.SyncKind, "truncated higher tier → user sees the present lower tier");
        Assert.AreEqual("subsonic", doc.SourceProvider);
        Assert.AreEqual(1, _store.WritePositiveCount, "the present winner is eager-pinned despite the truncated peer");
    }

    [TestMethod]
    public async Task Budget_Parameter_ThreadsThrough_AndTruncates()
    {
        var fast = new FakeLyricsProvider(_ => PlainContent("subsonic")) { Id = "subsonic" };
        var slowGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var slow = new FakeLyricsProvider(_ => LineContent("lrclib")) { Id = "lrclib", Gate = slowGate };
        var service = CreateService(null, fast, slow);

        var task = service.GetLyricsAsync(Query, budget: TimeSpan.FromMilliseconds(300));
        var doc = await CompleteUnderBudget(task, TimeSpan.FromMilliseconds(350));

        Assert.AreEqual(LyricsSyncKind.Plain, doc!.SyncKind, "explicit budget elapsed → only the fast provider's result");
    }

    [TestMethod]
    public async Task NegativeBudget_FallsBackToDefault_DoesNotThrow()
    {
        var provider = new FakeLyricsProvider(_ => PlainContent("subsonic")) { Id = "subsonic" };
        var service = CreateService(null, provider);

        // A negative budget would throw inside Task.Delay; it must clamp to the default and complete.
        var doc = await service.GetLyricsAsync(Query, budget: TimeSpan.FromMilliseconds(-5));

        Assert.AreEqual(LyricsSyncKind.Plain, doc!.SyncKind);
    }

    [TestMethod]
    public async Task Dispose_DuringInFlightFanOut_ReturnsNull_NoHang()
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeLyricsProvider(_ => LegacyContent("x")) { Id = "subsonic", Gate = gate };
        var service = CreateService(null, provider);

        var task = service.GetLyricsAsync(Query);

        // Wait until the fan-out has actually invoked the (gated) provider, so the dispose lands
        // mid-flight. CallCount is Volatile/Interlocked-safe for this cross-thread read.
        for (var i = 0; i < 1000 && provider.CallCount == 0; i++)
        {
            await Task.Yield();
        }
        Assert.AreEqual(1, provider.CallCount, "the gated provider should be in flight before dispose");

        service.Dispose();
        gate.SetResult(true); // release the parked provider after dispose

        var doc = await task;
        Assert.IsNull(doc, "a fan-out in flight when the service is disposed resolves to null without hanging");
    }

    [TestMethod]
    public async Task ForceRefresh_BypassesCacheRead_AndOverwrites()
    {
        var calls = 0;
        var provider = new FakeLyricsProvider(_ => LegacyContent(++calls == 1 ? "v1" : "v2")) { Id = "subsonic" };
        var service = CreateService(null, provider);

        var first = await service.GetLyricsAsync(Query);
        Assert.AreEqual("v1", first!.Lines[0].Text);
        Assert.AreEqual(1, provider.CallCount);

        var refreshed = await service.GetLyricsAsync(Query, forceRefresh: true);

        Assert.AreEqual("v2", refreshed!.Lines[0].Text, "forceRefresh bypasses the cached v1 and refetches");
        Assert.AreEqual(2, provider.CallCount, "forceRefresh refetches even though a positive entry exists");
        Assert.AreEqual(2, _store.WritePositiveCount, "forceRefresh overwrites the cache entry");
    }

    private static LyricsProviderResult WordContent(string marker)
    {
        var payload = Encoding.UTF8.GetBytes(marker);
        var doc = new LyricsDocument
        {
            SyncKind = LyricsSyncKind.Word,
            SourceProvider = marker,
            Lines =
            [
                new LyricsLine
                {
                    StartMs = 0, EndMs = 1000, Text = marker,
                    Words = [new LyricsWord { StartMs = 0, EndMs = 1000, Text = marker }],
                },
            ],
        };
        return LyricsProviderResult.Content(doc, payload, TtmlLyricsConstants.Format, definitive: true);
    }

    [TestMethod]
    public async Task TwoWordProviders_RegistrationOrderWins_NotArrivalOrder()
    {
        // The later-registered provider returns its Word result first; the earlier one is gated (slower).
        // With the Word early-exit removed, the fan-out waits for all providers and the policy picks the
        // earliest-registered on the tie — "first", not the faster "second".
        var gate = new TaskCompletionSource<bool>();
        var first = new FakeLyricsProvider(_ => WordContent("first")) { Id = "amll-a", Gate = gate };
        var second = new FakeLyricsProvider(_ => WordContent("second")) { Id = "amll-b" };
        var service = CreateService(null, first, second);

        var task = service.GetLyricsAsync(Query);
        gate.SetResult(true); // release the slower first provider so all-complete is reached
        var doc = await task;

        Assert.AreEqual("first", doc!.SourceProvider);
    }

    [TestMethod]
    public void ExtensionForFormat_Ttml_MapsToTtml()
        => Assert.AreEqual(".ttml", LyricsService.ExtensionForFormat("ttml"));

    // ---- Local override source (Phase 2) ----

    [TestMethod]
    public async Task Override_Present_ReturnsItDirectly_NoFetch_NoCacheWrite()
    {
        var overrideDoc = new LyricsDocument
        {
            SyncKind = LyricsSyncKind.Word,
            Lines = new[]
            {
                new LyricsLine
                {
                    StartMs = 0,
                    EndMs = 1000,
                    Text = "ov",
                    Words = new[] { new LyricsWord { StartMs = 0, EndMs = 1000, Text = "ov" } },
                },
            },
            SourceProvider = "ttml",
        };
        var provider = new FakeLyricsProvider(_ => LegacyContent("from network"));
        var service = CreateServiceWithOverride(new FakeOverrideSource(_ => overrideDoc), provider);

        var doc = await service.GetLyricsAsync(Query);

        Assert.AreSame(overrideDoc, doc, "a present override wins outright");
        Assert.AreEqual(0, provider.CallCount, "override bypasses the fan-out");
        Assert.AreEqual(0, _store.WritePositiveCount, "override writes nothing to the cache");
    }

    [TestMethod]
    public async Task Override_Present_WinsEvenOnForceRefresh()
    {
        var overrideDoc = new LyricsDocument { SyncKind = LyricsSyncKind.Plain, Lines = new[] { new LyricsLine { StartMs = 0, Text = "ov" } } };
        var provider = new FakeLyricsProvider(_ => LegacyContent("from network"));
        var service = CreateServiceWithOverride(new FakeOverrideSource(_ => overrideDoc), provider);

        var doc = await service.GetLyricsAsync(Query, forceRefresh: true);

        Assert.AreSame(overrideDoc, doc, "override takes precedence over a force-refresh too");
        Assert.AreEqual(0, provider.CallCount);
    }

    [TestMethod]
    public async Task Override_Absent_FallsThroughToNormalPath()
    {
        var provider = new FakeLyricsProvider(_ => LegacyContent("from network"));
        var service = CreateServiceWithOverride(new FakeOverrideSource(_ => null), provider);

        var doc = await service.GetLyricsAsync(Query);

        Assert.AreEqual("from network", doc!.Lines[0].Text, "absent override → normal cache/fan-out path");
        Assert.AreEqual(1, provider.CallCount);
    }

    private LyricsService CreateServiceWithOverride(ILocalLyricsOverrideSource overrideSource, params ILyricsProvider[] providers)
        => new(
            _clientService,
            providers,
            _store,
            _registry,
            Options.Create(new LyricsCacheOptions()),
            Options.Create(new LyricsOrchestrationOptions()),
            _time,
            new NullLoggingService(),
            overrideSource);

    // ---- Fakes ----

    private sealed class FakeOverrideSource : ILocalLyricsOverrideSource
    {
        private readonly Func<string, LyricsDocument?> _handler;
        public FakeOverrideSource(Func<string, LyricsDocument?> handler) => _handler = handler;

        public Task<LyricsDocument?> TryGetOverrideAsync(string songId, CancellationToken cancellationToken = default)
            => Task.FromResult(_handler(songId));
    }

    private sealed class StubRegistry : ILyricsParserRegistry
    {
        private readonly LyricsDocument? _document;

        public StubRegistry(LyricsDocument? document) => _document = document;

        public LyricsDocument? Parse(string formatTag, ReadOnlyMemory<byte> payload) => _document;
    }

    private sealed class FakeLyricsProvider : ILyricsProvider
    {
        private readonly Func<LyricsQuery, LyricsProviderResult> _handler;
        private int _callCount;

        public FakeLyricsProvider(Func<LyricsQuery, LyricsProviderResult> handler) => _handler = handler;

        public string Id { get; set; } = "subsonic";

        // Volatile-safe: the fan-out invokes FetchAsync on a pool thread while a test pump may read
        // CallCount on another thread to detect that the fan-out has started (the budget-timer arm proxy).
        public int CallCount => Volatile.Read(ref _callCount);
        public TaskCompletionSource<bool>? Gate { get; set; }

        public async Task<LyricsProviderResult> FetchAsync(LyricsQuery query, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            if (Gate is not null)
            {
                await Gate.Task;
            }

            return _handler(query);
        }
    }

    private sealed class FakeLyricsCacheStore : ILyricsCacheStore
    {
        private readonly Dictionary<string, (LyricsCacheEntry Entry, byte[]? Bytes)> _rows = new();

        public int WritePositiveCount { get; private set; }
        public int WriteNegativeCount { get; private set; }

        public void Seed(string keyHash, LyricsCacheEntry entry, byte[] bytes) => _rows[keyHash] = (entry.Clone(), bytes);

        public string CacheFolderPath => "fake";

        public Task<LyricsCacheEntry?> GetEntryAsync(string keyHash, CancellationToken cancellationToken = default)
            => Task.FromResult(_rows.TryGetValue(keyHash, out var row) ? row.Entry.Clone() : null);

        public Task TouchEntryAsync(string keyHash, DateTimeOffset lastAccessUtc, CancellationToken cancellationToken = default)
        {
            if (_rows.TryGetValue(keyHash, out var row))
            {
                row.Entry.LastAccessUtc = lastAccessUtc;
            }

            return Task.CompletedTask;
        }

        public Task UpsertEntryAsync(string keyHash, LyricsCacheEntry entry, CancellationToken cancellationToken = default)
        {
            _rows[keyHash] = (entry.Clone(), _rows.TryGetValue(keyHash, out var row) ? row.Bytes : null);
            return Task.CompletedTask;
        }

        public Task RemoveEntryAsync(string keyHash, bool flushImmediately = false, CancellationToken cancellationToken = default)
        {
            _rows.Remove(keyHash);
            return Task.CompletedTask;
        }

        public Task<CacheStoreStats> GetStatsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new CacheStoreStats(0, _rows.Count));

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            _rows.Clear();
            return Task.CompletedTask;
        }

        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<Uri?> WritePayloadAsync(string keyHash, ReadOnlyMemory<byte> bytes, LyricsCacheEntry entry, CancellationToken cancellationToken = default)
        {
            _rows[keyHash] = (entry.Clone(), bytes.ToArray());
            WritePositiveCount++;
            return Task.FromResult<Uri?>(new Uri("file:///fake/" + keyHash));
        }

        public Task WriteNegativeAsync(string keyHash, LyricsCacheEntry entry, CancellationToken cancellationToken = default)
        {
            _rows[keyHash] = (entry.Clone(), null);
            WriteNegativeCount++;
            return Task.CompletedTask;
        }

        public Task<CachePayload<LyricsCacheEntry>?> ReadPayloadAsync(string keyHash, CancellationToken cancellationToken = default)
        {
            if (_rows.TryGetValue(keyHash, out var row) && row.Bytes is not null)
            {
                return Task.FromResult<CachePayload<LyricsCacheEntry>?>(new CachePayload<LyricsCacheEntry>(row.Entry.Clone(), row.Bytes));
            }

            return Task.FromResult<CachePayload<LyricsCacheEntry>?>(null);
        }

        public Task ReconcileAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task EvictIfNeededAsync(string? protectedKeyHash = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
