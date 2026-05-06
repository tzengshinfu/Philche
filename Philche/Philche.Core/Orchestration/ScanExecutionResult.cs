namespace Philche.Core.Orchestration;

public sealed record ScanExecutionResult(
    int InventoryCount,
    int FindingCount,
    int WarningCount,
    string Status = "Completed");
