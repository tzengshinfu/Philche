using Avalonia.Headless.XUnit;
using Philche.Core.Data;
using Philche.Core.Domain.Enums;
using Philche.Core.Domain.Models;
using Philche.Core.Orchestration;
using Philche.Tray;
using System.Text.Json;

namespace Philche.Core.Test;

public sealed class MainWindowUiTests
{
    [AvaloniaFact]
    public void ScanProgressControls_AreBoundToController_AndReflectProgress()
    {
        var window = new MainWindow();

        Assert.Same(window.ScanProgressController, window.ScanProgressBar.DataContext);
        Assert.Same(window.ScanProgressController, window.ScanProgressStatusTextBlock.DataContext);
        Assert.Same(window.ScanProgressController, window.ScanProgressListBox.DataContext);
        Assert.Same(window.ScanProgressController, window.PauseResumeScanButton.DataContext);

        window.ScanProgressController.ApplyProgress(new ScanProgressInfo(
            "manual",
            "all",
            "Progress",
            0,
            [],
            4,
            2,
            @"C:\skills\alpha.py"));

        Assert.True(window.ScanProgressBar.IsVisible);
        Assert.False(window.ScanProgressBar.IsIndeterminate);
        Assert.Equal(50d, window.ScanProgressBar.Value);
        Assert.Equal("掃描中: 2/4 (50%)", window.ScanProgressStatusTextBlock.Text);
        Assert.Single(window.ScanProgressListBox.ItemsSource!.Cast<object>());
    }

    [AvaloniaFact]
    public void OpenScanProgressTab_SelectsScanProgressTab()
    {
        var window = new MainWindow();

        window.OpenScanProgressTab();

        Assert.Same(window.ScanProgressTabItem, window.RootTabControl.SelectedItem);
    }

    [AvaloniaFact]
    public async Task LoadFindingsAsync_IncludesLatestCodeScanRiskPaths()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"philche-mainwindow-{Guid.NewGuid():N}.db");

        try
        {
            var store = new PhilcheDataStore(dbPath);
            await store.MigrationRunner.ApplyAsync();

            await store.ScanTargets.UpsertAsync(new ScanTarget
            {
                Id = "target-1",
                SurfaceType = SurfaceType.Host,
                TargetKey = "host:/agents/demo",
                DisplayName = "Demo Agent",
                IsSelected = true,
            });

            await store.Findings.UpsertAsync(new Finding
            {
                Id = "finding-1",
                CanonicalVulnerabilityId = "CVE-2026-0001",
                TargetId = "target-1",
                FindingType = FindingType.Cve,
                Summary = "Demo finding",
                Description = "A demo vulnerability",
                Severity = "HIGH",
                Provenance = [new FieldProvenance("summary", "nvd", "CVE-2026-0001")],
            });

            await store.ScanRuns.UpsertAsync(new ScanRun
            {
                Id = "run-1",
                TriggerReason = ScanTriggerReason.ContextMenuScan,
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                EndedAt = DateTimeOffset.UtcNow,
                InventoryCount = 2,
                FindingCount = 1,
                WarningCount = 0,
                Status = "Completed",
                HighRiskPathsJson = JsonSerializer.Serialize(new[]
                {
                    @"C:\repo\malicious.md",
                    @"C:\repo\script.py",
                }),
            });

            var window = new MainWindow();
            window.SetDataStoreForTesting(store);

            await window.LoadFindingsAsync();

            var items = window.GetRiskReviewItemsSnapshot();

            Assert.Contains(items, static item => item.KindLabel == "Finding" && item.Title.Contains("Demo Agent", StringComparison.Ordinal));
            Assert.Contains(items, static item => item.KindLabel == "Code Scan" && item.Title.Contains("malicious.md", StringComparison.Ordinal));
            Assert.Contains(items, static item => item.KindLabel == "Code Scan" && item.Subtitle.Contains(@"C:\repo\script.py", StringComparison.Ordinal));
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }
}