using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Models;
using Hypersonic.Services.Cache;

namespace Hypersonic.Services;

/// <summary>
/// Image-specific subclass of <see cref="SqliteCacheStoreBase{TEntry}"/>. The base
/// owns all the generic SQLite plumbing (lifecycle, gate, schema driver, self-heal,
/// dispose drain, stats/clear/flush, atomic payload write, reconcile, eviction,
/// startup temp-file sweep). This subclass supplies only the image-specific bits:
/// the entries-table schema SQL, column mapping, image extension predicate, and
/// the legacy <c>manifest.json</c> discard one-off.
///
/// Declares <c>PayloadSubfolderName = "Images"</c> plus a single folder-migration
/// step renaming the legacy MSIX <c>ImageCache</c> folder to <c>Images</c>, so that
/// after migration packaged and unpackaged installs share the same subfolder name.
/// No image data is lost: the whole directory is moved as one unit.
/// </summary>
public sealed class SqliteImageCacheStore : SqliteCacheStoreBase<ImageCacheEntry>, IImageCacheStore
{
    internal const string LegacyManifestFileName = "manifest.json";

    private static readonly string[] CachedImageExtensions =
        [".jpg", ".png", ".gif", ".webp", ".bmp", ".img"];

    private readonly ILogger _log;

    public SqliteImageCacheStore(
        IOptions<ImageCacheOptions> options,
        IAppFolderLocator folderLocator,
        ILoggingService logService)
        : base(options.Value, folderLocator, logService)
    {
        _log = logService.GetLogger(nameof(SqliteImageCacheStore));
    }

    protected override string TableName => "Entries";

    protected override int CurrentSchemaVersion => 1;

    protected override IReadOnlyList<MigrationStep> MigrationSteps => Array.Empty<MigrationStep>();

    protected override string CreateCurrentSchemaSql => CreateEntriesTableSql + "\n" + CreateLastAccessIndexSql;

    private const string CreateEntriesTableSql = """
        CREATE TABLE IF NOT EXISTS Entries (
            KeyHash         TEXT    PRIMARY KEY NOT NULL,
            FileName        TEXT    NOT NULL,
            Length          INTEGER NOT NULL,
            Provider        TEXT    NOT NULL,
            Kind            TEXT    NOT NULL,
            ServerHash      TEXT    NOT NULL,
            ServerScopeHash TEXT    NOT NULL,
            Size            TEXT    NOT NULL,
            CreatedAtUtc    INTEGER NOT NULL,
            LastAccessUtc   INTEGER NOT NULL,
            ExpiresAtUtc    INTEGER NOT NULL
        ) WITHOUT ROWID;
        """;

    private const string CreateLastAccessIndexSql =
        "CREATE INDEX IF NOT EXISTS IX_Entries_LastAccessUtc ON Entries(LastAccessUtc);";

    protected override string PayloadSubfolderName => "Images";

    protected override IReadOnlyList<PayloadFolderMigrationStep> PayloadFolderMigrationSteps =>
    [
        new PayloadFolderMigrationStep(
            TargetVersion: 1,
            SourceSubfolder: "ImageCache",
            TargetSubfolder: "Images"),
    ];

    internal static bool IsCachedImageFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return false;
        }

        var dot = fileName.LastIndexOf('.');
        if (dot != 64)
        {
            return false;
        }

        var basenameSpan = fileName.AsSpan(0, dot);
        for (var i = 0; i < basenameSpan.Length; i++)
        {
            var c = basenameSpan[i];
            var isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
            if (!isHex)
            {
                return false;
            }
        }

        var ext = fileName.AsSpan(dot);
        foreach (var candidate in CachedImageExtensions)
        {
            if (ext.Equals(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    protected override bool IsKnownPayloadFileName(string fileName) => IsCachedImageFileName(fileName);

    /// <summary>
    /// One-off cleanup: the original cache implementation persisted a JSON manifest +
    /// loose image files. On first launch of the SQLite-backed cache we discard both
    /// rather than try to migrate, so the cache rebuilds itself on demand.
    /// </summary>
    protected override void OnBeforeOpenConnection(string cacheFolderPath, string databasePath)
    {
        var manifestPath = Path.Combine(cacheFolderPath, LegacyManifestFileName);

        if (File.Exists(databasePath) || !File.Exists(manifestPath))
        {
            return;
        }

        var deleted = 0;
        TryDeleteFile(manifestPath);

        foreach (var file in Directory.EnumerateFiles(cacheFolderPath))
        {
            if (IsCachedImageFileName(Path.GetFileName(file)))
            {
                if (TryDeleteFile(file))
                {
                    deleted++;
                }
            }
        }

        try
        {
            _log.Info(
                "Legacy JSON image cache manifest discarded; cache will rebuild on demand",
                new { ManifestPath = manifestPath, DeletedImageCount = deleted });
        }
        catch
        {
            // Logging failures are non-fatal during init.
        }
    }

    private static bool TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                return true;
            }
        }
        catch
        {
            // Best-effort.
        }

        return false;
    }

    // ----- *LockedAsync DB surface (gate-held; throw on SqliteException). -----

    protected override async Task<ImageCacheEntry?> GetEntryLockedAsync(
        SqliteConnection connection,
        string keyHash,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT FileName, Length, Provider, Kind, ServerHash, ServerScopeHash, Size,
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
        ImageCacheEntry entry,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Entries (
                KeyHash, FileName, Length, Provider, Kind, ServerHash, ServerScopeHash, Size,
                CreatedAtUtc, LastAccessUtc, ExpiresAtUtc)
            VALUES (
                $keyHash, $fileName, $length, $provider, $kind, $serverHash, $serverScopeHash, $size,
                $createdAtUtc, $lastAccessUtc, $expiresAtUtc)
            ON CONFLICT(KeyHash) DO UPDATE SET
                FileName        = excluded.FileName,
                Length          = excluded.Length,
                Provider        = excluded.Provider,
                Kind            = excluded.Kind,
                ServerHash      = excluded.ServerHash,
                ServerScopeHash = excluded.ServerScopeHash,
                Size            = excluded.Size,
                CreatedAtUtc    = excluded.CreatedAtUtc,
                LastAccessUtc   = excluded.LastAccessUtc,
                ExpiresAtUtc    = excluded.ExpiresAtUtc;
            """;
        command.Parameters.AddWithValue("$keyHash", keyHash);
        command.Parameters.AddWithValue("$fileName", entry.FileName);
        command.Parameters.AddWithValue("$length", entry.Length);
        command.Parameters.AddWithValue("$provider", entry.Provider);
        command.Parameters.AddWithValue("$kind", entry.Kind);
        command.Parameters.AddWithValue("$serverHash", entry.ServerHash);
        command.Parameters.AddWithValue("$serverScopeHash", entry.ServerScopeHash);
        command.Parameters.AddWithValue("$size", entry.Size);
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

    protected override async Task<IReadOnlyList<(string KeyHash, ImageCacheEntry Entry)>> GetEvictionCandidatesLockedAsync(
        SqliteConnection connection,
        string? protectedKeyHash,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        var sql = """
            SELECT KeyHash, FileName, Length, Provider, Kind, ServerHash, ServerScopeHash, Size,
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

        var results = new List<(string, ImageCacheEntry)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var keyHash = reader.GetString(0);
            results.Add((keyHash, ReadEntry(reader, offset: 1)));
        }

        return results;
    }

    protected override async Task<IReadOnlyList<(string KeyHash, ImageCacheEntry Entry)>> EnumerateAllEntriesLockedAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT KeyHash, FileName, Length, Provider, Kind, ServerHash, ServerScopeHash, Size,
                   CreatedAtUtc, LastAccessUtc, ExpiresAtUtc
            FROM Entries;
            """;

        var results = new List<(string, ImageCacheEntry)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var keyHash = reader.GetString(0);
            results.Add((keyHash, ReadEntry(reader, offset: 1)));
        }

        return results;
    }

    private static ImageCacheEntry ReadEntry(SqliteDataReader reader, int offset)
    {
        return new ImageCacheEntry
        {
            FileName = reader.GetString(offset + 0),
            Length = reader.GetInt64(offset + 1),
            Provider = reader.GetString(offset + 2),
            Kind = reader.GetString(offset + 3),
            ServerHash = reader.GetString(offset + 4),
            ServerScopeHash = reader.GetString(offset + 5),
            Size = reader.GetString(offset + 6),
            CreatedAtUtc = FromUnixMs(reader.GetInt64(offset + 7)),
            LastAccessUtc = FromUnixMs(reader.GetInt64(offset + 8)),
            ExpiresAtUtc = FromUnixMs(reader.GetInt64(offset + 9)),
        };
    }

    private static long ToUnixMs(DateTimeOffset value) => value.ToUnixTimeMilliseconds();

    private static DateTimeOffset FromUnixMs(long value) => DateTimeOffset.FromUnixTimeMilliseconds(value);
}
