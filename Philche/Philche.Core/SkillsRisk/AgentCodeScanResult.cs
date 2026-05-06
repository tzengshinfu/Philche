namespace Philche.Core.SkillsRisk;

public sealed record CodeScanFileResult(
    string FilePath,
    IReadOnlyList<SkillRiskEvidence> Evidence);

public sealed record AgentCodeScanResult(
    IReadOnlyList<CodeScanFileResult> FileResults,
    int TotalFilesScanned,
    int FilesWithFindings);
