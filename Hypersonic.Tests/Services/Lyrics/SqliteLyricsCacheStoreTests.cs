using System.Text;

using Hypersonic.Contracts.Services;
using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Models;
using Hypersonic.Services;
using Hypersonic.Services.Cache;
using Hypersonic.Tests.Fakes;
using Hypersonic.Tests.Services.Cache;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Hypersonic.Tests.Services.Lyrics;

[TestClass]
public class SqliteLyricsCacheStoreTests
{
    private string _cacheParent = null!;
    private string _cacheRoot = null!;

    [TestInitialize]
    public void Setup()
    {
        _cacheParent = Path.Combine(Path.GetTempPath(), "hypersonic-lyrics-cache-" + Guid.NewGuid().ToString("N"));
        _cacheRoot = Path.Combine(_cacheParent, "Lyrics");
        Directory.CreateDirectory(_cacheRoot);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (!Directory.Exists(_cacheParent))
        {
            return;
        }

        try
        {
            Directory.Delete(_cacheParent, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort.
        }
    }

    [TestMethod]
    public void CacheFolderPath_ComposesCacheParentWithLyricsSubfolder()
    {
        using var store = CreateStore();
        Assert.AreEqual(Path.Combine(_cacheParent, "Lyrics"), store.CacheFolderPath);
    }

    [TestMethod]
    public async Task UpsertAndGet_RoundTripsAllFieldsIncludingNullables()
    {
        using var store = CreateStore();
        var created = new DateTimeOffset(2026, 6, 9, 10, 0, 0, 123, TimeSpan.Zero);

        var key = new string('a', 64);
        await store.UpsertEntryAsync(key, new LyricsCacheEntry
        {
            FileName = key + ".json",
            Length = 321,
            SourceProvider = "subsonic",
            Format = LyricsFormat.SubsonicStructured,
            SyncKind = LyricsSyncKind.Line,
            LanguageCode = "en",
            OffsetMs = -250,
            CreatedAtUtc = created,
            LastAccessUtc = created.AddMinutes(3),
            ExpiresAtUtc = created.AddDays(30),
        });

        var read = await store.GetEntryAsync(key);

        Assert.IsNotNull(read);
        Assert.AreEqual(key + ".json", read!.FileName);
        Assert.AreEqual(321, read.Length);
        Assert.AreEqual("subsonic", read.SourceProvider);
        Assert.AreEqual(LyricsFormat.SubsonicStructured, read.Format);
        Assert.AreEqual(LyricsSyncKind.Line, read.SyncKind);
        Assert.AreEqual("en", read.LanguageCode);
        Assert.AreEqual(-250, read.OffsetMs);
        AssertWithinMs(created, read.CreatedAtUtc);
        AssertWithinMs(created.AddDays(30), read.ExpiresAtUtc);
    }

    [TestMethod]
    public async Task UpsertAndGet_PreservesNullLanguageAndOffset()
    {
        using var store = CreateStore();
        var now = new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero);
        var key = new string('b', 64);

        await store.UpsertEntryAsync(key, new LyricsCacheEntry
        {
            FileName = key + ".txt",
            Length = 10,
            SourceProvider = "subsonic",
            Format = LyricsFormat.PlainText,
            SyncKind = LyricsSyncKind.Plain,
            LanguageCode = null,
            OffsetMs = null,
            CreatedAtUtc = now,
            LastAccessUtc = now,
            ExpiresAtUtc = now.AddDays(30),
        });

        var read = await store.GetEntryAsync(key);

        Assert.IsNotNull(read);
        Assert.IsNull(read!.LanguageCode);
        Assert.IsNull(read.OffsetMs);
        Assert.AreEqual(LyricsSyncKind.Plain, read.SyncKind);
    }

    [TestMethod]
    public async Task GetEntry_ReturnsNullWhenAbsent()
    {
        using var store = CreateStore();
        Assert.IsNull(await store.GetEntryAsync(new string('0', 64)));
    }

    [TestMethod]
    public async Task TouchEntry_UpdatesOnlyLastAccess()
    {
        using var store = CreateStore();
        var key = new string('c', 64);
        var initial = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        await store.UpsertEntryAsync(key, Positive(key, initial));

        await store.TouchEntryAsync(key, initial.AddHours(2));

        var read = await store.GetEntryAsync(key);
        AssertWithinMs(initial.AddHours(2), read!.LastAccessUtc);
        AssertWithinMs(initial, read.CreatedAtUtc);
    }

    [TestMethod]
    public async Task WriteAndReadPayload_RoundTripsBytesAndEntry()
    {
        using var store = CreateStore();
        var key = new string('d', 64);
        var bytes = Encoding.UTF8.GetBytes("{\"synced\":true}");
        var entry = Positive(key, new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero));

        var uri = await store.WritePayloadAsync(key, bytes, entry);
        Assert.IsNotNull(uri);

        var payload = await store.ReadPayloadAsync(key);

        Assert.IsNotNull(payload);
        CollectionAssert.AreEqual(bytes, payload!.Bytes.ToArray());
        Assert.AreEqual(LyricsFormat.SubsonicStructured, payload.Entry.Format);
        Assert.AreEqual(LyricsSyncKind.Line, payload.Entry.SyncKind);
    }

    [TestMethod]
    public async Task ReadPayload_ReturnsNullForNegativeRow()
    {
        using var store = CreateStore();
        var key = new string('e', 64);
        var now = new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero);

        await store.WriteNegativeAsync(key, new LyricsCacheEntry
        {
            FileName = string.Empty,
            Length = 0,
            SourceProvider = "subsonic",
            Format = LyricsFormat.Unknown,
            SyncKind = LyricsSyncKind.None,
            CreatedAtUtc = now,
            LastAccessUtc = now,
            ExpiresAtUtc = now.AddDays(7),
        });

        var entry = await store.GetEntryAsync(key);
        Assert.IsNotNull(entry, "the negative row is readable as an entry");
        Assert.AreEqual(LyricsSyncKind.None, entry!.SyncKind);
        Assert.AreEqual(string.Empty, entry.FileName);

        Assert.IsNull(await store.ReadPayloadAsync(key), "negative rows have no payload bytes");
    }

    [TestMethod]
    public async Task ReadPayload_RemovesOrphanRowWhenFileMissing()
    {
        using var store = CreateStore();
        var key = new string('f', 64);
        await store.UpsertEntryAsync(key, Positive(key, new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero)));
        // Row exists but no payload file was ever written.

        Assert.IsNull(await store.ReadPayloadAsync(key));
        Assert.IsNull(await store.GetEntryAsync(key), "orphan row should be auto-removed");
    }

    [TestMethod]
    public async Task Reconcile_RemovesOrphanFilesAndRows_PreservesUnrelated()
    {
        using var store = CreateStore();
        var orphanFile = Path.Combine(_cacheRoot, new string('1', 64) + ".json");
        var unrelated = Path.Combine(_cacheRoot, "user-notes.txt");
        await File.WriteAllTextAsync(orphanFile, "{}");
        await File.WriteAllTextAsync(unrelated, "keep me");

        await store.ReconcileAsync();

        Assert.IsFalse(File.Exists(orphanFile), "payload-shaped orphan deleted");
        Assert.IsTrue(File.Exists(unrelated), "non-payload file preserved");
    }

    [TestMethod]
    public async Task GetStats_SumsLengthAndCount()
    {
        using var store = CreateStore();
        var now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        await store.UpsertEntryAsync(new string('1', 64), Positive(new string('1', 64), now, length: 100));
        await store.UpsertEntryAsync(new string('2', 64), Positive(new string('2', 64), now, length: 50));

        var stats = await store.GetStatsAsync();

        Assert.AreEqual(2, stats.EntryCount);
        Assert.AreEqual(150, stats.TotalBytes);
    }

    [TestMethod]
    public async Task ReopeningStore_PreservesEntries()
    {
        var key = new string('9', 64);
        var now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var first = CreateStore();
        await first.UpsertEntryAsync(key, Positive(key, now));
        first.Dispose();
        SqliteConnection.ClearAllPools();

        using var second = CreateStore();
        var read = await second.GetEntryAsync(key);
        Assert.IsNotNull(read);
        Assert.AreEqual(LyricsFormat.SubsonicStructured, read!.Format);
    }

    [TestMethod]
    public async Task IsKnownLyricsFileName_AcceptsJsonAndTxt_RejectsOthers()
    {
        await Task.Yield();
        Assert.IsTrue(SqliteLyricsCacheStore.IsKnownLyricsFileName(new string('a', 64) + ".json"));
        Assert.IsTrue(SqliteLyricsCacheStore.IsKnownLyricsFileName(new string('a', 64) + ".txt"));
        Assert.IsTrue(SqliteLyricsCacheStore.IsKnownLyricsFileName(new string('a', 64) + ".LRC"));
        Assert.IsFalse(SqliteLyricsCacheStore.IsKnownLyricsFileName("cache.db"));
        Assert.IsFalse(SqliteLyricsCacheStore.IsKnownLyricsFileName("manifest.json"));
        Assert.IsFalse(SqliteLyricsCacheStore.IsKnownLyricsFileName(new string('a', 64) + ".png"));
        Assert.IsFalse(SqliteLyricsCacheStore.IsKnownLyricsFileName(new string('z', 64) + ".json"));
    }

    [TestMethod]
    public async Task WriteNegative_DeletesPriorPositivePayload()
    {
        using var store = CreateStore();
        var key = new string('7', 64);
        var now = new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero);

        var uri = await store.WritePayloadAsync(key, Encoding.UTF8.GetBytes("{\"x\":1}"), Positive(key, now));
        Assert.IsNotNull(uri);
        Assert.IsTrue(File.Exists(uri!.LocalPath));

        await store.WriteNegativeAsync(key, new LyricsCacheEntry
        {
            FileName = string.Empty,
            Length = 0,
            SourceProvider = "subsonic",
            Format = LyricsFormat.Unknown,
            SyncKind = LyricsSyncKind.None,
            CreatedAtUtc = now,
            LastAccessUtc = now,
            ExpiresAtUtc = now.AddDays(7),
        });

        Assert.IsFalse(File.Exists(uri.LocalPath), "prior positive payload must be deleted when the entry goes negative");
        var entry = await store.GetEntryAsync(key);
        Assert.AreEqual(string.Empty, entry!.FileName);
        Assert.AreEqual(LyricsSyncKind.None, entry.SyncKind);
    }

    [TestMethod]
    public async Task LegacyV1Row_IsDiscardedOnUpgrade()
    {
        // Hand-craft a v1 DB (old PayloadFormat column + a legacy-tag row) where the store will open it.
        var dbPath = Path.Combine(_cacheRoot, "cache.db");
        await using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            await using var create = connection.CreateCommand();
            create.CommandText = """
                CREATE TABLE Entries (
                    KeyHash TEXT PRIMARY KEY NOT NULL, FileName TEXT NOT NULL, Length INTEGER NOT NULL,
                    SourceProvider TEXT NOT NULL, PayloadFormat TEXT NOT NULL, SyncKind TEXT NOT NULL,
                    LanguageCode TEXT, OffsetMs INTEGER, CreatedAtUtc INTEGER NOT NULL,
                    LastAccessUtc INTEGER NOT NULL, ExpiresAtUtc INTEGER NOT NULL) WITHOUT ROWID;
                INSERT INTO Entries VALUES ('deadbeef','x.lrc',3,'netease','netease-lrc','Line',NULL,NULL,0,0,999999999999);
                PRAGMA user_version = 1;
                """;
            await create.ExecuteNonQueryAsync();
        }
        SqliteConnection.ClearAllPools(); // release the handle so the store can reopen the file

        // Opening the store triggers the v1→v2 migration step (DROP + recreate).
        var store = CreateStore();
        var read = await store.GetEntryAsync("deadbeef", CancellationToken.None);
        Assert.IsNull(read); // discarded — a later fetch repopulates under the v2 schema
    }

    private SqliteLyricsCacheStore CreateStore(LyricsCacheOptions? options = null)
    {
        options ??= new LyricsCacheOptions();
        return new SqliteLyricsCacheStore(
            Options.Create(options),
            new FixedAppFolderLocator(_cacheParent),
            new NullLoggingService());
    }

    private static LyricsCacheEntry Positive(string key, DateTimeOffset at, long length = 64) => new()
    {
        FileName = key + ".json",
        Length = length,
        SourceProvider = "subsonic",
        Format = LyricsFormat.SubsonicStructured,
        SyncKind = LyricsSyncKind.Line,
        LanguageCode = "en",
        OffsetMs = 0,
        CreatedAtUtc = at,
        LastAccessUtc = at,
        ExpiresAtUtc = at.AddDays(30),
    };

    private static void AssertWithinMs(DateTimeOffset expected, DateTimeOffset actual)
    {
        var delta = (actual - expected).Duration();
        Assert.IsTrue(delta < TimeSpan.FromMilliseconds(1), $"Expected {expected:O} within 1ms of {actual:O} (delta {delta}).");
    }
}
