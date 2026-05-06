using Philche.Core.Domain.Enums;
using Philche.Core.Domain.Models;

namespace Philche.Core.Data.Repositories;

public sealed class SqliteInventoryRepository : IInventoryRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public SqliteInventoryRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task UpsertAsync(InventoryItem item, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateOpenConnection();
        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO inventory_items (
              id, agent_key, surface_type, surface_target_id, display_name, version,
              executable_path, version_evidence, discovered_at_utc, updated_at_utc
            ) VALUES (
              $id, $agentKey, $surfaceType, $surfaceTargetId, $displayName, $version,
              $executablePath, $versionEvidence, $discoveredAt, $updatedAt
            )
            ON CONFLICT(id) DO UPDATE SET
              agent_key = excluded.agent_key,
              surface_type = excluded.surface_type,
              surface_target_id = excluded.surface_target_id,
              display_name = excluded.display_name,
              version = excluded.version,
              executable_path = excluded.executable_path,
              version_evidence = excluded.version_evidence,
              updated_at_utc = excluded.updated_at_utc;
            """;

        command.Parameters.AddWithValue("$id", item.Id);
        command.Parameters.AddWithValue("$agentKey", item.AgentKey);
        command.Parameters.AddWithValue("$surfaceType", item.SurfaceType.ToString().ToLowerInvariant());
        command.Parameters.AddWithValue("$surfaceTargetId", item.SurfaceTargetId);
        command.Parameters.AddWithValue("$displayName", item.DisplayName);
        command.Parameters.AddWithValue("$version", item.Version);
        command.Parameters.AddWithValue("$executablePath", (object?)item.ExecutablePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$versionEvidence", (object?)item.VersionEvidence ?? DBNull.Value);
        command.Parameters.AddWithValue("$discoveredAt", item.DiscoveredAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<InventoryItem>> ListAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<InventoryItem>();
        await using var connection = connectionFactory.CreateOpenConnection();
        var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, agent_key, surface_type, surface_target_id, display_name, version, executable_path, version_evidence, discovered_at_utc, updated_at_utc FROM inventory_items ORDER BY updated_at_utc DESC;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new InventoryItem
            {
                Id = reader.GetString(0),
                AgentKey = reader.GetString(1),
                SurfaceType = Enum.Parse<SurfaceType>(reader.GetString(2), ignoreCase: true),
                SurfaceTargetId = reader.GetString(3),
                DisplayName = reader.GetString(4),
                Version = reader.GetString(5),
                ExecutablePath = reader.IsDBNull(6) ? null : reader.GetString(6),
                VersionEvidence = reader.IsDBNull(7) ? null : reader.GetString(7),
                DiscoveredAt = DateTimeOffset.Parse(reader.GetString(8)),
                UpdatedAt = DateTimeOffset.Parse(reader.GetString(9)),
            });
        }

        return results;
    }

    public async Task<InventoryItem?> GetLatestByAgentKeyAsync(string agentKey, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateOpenConnection();
        var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, agent_key, surface_type, surface_target_id, display_name, version, executable_path, version_evidence, discovered_at_utc, updated_at_utc FROM inventory_items WHERE agent_key = $agentKey ORDER BY updated_at_utc DESC LIMIT 1;";
        command.Parameters.AddWithValue("$agentKey", agentKey);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new InventoryItem
        {
            Id = reader.GetString(0),
            AgentKey = reader.GetString(1),
            SurfaceType = Enum.Parse<SurfaceType>(reader.GetString(2), ignoreCase: true),
            SurfaceTargetId = reader.GetString(3),
            DisplayName = reader.GetString(4),
            Version = reader.GetString(5),
            ExecutablePath = reader.IsDBNull(6) ? null : reader.GetString(6),
            VersionEvidence = reader.IsDBNull(7) ? null : reader.GetString(7),
            DiscoveredAt = DateTimeOffset.Parse(reader.GetString(8)),
            UpdatedAt = DateTimeOffset.Parse(reader.GetString(9)),
        };
    }
}
