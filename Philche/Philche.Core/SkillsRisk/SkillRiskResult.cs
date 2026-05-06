using Philche.Core.Domain.Enums;

namespace Philche.Core.SkillsRisk;

public sealed record SkillRiskResult(
    RiskLevel RiskLevel,
    bool IsDegradedMode,
    bool ShouldBlock,
    IReadOnlyList<SkillRiskEvidence> Evidence);
