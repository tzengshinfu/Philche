using Philche.Core.Domain.Enums;

namespace Philche.Core.Domain.Models;

public sealed record Finding
{
    public required string Id { get; init; }
    public required string CanonicalVulnerabilityId { get; init; }
    public required string TargetId { get; init; }
    public required FindingType FindingType { get; init; }
    public string? Summary { get; init; }
    public string? OriginalSummary { get; init; }
    public string? SimplifiedSummary { get; init; }
    public string? Description { get; init; }
    public string? Severity { get; init; }
    public RiskLevel? SkillsRiskLevel { get; init; }
    public required IReadOnlyList<FieldProvenance> Provenance { get; init; }
    public string? SourceReferences { get; init; }
    public int? StartLine { get; init; }
    public int? EndLine { get; init; }
    public DateTimeOffset FirstSeenAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}
