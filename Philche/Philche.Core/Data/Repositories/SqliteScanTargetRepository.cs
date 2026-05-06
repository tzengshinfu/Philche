using Philche.Core.Domain.Enums;
using Philche.Core.Domain.Models;

namespace Philche.Core.Data.Repositories;

public sealed class SqliteScanTargetRepository : IScanTargetRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public SqliteScanTargetRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task UpsertAsync(ScanTarget target, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateOpenConnection();
        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO scan_targets (
              id, surface_type, target_key, display_name, is_selected,
              is_newly_discovered, discovered_at_utc, updated_at_utc
            ) VALUES (
              $id, $surfaceType, $targetKey, $displayName, $isSelected,
              $isNewlyDiscovered, $discoveredAt, $updatedAt
            )
            ON CONFLICT(id) DO UPDATE SET
              surface_type = excluded.surface_type,
              target_key = excluded.target_key,
              display_name = excluded.display_name,
              is_selected = excluded.is_selected,
              is_newly_discovered = excluded.is_newly_discovered,
              updated_at_utc = excluded.updated_at_utc;
            """;

        command.Parameters.AddWithValue("$id", target.Id);
        command.Parameters.AddWithValue("$surfaceType", target.SurfaceType.ToString().ToLowerInvariant());
        command.Parameters.AddWithValue("$targetKey", target.TargetKey);
        command.Parameters.AddWithValue("$displayName", target.DisplayName);
        command.Parameters.AddWithValue("$isSelected", target.IsSelected ? 1 : 0);
        command.Parameters.AddWithValue("$isNewlyDiscovered", target.IsNewlyDiscovered ? 1 : 0);
        command.Parameters.AddWithValue("$discoveredAt", target.DiscoveredAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ScanTarget>> ListBySurfaceAsync(SurfaceType surfaceType, CancellationToken cancellationToken = default)
    {
        var results = new List<ScanTarget>();
        await using var connection = connectionFactory.CreateOpenConnection();
        var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, surface_type, target_key, display_name, is_selected, is_newly_discovered, discovered_at_utc, updated_at_utc FROM scan_targets WHERE surface_type = $surfaceType ORDER BY display_name;";
        command.Parameters.AddWithValue("$surfaceType", surfaceType.ToString().ToLowerInvariant());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new ScanTarget
            {
                Id = reader.GetString(0),
                SurfaceType = Enum.Parse<SurfaceType>(reader.GetString(1), true),
                TargetKey = reader.GetString(2),
                DisplayName = reader.GetString(3),
                IsSelected = reader.GetInt64(4) == 1,
                IsNewlyDiscovered = reader.GetInt64(5) == 1,
                DiscoveredAt = DateTimeOffset.Parse(reader.GetString(6)),
                UpdatedAt = DateTimeOffset.Parse(reader.GetString(7)),
            });
        }

        return results;
    }
}
