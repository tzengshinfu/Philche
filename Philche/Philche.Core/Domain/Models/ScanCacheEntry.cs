namespace Philche.Core.Domain.Models;

public sealed class ScanCacheEntry
{
    public string SkillPath { get; init; } = string.Empty;
    public string ScanType { get; init; } = string.Empty;
    public string ContentHash { get; init; } = string.Empty;
    public string? AgentVersion { get; init; }
    public DateTimeOffset LastScannedAt { get; init; }
    public string FindingsJson { get; init; } = string.Empty;
}
