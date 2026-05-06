using Philche.Core.Domain.Models;

namespace Philche.Core.Correlation;

public sealed class DefaultPackageIdentityExtractor : IPackageIdentityExtractor
{
    public PackageIdentity? TryExtract(InventoryItem item)
    {
        if (string.IsNullOrWhiteSpace(item.AgentKey) || string.IsNullOrWhiteSpace(item.Version))
        {
            return null;
        }

        var normalizedName = item.AgentKey.Trim().ToLowerInvariant();
        var normalizedVersion = item.Version.Trim();
        var purl = $"pkg:generic/{normalizedName}@{normalizedVersion}";
        return new PackageIdentity(normalizedName, normalizedVersion, purl);
    }
}
