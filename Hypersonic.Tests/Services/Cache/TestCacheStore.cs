using Hypersonic.Core.Contracts.Services;
using Hypersonic.Services.Cache;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Hypersonic.Tests.Services.Cache;

/// <summary>
/// Minimal in-test subclass of <see cref="SqliteCacheStoreBase{TEntry}"/> used by
/// the generic base-class tests. Mirrors what a real entity subclass looks like
/// (image / lyrics / audio) but keeps the schema as small as possible — just
/// <c>KeyHash</c> primary key + the three columns mandated by <see cref="ICacheEntry"/>.
/// </summary>
internal sealed class TestCacheStore : SqliteCacheStoreBase<TestCacheEntry>
{
    public TestCacheStore(IOptions<TestCacheOptions> options, ILoggingService logService)
        : base(options.Value, logService)
    {
        Options = options.Value;
    }

    public TestCacheOptions Options { get; }

    /// <summary>
    /// Test-only switch: when true, the next <c>EnumerateAllEntriesLockedAsync</c> call
    /// throws <see cref="SqliteException"/> so the reconcile safety-abort path can be
    /// exercised without corrupting the real DB.
    /// </summary>
    public bool ThrowOnEnumerateAll { get; set; }

    /// <summary>
    /// Test-only switch: when true, every <c>GetEvictionCandidatesLockedAsync</c> call
    /// throws <see cref="SqliteException"/>. Used to verify the base's post-write
    /// eviction call never lets a DB hiccup bubble out of a successful write.
    /// </summary>
    public bool ThrowOnGetEvictionCandidates { get; set; }

    protected override string TableName => "Entries";

    protected override int CurrentSchemaVersion => 1;

    protected override IReadOnlyList<MigrationStep> MigrationSteps => Array.Empty<MigrationStep>();

    protected override string CreateCurrentSchemaSql => """
        CREATE TABLE IF NOT EXISTS Entries (
            KeyHash       TEXT    PRIMARY KEY NOT NULL,
            FileName      TEXT    NOT NULL,
            Length        INTEGER NOT NULL,
            LastAccessUtc INTEGER NOT NULL
        ) WITHOUT ROWID;
        CREATE INDEX IF NOT EXISTS IX_TestEntries_LastAccessUtc ON Entries(LastAccessUtc);
        """;

    protected override string ResolvePayloadFolder()
    {
        if (string.IsNullOrWhiteSpace(Options.RootPath))
        {
            throw new InvalidOperationException("TestCacheStore requires Options.RootPath.");
        }

        return Options.RootPath;
    }

    protected override bool IsKnownPayloadFileName(string fileName)
    {
        // Recognises <64-hex>.bin files only. Mirrors the shape of the real image
        // predicate (hex basename + extension allowlist), trimmed to one extension
        // so the tests can pre-seed files with a predictable naming scheme.
        if (string.IsNullOrEmpty(fileName))
        {
            return false;
        }

        var dot = fileName.LastIndexOf('.');
        if (dot != 64)
        {
            return false;
        }

        for (var i = 0; i < dot; i++)
        {
            var c = fileName[i];
            var isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
            if (!isHex)
            {
                return false;
            }
        }

        return fileName.AsSpan(dot).Equals(".bin", StringComparison.OrdinalIgnoreCase);
    }

    protected override async Task<TestCacheEntry?> GetEntryLockedAsync(
        SqliteConnection connection,
        string keyHash,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT FileName, Length, LastAccessUtc FROM Entries WHERE KeyHash = $keyHash LIMIT 1;";
        command.Parameters.AddWithValue("$keyHash", keyHash);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return ReadEntry(reader, offset: 0);
    }

    protected override async Task TouchEntryLockedAsync(
        SqliteConnection connection,
        string keyHash,
        DateTimeOffset lastAccessUtc,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Entries SET LastAccessUtc = $lastAccessUtc WHERE KeyHash = $keyHash;";
        command.Parameters.AddWithValue("$lastAccessUtc", lastAccessUtc.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$keyHash", keyHash);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task UpsertEntryLockedAsync(
        SqliteConnection connection,
        string keyHash,
        TestCacheEntry entry,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Entries (KeyHash, FileName, Length, LastAccessUtc)
            VALUES ($keyHash, $fileName, $length, $lastAccessUtc)
            ON CONFLICT(KeyHash) DO UPDATE SET
                FileName      = excluded.FileName,
                Length        = excluded.Length,
                LastAccessUtc = excluded.LastAccessUtc;
            """;
        command.Parameters.AddWithValue("$keyHash", keyHash);
        command.Parameters.AddWithValue("$fileName", entry.FileName);
        command.Parameters.AddWithValue("$length", entry.Length);
        command.Parameters.AddWithValue("$lastAccessUtc", entry.LastAccessUtc.ToUnixTimeMilliseconds());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task RemoveEntryLockedAsync(
        SqliteConnection connection,
        string keyHash,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Entries WHERE KeyHash = $keyHash;";
        command.Parameters.AddWithValue("$keyHash", keyHash);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task<IReadOnlyList<(string KeyHash, TestCacheEntry Entry)>> GetEvictionCandidatesLockedAsync(
        SqliteConnection connection,
        string? protectedKeyHash,
        CancellationToken cancellationToken)
    {
        if (ThrowOnGetEvictionCandidates)
        {
            throw new SqliteException("injected failure for post-write-eviction safety test", 1);
        }

        await using var command = connection.CreateCommand();
        var sql = "SELECT KeyHash, FileName, Length, LastAccessUtc FROM Entries WHERE FileName != ''";
        if (protectedKeyHash is not null)
        {
            sql += " AND KeyHash != $protected";
            command.Parameters.AddWithValue("$protected", protectedKeyHash);
        }

        sql += " ORDER BY LastAccessUtc ASC;";
        command.CommandText = sql;

        var results = new List<(string, TestCacheEntry)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var keyHash = reader.GetString(0);
            results.Add((keyHash, ReadEntry(reader, offset: 1)));
        }

        return results;
    }

    protected override async Task<IReadOnlyList<(string KeyHash, TestCacheEntry Entry)>> EnumerateAllEntriesLockedAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        if (ThrowOnEnumerateAll)
        {
            throw new SqliteException("injected failure for reconcile abort test", 1);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT KeyHash, FileName, Length, LastAccessUtc FROM Entries;";

        var results = new List<(string, TestCacheEntry)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var keyHash = reader.GetString(0);
            results.Add((keyHash, ReadEntry(reader, offset: 1)));
        }

        return results;
    }

    private static TestCacheEntry ReadEntry(SqliteDataReader reader, int offset)
    {
        return new TestCacheEntry
        {
            FileName = reader.GetString(offset + 0),
            Length = reader.GetInt64(offset + 1),
            LastAccessUtc = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(offset + 2)),
        };
    }
}
