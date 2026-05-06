namespace Philche.Core.Correlation;

public sealed record PackageIdentity(
    string Name,
    string Version,
    string Purl);
