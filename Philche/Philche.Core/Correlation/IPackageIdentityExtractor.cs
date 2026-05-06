using Philche.Core.Domain.Models;

namespace Philche.Core.Correlation;

public interface IPackageIdentityExtractor
{
    PackageIdentity? TryExtract(InventoryItem item);
}
