using Philche.Core.Data;
using Philche.Core.Domain.Enums;
using Philche.Core.Domain.Models;

namespace Philche.Core.Test;

public sealed class DataStoreTests
{
    [Fact(DisplayName = "資料儲存測試：Migration Runner Creates Required Tables")]
    public async Task MigrationRunner_CreatesRequiredTables()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"philche-test-{Guid.NewGuid():N}.db");
        try
        {
            var store = new PhilcheDataStore(dbPath);
            await store.MigrationRunner.ApplyAsync();

            await using var connection = store.ConnectionFactory.CreateOpenConnection();
            var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name IN ('inventory_items','scan_targets','findings','scan_runs','scan_cache','schema_migrations') ORDER BY name;";

            var names = new List<string>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                names.Add(reader.GetString(0));
            }

            Assert.Equal(6, names.Count);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    [Fact(DisplayName = "資料儲存測試：Repositories Upsert And Read Normalized Finding")]
    public async Task Repositories_UpsertAndReadNormalizedFinding()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"philche-test-{Guid.NewGuid():N}.db");
        try
        {
            var store = new PhilcheDataStore(dbPath);
            await store.MigrationRunner.ApplyAsync();

            var target = new ScanTarget
            {
                Id = "target-host",
                SurfaceType = SurfaceType.Host,
                TargetKey = "host",
                DisplayName = "Host",
                IsSelected = true,
                IsNewlyDiscovered = false,
            };
            await store.ScanTargets.UpsertAsync(target);

            var finding = new Finding
            {
                Id = "finding-1",
                CanonicalVulnerabilityId = "CVE-2026-0001",
                TargetId = target.Id,
                FindingType = FindingType.Skills,
                Summary = "Prompt exfiltration pattern",
                Description = "Potential malicious extraction intent",
                Severity = "HIGH",
                SkillsRiskLevel = RiskLevel.High,
                Provenance = [
                    new FieldProvenance("summary", "osv", "OSV-2026-1"),
                    new FieldProvenance("severity", "nvd", "CVE-2026-0001")
                ],
                SourceReferences = "osv,nvd",
            };

            await store.Findings.UpsertAsync(finding);

            var loaded = await store.Findings.ListByTargetAsync(target.Id);
            var actual = Assert.Single(loaded);

            Assert.Equal(RiskLevel.High, actual.SkillsRiskLevel);
            Assert.Equal(2, actual.Provenance.Count);
            Assert.Equal("summary", actual.Provenance[0].Field);
            Assert.Equal("osv", actual.Provenance[0].Source);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }
}


