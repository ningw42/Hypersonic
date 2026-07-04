using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Models;
using Hypersonic.Services.Cache;

namespace Hypersonic.Services;

/// <summary>
/// Lyrics-specific subclass of <see cref="SqliteCacheStoreBase{TEntry}"/>. The base owns all the
/// generic SQLite plumbing (lifecycle, gate, schema driver, self-heal, dispose drain, stats/clear/
/// flush, atomic payload write, negative-cache rows, reconcile, eviction, startup temp-file sweep,
/// parse-on-read <c>ReadPayloadAsync</c>). This subclass supplies the lyrics-specific bits: the
/// entries-table schema (including the nullable <c>LanguageCode</c>/<c>OffsetMs</c> columns), column
/// mapping, the payload-filename predicate, and the format→extension map. Greenfield store, so it
/// declares no payload-folder migration.
/// </summary>
public sealed class SqliteLyricsCacheStore : SqliteCacheStoreBase<LyricsCacheEntry>, ILyricsCacheStore
{
    private static readonly string[] CachedLyricsExtensions =
        [".json", ".txt", ".lrc", ".ttml", ".yrc", ".qrc", ".krc"];

    public SqliteLyricsCacheStore(
        IOptions<LyricsCacheOptions> options,
        IAppFolderLocator folderLocator,
        ILoggingService logService)
        : base(options.Value, folderLocator, logService)
    {
    }

    protected override string TableName => "Entries";

    protected override int CurrentSchemaVersion => 1;

    protected override IReadOnlyList<MigrationStep> MigrationSteps => Array.Empty<MigrationStep>();

    protected override string CreateCurrentSchemaSql => CreateEntriesTableSql + "\n" + CreateLastAccessIndexSql;

    private const string CreateEntriesTableSql = """
        CREATE TABLE IF NOT EXISTS Entries (
            KeyHash        TEXT    PRIMARY KEY NOT NULL,
            FileName       TEXT    NOT NULL,
            Length         INTEGER NOT NULL,
            SourceProvider TEXT    NOT NULL,
            PayloadFormat  TEXT    NOT NULL,
            SyncKind       TEXT    NOT NULL,
            LanguageCode   TEXT,
            OffsetMs       INTEGER,
            CreatedAtUtc   INTEGER NOT NULL,
            LastAccessUtc  INTEGER NOT NULL,
            ExpiresAtUtc   INTEGER NOT NULL
        ) WITHOUT ROWID;
        """;

    private const string CreateLastAccessIndexSql =
        "CREATE INDEX IF NOT EXISTS IX_Entries_LastAccessUtc ON Entries(LastAccessUtc);";

    protected override string PayloadSubfolderName => "Lyrics";

    /// <summary>
    /// True for a 64-hex basename plus a known lyrics extension (<c>.json</c>/<c>.txt</c>/<c>.lrc</c>/
    /// <c>.ttml</c>/<c>.yrc</c>/<c>.qrc</c>/<c>.krc</c>). Excludes <c>cache.db</c> and unrelated files.
    /// </summary>
    internal static bool IsKnownLyricsFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return false;
        }

        var dot = fileName.IndexOf('.');
        if (dot != 64)
        {
            return false;
        }

        var basename = fileName.AsSpan(0, dot);
        foreach (var c in basename)
        {
            var isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
            if (!isHex)
            {
                return false;
            }
        }

        var ext = fileName.AsSpan(dot);
        foreach (var candidate in CachedLyricsExtensions)
        {
            if (ext.Equals(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    protected override bool IsKnownPayloadFileName(string fileName) => IsKnownLyricsFileName(fileName);

    // ----- *LockedAsync DB surface (gate-held; throw on SqliteException). -----

    protected override async Task<LyricsCacheEntry?> GetEntryLockedAsync(
        SqliteConnection connection,
        string keyHash,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT FileName, Length, SourceProvider, PayloadFormat, SyncKind, LanguageCode, OffsetMs,
                   CreatedAtUtc, LastAccessUtc, ExpiresAtUtc
            FROM Entries
            WHERE KeyHash = $keyHash
            LIMIT 1;
            """;
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
        command.Parameters.AddWithValue("$lastAccessUtc", ToUnixMs(lastAccessUtc));
        command.Parameters.AddWithValue("$keyHash", keyHash);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task UpsertEntryLockedAsync(
        SqliteConnection connection,
        string keyHash,
        LyricsCacheEntry entry,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Entries (
                KeyHash, FileName, Length, SourceProvider, PayloadFormat, SyncKind, LanguageCode, OffsetMs,
                CreatedAtUtc, LastAccessUtc, ExpiresAtUtc)
            VALUES (
                $keyHash, $fileName, $length, $sourceProvider, $payloadFormat, $syncKind, $languageCode, $offsetMs,
                $createdAtUtc, $lastAccessUtc, $expiresAtUtc)
            ON CONFLICT(KeyHash) DO UPDATE SET
                FileName       = excluded.FileName,
                Length         = excluded.Length,
                SourceProvider = excluded.SourceProvider,
                PayloadFormat  = excluded.PayloadFormat,
                SyncKind       = excluded.SyncKind,
                LanguageCode   = excluded.LanguageCode,
                OffsetMs       = excluded.OffsetMs,
                CreatedAtUtc   = excluded.CreatedAtUtc,
                LastAccessUtc  = excluded.LastAccessUtc,
                ExpiresAtUtc   = excluded.ExpiresAtUtc;
            """;
        command.Parameters.AddWithValue("$keyHash", keyHash);
        command.Parameters.AddWithValue("$fileName", entry.FileName);
        command.Parameters.AddWithValue("$length", entry.Length);
        command.Parameters.AddWithValue("$sourceProvider", entry.SourceProvider);
        command.Parameters.AddWithValue("$payloadFormat", entry.PayloadFormat);
        command.Parameters.AddWithValue("$syncKind", entry.SyncKind.ToString());
        command.Parameters.AddWithValue("$languageCode", (object?)entry.LanguageCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$offsetMs", (object?)entry.OffsetMs ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAtUtc", ToUnixMs(entry.CreatedAtUtc));
        command.Parameters.AddWithValue("$lastAccessUtc", ToUnixMs(entry.LastAccessUtc));
        command.Parameters.AddWithValue("$expiresAtUtc", ToUnixMs(entry.ExpiresAtUtc));

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

    protected override async Task<IReadOnlyList<(string KeyHash, LyricsCacheEntry Entry)>> GetEvictionCandidatesLockedAsync(
        SqliteConnection connection,
        string? protectedKeyHash,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        var sql = """
            SELECT KeyHash, FileName, Length, SourceProvider, PayloadFormat, SyncKind, LanguageCode, OffsetMs,
                   CreatedAtUtc, LastAccessUtc, ExpiresAtUtc
            FROM Entries
            WHERE FileName != ''
            """;

        if (protectedKeyHash is not null)
        {
            sql += " AND KeyHash != $protected";
            command.Parameters.AddWithValue("$protected", protectedKeyHash);
        }

        sql += " ORDER BY LastAccessUtc ASC;";
        command.CommandText = sql;

        var results = new List<(string, LyricsCacheEntry)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add((reader.GetString(0), ReadEntry(reader, offset: 1)));
        }

        return results;
    }

    protected override async Task<IReadOnlyList<(string KeyHash, LyricsCacheEntry Entry)>> EnumerateAllEntriesLockedAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT KeyHash, FileName, Length, SourceProvider, PayloadFormat, SyncKind, LanguageCode, OffsetMs,
                   CreatedAtUtc, LastAccessUtc, ExpiresAtUtc
            FROM Entries;
            """;

        var results = new List<(string, LyricsCacheEntry)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add((reader.GetString(0), ReadEntry(reader, offset: 1)));
        }

        return results;
    }

    private static LyricsCacheEntry ReadEntry(SqliteDataReader reader, int offset)
    {
        return new LyricsCacheEntry
        {
            FileName = reader.GetString(offset + 0),
            Length = reader.GetInt64(offset + 1),
            SourceProvider = reader.GetString(offset + 2),
            PayloadFormat = reader.GetString(offset + 3),
            SyncKind = Enum.TryParse<LyricsSyncKind>(reader.GetString(offset + 4), out var kind) ? kind : LyricsSyncKind.None,
            LanguageCode = reader.IsDBNull(offset + 5) ? null : reader.GetString(offset + 5),
            OffsetMs = reader.IsDBNull(offset + 6) ? null : reader.GetInt32(offset + 6),
            CreatedAtUtc = FromUnixMs(reader.GetInt64(offset + 7)),
            LastAccessUtc = FromUnixMs(reader.GetInt64(offset + 8)),
            ExpiresAtUtc = FromUnixMs(reader.GetInt64(offset + 9)),
        };
    }

    private static long ToUnixMs(DateTimeOffset value) => value.ToUnixTimeMilliseconds();

    private static DateTimeOffset FromUnixMs(long value) => DateTimeOffset.FromUnixTimeMilliseconds(value);
}
