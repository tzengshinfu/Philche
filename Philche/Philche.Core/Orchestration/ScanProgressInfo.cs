namespace Philche.Core.Orchestration;

public sealed record ScanProgressInfo(
    string ScanType,
    string AgentKey,
    string Status,
    int FindingsCount,
    IReadOnlyList<string> HighRiskPaths,
    int TotalFiles = 0,
    int ScannedFiles = 0,
    string? CurrentFile = null,
    string? ScanTargetDisplayName = null,
    long TotalSizeBytes = 0,
    string? ErrorMessage = null);
