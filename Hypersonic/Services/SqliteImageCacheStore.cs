using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Models;

namespace Hypersonic.Services;

public sealed class SqliteImageCacheStore : IImageCacheStore, IDisposable
{
    internal const string DatabaseFileName = "cache.db";
    internal const string LegacyManifestFileName = "manifest.json";

    private static readonly string[] CachedImageExtensions =
        [".jpg", ".png", ".gif", ".webp", ".bmp", ".img"];

    private const int ConnectionDefaultTimeoutSeconds = 2;
    private static readonly TimeSpan DisposeDrainTimeout = TimeSpan.FromSeconds(5);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger _log;
    private readonly string _cacheFolderPath;
    private readonly string _databasePath;

    private SqliteConnection? _connection;
    private bool _initialized;
    private bool _disposed;

    public SqliteImageCacheStore(IOptions<ImageCacheOptions> options, ILoggingService logService)
    {
        _cacheFolderPath = ImageCachePath.Resolve(options.Value);
        _databasePath = Path.Combine(_cacheFolderPath, DatabaseFileName);
        _log = logService.GetLogger(nameof(SqliteImageCacheStore));
    }

    public async Task<ImageCacheEntry?> GetEntryAsync(string keyHash, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connection = await EnsureInitializedLockedAsync(cancellationToken).ConfigureAwait(false);
            if (connection is null)
            {
                return null;
            }

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

            return ReadEntry(reader);
        }
        catch (SqliteException ex)
        {
            LogWarning("Image cache GetEntryAsync failed; returning miss", ex);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task TouchEntryAsync(string keyHash, DateTimeOffset lastAccessUtc, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connection = await EnsureInitializedLockedAsync(cancellationToken).ConfigureAwait(false);
            if (connection is null)
            {
                return;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Entries SET LastAccessUtc = $lastAccessUtc WHERE KeyHash = $keyHash;";
            command.Parameters.AddWithValue("$lastAccessUtc", ToUnixMs(lastAccessUtc));
            command.Parameters.AddWithValue("$keyHash", keyHash);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException ex)
        {
            LogWarning("Image cache TouchEntryAsync failed; ignoring", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpsertEntryAsync(string keyHash, ImageCacheEntry entry, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connection = await EnsureInitializedLockedAsync(cancellationToken).ConfigureAwait(false);
            if (connection is null)
            {
                return;
            }

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
        catch (SqliteException ex)
        {
            LogWarning("Image cache UpsertEntryAsync failed; image may be orphaned until next reconcile", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveEntryAsync(
        string keyHash,
        bool flushImmediately = false,
        CancellationToken cancellationToken = default)
    {
        _ = flushImmediately; // SQLite commits each mutation; no deferred flush to force.

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connection = await EnsureInitializedLockedAsync(cancellationToken).ConfigureAwait(false);
            if (connection is null)
            {
                return;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Entries WHERE KeyHash = $keyHash;";
            command.Parameters.AddWithValue("$keyHash", keyHash);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException ex)
        {
            LogWarning("Image cache RemoveEntryAsync failed; ignoring", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ImageCacheStoreStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connection = await EnsureInitializedLockedAsync(cancellationToken).ConfigureAwait(false);
            if (connection is null)
            {
                return new ImageCacheStoreStats(0, 0);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COALESCE(SUM(Length), 0), COUNT(*) FROM Entries;";

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return new ImageCacheStoreStats(0, 0);
            }

            var totalBytes = reader.IsDBNull(0) ? 0L : reader.GetInt64(0);
            var count = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
            return new ImageCacheStoreStats(totalBytes, count);
        }
        catch (SqliteException ex)
        {
            LogWarning("Image cache GetStatsAsync failed; returning zero stats", ex);
            return new ImageCacheStoreStats(0, 0);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ImageCacheStoreEntry>> GetEntriesAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connection = await EnsureInitializedLockedAsync(cancellationToken).ConfigureAwait(false);
            if (connection is null)
            {
                throw new InvalidOperationException(
                    "Image cache database is unavailable; cannot enumerate entries.");
            }

            return await ReadEntriesAsync(connection, orderByLastAccess: false, protectedKeyHash: null, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ImageCacheStoreEntry>> GetEvictionCandidatesAsync(
        string? protectedKeyHash = null,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connection = await EnsureInitializedLockedAsync(cancellationToken).ConfigureAwait(false);
            if (connection is null)
            {
                return [];
            }

            return await ReadEntriesAsync(connection, orderByLastAccess: true, protectedKeyHash, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (SqliteException ex)
        {
            LogWarning("Image cache GetEvictionCandidatesAsync failed; returning empty list", ex);
            return [];
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connection = await EnsureInitializedLockedAsync(cancellationToken).ConfigureAwait(false);
            if (connection is null)
            {
                return;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Entries;";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException ex)
        {
            LogWarning("Image cache ClearAsync failed", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection is not { State: System.Data.ConnectionState.Open } connection)
            {
                return;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA wal_checkpoint(PASSIVE);";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException ex)
        {
            LogWarning("Image cache FlushAsync (WAL checkpoint) failed", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // Mark disposed first so new EnsureInitializedLockedAsync calls bail before re-opening.
        _disposed = true;

        // Drain the gate so any in-flight read/write completes before we tear down the connection.
        // We do NOT dispose the semaphore — queued waiters may still wake up and observe _disposed,
        // and disposing it under them would surface ObjectDisposedException on a benign release path.
        var gateAcquired = false;
        try
        {
            gateAcquired = _gate.Wait(DisposeDrainTimeout);
            if (!gateAcquired)
            {
                LogWarning(
                    "Image cache dispose timed out draining the gate; tearing down connection anyway",
                    new TimeoutException($"Gate did not drain within {DisposeDrainTimeout.TotalSeconds:F0}s."));
            }

            try
            {
                if (_connection is not null)
                {
                    _connection.Dispose();
                    _connection = null;
                }
            }
            catch (Exception ex)
            {
                LogWarning("Image cache connection dispose failed", ex);
            }

            try
            {
                SqliteConnection.ClearAllPools();
            }
            catch
            {
                // Best-effort.
            }
        }
        finally
        {
            if (gateAcquired)
            {
                _gate.Release();
            }
        }
    }

    private async Task<SqliteConnection?> EnsureInitializedLockedAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return null;
        }

        if (_initialized && _connection is { State: System.Data.ConnectionState.Open })
        {
            return _connection;
        }

        try
        {
            Directory.CreateDirectory(_cacheFolderPath);
            DiscardLegacyJsonManifestIfPresent();
            _connection = await OpenAndPrepareConnectionAsync(cancellationToken).ConfigureAwait(false);
            _initialized = true;
            return _connection;
        }
        catch (SqliteException ex)
        {
            LogWarning("Image cache database initialization failed; rebuilding from scratch", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogWarning("Image cache database initialization I/O failure; rebuilding from scratch", ex);
        }

        // Self-heal: dispose, delete cache.db + sidecars, retry once.
        await TryDisposeConnectionAsync().ConfigureAwait(false);
        TryDeleteDatabaseFiles();

        try
        {
            _connection = await OpenAndPrepareConnectionAsync(cancellationToken).ConfigureAwait(false);
            _initialized = true;
            return _connection;
        }
        catch (OperationCanceledException)
        {
            // Don't swallow cancellation; let the caller observe it like every other public method does.
            await TryDisposeConnectionAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            LogWarning("Image cache database rebuild failed; cache will be unavailable", ex);
            await TryDisposeConnectionAsync().ConfigureAwait(false);
            return null;
        }
    }

    private async Task<SqliteConnection> OpenAndPrepareConnectionAsync(CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            DefaultTimeout = ConnectionDefaultTimeoutSeconds,
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ApplyConnectionPragmasAsync(connection, cancellationToken).ConfigureAwait(false);
            await SqliteImageCacheSchema.EnsureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task ApplyConnectionPragmasAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private void DiscardLegacyJsonManifestIfPresent()
    {
        var manifestPath = Path.Combine(_cacheFolderPath, LegacyManifestFileName);

        if (File.Exists(_databasePath) || !File.Exists(manifestPath))
        {
            return;
        }

        var deleted = 0;
        TryDeleteFile(manifestPath);

        foreach (var file in Directory.EnumerateFiles(_cacheFolderPath))
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

    private async Task TryDisposeConnectionAsync()
    {
        if (_connection is null)
        {
            return;
        }

        try
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Best-effort.
        }
        finally
        {
            _connection = null;
        }

        try
        {
            SqliteConnection.ClearAllPools();
        }
        catch
        {
            // Best-effort.
        }
    }

    private void TryDeleteDatabaseFiles()
    {
        TryDeleteFile(_databasePath);
        TryDeleteFile(_databasePath + "-wal");
        TryDeleteFile(_databasePath + "-shm");
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

    private static async Task<IReadOnlyList<ImageCacheStoreEntry>> ReadEntriesAsync(
        SqliteConnection connection,
        bool orderByLastAccess,
        string? protectedKeyHash,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();

        var sql = """
            SELECT KeyHash, FileName, Length, Provider, Kind, ServerHash, ServerScopeHash, Size,
                   CreatedAtUtc, LastAccessUtc, ExpiresAtUtc
            FROM Entries
            """;

        if (protectedKeyHash is not null)
        {
            sql += " WHERE KeyHash != $protected";
            command.Parameters.AddWithValue("$protected", protectedKeyHash);
        }

        if (orderByLastAccess)
        {
            sql += " ORDER BY LastAccessUtc ASC";
        }

        sql += ";";
        command.CommandText = sql;

        var results = new List<ImageCacheStoreEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var keyHash = reader.GetString(0);
            var entry = ReadEntryWithOffset(reader, offset: 1);
            results.Add(new ImageCacheStoreEntry(keyHash, entry));
        }

        return results;
    }

    private static ImageCacheEntry ReadEntry(SqliteDataReader reader)
    {
        return ReadEntryWithOffset(reader, offset: 0);
    }

    private static ImageCacheEntry ReadEntryWithOffset(SqliteDataReader reader, int offset)
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

    private void LogWarning(string message, Exception exception)
    {
        try
        {
            _log.Warning(message, exception: exception);
        }
        catch
        {
            System.Diagnostics.Debug.WriteLine($"[SqliteImageCacheStore] {message}: {exception}");
        }
    }
}
