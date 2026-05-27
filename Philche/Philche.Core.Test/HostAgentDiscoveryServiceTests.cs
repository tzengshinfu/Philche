using Philche.Core.Discovery;
using Philche.Core.Domain.Enums;

namespace Philche.Core.Test;

public sealed class HostAgentDiscoveryServiceTests
{
    [Fact(DisplayName = "主機代理探索服務測試：Discover Async Returns Only Known Agents Found On Configured Paths")]
    public async Task DiscoverAsync_ReturnsOnlyKnownAgentsFoundOnConfiguredPaths()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"philche-discovery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var foundExe = Path.Combine(tempDir, "known-agent.exe");
        await File.WriteAllTextAsync(foundExe, "not-a-real-exe");

        try
        {
            var catalog = new List<KnownAgentCatalogEntry>
            {
                new()
                {
                    AgentKey = "known-agent",
                    DisplayName = "Known Agent",
                    HostExecutablePaths = [foundExe],
                },
                new()
                {
                    AgentKey = "missing-agent",
                    DisplayName = "Missing Agent",
                    HostExecutablePaths = [Path.Combine(tempDir, "missing.exe")],
                },
            };

            var service = new HostAgentDiscoveryService(catalog);
            var items = await service.DiscoverAsync();

            var actual = Assert.Single(items);
            Assert.Equal("known-agent", actual.AgentKey);
            Assert.Equal(SurfaceType.Host, actual.SurfaceType);
            Assert.Equal(foundExe, actual.ExecutablePath);
            Assert.NotNull(actual.Version);
            Assert.Equal("file-version", actual.VersionEvidence);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

}


