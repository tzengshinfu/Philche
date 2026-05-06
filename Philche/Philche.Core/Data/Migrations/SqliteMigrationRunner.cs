using Microsoft.Data.Sqlite;

namespace Philche.Core.Data.Migrations;

public sealed class SqliteMigrationRunner
{
    private readonly SqliteConnectionFactory connectionFactory;

    public SqliteMigrationRunner(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateOpenConnection();

        var ensureTableCommand = connection.CreateCommand();
        ensureTableCommand.CommandText =
            "CREATE TABLE IF NOT EXISTS schema_migrations (id TEXT NOT NULL PRIMARY KEY, applied_at_utc TEXT NOT NULL);";
        await ensureTableCommand.ExecuteNonQueryAsync(cancellationToken);

        foreach (var migration in MigrationCatalog.All)
        {
            if (await IsAppliedAsync(connection, migration.Id, cancellationToken))
            {
                continue;
            }

            await using var transaction = connection.BeginTransaction();
            var migrationCommand = connection.CreateCommand();
            migrationCommand.Transaction = transaction;
            migrationCommand.CommandText = migration.Sql;
            await migrationCommand.ExecuteNonQueryAsync(cancellationToken);

            var markCommand = connection.CreateCommand();
            markCommand.Transaction = transaction;
            markCommand.CommandText = "INSERT INTO schema_migrations(id, applied_at_utc) VALUES($id, $at);";
            markCommand.Parameters.AddWithValue("$id", migration.Id);
            markCommand.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
            await markCommand.ExecuteNonQueryAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
    }

    private static async Task<bool> IsAppliedAsync(SqliteConnection connection, string migrationId, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM schema_migrations WHERE id = $id;";
        command.Parameters.AddWithValue("$id", migrationId);
        var result = (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
        return result > 0;
    }
}
