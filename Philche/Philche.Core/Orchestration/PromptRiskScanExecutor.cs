using System.Security.Cryptography;
using System.Text;
using Philche.Core.Config;
using Philche.Core.Data.Repositories;
using Philche.Core.Discovery;
using Philche.Core.Domain.Enums;
using Philche.Core.Domain.Models;
using Philche.Core.SkillsRisk;

namespace Philche.Core.Orchestration;

public sealed class PromptRiskScanExecutor
{
    private readonly ISettingsYamlStore settingsStore;
    private readonly IScanTargetRepository scanTargetRepository;
    private readonly IFindingRepository findingRepository;
    private readonly IScanCacheRepository? scanCacheRepository;

    public PromptRiskScanExecutor(
        ISettingsYamlStore settingsStore,
        IScanTargetRepository scanTargetRepository,
        IFindingRepository findingRepository,
        IScanCacheRepository? scanCacheRepository = null)
    {
        this.settingsStore = settingsStore;
        this.scanTargetRepository = scanTargetRepository;
        this.findingRepository = findingRepository;
        this.scanCacheRepository = scanCacheRepository;
    }

    public async Task<ScanExecutionResult> ExecuteAsync(
        IReadOnlyList<KnownAgentCatalogEntry> catalogEntries,
        string triggerReason,
        CancellationToken cancellationToken = default)
    {
        using var snapshot = new EvaluatorFactory(settingsStore).Build();
        var inventoryCount = 0;
        var findingCount = 0;
        var warningCount = 0;
        var bypassCache = triggerReason.Equals(ScanTriggerReason.Manual, StringComparison.OrdinalIgnoreCase);

        foreach (var entry in catalogEntries)
        {
            var target = await EnsureScanTargetAsync(entry, cancellationToken);

            foreach (var skillsPath in entry.SkillsPaths.Where(static item => !item.Trusted))
            {
                if (string.IsNullOrWhiteSpace(skillsPath.Path) || !Directory.Exists(skillsPath.Path))
                {
                    continue;
                }

                foreach (var promptFile in EnumeratePromptFiles(skillsPath.Path))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    inventoryCount++;
                    var content = await File.ReadAllTextAsync(promptFile, cancellationToken);
                    var result = await snapshot.Evaluator.EvaluatePromptWithCacheAsync(
                        promptFile,
                        new SkillEvaluationInput(content, promptFile, false),
                        scanCacheRepository,
                        bypassCache,
                        cancellationToken);

                    if (result.IsDegradedMode)
                    {
                        warningCount++;
                    }

                    var findingId = BuildFindingId(target.Id, promptFile);
                    if (result.RiskLevel >= RiskLevel.Medium)
                    {
                        findingCount++;
                        await findingRepository.UpsertAsync(
                            BuildFinding(target, promptFile, result, findingId),
                            cancellationToken);
                    }
                    else
                    {
                        await findingRepository.DeleteByIdAsync(findingId, cancellationToken);
                    }
                }
            }
        }

        return new ScanExecutionResult(inventoryCount, findingCount, warningCount);
    }

    internal static IEnumerable<string> EnumeratePromptFiles(string root)
    {
        try
        {
            return Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories)
                .Where(static path => !string.IsNullOrWhiteSpace(path));
        }
        catch
        {
            return [];
        }
    }

    internal static string BuildFindingId(string targetId, string promptFile)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{targetId}|{promptFile}"));
        return $"skills-{Convert.ToHexString(bytes)}";
    }

    private async Task<ScanTarget> EnsureScanTargetAsync(KnownAgentCatalogEntry entry, CancellationToken cancellationToken)
    {
        var existingTargets = await scanTargetRepository.ListBySurfaceAsync(SurfaceType.Host, cancellationToken);
        var targetKey = $"agent:{entry.AgentKey}";
        var existing = existingTargets.FirstOrDefault(target =>
            target.TargetKey.Equals(targetKey, StringComparison.OrdinalIgnoreCase));

        var target = new ScanTarget
        {
            Id = existing?.Id ?? $"host-agent:{entry.AgentKey}",
            SurfaceType = SurfaceType.Host,
            TargetKey = targetKey,
            DisplayName = entry.DisplayName,
            IsSelected = existing?.IsSelected ?? true,
            IsNewlyDiscovered = existing?.IsNewlyDiscovered ?? false,
            DiscoveredAt = existing?.DiscoveredAt ?? DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await scanTargetRepository.UpsertAsync(target, cancellationToken);
        return target;
    }

    private static Finding BuildFinding(ScanTarget target, string promptFile, SkillRiskResult result, string findingId)
    {
        var canonicalId = promptFile;
        var summary = $"{Path.GetFileName(promptFile)} ({result.RiskLevel})";
        var description = string.Join(Environment.NewLine, result.Evidence.Select(static evidence =>
            $"[{evidence.Detector}] score={evidence.Score:F3} {evidence.Message}"));
        var references = promptFile;
        var firstEvidenceWithLines = result.Evidence.FirstOrDefault(e => e.StartLine.HasValue || e.EndLine.HasValue);

        return new Finding
        {
            Id = findingId,
            CanonicalVulnerabilityId = canonicalId,
            TargetId = target.Id,
            FindingType = FindingType.Skills,
            Summary = summary,
            OriginalSummary = summary,
            SimplifiedSummary = summary,
            Description = description,
            Severity = result.RiskLevel.ToString().ToUpperInvariant(),
            SkillsRiskLevel = result.RiskLevel,
            Provenance =
            [
                new FieldProvenance("summary", "philche-prompt-scan", promptFile),
                new FieldProvenance("description", "philche-prompt-scan", promptFile),
                new FieldProvenance("skillsRiskLevel", "philche-prompt-scan", promptFile),
            ],
            SourceReferences = references,
            StartLine = firstEvidenceWithLines?.StartLine,
            EndLine = firstEvidenceWithLines?.EndLine,
            FirstSeenAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }
}