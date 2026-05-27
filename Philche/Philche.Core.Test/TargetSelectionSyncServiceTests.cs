using Philche.Core.Data;
using Philche.Core.Discovery;
using Philche.Core.Config;
using Philche.Core.Domain.Enums;
using Philche.Core.Domain.Models;

namespace Philche.Core.Test;

public sealed class TargetSelectionSyncServiceTests
{
    [Fact(DisplayName = "目標選取同步服務測試：Sync Async New Targets Default To Unselected And Existing Selection Is Preserved")]
    public async Task SyncAsync_NewTargetsDefaultToUnselected_AndExistingSelectionIsPreserved()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"philche-sync-{Guid.NewGuid():N}.db");
        try
        {
            var store = new PhilcheDataStore(dbPath);
            await store.MigrationRunner.ApplyAsync();

            await store.ScanTargets.UpsertAsync(new ScanTarget
            {
                Id = "wsl:ubuntu",
                SurfaceType = SurfaceType.Wsl,
                TargetKey = "Ubuntu",
                DisplayName = "Ubuntu",
                IsSelected = true,
                IsNewlyDiscovered = false,
            });

            var service = new TargetSelectionSyncService(
                store.ScanTargets,
                new FakeWslProvider(["Ubuntu", "Debian"]));

            var diagnostics = await service.SyncAsync(knownAgentsDiscovered: 2, unknownCandidatesIgnored: 1);
            Assert.Equal(2, diagnostics.WslTargetsEnumerated);
            Assert.Equal(1, diagnostics.UnknownCandidatesIgnored);

            var wslTargets = await store.ScanTargets.ListBySurfaceAsync(SurfaceType.Wsl);
            Assert.Equal(2, wslTargets.Count);

            var ubuntu = wslTargets.Single(x => x.TargetKey == "Ubuntu");
            Assert.True(ubuntu.IsSelected);
            Assert.False(ubuntu.IsNewlyDiscovered);

            var debian = wslTargets.Single(x => x.TargetKey == "Debian");
            Assert.False(debian.IsSelected);
            Assert.True(debian.IsNewlyDiscovered);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    [Fact(DisplayName = "目標選取同步服務測試：Filter Known Executables Ignores Unknown Candidates")]
    public void FilterKnownExecutables_IgnoresUnknownCandidates()
    {
        var classifier = new KnownAgentClassifier([
            new KnownAgentCatalogEntry
            {
                AgentKey = "known",
                DisplayName = "Known",
                HostExecutablePaths = ["known-agent.exe"],
                ExecutableNames = ["known-agent.exe"],
            }
        ]);

        var (known, unknownIgnored) = classifier.FilterKnownExecutables([
            @"C:\tools\known-agent.exe",
            @"C:\tools\random.exe"
        ]);

        var one = Assert.Single(known);
        Assert.Equal(@"C:\tools\known-agent.exe", one);
        Assert.Equal(1, unknownIgnored);
    }

    [Fact(DisplayName = "目標選取同步服務測試：Sync Async Respects Wsl Feature Flag")]
    public async Task SyncAsync_RespectsWslFeatureFlag()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"philche-sync-{Guid.NewGuid():N}.db");
        try
        {
            var store = new PhilcheDataStore(dbPath);
            await store.MigrationRunner.ApplyAsync();

            var service = new TargetSelectionSyncService(
                store.ScanTargets,
                new FakeWslProvider(["Ubuntu"]),
                new RuntimeFeatureFlags
                {
                    EnableWslScanning = false,
                });

            var diagnostics = await service.SyncAsync(knownAgentsDiscovered: 0, unknownCandidatesIgnored: 0);
            Assert.Equal(0, diagnostics.WslTargetsEnumerated);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    private sealed class FakeWslProvider(IReadOnlyList<string> distros) : IWslDistroProvider
    {
        public Task<IReadOnlyList<string>> ListDistrosAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(distros);
    }
}


