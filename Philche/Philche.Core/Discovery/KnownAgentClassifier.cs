using Philche.Core.Config;

namespace Philche.Core.Discovery;

public sealed class KnownAgentClassifier
{
    private readonly IReadOnlyList<KnownAgentCatalogEntry>? catalogOverride;
    private readonly ISettingsYamlStore settingsYamlStore;

    public KnownAgentClassifier(IReadOnlyList<KnownAgentCatalogEntry>? catalog = null, ISettingsYamlStore? settingsYamlStore = null)
    {
        catalogOverride = catalog;
        this.settingsYamlStore = settingsYamlStore ?? new SettingsYamlStore();
    }

    public (IReadOnlyList<string> KnownCandidates, int UnknownIgnored) FilterKnownExecutables(IEnumerable<string> executablePaths)
    {
        var catalog = catalogOverride ?? settingsYamlStore.LoadCatalog();

        var knownNames = catalog
            .SelectMany(x => x.ExecutableNames)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var known = new List<string>();
        var unknownIgnored = 0;

        foreach (var path in executablePaths)
        {
            var name = Path.GetFileName(path).ToLowerInvariant();
            if (knownNames.Contains(name))
            {
                known.Add(path);
            }
            else
            {
                unknownIgnored++;
            }
        }

        return (known, unknownIgnored);
    }
}
