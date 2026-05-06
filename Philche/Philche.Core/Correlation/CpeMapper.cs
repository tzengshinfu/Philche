namespace Philche.Core.Correlation;

/// <summary>
/// Maps a <see cref="PackageIdentity"/> to an NVD CPE 2.3 virtual match string
/// (version wildcard) suitable for use with the NVD CVE API virtualMatchString parameter.
/// </summary>
public sealed class CpeMapper : ICpeMapper
{
    /// <inheritdoc />
    public string? TryMapToCpe(PackageIdentity identity)
    {
        // Parse the PURL to extract ecosystem, (optional namespace), and product name.
        // PURL format: pkg:<type>/<[namespace/]name>@<version>[?<qualifiers>][#<subpath>]
        var purl = identity.Purl;
        if (string.IsNullOrWhiteSpace(purl) || !purl.StartsWith("pkg:", StringComparison.OrdinalIgnoreCase))
        {
            return BuildCpe(Sanitize(identity.Name), Sanitize(identity.Name));
        }

        // Strip "pkg:" prefix
        var body = purl["pkg:".Length..];

        // Strip version, qualifiers, subpath
        var atIndex = body.IndexOf('@');
        if (atIndex >= 0) body = body[..atIndex];
        var questionIndex = body.IndexOf('?');
        if (questionIndex >= 0) body = body[..questionIndex];
        var hashIndex = body.IndexOf('#');
        if (hashIndex >= 0) body = body[..hashIndex];

        // Split into type and the rest
        var slashIndex = body.IndexOf('/');
        if (slashIndex < 0)
        {
            return null;
        }

        var ecosystemType = body[..slashIndex].ToLowerInvariant();
        var nameSegment = body[(slashIndex + 1)..];

        // Some ecosystems use namespace/name (e.g. npm scoped packages: @scope/pkg)
        // The last segment after the last '/' is typically the product name;
        // the penultimate segment (if any) approximates the vendor.
        var parts = nameSegment.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        var product = Sanitize(DecodePurlComponent(parts[^1]));
        var vendor = parts.Length > 1 ? Sanitize(DecodePurlComponent(parts[^2])) : product;

        if (string.IsNullOrEmpty(product))
        {
            return null;
        }

        // Ecosystem-specific tweaks
        switch (ecosystemType)
        {
            case "npm":
                // Scoped npm packages: @scope/name → vendor=scope, product=name
                // Already handled by parts split above.
                break;
            case "nuget":
            case "pypi":
            case "maven":
            case "generic":
            default:
                // vendor defaults to product when no namespace is present
                break;
        }

        return BuildCpe(vendor, product);
    }

    private static string BuildCpe(string vendor, string product) =>
        $"cpe:2.3:a:{vendor}:{product}:*:*:*:*:*:*:*:*";

    private static string DecodePurlComponent(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch
        {
            return value;
        }
    }

    /// <summary>
    /// Sanitises a component value for inclusion in a CPE: lowercase, replace
    /// spaces and dots with underscores, strip non-alphanumeric/hyphen/underscore chars.
    /// </summary>
    private static string Sanitize(string value)
    {
        value = value.ToLowerInvariant().Replace(' ', '_').Replace('.', '_');
        var chars = value.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray();
        return new string(chars);
    }
}
