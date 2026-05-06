using Philche.Core.Data.Repositories;
using Philche.Core.Config;
using Philche.Core.Domain.Enums;
using Philche.Core.Domain.Models;

namespace Philche.Core.Discovery;

public sealed class TargetSelectionSyncService
{
    private readonly IScanTargetRepository scanTargetRepository;
    private readonly IWslDistroProvider wslProvider;
    private readonly RuntimeFeatureFlags featureFlags;

    public TargetSelectionSyncService(
        IScanTargetRepository scanTargetRepository,
        IWslDistroProvider wslProvider,
        RuntimeFeatureFlags? featureFlags = null)
    {
        this.scanTargetRepository = scanTargetRepository;
        this.wslProvider = wslProvider;
        this.featureFlags = featureFlags ?? new RuntimeFeatureFlags();
    }

    public async Task<DiscoveryDiagnostics> SyncAsync(int knownAgentsDiscovered, int unknownCandidatesIgnored, CancellationToken cancellationToken = default)
    {
        var existingWsl = await scanTargetRepository.ListBySurfaceAsync(SurfaceType.Wsl, cancellationToken);

        var wslDistros = featureFlags.EnableWslScanning
            ? await wslProvider.ListDistrosAsync(cancellationToken)
            : [];
        foreach (var distro in wslDistros)
        {
            var existing = existingWsl.FirstOrDefault(x => x.TargetKey.Equals(distro, StringComparison.OrdinalIgnoreCase));
            await scanTargetRepository.UpsertAsync(new ScanTarget
            {
                Id = existing?.Id ?? $"wsl:{distro.ToLowerInvariant()}",
                SurfaceType = SurfaceType.Wsl,
                TargetKey = distro,
                DisplayName = distro,
                IsSelected = existing?.IsSelected ?? false,
                IsNewlyDiscovered = existing is null,
                DiscoveredAt = existing?.DiscoveredAt ?? DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            }, cancellationToken);
        }

        return new DiscoveryDiagnostics(
            knownAgentsDiscovered,
            unknownCandidatesIgnored,
            wslDistros.Count);
    }
}
