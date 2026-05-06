using Philche.Core.Domain.Enums;

namespace Philche.Core.Domain.Models;

public sealed record ScanTarget
{
    public required string Id { get; init; }
    public required SurfaceType SurfaceType { get; init; }
    public required string TargetKey { get; init; }
    public required string DisplayName { get; init; }
    public bool IsSelected { get; init; }
    public bool IsNewlyDiscovered { get; init; } = true;
    public DateTimeOffset DiscoveredAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}
