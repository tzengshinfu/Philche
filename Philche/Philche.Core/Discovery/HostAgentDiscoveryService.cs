using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Philche.Core.Config;
using Philche.Core.Domain.Enums;
using Philche.Core.Domain.Models;

namespace Philche.Core.Discovery;

public sealed class HostAgentDiscoveryService
{
    private readonly IReadOnlyList<KnownAgentCatalogEntry>? catalogOverride;
    private readonly ISettingsYamlStore settingsYamlStore;

    public HostAgentDiscoveryService(IReadOnlyList<KnownAgentCatalogEntry>? catalog = null, ISettingsYamlStore? settingsYamlStore = null)
    {
        catalogOverride = catalog;
        this.settingsYamlStore = settingsYamlStore ?? new SettingsYamlStore();
    }

    public async Task<IReadOnlyList<InventoryItem>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var catalog = catalogOverride ?? settingsYamlStore.LoadCatalog();
        var items = new List<InventoryItem>();

        foreach (var entry in catalog)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (TryDiscoverByExecutablePath(entry, out var fileItem))
            {
                items.Add(fileItem);
                continue;
            }
        }

        return items;
    }

    private static bool TryDiscoverByExecutablePath(KnownAgentCatalogEntry entry, out InventoryItem item)
    {
        item = default!;

        foreach (var rawPath in entry.HostExecutablePaths)
        {
            var resolved = Environment.ExpandEnvironmentVariables(rawPath);
            if (!File.Exists(resolved))
            {
                continue;
            }

            var version = TryGetFileVersion(resolved);
            item = new InventoryItem
            {
                Id = CreateStableId(entry.AgentKey, SurfaceType.Host, "host", resolved, version),
                AgentKey = entry.AgentKey,
                SurfaceType = SurfaceType.Host,
                SurfaceTargetId = "host",
                DisplayName = entry.DisplayName,
                Version = version,
                ExecutablePath = resolved,
                VersionEvidence = "file-version",
                DiscoveredAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            return true;
        }

        return false;
    }

    private static string TryGetFileVersion(string executablePath)
    {
        var info = FileVersionInfo.GetVersionInfo(executablePath);
        return string.IsNullOrWhiteSpace(info.FileVersion) ? "unknown" : info.FileVersion!;
    }

    private static string CreateStableId(string agentKey, SurfaceType surfaceType, string surfaceTargetId, string identifier, string version)
    {
        var payload = $"{agentKey}|{surfaceType}|{surfaceTargetId}|{identifier}|{version}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
