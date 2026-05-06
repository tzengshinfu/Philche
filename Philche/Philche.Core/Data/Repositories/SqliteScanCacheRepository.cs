using Philche.Core.Domain.Models;

namespace Philche.Core.Data.Repositories;

public sealed class SqliteScanCacheRepository : IScanCacheRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public SqliteScanCacheRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<ScanCacheEntry?> GetAsync(string skillPath, string scanType, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateOpenConnection();
        var command = connection.CreateCommand();
        command.CommandText =
            "SELECT skill_path, scan_type, content_hash, last_scanned_utc, findings_json, agent_version FROM scan_cache WHERE skill_path = $skillPath AND scan_type = $scanType LIMIT 1;";
        command.Parameters.AddWithValue("$skillPath", skillPath);
        command.Parameters.AddWithValue("$scanType", scanType);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ScanCacheEntry
        {
            SkillPath = reader.GetString(0),
            ScanType = reader.GetString(1),
            ContentHash = reader.GetString(2),
            LastScannedAt = DateTimeOffset.Parse(reader.GetString(3)),
            FindingsJson = reader.GetString(4),
            AgentVersion = reader.IsDBNull(5) ? null : reader.GetString(5),
        };
    }

    public async Task UpsertAsync(ScanCacheEntry entry, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateOpenConnection();
        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO scan_cache (skill_path, scan_type, content_hash, last_scanned_utc, findings_json, agent_version)
            VALUES ($skillPath, $scanType, $contentHash, $lastScannedAt, $findingsJson, $agentVersion)
            ON CONFLICT(skill_path, scan_type) DO UPDATE SET
                content_hash = excluded.content_hash,
                last_scanned_utc = excluded.last_scanned_utc,
                findings_json = excluded.findings_json,
                agent_version = excluded.agent_version;
            """;

        command.Parameters.AddWithValue("$skillPath", entry.SkillPath);
        command.Parameters.AddWithValue("$scanType", entry.ScanType);
        command.Parameters.AddWithValue("$contentHash", entry.ContentHash);
        command.Parameters.AddWithValue("$lastScannedAt", entry.LastScannedAt.ToString("O"));
        command.Parameters.AddWithValue("$findingsJson", entry.FindingsJson);
        command.Parameters.AddWithValue("$agentVersion", (object?)entry.AgentVersion ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteByScanTypeAsync(string scanType, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateOpenConnection();
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM scan_cache WHERE scan_type = $scanType;";
        command.Parameters.AddWithValue("$scanType", scanType);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ClearAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateOpenConnection();
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM scan_cache;";

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
