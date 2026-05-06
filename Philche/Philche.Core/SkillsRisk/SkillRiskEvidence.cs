namespace Philche.Core.SkillsRisk;

public sealed record SkillRiskEvidence(
    string Detector,
    double Score,
    string Message,
    int? StartLine = null,
    int? EndLine = null);
