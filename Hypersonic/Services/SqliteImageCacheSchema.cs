using Microsoft.Data.Sqlite;

namespace Hypersonic.Services;

internal static class SqliteImageCacheSchema
{
    public const int CurrentSchemaVersion = 1;

    public static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var version = await ReadUserVersionAsync(connection, cancellationToken).ConfigureAwait(false);

        if (version == CurrentSchemaVersion)
        {
            return;
        }

        if (version > CurrentSchemaVersion)
        {
            await RebuildAsync(connection, cancellationToken).ConfigureAwait(false);
            return;
        }

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        if (version < 1)
        {
            await ExecuteAsync(connection, transaction, CreateEntriesTableSql, cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, CreateLastAccessIndexSql, cancellationToken).ConfigureAwait(false);
        }

        await ExecuteAsync(connection, transaction, $"PRAGMA user_version = {CurrentSchemaVersion};", cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

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

    private static async Task RebuildAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, transaction, "DROP INDEX IF EXISTS IX_Entries_LastAccessUtc;", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, transaction, "DROP TABLE IF EXISTS Entries;", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, transaction, CreateEntriesTableSql, cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, transaction, CreateLastAccessIndexSql, cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, transaction, $"PRAGMA user_version = {CurrentSchemaVersion};", cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> ReadUserVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is long value ? (int)value : 0;
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
