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
    {
        options ??= new LyricsCacheOptions();
        return new LyricsService(
            _clientService,
            providers,
            _store,
            _registry,
            Options.Create(options),
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
            Options.Create(new LyricsCacheOptions()), _time, new NullLoggingService());

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
            Options.Create(new LyricsCacheOptions()), _time, new NullLoggingService());

        var doc = await service.GetLyricsAsync(Query);

        Assert.AreEqual("parsed-src", doc!.SourceProvider);
        Assert.AreEqual("parsed-lang", doc.LanguageCode);
        Assert.AreEqual(222, doc.OffsetMs);
    }

    // ---- Fakes ----

    private sealed class StubRegistry : ILyricsParserRegistry
    {
        private readonly LyricsDocument? _document;

        public StubRegistry(LyricsDocument? document) => _document = document;

        public LyricsDocument? Parse(string formatTag, ReadOnlyMemory<byte> payload) => _document;
    }

    private sealed class FakeLyricsProvider : ILyricsProvider
    {
        private readonly Func<LyricsQuery, LyricsProviderResult> _handler;

        public FakeLyricsProvider(Func<LyricsQuery, LyricsProviderResult> handler) => _handler = handler;

        public string Id { get; set; } = "subsonic";
        public int CallCount { get; private set; }
        public TaskCompletionSource<bool>? Gate { get; set; }

        public async Task<LyricsProviderResult> FetchAsync(LyricsQuery query, CancellationToken cancellationToken)
        {
            CallCount++;
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
