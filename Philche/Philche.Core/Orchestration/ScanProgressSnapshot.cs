namespace Philche.Core.Orchestration;

public sealed record ScanProgressSnapshot(
    ScanProgressInfo Progress,
    IReadOnlyList<string> Files);