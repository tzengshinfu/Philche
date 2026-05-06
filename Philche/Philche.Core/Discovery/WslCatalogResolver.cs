using Philche.Core.Domain.Models;

namespace Philche.Core.Discovery;

public sealed class WslCatalogResolver
{
    private readonly IWslDistroProvider wslProvider;
    private readonly Func<string, string> homeRootPathFactory;
    private readonly Func<string> windowsUserProfileFactory;

    public WslCatalogResolver(
        IWslDistroProvider? wslProvider = null,
        Func<string, string>? homeRootPathFactory = null,
        Func<string>? windowsUserProfileFactory = null)
    {
        this.wslProvider = wslProvider ?? new CommandWslDistroProvider();
        this.homeRootPathFactory = homeRootPathFactory ?? (static distro => $@"\\wsl.localhost\{distro}\home");
        this.windowsUserProfileFactory = windowsUserProfileFactory
            ?? (() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    public IReadOnlyList<KnownAgentCatalogEntry> Expand(IReadOnlyList<KnownAgentCatalogEntry> entries, CancellationToken cancellationToken = default)
    {
        return ExpandAsync(entries, cancellationToken).GetAwaiter().GetResult();
    }

    public async Task<IReadOnlyList<KnownAgentCatalogEntry>> ExpandAsync(
        IReadOnlyList<KnownAgentCatalogEntry> entries,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows() || entries.Count == 0)
        {
            return entries;
        }

        IReadOnlyList<string> distros;
        try
        {
            distros = await wslProvider.ListDistrosAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            distros = [];
        }

        return entries
            .Select(entry => ExpandEntry(entry, distros))
            .ToList();
    }

    private KnownAgentCatalogEntry ExpandEntry(KnownAgentCatalogEntry entry, IReadOnlyList<string> distros)
    {
        var relativePath = NormalizeRelativePath(entry.WslUserRelativePath);
        var generatedPaths = string.IsNullOrWhiteSpace(relativePath)
            ? []
            : BuildDefaultPaths(relativePath, distros);

        return entry with
        {
            SkillsPaths = MergeSkillsPaths(entry.SkillsPaths, generatedPaths),
        };
    }

    private IReadOnlyList<SkillsPathEntry> BuildDefaultPaths(string relativePath, IReadOnlyList<string> distros)
    {
        var generatedPaths = new List<SkillsPathEntry>();

        var windowsPath = Path.Combine(windowsUserProfileFactory(), relativePath);
        if (Directory.Exists(windowsPath))
        {
            generatedPaths.Add(new SkillsPathEntry(windowsPath, Trusted: true, Default: true));
        }

        foreach (var distro in distros
                     .Where(static value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var userName in GetUserNames(distro))
            {
                var fullPath = Path.Combine(homeRootPathFactory(distro), userName, relativePath);
                if (Directory.Exists(fullPath))
                {
                    generatedPaths.Add(new SkillsPathEntry(fullPath, Trusted: true, Default: true));
                }
            }
        }

        return generatedPaths;
    }

    private IReadOnlyList<string> GetUserNames(string distro)
    {
        var homeRoot = homeRootPathFactory(distro);
        if (string.IsNullOrWhiteSpace(homeRoot) || !Directory.Exists(homeRoot))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateDirectories(homeRoot)
                .Select(Path.GetFileName)
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()!;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IReadOnlyList<SkillsPathEntry> MergeSkillsPaths(
        IReadOnlyList<SkillsPathEntry> existingPaths,
        IReadOnlyList<SkillsPathEntry> generatedPaths)
    {
        var merged = new Dictionary<string, SkillsPathEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var generated in generatedPaths.Where(static entry => !string.IsNullOrWhiteSpace(entry.Path)))
        {
            var normalized = generated.Path.Trim();
            merged[normalized] = generated;
        }

        foreach (var existing in existingPaths.Where(static entry => !string.IsNullOrWhiteSpace(entry.Path)))
        {
            var trimmedPath = existing.Path.Trim();
            var expandedPath = Environment.ExpandEnvironmentVariables(trimmedPath);

            // If the expanded path already exists as a default path, we override its Trusted value but keep Default=true
            // This allows users to explicitly set Trusted=false on default paths.
            if (merged.TryGetValue(expandedPath, out var defaultEntry) || merged.TryGetValue(trimmedPath, out defaultEntry))
            {
                merged[defaultEntry.Path] = defaultEntry with { Trusted = existing.Trusted };
            }
            else
            {
                merged[trimmedPath] = existing;
            }
        }

        return [.. merged.Values];
    }

    private static string NormalizeRelativePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim()
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
    }
}