using Microsoft.Data.Sqlite;

namespace Hypersonic.Services.Cache;

/// <summary>
/// Drives schema-version management for any <see cref="SqliteCacheStoreBase{TEntry}"/>:
/// reads <c>PRAGMA user_version</c>, applies the v0→current shortcut or incremental
/// deltas, and falls back to a full rebuild when the DB is at a future version.
///
/// All work happens in a single outer transaction so any failure rolls the entire
/// bump back and leaves <c>user_version</c> unchanged for the next launch to retry.
/// </summary>
internal static class SqliteCacheSchemaDriver
{
    public static async Task EnsureSchemaAsync(
        SqliteConnection connection,
        int currentSchemaVersion,
        string tableName,
        string createCurrentSchemaSql,
        IReadOnlyList<MigrationStep> migrationSteps,
        CancellationToken cancellationToken)
    {
        var version = await ReadUserVersionAsync(connection, cancellationToken).ConfigureAwait(false);

        if (version == currentSchemaVersion)
        {
            return;
        }

        if (version > currentSchemaVersion)
        {
            await RebuildAsync(connection, currentSchemaVersion, tableName, createCurrentSchemaSql, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await using var transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        if (version < 1)
        {
            await ExecuteAsync(connection, transaction, createCurrentSchemaSql, cancellationToken).ConfigureAwait(false);
            // v0→v1 shortcut: createCurrentSchemaSql contains every table/index/trigger
            // at the latest version, so we can skip the per-step loop entirely when
            // bootstrapping. Steps targeting v2+ still need to run if they exist.
            version = currentSchemaVersion;
        }

        foreach (var step in migrationSteps)
        {
            if (step.TargetVersion <= version)
            {
                continue;
            }

            if (step.TargetVersion > currentSchemaVersion)
            {
                continue;
            }

            await ExecuteAsync(connection, transaction, step.Sql, cancellationToken).ConfigureAwait(false);
        }

        await ExecuteAsync(
            connection,
            transaction,
            $"PRAGMA user_version = {currentSchemaVersion};",
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task RebuildAsync(
        SqliteConnection connection,
        int currentSchemaVersion,
        string tableName,
        string createCurrentSchemaSql,
        CancellationToken cancellationToken)
    {
        // SQLite drops indexes/triggers when their owning table is dropped, so we only
        // need to drop the entity's single table by name. Subclasses with auxiliary
        // tables in future versions will need to extend this hook.
        await using var transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, transaction, $"DROP TABLE IF EXISTS {tableName};", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, transaction, createCurrentSchemaSql, cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(
            connection,
            transaction,
            $"PRAGMA user_version = {currentSchemaVersion};",
            cancellationToken).ConfigureAwait(false);

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
