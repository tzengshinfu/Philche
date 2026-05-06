namespace Philche.Core.Domain.Models;

public sealed record ScanRun
{
    public required string Id { get; init; }
    public required string TriggerReason { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
    public int InventoryCount { get; init; }
    public int FindingCount { get; init; }
    public int WarningCount { get; init; }
    public string Status { get; init; } = "Running";
    public string? HighRiskPathsJson { get; init; }
}
