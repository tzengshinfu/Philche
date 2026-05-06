namespace Philche.Core.Correlation;

public interface ICpeMapper
{
    /// <summary>
    /// Attempts to map a package identity to an NVD CPE 2.3 virtual match string
    /// (without version, e.g. cpe:2.3:a:vendor:product:*:*:*:*:*:*:*:*).
    /// Returns null when a reliable mapping cannot be determined.
    /// </summary>
    string? TryMapToCpe(PackageIdentity identity);
}
