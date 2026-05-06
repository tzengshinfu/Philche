using System.Text.Json;
using Philche.Core.Domain.Enums;
using Philche.Core.Domain.Models;

namespace Philche.Core.Data.Repositories;

public sealed class SqliteFindingRepository : IFindingRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public SqliteFindingRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task UpsertAsync(Finding finding, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateOpenConnection();
        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO findings (
              id, canonical_vulnerability_id, target_id, finding_type, summary,
                            original_summary, simplified_summary, description, severity, skills_risk_level, provenance_json,
                            source_references, first_seen_at_utc, updated_at_utc, start_line, end_line
            ) VALUES (
              $id, $canonicalId, $targetId, $findingType, $summary,
                            $originalSummary, $simplifiedSummary, $description, $severity, $skillsRiskLevel, $provenance,
              $sourceReferences, $firstSeenAt, $updatedAt, $startLine, $endLine
            )
            ON CONFLICT(id) DO UPDATE SET
              canonical_vulnerability_id = excluded.canonical_vulnerability_id,
              target_id = excluded.target_id,
              finding_type = excluded.finding_type,
              summary = excluded.summary,
                            original_summary = excluded.original_summary,
                            simplified_summary = excluded.simplified_summary,
              description = excluded.description,
              severity = excluded.severity,
              skills_risk_level = excluded.skills_risk_level,
              provenance_json = excluded.provenance_json,
              source_references = excluded.source_references,
              updated_at_utc = excluded.updated_at_utc,
              start_line = excluded.start_line,
              end_line = excluded.end_line;
            """;

        command.Parameters.AddWithValue("$id", finding.Id);
        command.Parameters.AddWithValue("$canonicalId", finding.CanonicalVulnerabilityId);
        command.Parameters.AddWithValue("$targetId", finding.TargetId);
        command.Parameters.AddWithValue("$findingType", finding.FindingType.ToString().ToLowerInvariant());
        command.Parameters.AddWithValue("$summary", (object?)finding.Summary ?? DBNull.Value);
        command.Parameters.AddWithValue("$originalSummary", (object?)finding.OriginalSummary ?? DBNull.Value);
        command.Parameters.AddWithValue("$simplifiedSummary", (object?)finding.SimplifiedSummary ?? DBNull.Value);
        command.Parameters.AddWithValue("$description", (object?)finding.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("$severity", (object?)finding.Severity ?? DBNull.Value);
        command.Parameters.AddWithValue("$skillsRiskLevel", finding.SkillsRiskLevel?.ToString().ToUpperInvariant() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$provenance", JsonSerializer.Serialize(finding.Provenance, RepositoryJson.Options));
        command.Parameters.AddWithValue("$sourceReferences", (object?)finding.SourceReferences ?? DBNull.Value);
        command.Parameters.AddWithValue("$firstSeenAt", finding.FirstSeenAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$startLine", (object?)finding.StartLine ?? DBNull.Value);
        command.Parameters.AddWithValue("$endLine", (object?)finding.EndLine ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<Finding?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateOpenConnection();
        var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, canonical_vulnerability_id, target_id, finding_type, summary, original_summary, simplified_summary, description, severity, skills_risk_level, provenance_json, source_references, first_seen_at_utc, updated_at_utc, start_line, end_line FROM findings WHERE id = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var provenanceJson = reader.GetString(10);
        var provenance = JsonSerializer.Deserialize<List<FieldProvenance>>(provenanceJson, RepositoryJson.Options) ?? [];

        return new Finding
        {
            Id = reader.GetString(0),
            CanonicalVulnerabilityId = reader.GetString(1),
            TargetId = reader.GetString(2),
            FindingType = Enum.Parse<FindingType>(reader.GetString(3), true),
            Summary = reader.IsDBNull(4) ? null : reader.GetString(4),
            OriginalSummary = reader.IsDBNull(5) ? null : reader.GetString(5),
            SimplifiedSummary = reader.IsDBNull(6) ? null : reader.GetString(6),
            Description = reader.IsDBNull(7) ? null : reader.GetString(7),
            Severity = reader.IsDBNull(8) ? null : reader.GetString(8),
            SkillsRiskLevel = reader.IsDBNull(9) ? null : Enum.Parse<RiskLevel>(reader.GetString(9), true),
            Provenance = provenance,
            SourceReferences = reader.IsDBNull(11) ? null : reader.GetString(11),
            FirstSeenAt = DateTimeOffset.Parse(reader.GetString(12)),
            UpdatedAt = DateTimeOffset.Parse(reader.GetString(13)),
            StartLine = reader.IsDBNull(14) ? null : reader.GetInt32(14),
            EndLine = reader.IsDBNull(15) ? null : reader.GetInt32(15),
        };
    }

    public async Task<IReadOnlyList<Finding>> ListByTargetAsync(string targetId, CancellationToken cancellationToken = default)
    {
        var results = new List<Finding>();
        await using var connection = connectionFactory.CreateOpenConnection();
        var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, canonical_vulnerability_id, target_id, finding_type, summary, original_summary, simplified_summary, description, severity, skills_risk_level, provenance_json, source_references, first_seen_at_utc, updated_at_utc, start_line, end_line FROM findings WHERE target_id = $targetId ORDER BY updated_at_utc DESC;";
        command.Parameters.AddWithValue("$targetId", targetId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var provenanceJson = reader.GetString(10);
            var provenance = JsonSerializer.Deserialize<List<FieldProvenance>>(provenanceJson, RepositoryJson.Options) ?? [];

            results.Add(new Finding
            {
                Id = reader.GetString(0),
                CanonicalVulnerabilityId = reader.GetString(1),
                TargetId = reader.GetString(2),
                FindingType = Enum.Parse<FindingType>(reader.GetString(3), true),
                Summary = reader.IsDBNull(4) ? null : reader.GetString(4),
                OriginalSummary = reader.IsDBNull(5) ? null : reader.GetString(5),
                SimplifiedSummary = reader.IsDBNull(6) ? null : reader.GetString(6),
                Description = reader.IsDBNull(7) ? null : reader.GetString(7),
                Severity = reader.IsDBNull(8) ? null : reader.GetString(8),
                SkillsRiskLevel = reader.IsDBNull(9) ? null : Enum.Parse<RiskLevel>(reader.GetString(9), true),
                Provenance = provenance,
                SourceReferences = reader.IsDBNull(11) ? null : reader.GetString(11),
                FirstSeenAt = DateTimeOffset.Parse(reader.GetString(12)),
                UpdatedAt = DateTimeOffset.Parse(reader.GetString(13)),
                StartLine = reader.IsDBNull(14) ? null : reader.GetInt32(14),
                EndLine = reader.IsDBNull(15) ? null : reader.GetInt32(15),
            });
        }

        return results;
    }
}
