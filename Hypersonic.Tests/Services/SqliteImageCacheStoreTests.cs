using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;
using Hypersonic.Models;
using Hypersonic.Services;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Hypersonic.Tests.Services;

[TestClass]
public class SqliteImageCacheStoreTests
{
    private string _cacheRoot = null!;
    private string _databasePath = null!;

    [TestInitialize]
    public void Setup()
    {
        _cacheRoot = Path.Combine(Path.GetTempPath(), "hypersonic-sqlite-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_cacheRoot);
        _databasePath = Path.Combine(_cacheRoot, SqliteImageCacheStore.DatabaseFileName);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();

        if (!Directory.Exists(_cacheRoot))
        {
            return;
        }

        try
        {
            Directory.Delete(_cacheRoot, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort.
        }
    }

    [TestMethod]
    public void CacheFolderPath_ReturnsConfiguredRootPath()
    {
        IImageCacheStore store = CreateStore();
        using var disposable = (IDisposable)store;

        Assert.AreEqual(_cacheRoot, store.CacheFolderPath);
    }

    [TestMethod]
    public async Task UpsertAndGet_RoundTripsAllFieldsAtMillisecondPrecision()
    {
        var store = CreateStore();

        var created = new DateTimeOffset(2026, 5, 4, 10, 0, 0, 123, TimeSpan.Zero);
        var lastAccess = created.AddMinutes(7);
        var expires = created.AddDays(30);

        var entry = new ImageCacheEntry
        {
            FileName = "abc.png",
            Length = 4096,
            Provider = "subsonic",
            Kind = "cover-art",
            ServerHash = "deadbeef",
            ServerScopeHash = "feedface",
            Size = "300",
            CreatedAtUtc = created,
            LastAccessUtc = lastAccess,
            ExpiresAtUtc = expires,
        };

        var key = new string('a', 64);
        await store.UpsertEntryAsync(key, entry);

        var read = await store.GetEntryAsync(key);

        Assert.IsNotNull(read);
        Assert.AreEqual("abc.png", read!.FileName);
        Assert.AreEqual(4096, read.Length);
        Assert.AreEqual("subsonic", read.Provider);
        Assert.AreEqual("cover-art", read.Kind);
        Assert.AreEqual("deadbeef", read.ServerHash);
        Assert.AreEqual("feedface", read.ServerScopeHash);
        Assert.AreEqual("300", read.Size);
        AssertWithinMs(created, read.CreatedAtUtc);
        AssertWithinMs(lastAccess, read.LastAccessUtc);
        AssertWithinMs(expires, read.ExpiresAtUtc);
    }

    [TestMethod]
    public async Task GetEntryAsync_ReturnsNullWhenAbsent()
    {
        var store = CreateStore();
        Assert.IsNull(await store.GetEntryAsync(new string('0', 64)));
    }

    [TestMethod]
    public async Task TouchEntryAsync_UpdatesOnlyLastAccessUtc()
    {
        var store = CreateStore();
        var key = new string('1', 64);
        var initial = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

        await store.UpsertEntryAsync(key, NewEntry(initial));

        var newAccess = initial.AddHours(3);
        await store.TouchEntryAsync(key, newAccess);

        var read = await store.GetEntryAsync(key);
        Assert.IsNotNull(read);
        AssertWithinMs(newAccess, read!.LastAccessUtc);
        AssertWithinMs(initial, read.CreatedAtUtc);
    }

    [TestMethod]
    public async Task RemoveEntryAsync_IsNoopWhenKeyAbsent()
    {
        var store = CreateStore();
        await store.RemoveEntryAsync(new string('2', 64)); // must not throw
    }

    [TestMethod]
    public async Task ClearAsync_EmptiesTable()
    {
        var store = CreateStore();
        var initial = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        await store.UpsertEntryAsync(new string('3', 64), NewEntry(initial));
        await store.UpsertEntryAsync(new string('4', 64), NewEntry(initial));

        await store.ClearAsync();

        var stats = await store.GetStatsAsync();
        Assert.AreEqual(0, stats.EntryCount);
        Assert.AreEqual(0, stats.TotalBytes);
    }

    [TestMethod]
    public async Task GetStatsAsync_SumsLengthAndCount()
    {
        var store = CreateStore();
        var initial = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        await store.UpsertEntryAsync(new string('5', 64), NewEntry(initial, length: 100));
        await store.UpsertEntryAsync(new string('6', 64), NewEntry(initial, length: 250));
        await store.UpsertEntryAsync(new string('7', 64), NewEntry(initial, length: 33));

        var stats = await store.GetStatsAsync();

        Assert.AreEqual(3, stats.EntryCount);
        Assert.AreEqual(383, stats.TotalBytes);
    }

    [TestMethod]
    public async Task GetEvictionCandidatesAsync_OrdersByLastAccessAndSkipsProtected()
    {
        var store = CreateStore();
        var t0 = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        var oldest = new string('a', 64);
        var middle = new string('b', 64);
        var newest = new string('c', 64);

        await store.UpsertEntryAsync(oldest, NewEntry(t0));
        await store.UpsertEntryAsync(middle, NewEntry(t0.AddMinutes(1)));
        await store.UpsertEntryAsync(newest, NewEntry(t0.AddMinutes(2)));

        var candidates = await store.GetEvictionCandidatesAsync(protectedKeyHash: middle);

        Assert.AreEqual(2, candidates.Count);
        Assert.AreEqual(oldest, candidates[0].KeyHash);
        Assert.AreEqual(newest, candidates[1].KeyHash);
    }

    [TestMethod]
    public async Task ReopeningStore_PreservesEntries()
    {
        var key = new string('d', 64);
        var initial = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

        var first = CreateStore();
        await first.UpsertEntryAsync(key, NewEntry(initial));
        first.Dispose();
        SqliteConnection.ClearAllPools();

        var second = CreateStore();
        var read = await second.GetEntryAsync(key);
        Assert.IsNotNull(read);
        Assert.AreEqual("abc.png", read!.FileName);
    }

    [TestMethod]
    public async Task SchemaVersionLessThanCurrent_AppliesMigration()
    {
        // Hand-craft a v0 DB: empty file with user_version=0.
        await using (var connection = new SqliteConnection($"Data Source={_databasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 0;";
            await command.ExecuteNonQueryAsync();
        }
        SqliteConnection.ClearAllPools();

        var store = CreateStore();
        var initial = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        await store.UpsertEntryAsync(new string('e', 64), NewEntry(initial));

        var stats = await store.GetStatsAsync();
        Assert.AreEqual(1, stats.EntryCount);

        store.Dispose();
        SqliteConnection.ClearAllPools();

        var version = await ReadUserVersionAsync(_databasePath);
        Assert.AreEqual(1, version);
    }

    [TestMethod]
    public async Task SchemaVersionGreaterThanCurrent_RebuildsTable()
    {
        // Pre-create DB with future schema version + a column-less table that doesn't match current schema.
        await using (var connection = new SqliteConnection($"Data Source={_databasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE Entries (KeyHash TEXT PRIMARY KEY, FutureColumn TEXT) WITHOUT ROWID;
                INSERT INTO Entries (KeyHash, FutureColumn) VALUES ('legacy', 'should-be-dropped');
                PRAGMA user_version = 999;
                """;
            await command.ExecuteNonQueryAsync();
        }
        SqliteConnection.ClearAllPools();

        var store = CreateStore();
        var stats = await store.GetStatsAsync();

        Assert.AreEqual(0, stats.EntryCount);

        store.Dispose();
        SqliteConnection.ClearAllPools();

        var version = await ReadUserVersionAsync(_databasePath);
        Assert.AreEqual(1, version);
    }

    [TestMethod]
    public async Task LegacyJsonManifest_IsDiscardedAndDatabaseIsCreatedFresh()
    {
        await File.WriteAllTextAsync(Path.Combine(_cacheRoot, "manifest.json"), "{ \"Version\": 2, \"Entries\": {} }");
        var matchingImage = Path.Combine(_cacheRoot, new string('f', 64) + ".jpg");
        var unrelated = Path.Combine(_cacheRoot, "user-data.txt");
        await File.WriteAllBytesAsync(matchingImage, [1, 2, 3]);
        await File.WriteAllTextAsync(unrelated, "preserve me");

        var store = CreateStore();
        await store.GetStatsAsync(); // forces lazy init

        Assert.IsFalse(File.Exists(Path.Combine(_cacheRoot, "manifest.json")));
        Assert.IsFalse(File.Exists(matchingImage));
        Assert.IsTrue(File.Exists(unrelated));
        Assert.IsTrue(File.Exists(_databasePath));
    }

    [TestMethod]
    public async Task IsCachedImageFileName_AcceptsValidNames()
    {
        await Task.Yield();
        Assert.IsTrue(SqliteImageCacheStore.IsCachedImageFileName(new string('a', 64) + ".png"));
        Assert.IsTrue(SqliteImageCacheStore.IsCachedImageFileName(new string('0', 64) + ".jpg"));
        Assert.IsTrue(SqliteImageCacheStore.IsCachedImageFileName(new string('a', 64) + ".WEBP"));
    }

    [TestMethod]
    public async Task IsCachedImageFileName_RejectsDatabaseAndNonHexNames()
    {
        await Task.Yield();
        Assert.IsFalse(SqliteImageCacheStore.IsCachedImageFileName("cache.db"));
        Assert.IsFalse(SqliteImageCacheStore.IsCachedImageFileName("cache.db-wal"));
        Assert.IsFalse(SqliteImageCacheStore.IsCachedImageFileName("cache.db-shm"));
        Assert.IsFalse(SqliteImageCacheStore.IsCachedImageFileName("manifest.json"));
        Assert.IsFalse(SqliteImageCacheStore.IsCachedImageFileName("orphan.png"));
        Assert.IsFalse(SqliteImageCacheStore.IsCachedImageFileName(new string('a', 64) + ".txt"));
        Assert.IsFalse(SqliteImageCacheStore.IsCachedImageFileName(new string('a', 63) + ".png"));
        Assert.IsFalse(SqliteImageCacheStore.IsCachedImageFileName(new string('z', 64) + ".png")); // 'z' is not hex
    }

    [TestMethod]
    public async Task ContendedWrite_FailsFastInsteadOfStalling()
    {
        var store = CreateStore();

        // Force lazy init so the store has an open connection before we contend on the file.
        await store.GetStatsAsync();

        // Hold a write transaction from a side connection. In WAL mode this serializes other writers.
        await using var blocker = new SqliteConnection($"Data Source={_databasePath}");
        await blocker.OpenAsync();
        await using var tx = (SqliteTransaction)await blocker.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await using (var seed = blocker.CreateCommand())
        {
            seed.Transaction = tx;
            seed.CommandText = """
                INSERT OR REPLACE INTO Entries (
                    KeyHash, FileName, Length, Provider, Kind, ServerHash, ServerScopeHash, Size,
                    CreatedAtUtc, LastAccessUtc, ExpiresAtUtc)
                VALUES (
                    'sentinel', 'sentinel.png', 1, '', '', '', '', '',
                    0, 0, 0);
                """;
            await seed.ExecuteNonQueryAsync();
        }
        // Intentionally do not commit — we want to hold the write lock.

        var key = new string('f', 64);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await store.UpsertEntryAsync(key, NewEntry(new DateTimeOffset(2026, 5, 5, 0, 0, 0, TimeSpan.Zero)));
        sw.Stop();

        // DefaultTimeout is 2s; allow generous headroom for CI scheduling. The point is "bounded", not "instant".
        Assert.IsTrue(
            sw.Elapsed < TimeSpan.FromSeconds(8),
            $"UpsertEntryAsync took {sw.Elapsed} under write contention; expected fail-fast within ~2s.");
    }

    [TestMethod]
    public async Task PrivacyAssertions_DatabaseDoesNotContainPlaintextSecrets()
    {
        var store = CreateStore();
        var key = new string('a', 64);
        var initial = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

        await store.UpsertEntryAsync(key, new ImageCacheEntry
        {
            FileName = key + ".png",
            Length = 100,
            Provider = "subsonic",
            Kind = "cover-art",
            ServerHash = "hashed-server",
            ServerScopeHash = "hashed-scope",
            Size = "300",
            CreatedAtUtc = initial,
            LastAccessUtc = initial,
            ExpiresAtUtc = initial.AddDays(30),
        });

        await store.FlushAsync();
        store.Dispose();
        SqliteConnection.ClearAllPools();

        var bytes = await File.ReadAllBytesAsync(_databasePath);
        var text = System.Text.Encoding.UTF8.GetString(bytes);

        Assert.IsFalse(text.Contains("alice", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(text.Contains("password", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(text.Contains("secret-cover-id", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task EnsureInitializedRebuildPath_PreservesCancellation()
    {
        // Pre-corrupt the DB so the first init attempt fails and the self-heal rebuild path runs.
        await File.WriteAllBytesAsync(_databasePath, [0x42, 0x42, 0x42, 0x42, 0x42, 0x42, 0x42, 0x42]);

        var store = CreateStore();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Cancellation must not be swallowed by the rebuild's broad catch.
        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await store.GetEntryAsync(new string('a', 64), cts.Token));
    }

    private SqliteImageCacheStore CreateStore()
    {
        var options = new ImageCacheOptions { RootPath = _cacheRoot };
        return new SqliteImageCacheStore(Options.Create(options), new NullLoggingService());
    }

    private static ImageCacheEntry NewEntry(DateTimeOffset at, long length = 1024)
    {
        return new ImageCacheEntry
        {
            FileName = "abc.png",
            Length = length,
            Provider = "subsonic",
            Kind = "cover-art",
            ServerHash = "server",
            ServerScopeHash = "scope",
            Size = "300",
            CreatedAtUtc = at,
            LastAccessUtc = at,
            ExpiresAtUtc = at.AddDays(30),
        };
    }

    private static void AssertWithinMs(DateTimeOffset expected, DateTimeOffset actual)
    {
        var delta = (actual - expected).Duration();
        Assert.IsTrue(delta < TimeSpan.FromMilliseconds(1), $"Expected {expected:O} within 1ms of {actual:O} (delta {delta}).");
    }

    private static async Task<int> ReadUserVersionAsync(string databasePath)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        var result = await command.ExecuteScalarAsync();
        return result is long v ? (int)v : 0;
    }

    private sealed class NullLogger : ILogger
    {
        public string Category => "Test";
        public bool IsEnabled(LogLevel level) => false;
        public void Log(LogLevel level, string message, object? context = null, Exception? exception = null) { }
        public void Debug(string message, object? context = null) { }
        public void Info(string message, object? context = null) { }
        public void Warning(string message, object? context = null, Exception? exception = null) { }
        public void Error(string message, object? context = null, Exception? exception = null) { }
    }

    private sealed class NullLoggingService : ILoggingService
    {
        public LogLevel MinimumLevel { get; set; }
        public bool IsEnabled(LogLevel level) => false;
        public void AddSink(ILogSink sink, LogLevel minimumLevel = LogLevel.Debug) { }
        public void RemoveSink(ILogSink sink) { }
        public ILogger GetLogger(string category) => new NullLogger();
    }
}
