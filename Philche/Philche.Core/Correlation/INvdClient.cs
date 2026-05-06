namespace Philche.Core.Correlation;

public interface INvdClient
{
    /// <summary>
    /// Queries the NVD CVE API 2.0 using a CPE virtual match string and an upper-bound
    /// version filter. Only CVEs that affect versions up to and including
    /// <paramref name="version"/> are returned.
    /// </summary>
    Task<IReadOnlyList<VulnerabilityRecord>> QueryByCpeAsync(
        string virtualMatchString,
        string version,
        CancellationToken cancellationToken = default);
}
