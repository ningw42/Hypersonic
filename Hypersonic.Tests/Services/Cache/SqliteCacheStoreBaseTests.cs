using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;
using Hypersonic.Services.Cache;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Hypersonic.Tests.Services.Cache;

[TestClass]
public class SqliteCacheStoreBaseTests
{
    private string _cacheParent = null!;
    private string _cacheRoot = null!;

    [TestInitialize]
    public void Setup()
    {
        _cacheParent = Path.Combine(Path.GetTempPath(), "hypersonic-cache-base-" + Guid.NewGuid().ToString("N"));
        _cacheRoot = Path.Combine(_cacheParent, "Payload");
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
    public async Task GetStatsAsync_FreshStore_ReturnsZeroBytesAndZeroCount()
    {
        using var store = CreateStore();

        var stats = await store.GetStatsAsync();

        Assert.AreEqual(0L, stats.TotalBytes);
        Assert.AreEqual(0, stats.EntryCount);
    }

    [TestMethod]
    public async Task SchemaVersionZero_AppliesV0ToCurrentShortcutAndStampsUserVersion()
    {
        var databasePath = Path.Combine(_cacheRoot, "cache.db");

        // Hand-craft a v0 DB: empty file with user_version=0 explicitly set.
        await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 0;";
            await command.ExecuteNonQueryAsync();
        }
        SqliteConnection.ClearAllPools();

        using (var store = CreateStore())
        {
            // First touch triggers init -> schema migration.
            await store.GetStatsAsync();
        }
        SqliteConnection.ClearAllPools();

        var version = await ReadUserVersionAsync(databasePath);
        Assert.AreEqual(1, version);
    }

    [TestMethod]
    public async Task SchemaVersionGreaterThanCurrent_RebuildsTableAndStampsCurrentVersion()
    {
        var databasePath = Path.Combine(_cacheRoot, "cache.db");

        // Pre-create DB with a future schema version and a divergent table that the
        // current schema does not match. The rebuild path should drop the table and
        // recreate from CreateCurrentSchemaSql.
        await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
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

        using (var store = CreateStore())
        {
            var stats = await store.GetStatsAsync();
            Assert.AreEqual(0, stats.EntryCount, "Rebuild must drop pre-existing rows.");
        }
        SqliteConnection.ClearAllPools();

        var version = await ReadUserVersionAsync(databasePath);
        Assert.AreEqual(1, version);
    }

    [TestMethod]
    public async Task CorruptedDatabase_SelfHealsOnFirstAccess()
    {
        var databasePath = Path.Combine(_cacheRoot, "cache.db");

        // Pre-corrupt the DB file so the first open + schema check fails.
        await File.WriteAllBytesAsync(databasePath, [0x42, 0x42, 0x42, 0x42, 0x42, 0x42, 0x42, 0x42]);

        using (var store = CreateStore())
        {
            var stats = await store.GetStatsAsync();
            Assert.AreEqual(0, stats.EntryCount, "Self-heal rebuilds an empty cache.");
            Assert.AreEqual(0L, stats.TotalBytes);
        }
        SqliteConnection.ClearAllPools();

        var version = await ReadUserVersionAsync(databasePath);
        Assert.AreEqual(1, version, "Rebuilt DB must carry the current schema version.");
    }

    [TestMethod]
    public async Task UpsertAndGet_RoundTripsAllRequiredFields()
    {
        using var store = CreateStore();

        var lastAccess = new DateTimeOffset(2026, 5, 10, 12, 0, 0, 123, TimeSpan.Zero);
        var entry = new TestCacheEntry
        {
            FileName = new string('a', 64) + ".bin",
            Length = 1234,
            LastAccessUtc = lastAccess,
        };
        var keyHash = new string('a', 64);

        await store.UpsertEntryAsync(keyHash, entry);
        var read = await store.GetEntryAsync(keyHash);

        Assert.IsNotNull(read);
        Assert.AreEqual(entry.FileName, read!.FileName);
        Assert.AreEqual(entry.Length, read.Length);
        AssertWithinMs(lastAccess, read.LastAccessUtc);
    }

    [TestMethod]
    public async Task GetEntryAsync_ReturnsNullWhenAbsent()
    {
        using var store = CreateStore();
        Assert.IsNull(await store.GetEntryAsync(new string('0', 64)));
    }

    [TestMethod]
    public async Task TouchEntryAsync_UpdatesOnlyLastAccessUtc()
    {
        using var store = CreateStore();
        var keyHash = new string('1', 64);
        var initial = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

        await store.UpsertEntryAsync(keyHash, new TestCacheEntry
        {
            FileName = "abc.bin",
            Length = 100,
            LastAccessUtc = initial,
        });

        var newAccess = initial.AddHours(3);
        await store.TouchEntryAsync(keyHash, newAccess);

        var read = await store.GetEntryAsync(keyHash);
        Assert.IsNotNull(read);
        Assert.AreEqual("abc.bin", read!.FileName);
        Assert.AreEqual(100, read.Length);
        AssertWithinMs(newAccess, read.LastAccessUtc);
    }

    [TestMethod]
    public async Task RemoveEntryAsync_RemovesRowAndIsNoopWhenAbsent()
    {
        using var store = CreateStore();
        var keyHash = new string('2', 64);

        await store.RemoveEntryAsync(keyHash); // absent: must not throw

        await store.UpsertEntryAsync(keyHash, new TestCacheEntry
        {
            FileName = "f.bin",
            Length = 7,
            LastAccessUtc = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
        });
        Assert.IsNotNull(await store.GetEntryAsync(keyHash));

        await store.RemoveEntryAsync(keyHash);
        Assert.IsNull(await store.GetEntryAsync(keyHash));
    }

    [TestMethod]
    public async Task GetEvictionCandidatesAsync_OrdersByLastAccessSkipsProtectedSkipsNegatives()
    {
        using var store = CreateStore();
        var t0 = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

        var oldest = new string('a', 64);
        var middle = new string('b', 64);
        var newest = new string('c', 64);
        var negative = new string('d', 64);

        await store.UpsertEntryAsync(oldest, new TestCacheEntry { FileName = "o.bin", Length = 1, LastAccessUtc = t0 });
        await store.UpsertEntryAsync(middle, new TestCacheEntry { FileName = "m.bin", Length = 1, LastAccessUtc = t0.AddMinutes(1) });
        await store.UpsertEntryAsync(newest, new TestCacheEntry { FileName = "n.bin", Length = 1, LastAccessUtc = t0.AddMinutes(2) });
        // Negative-cache row: empty FileName + Length=0. Must be skipped by eviction candidates.
        await store.UpsertEntryAsync(negative, new TestCacheEntry { FileName = string.Empty, Length = 0, LastAccessUtc = t0.AddSeconds(30) });

        var candidates = await store.GetEvictionCandidatesAsync(protectedKeyHash: middle);

        Assert.AreEqual(2, candidates.Count);
        Assert.AreEqual(oldest, candidates[0].KeyHash);
        Assert.AreEqual(newest, candidates[1].KeyHash);
    }

    [TestMethod]
    public async Task EnumerateAllEntriesAsync_SeesNegativeAndOrdinaryRows()
    {
        using var store = CreateStore();
        var t0 = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

        var ordinary = new string('a', 64);
        var negative = new string('b', 64);

        await store.UpsertEntryAsync(ordinary, new TestCacheEntry { FileName = "o.bin", Length = 5, LastAccessUtc = t0 });
        await store.UpsertEntryAsync(negative, new TestCacheEntry { FileName = string.Empty, Length = 0, LastAccessUtc = t0 });

        var all = await store.EnumerateAllEntriesAsync();
        var keys = all.Select(e => e.KeyHash).OrderBy(k => k).ToArray();

        CollectionAssert.AreEqual(new[] { ordinary, negative }.OrderBy(k => k).ToArray(), keys);
    }

    [TestMethod]
    public async Task ClearAsync_DeletesRowsAndKnownPayloadFilesButLeavesUnrelatedFilesAlone()
    {
        using var store = CreateStore();
        var t0 = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

        var keyA = new string('a', 64);
        var keyB = new string('b', 64);
        var payloadA = Path.Combine(_cacheRoot, keyA + ".bin");
        var payloadB = Path.Combine(_cacheRoot, keyB + ".bin");
        var unrelated = Path.Combine(_cacheRoot, "user-notes.txt");
        await File.WriteAllBytesAsync(payloadA, [1, 2, 3]);
        await File.WriteAllBytesAsync(payloadB, [4, 5, 6]);
        await File.WriteAllTextAsync(unrelated, "preserve me");

        await store.UpsertEntryAsync(keyA, new TestCacheEntry { FileName = keyA + ".bin", Length = 3, LastAccessUtc = t0 });
        await store.UpsertEntryAsync(keyB, new TestCacheEntry { FileName = keyB + ".bin", Length = 3, LastAccessUtc = t0 });

        await store.ClearAsync();

        var stats = await store.GetStatsAsync();
        Assert.AreEqual(0, stats.EntryCount);
        Assert.AreEqual(0L, stats.TotalBytes);
        Assert.IsFalse(File.Exists(payloadA), "Known payload file must be deleted.");
        Assert.IsFalse(File.Exists(payloadB), "Known payload file must be deleted.");
        Assert.IsTrue(File.Exists(unrelated), "Unrelated user files must be left alone.");
    }

    [TestMethod]
    public async Task FlushAsync_DoesNotThrow()
    {
        using var store = CreateStore();
        await store.UpsertEntryAsync(
            new string('a', 64),
            new TestCacheEntry
            {
                FileName = "f.bin",
                Length = 1,
                LastAccessUtc = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            });

        await store.FlushAsync();
    }

    [TestMethod]
    public async Task TryGetPayloadUriAsync_ReturnsNullOnMissingRow()
    {
        using var store = CreateStore();
        var uri = await store.TryGetPayloadUriAsync(new string('a', 64));
        Assert.IsNull(uri);
    }

    [TestMethod]
    public async Task TryGetPayloadUriAsync_ReturnsUriWhenRowAndFileExist()
    {
        using var store = CreateStore();
        var keyHash = new string('a', 64);
        var fileName = keyHash + ".bin";
        var filePath = Path.Combine(_cacheRoot, fileName);
        await File.WriteAllBytesAsync(filePath, [1, 2, 3]);

        await store.UpsertEntryAsync(keyHash, new TestCacheEntry
        {
            FileName = fileName,
            Length = 3,
            LastAccessUtc = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
        });

        var uri = await store.TryGetPayloadUriAsync(keyHash);

        Assert.IsNotNull(uri);
        Assert.AreEqual(filePath, uri!.LocalPath);
    }

    [TestMethod]
    public async Task TryGetPayloadUriAsync_RemovesOrphanRowAndReturnsNullWhenFileMissing()
    {
        using var store = CreateStore();
        var keyHash = new string('a', 64);
        var fileName = keyHash + ".bin";

        // Row says the file exists but the file is absent from disk.
        await store.UpsertEntryAsync(keyHash, new TestCacheEntry
        {
            FileName = fileName,
            Length = 3,
            LastAccessUtc = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
        });

        var uri = await store.TryGetPayloadUriAsync(keyHash);

        Assert.IsNull(uri);
        // Orphan-row cleanup: the row must have been swept by the read path.
        Assert.IsNull(await store.GetEntryAsync(keyHash));
    }

    [TestMethod]
    public async Task TryGetPayloadUriAsync_ReturnsNullForNegativeEntryWithoutRemovingRow()
    {
        using var store = CreateStore();
        var keyHash = new string('a', 64);

        // Negative-cache row: empty FileName + Length=0.
        await store.UpsertEntryAsync(keyHash, new TestCacheEntry
        {
            FileName = string.Empty,
            Length = 0,
            LastAccessUtc = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
        });

        var uri = await store.TryGetPayloadUriAsync(keyHash);

        Assert.IsNull(uri);
        // Negative entries must survive — service layer relies on them as the negative signal.
        var stillPresent = await store.GetEntryAsync(keyHash);
        Assert.IsNotNull(stillPresent);
        Assert.AreEqual(string.Empty, stillPresent!.FileName);
    }

    [TestMethod]
    public async Task TryGetPayloadUriAsync_DoesNotTouchLastAccess()
    {
        using var store = CreateStore();
        var keyHash = new string('a', 64);
        var fileName = keyHash + ".bin";
        var filePath = Path.Combine(_cacheRoot, fileName);
        await File.WriteAllBytesAsync(filePath, [1]);

        var initialAccess = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        await store.UpsertEntryAsync(keyHash, new TestCacheEntry
        {
            FileName = fileName,
            Length = 1,
            LastAccessUtc = initialAccess,
        });

        await store.TryGetPayloadUriAsync(keyHash);

        var read = await store.GetEntryAsync(keyHash);
        Assert.IsNotNull(read);
        // No touch policy: LastAccessUtc must be unchanged.
        AssertWithinMs(initialAccess, read!.LastAccessUtc);
    }

    [TestMethod]
    public async Task WritePayloadAsync_WritesFileAndRowAndReturnsUri()
    {
        using var store = CreateStore();
        var keyHash = new string('a', 64);
        var fileName = keyHash + ".bin";
        var bytes = new byte[] { 9, 8, 7, 6 };
        var entry = new TestCacheEntry
        {
            FileName = fileName,
            Length = bytes.Length,
            LastAccessUtc = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
        };

        var uri = await store.WritePayloadAsync(keyHash, bytes, entry);

        Assert.IsNotNull(uri);
        var expectedPath = Path.Combine(_cacheRoot, fileName);
        Assert.AreEqual(expectedPath, uri!.LocalPath);
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(expectedPath));

        var row = await store.GetEntryAsync(keyHash);
        Assert.IsNotNull(row);
        Assert.AreEqual(fileName, row!.FileName);
        Assert.AreEqual(bytes.Length, row.Length);
    }

    [TestMethod]
    public async Task WritePayloadAsync_OverwriteSameName_UpdatesFileAndRow()
    {
        using var store = CreateStore();
        var keyHash = new string('a', 64);
        var fileName = keyHash + ".bin";
        var t0 = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

        await store.WritePayloadAsync(
            keyHash,
            new byte[] { 1, 2, 3 },
            new TestCacheEntry { FileName = fileName, Length = 3, LastAccessUtc = t0 });

        var t1 = t0.AddMinutes(5);
        var newBytes = new byte[] { 10, 20, 30, 40, 50 };
        await store.WritePayloadAsync(
            keyHash,
            newBytes,
            new TestCacheEntry { FileName = fileName, Length = newBytes.Length, LastAccessUtc = t1 });

        var expectedPath = Path.Combine(_cacheRoot, fileName);
        CollectionAssert.AreEqual(newBytes, await File.ReadAllBytesAsync(expectedPath));

        var row = await store.GetEntryAsync(keyHash);
        Assert.IsNotNull(row);
        Assert.AreEqual(newBytes.Length, row!.Length);
        AssertWithinMs(t1, row.LastAccessUtc);

        // No stray sibling payload files.
        var payloadFiles = Directory.GetFiles(_cacheRoot, "*.bin");
        Assert.AreEqual(1, payloadFiles.Length, "Same-name overwrite must not leak old payload files.");
    }

    [TestMethod]
    public async Task WritePayloadAsync_OverwriteDifferentName_DeletesOldFile()
    {
        using var store = CreateStore();
        var keyHash = new string('a', 64);
        var firstName = keyHash + ".bin";  // .bin is the only ext TestCacheStore recognises;
                                            // simulate "different name" by renaming below.
        var t0 = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

        // Write the first payload then directly mutate the row to point at a different
        // filename so the next write triggers the "old != new" cleanup branch. The bin
        // extension stays the same so IsKnownPayloadFileName still recognises both.
        await store.WritePayloadAsync(
            keyHash,
            new byte[] { 1, 2 },
            new TestCacheEntry { FileName = firstName, Length = 2, LastAccessUtc = t0 });

        var alternateName = new string('a', 63) + "b.bin";  // different hex basename, same ext
        var alternatePath = Path.Combine(_cacheRoot, alternateName);
        File.Move(Path.Combine(_cacheRoot, firstName), alternatePath, overwrite: true);
        await store.UpsertEntryAsync(keyHash, new TestCacheEntry
        {
            FileName = alternateName,
            Length = 2,
            LastAccessUtc = t0,
        });
        Assert.IsTrue(File.Exists(alternatePath));

        // Now write again with the original filename. The base must delete alternatePath.
        await store.WritePayloadAsync(
            keyHash,
            new byte[] { 7, 8, 9 },
            new TestCacheEntry { FileName = firstName, Length = 3, LastAccessUtc = t0.AddHours(1) });

        Assert.IsTrue(File.Exists(Path.Combine(_cacheRoot, firstName)));
        Assert.IsFalse(File.Exists(alternatePath), "Old payload file must be deleted when FileName changes.");
    }

    [TestMethod]
    public async Task WritePayloadAsync_LeavesNoTempFiles()
    {
        using var store = CreateStore();
        var keyHash = new string('a', 64);

        await store.WritePayloadAsync(
            keyHash,
            new byte[] { 1 },
            new TestCacheEntry { FileName = keyHash + ".bin", Length = 1, LastAccessUtc = DateTimeOffset.UtcNow });

        var tempFiles = Directory.GetFiles(_cacheRoot, "*.tmp");
        Assert.AreEqual(0, tempFiles.Length, "No temp files must linger after a successful write.");
    }

    [TestMethod]
    public async Task WriteNegativeAsync_WritesRowWithEmptyFileNameAndNoPayloadFile()
    {
        using var store = CreateStore();
        var keyHash = new string('a', 64);
        var t0 = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

        await store.WriteNegativeAsync(keyHash, new TestCacheEntry
        {
            FileName = string.Empty,
            Length = 0,
            LastAccessUtc = t0,
        });

        var row = await store.GetEntryAsync(keyHash);
        Assert.IsNotNull(row);
        Assert.AreEqual(string.Empty, row!.FileName);
        Assert.AreEqual(0L, row.Length);

        // No payload file written.
        var payloads = Directory.GetFiles(_cacheRoot, "*.bin");
        Assert.AreEqual(0, payloads.Length);

        // TryGetPayloadUriAsync returns null for negative entries (file-backed contract).
        Assert.IsNull(await store.TryGetPayloadUriAsync(keyHash));
    }

    [TestMethod]
    public async Task WriteNegativeAsync_RejectsEntriesWithFileNameOrLength()
    {
        using var store = CreateStore();
        var keyHash = new string('a', 64);
        var t0 = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await store.WriteNegativeAsync(keyHash, new TestCacheEntry
            {
                FileName = "not-empty.bin",
                Length = 0,
                LastAccessUtc = t0,
            }));

        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await store.WriteNegativeAsync(keyHash, new TestCacheEntry
            {
                FileName = string.Empty,
                Length = 1,
                LastAccessUtc = t0,
            }));
    }

    [TestMethod]
    public async Task EvictIfNeededAsync_OverBudget_EvictsLruRespectingProtectedKey()
    {
        // Cap = 250 bytes; three 100-byte entries puts us 50 over.
        using var store = CreateStore(maximumBytes: 250);

        var t0 = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        var oldest = new string('a', 64);
        var middle = new string('b', 64);
        var newest = new string('c', 64);
        var oldestFile = oldest + ".bin";
        var middleFile = middle + ".bin";
        var newestFile = newest + ".bin";

        var payload = new byte[100];

        await store.WritePayloadAsync(oldest, payload, new TestCacheEntry { FileName = oldestFile, Length = payload.Length, LastAccessUtc = t0 });
        await store.WritePayloadAsync(middle, payload, new TestCacheEntry { FileName = middleFile, Length = payload.Length, LastAccessUtc = t0.AddMinutes(1) });
        // The third write triggers post-write eviction, with `newest` protected.
        await store.WritePayloadAsync(newest, payload, new TestCacheEntry { FileName = newestFile, Length = payload.Length, LastAccessUtc = t0.AddMinutes(2) });

        var stats = await store.GetStatsAsync();
        Assert.IsTrue(stats.TotalBytes <= 250, $"Total bytes ({stats.TotalBytes}) must fit under the cap (250).");

        // Oldest must be evicted; middle and newest survive (middle is younger than oldest;
        // newest is the protected key from the triggering write).
        Assert.IsNull(await store.GetEntryAsync(oldest), "Oldest LRU entry must be evicted.");
        Assert.IsFalse(File.Exists(Path.Combine(_cacheRoot, oldestFile)));

        Assert.IsNotNull(await store.GetEntryAsync(newest), "Triggering write's entry must be protected.");
        Assert.IsTrue(File.Exists(Path.Combine(_cacheRoot, newestFile)));
    }

    [TestMethod]
    public async Task EvictIfNeededAsync_MaximumBytesZero_DisablesEviction()
    {
        // MaximumBytes = 0 ⇒ eviction disabled even when way over any reasonable cap.
        using var store = CreateStore(maximumBytes: 0);

        var payload = new byte[1024 * 1024]; // 1 MiB
        for (var i = 0; i < 5; i++)
        {
            var key = new string((char)('a' + i), 64);
            await store.WritePayloadAsync(
                key,
                payload,
                new TestCacheEntry
                {
                    FileName = key + ".bin",
                    Length = payload.Length,
                    LastAccessUtc = new DateTimeOffset(2026, 5, 1, 0, i, 0, TimeSpan.Zero),
                });
        }

        var stats = await store.GetStatsAsync();
        Assert.AreEqual(5, stats.EntryCount, "MaximumBytes=0 must disable eviction entirely.");
        Assert.AreEqual(5 * 1024L * 1024L, stats.TotalBytes);
    }

    [TestMethod]
    public async Task EvictIfNeededAsync_SkipsNegativeCacheRows()
    {
        // Negative rows are tiny (Length=0) and aren't eligible for file-eviction.
        // They must survive even when the cache is over budget.
        using var store = CreateStore(maximumBytes: 150);

        var t0 = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        var negative = new string('a', 64);
        var positive = new string('b', 64);

        await store.WriteNegativeAsync(negative, new TestCacheEntry { FileName = string.Empty, Length = 0, LastAccessUtc = t0 });
        // 100 bytes — single positive fits the cap.
        await store.WritePayloadAsync(positive, new byte[100], new TestCacheEntry { FileName = positive + ".bin", Length = 100, LastAccessUtc = t0.AddMinutes(2) });
        // Triggering write — 200 bytes, pushes us 150 over.
        var triggering = new string('c', 64);
        await store.WritePayloadAsync(triggering, new byte[200], new TestCacheEntry { FileName = triggering + ".bin", Length = 200, LastAccessUtc = t0.AddMinutes(3) });

        // Negative row must survive eviction.
        var negativeRow = await store.GetEntryAsync(negative);
        Assert.IsNotNull(negativeRow, "Negative-cache row must not be evicted.");
        Assert.AreEqual(string.Empty, negativeRow!.FileName);
    }

    [TestMethod]
    public async Task ReconcileAsync_RemovesRowsWhoseFileIsMissing()
    {
        using var store = CreateStore();
        var t0 = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

        var present = new string('a', 64);
        var missing = new string('b', 64);
        var presentFile = present + ".bin";
        var missingFile = missing + ".bin";

        await store.WritePayloadAsync(present, new byte[5], new TestCacheEntry { FileName = presentFile, Length = 5, LastAccessUtc = t0 });
        await store.WritePayloadAsync(missing, new byte[5], new TestCacheEntry { FileName = missingFile, Length = 5, LastAccessUtc = t0 });

        // Delete one file behind the store's back.
        File.Delete(Path.Combine(_cacheRoot, missingFile));

        await store.ReconcileAsync();

        Assert.IsNotNull(await store.GetEntryAsync(present), "Row with existing file must survive.");
        Assert.IsNull(await store.GetEntryAsync(missing), "Row whose file is missing must be removed.");
    }

    [TestMethod]
    public async Task ReconcileAsync_DeletesOrphanFilesAndLeavesUnrelatedFilesAlone()
    {
        using var store = CreateStore();
        var t0 = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

        var known = new string('a', 64);
        var knownFile = known + ".bin";
        await store.WritePayloadAsync(known, new byte[5], new TestCacheEntry { FileName = knownFile, Length = 5, LastAccessUtc = t0 });

        // Seed an orphan payload-shaped file (matches IsKnownPayloadFileName predicate)
        // and an unrelated file (must be left alone).
        var orphanName = new string('b', 64) + ".bin";
        var orphanPath = Path.Combine(_cacheRoot, orphanName);
        await File.WriteAllBytesAsync(orphanPath, [9, 9, 9]);

        var unrelated = Path.Combine(_cacheRoot, "user-notes.txt");
        await File.WriteAllTextAsync(unrelated, "preserve me");

        await store.ReconcileAsync();

        Assert.IsTrue(File.Exists(Path.Combine(_cacheRoot, knownFile)), "Known payload must survive.");
        Assert.IsFalse(File.Exists(orphanPath), "Orphan payload-shaped file must be deleted.");
        Assert.IsTrue(File.Exists(unrelated), "Unrelated files must be left alone.");
    }

    [TestMethod]
    public async Task ReconcileAsync_NegativeCacheRowsAreNotMistakenForOrphans()
    {
        using var store = CreateStore();
        var t0 = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

        var negative = new string('a', 64);
        await store.WriteNegativeAsync(negative, new TestCacheEntry { FileName = string.Empty, Length = 0, LastAccessUtc = t0 });

        await store.ReconcileAsync();

        var stillThere = await store.GetEntryAsync(negative);
        Assert.IsNotNull(stillThere, "Negative-cache row must survive reconcile.");
        Assert.AreEqual(string.Empty, stillThere!.FileName);
    }

    [TestMethod]
    public async Task ReconcileAsync_AbortsWithoutDeletingFilesWhenEnumerationFails()
    {
        using var store = CreateStore();
        var t0 = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

        // Seed a real entry + an orphan-shaped file. If the safety abort works, neither
        // is touched. If we treated enumeration failure as "empty list," the orphan
        // would be deleted (and so would the real one because its row wouldn't appear
        // in the "known files" set).
        var real = new string('a', 64);
        var realFile = real + ".bin";
        await store.WritePayloadAsync(real, new byte[5], new TestCacheEntry { FileName = realFile, Length = 5, LastAccessUtc = t0 });

        var orphan = new string('b', 64) + ".bin";
        var orphanPath = Path.Combine(_cacheRoot, orphan);
        await File.WriteAllBytesAsync(orphanPath, [9, 9, 9]);

        store.ThrowOnEnumerateAll = true;

        await store.ReconcileAsync();

        Assert.IsTrue(File.Exists(Path.Combine(_cacheRoot, realFile)), "Real payload must survive a failed reconcile.");
        Assert.IsTrue(File.Exists(orphanPath), "Orphan must survive a failed reconcile — abort is the whole point.");
    }

    [TestMethod]
    public async Task WriteStreamedPayloadAsync_WritesFileAndRowAndReturnsUri()
    {
        using var store = CreateStore();
        var keyHash = new string('a', 64);
        var fileName = keyHash + ".bin";
        var payload = new byte[] { 1, 2, 3, 4, 5 };

        var uri = await store.WriteStreamedPayloadAsync(
            keyHash,
            async (stream, ct) => await stream.WriteAsync(payload, ct),
            ctx => new TestCacheEntry
            {
                FileName = fileName,
                Length = ctx.Length,
                LastAccessUtc = ctx.WrittenAtUtc,
            });

        Assert.IsNotNull(uri);
        var finalPath = Path.Combine(_cacheRoot, fileName);
        Assert.AreEqual(finalPath, uri!.LocalPath);
        CollectionAssert.AreEqual(payload, await File.ReadAllBytesAsync(finalPath));

        var row = await store.GetEntryAsync(keyHash);
        Assert.IsNotNull(row);
        Assert.AreEqual(fileName, row!.FileName);
        Assert.AreEqual(payload.Length, row.Length);

        Assert.AreEqual(0, Directory.GetFiles(_cacheRoot, "*.tmp").Length);
    }

    [TestMethod]
    public async Task WriteStreamedPayloadAsync_EntryFactoryReceivesActualWrittenLength()
    {
        using var store = CreateStore();
        var keyHash = new string('a', 64);
        long observed = -1;
        var payload = new byte[42];

        await store.WriteStreamedPayloadAsync(
            keyHash,
            async (stream, ct) => await stream.WriteAsync(payload, ct),
            ctx =>
            {
                observed = ctx.Length;
                return new TestCacheEntry
                {
                    FileName = keyHash + ".bin",
                    Length = ctx.Length,
                    LastAccessUtc = ctx.WrittenAtUtc,
                };
            });

        Assert.AreEqual(payload.Length, observed,
            "entryFactory must receive the actual byte count the writer produced.");
    }

    [TestMethod]
    public async Task WriteStreamedPayloadAsync_ReadsAgainstOtherEntriesProgressDuringTheStreamedWrite()
    {
        using var store = CreateStore();

        // Pre-seed a "co-tenant" entry that a concurrent reader will lookup while the
        // streamed write is in flight. If the gate were held for the duration of the
        // writer, the read would block until the writer finished.
        var coTenant = new string('c', 64);
        var coFile = coTenant + ".bin";
        await File.WriteAllBytesAsync(Path.Combine(_cacheRoot, coFile), [1, 2, 3]);
        await store.UpsertEntryAsync(coTenant, new TestCacheEntry
        {
            FileName = coFile,
            Length = 3,
            LastAccessUtc = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
        });

        var writerInside = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWriter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var streamedKey = new string('a', 64);
        var streamedWrite = Task.Run(() => store.WriteStreamedPayloadAsync(
            streamedKey,
            async (stream, ct) =>
            {
                writerInside.TrySetResult();
                await releaseWriter.Task.WaitAsync(ct);
                await stream.WriteAsync(new byte[] { 9 }, ct);
            },
            ctx => new TestCacheEntry
            {
                FileName = streamedKey + ".bin",
                Length = ctx.Length,
                LastAccessUtc = ctx.WrittenAtUtc,
            }));

        await writerInside.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Reader: must complete while the streamed writer is still in flight.
        var readUri = await store.TryGetPayloadUriAsync(coTenant)
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsNotNull(readUri, "Concurrent read must succeed during a streamed write.");

        releaseWriter.SetResult();
        await streamedWrite.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task WriteStreamedPayloadAsync_DisposeCancelsWriterAndCleansUpTempFile()
    {
        var store = CreateStore();

        var writerInside = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var streamedKey = new string('a', 64);
        var streamedWrite = Task.Run(() => store.WriteStreamedPayloadAsync(
            streamedKey,
            async (stream, ct) =>
            {
                writerInside.TrySetResult();
                // Block forever — only cancellation should unblock us.
                await Task.Delay(Timeout.Infinite, ct);
            },
            ctx => new TestCacheEntry
            {
                FileName = streamedKey + ".bin",
                Length = ctx.Length,
                LastAccessUtc = ctx.WrittenAtUtc,
            }));

        await writerInside.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Dispose on the test thread. The base's dispose drain must observe the
        // in-flight writer, cancel it via the dispose CTS, wait for the writer's
        // finally to delete the temp file, and complete within a reasonable timeout.
        var disposeStart = Environment.TickCount64;
        store.Dispose();
        var disposeMs = Environment.TickCount64 - disposeStart;

        Assert.IsTrue(disposeMs < 6000, $"Dispose took {disposeMs} ms; expected to drain within the 5s timeout.");

        // Writer's task either propagated cancellation or returned null on dispose.
        try
        {
            var result = await streamedWrite.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.IsNull(result, "Cancelled streamed write must not commit a payload URI.");
        }
        catch (OperationCanceledException)
        {
            // Acceptable.
        }
        catch (ObjectDisposedException)
        {
            // Acceptable.
        }

        Assert.AreEqual(0, Directory.GetFiles(_cacheRoot, "*.tmp").Length,
            "Cancelled streamed writer must delete its temp file before exiting.");
    }

    [TestMethod]
    public async Task StartupTempFileSweep_DeletesStreamedWriterTempsAndLeavesEverythingElseAlone()
    {
        // Pre-seed:
        //   - one .tmp matching the streamed-writer pattern (<64-hex>.<32-hex>.tmp) — must be swept
        //   - one .tmp with an unrelated name — must survive (sweep is narrow, not "any .tmp")
        //   - one real payload file — must survive
        var streamedTemp = Path.Combine(
            _cacheRoot,
            new string('a', 64) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        var unrelatedTemp = Path.Combine(_cacheRoot, "scratch.tmp");
        var realPayload = Path.Combine(_cacheRoot, new string('b', 64) + ".bin");

        await File.WriteAllBytesAsync(streamedTemp, [1, 2, 3]);
        await File.WriteAllBytesAsync(unrelatedTemp, [4, 5, 6]);
        await File.WriteAllBytesAsync(realPayload, [7, 8, 9]);

        using var store = CreateStore();
        // Trigger init so the sweep runs.
        await store.GetStatsAsync();

        Assert.IsFalse(File.Exists(streamedTemp), "Streamed-writer-shaped temp file must be swept.");
        Assert.IsTrue(File.Exists(unrelatedTemp), "Unrelated .tmp files must be left alone by the sweep.");
        Assert.IsTrue(File.Exists(realPayload), "Real payload files must be left alone by the sweep.");
    }

    [TestMethod]
    public async Task WritePayloadAsync_DoesNotThrowWhenPostWriteEvictionFails()
    {
        // Cap = 250 bytes; two 200-byte writes will push us over and trigger eviction.
        using var store = CreateStore(maximumBytes: 250);
        var t0 = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

        var first = new string('a', 64);
        await store.WritePayloadAsync(
            first,
            new byte[200],
            new TestCacheEntry { FileName = first + ".bin", Length = 200, LastAccessUtc = t0 });

        // Now arm the eviction-time failure. The next write tips us 150 bytes over the
        // cap and triggers EvictIfNeededLockedAsync, whose candidate query will throw.
        // The base must swallow that and still return the URI of the successful write —
        // otherwise CachedImageService.FetchAndStoreAsync turns a DB hiccup into a
        // user-visible exception instead of a benign cache miss.
        store.ThrowOnGetEvictionCandidates = true;

        var second = new string('b', 64);
        var uri = await store.WritePayloadAsync(
            second,
            new byte[200],
            new TestCacheEntry { FileName = second + ".bin", Length = 200, LastAccessUtc = t0.AddMinutes(1) });

        Assert.IsNotNull(uri, "Write must succeed even when post-write eviction fails.");

        // The successful write's row must be present and the file on disk.
        Assert.IsNotNull(await store.GetEntryAsync(second));
        Assert.IsTrue(File.Exists(Path.Combine(_cacheRoot, second + ".bin")));
    }

    [TestMethod]
    public async Task WriteStreamedPayloadAsync_DoesNotThrowWhenPostWriteEvictionFails()
    {
        using var store = CreateStore(maximumBytes: 250);
        var t0 = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

        var first = new string('a', 64);
        await store.WritePayloadAsync(
            first,
            new byte[200],
            new TestCacheEntry { FileName = first + ".bin", Length = 200, LastAccessUtc = t0 });

        store.ThrowOnGetEvictionCandidates = true;

        var second = new string('b', 64);
        var uri = await store.WriteStreamedPayloadAsync(
            second,
            async (stream, ct) => await stream.WriteAsync(new byte[200], ct),
            ctx => new TestCacheEntry
            {
                FileName = second + ".bin",
                Length = ctx.Length,
                LastAccessUtc = ctx.WrittenAtUtc,
            });

        Assert.IsNotNull(uri, "Streamed write must succeed even when post-write eviction fails.");
        Assert.IsNotNull(await store.GetEntryAsync(second));
        Assert.IsTrue(File.Exists(Path.Combine(_cacheRoot, second + ".bin")));
    }

    [TestMethod]
    public async Task FolderMigratorFailure_ReturnsNullWithoutCreatingTargetOrOpeningDb_ThenRetriesNextCall()
    {
        // A migrating store whose target path is blocked by a file: the migrator's Directory.Move
        // throws, so init must return null without creating the target folder or opening the DB.
        Directory.Delete(_cacheRoot, recursive: true); // remove the pre-created Payload folder

        var legacy = Path.Combine(_cacheParent, "Legacy");
        Directory.CreateDirectory(legacy);
        await File.WriteAllBytesAsync(Path.Combine(legacy, "data.bin"), [1, 2, 3]);
        await File.WriteAllBytesAsync(_cacheRoot, [0]); // _cacheRoot == <parent>/Payload, now a file

        using var store = CreateMigratingStore("Payload", new PayloadFolderMigrationStep(1, "Legacy", "Payload"));

        var stats = await store.GetStatsAsync();
        Assert.AreEqual(0, stats.EntryCount, "A failed migration leaves the cache unavailable (safe default).");
        Assert.IsFalse(Directory.Exists(_cacheRoot), "Target folder must not be created on a migration failure.");
        Assert.IsTrue(File.Exists(Path.Combine(legacy, "data.bin")), "Source must remain intact.");

        // _initialized stayed false; clearing the obstacle lets the next call re-run the migrator.
        File.Delete(_cacheRoot);
        var stats2 = await store.GetStatsAsync();

        Assert.AreEqual(0, stats2.EntryCount);
        Assert.IsTrue(File.Exists(Path.Combine(_cacheRoot, "data.bin")), "Recovery move lands the payload.");
        Assert.IsFalse(Directory.Exists(legacy), "Recovery move clears the source.");
    }

    [TestMethod]
    public async Task FolderMigrator_DoesNotRunAfterSuccessfulInit()
    {
        Directory.Delete(_cacheRoot, recursive: true);

        var legacy = Path.Combine(_cacheParent, "Legacy");
        Directory.CreateDirectory(legacy);
        await File.WriteAllBytesAsync(Path.Combine(legacy, "data.bin"), [1]);

        using var store = CreateMigratingStore("Payload", new PayloadFolderMigrationStep(1, "Legacy", "Payload"));

        await store.GetStatsAsync(); // migrates Legacy -> Payload and opens the DB
        Assert.IsFalse(Directory.Exists(legacy));
        Assert.IsTrue(File.Exists(Path.Combine(_cacheRoot, "data.bin")));

        // Re-create the legacy source AFTER a successful init. The contract is "we don't migrate
        // after we've successfully opened," so the migrator must not run again.
        Directory.CreateDirectory(legacy);
        await File.WriteAllBytesAsync(Path.Combine(legacy, "second.bin"), [2]);

        await store.GetStatsAsync();

        Assert.IsTrue(File.Exists(Path.Combine(legacy, "second.bin")), "Migrator must not run after a successful init.");
    }

    [TestMethod]
    public async Task FolderMigrator_UsesSubfolderNameCapturedDuringPathResolution()
    {
        // A pathological store whose PayloadSubfolderName changes after its first read. The base
        // must capture the validated value once during path resolution and reuse THAT value for
        // both the migrator's final-target check and the default marker name — otherwise the
        // store opens one folder while migration evaluates (or names its marker after) another.
        // Here the migrator's final-target check would throw on the re-read "Second" (step targets
        // "First"), and the default marker would be named "Second.json"; a passing call with a
        // "First.json" marker proves the captured value was reused end-to-end.
        Directory.Delete(_cacheRoot, recursive: true);

        var legacy = Path.Combine(_cacheParent, "Legacy");
        Directory.CreateDirectory(legacy);
        await File.WriteAllBytesAsync(Path.Combine(legacy, "data.bin"), [1]);

        using var store = new TestCacheStore(
            Options.Create(new TestCacheOptions()),
            new FixedAppFolderLocator(_cacheParent),
            new NullLoggingService())
        {
            SubfolderName = "First",
            SubfolderNameAfterFirstRead = "Second",
            MigrationStepsValue = [new PayloadFolderMigrationStep(1, "Legacy", "First")],
        };

        var stats = await store.GetStatsAsync();

        Assert.AreEqual(0, stats.EntryCount);
        Assert.IsTrue(
            File.Exists(Path.Combine(_cacheParent, "First", "data.bin")),
            "Store must open and migrate the captured 'First' folder.");
        Assert.IsFalse(Directory.Exists(legacy), "Legacy folder must be migrated.");
        Assert.IsTrue(
            File.Exists(Path.Combine(_cacheParent, "First.json")),
            "Default marker name must derive from the captured subfolder.");
        Assert.IsFalse(
            File.Exists(Path.Combine(_cacheParent, "Second.json")),
            "Marker name must not derive from a re-read of the subfolder virtual.");
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

    private TestCacheStore CreateStore(long maximumBytes = 0)
    {
        var options = new TestCacheOptions { MaximumBytes = maximumBytes };
        return new TestCacheStore(
            Options.Create(options),
            new FixedAppFolderLocator(_cacheParent),
            new NullLoggingService());
    }

    private TestCacheStore CreateMigratingStore(string subfolder, params PayloadFolderMigrationStep[] steps)
    {
        return new TestCacheStore(
            Options.Create(new TestCacheOptions()),
            new FixedAppFolderLocator(_cacheParent),
            new NullLoggingService())
        {
            SubfolderName = subfolder,
            MigrationStepsValue = steps,
        };
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
