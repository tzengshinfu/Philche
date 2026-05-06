using Philche.Core.Domain.Models;

namespace Philche.Core.Data.Repositories;

public sealed class SqliteScanRunRepository : IScanRunRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public SqliteScanRunRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task UpsertAsync(ScanRun run, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateOpenConnection();
        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO scan_runs (
              id, trigger_reason, started_at_utc, ended_at_utc,
                            inventory_count, finding_count, warning_count, status, high_risk_paths_json
            ) VALUES (
              $id, $triggerReason, $startedAt, $endedAt,
                            $inventoryCount, $findingCount, $warningCount, $status, $highRiskPathsJson
            )
            ON CONFLICT(id) DO UPDATE SET
              trigger_reason = excluded.trigger_reason,
              started_at_utc = excluded.started_at_utc,
              ended_at_utc = excluded.ended_at_utc,
              inventory_count = excluded.inventory_count,
              finding_count = excluded.finding_count,
              warning_count = excluded.warning_count,
                            status = excluded.status,
                            high_risk_paths_json = excluded.high_risk_paths_json;
            """;

        command.Parameters.AddWithValue("$id", run.Id);
        command.Parameters.AddWithValue("$triggerReason", run.TriggerReason);
        command.Parameters.AddWithValue("$startedAt", run.StartedAt.ToString("O"));
        command.Parameters.AddWithValue("$endedAt", run.EndedAt?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$inventoryCount", run.InventoryCount);
        command.Parameters.AddWithValue("$findingCount", run.FindingCount);
        command.Parameters.AddWithValue("$warningCount", run.WarningCount);
        command.Parameters.AddWithValue("$status", run.Status);
        command.Parameters.AddWithValue("$highRiskPathsJson", run.HighRiskPathsJson ?? (object)DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ScanRun>> ListRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        var safeLimit = Math.Max(1, limit);
        var results = new List<ScanRun>();

        await using var connection = connectionFactory.CreateOpenConnection();
        var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, trigger_reason, started_at_utc, ended_at_utc, inventory_count, finding_count, warning_count, status, high_risk_paths_json FROM scan_runs ORDER BY started_at_utc DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", safeLimit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new ScanRun
            {
                Id = reader.GetString(0),
                TriggerReason = reader.GetString(1),
                StartedAt = DateTimeOffset.Parse(reader.GetString(2)),
                EndedAt = reader.IsDBNull(3) ? null : DateTimeOffset.Parse(reader.GetString(3)),
                InventoryCount = reader.GetInt32(4),
                FindingCount = reader.GetInt32(5),
                WarningCount = reader.GetInt32(6),
                Status = reader.GetString(7),
                HighRiskPathsJson = reader.IsDBNull(8) ? null : reader.GetString(8),
            });
        }

        return results;
    }
}
