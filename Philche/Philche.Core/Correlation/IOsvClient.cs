namespace Philche.Core.Correlation;

public interface IOsvClient
{
    Task<IReadOnlyList<VulnerabilityRecord>> QueryByPurlAsync(PackageIdentity identity, CancellationToken cancellationToken = default);
}
