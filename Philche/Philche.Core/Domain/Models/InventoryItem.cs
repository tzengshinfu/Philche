using Philche.Core.Domain.Enums;

namespace Philche.Core.Domain.Models;

public sealed record InventoryItem
{
    public required string Id { get; init; }
    public required string AgentKey { get; init; }
    public required SurfaceType SurfaceType { get; init; }
    public required string SurfaceTargetId { get; init; }
    public required string DisplayName { get; init; }
    public required string Version { get; init; }
    public string? ExecutablePath { get; init; }
    public string? VersionEvidence { get; init; }
    public DateTimeOffset DiscoveredAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}
