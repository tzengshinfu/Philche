using Philche.Core.Data;
using Philche.Core.Discovery;
using Philche.Core.Domain.Models;
using Philche.Core.Orchestration;
using Philche.Core.SkillsRisk;
using Philche.Tray;

namespace Philche.Core.Test;

public sealed class ScanProgressControllerTests
{
    [Fact(DisplayName = "掃描進度控制器測試：Apply Progress Tracks Files And Progress Status")]
    public void ApplyProgress_TracksFilesAndProgressStatus()
    {
        var controller = new ScanProgressController(action => action());

        controller.ApplyProgress(new ScanProgressInfo("manual", "all", "Started", 0, [], 3, 0));
        controller.ApplyProgress(new ScanProgressInfo("manual", "agent-a", "Progress", 0, [], 3, 1, @"C:\skills\a.py"));
        controller.ApplyProgress(new ScanProgressInfo("manual", "agent-a", "Progress", 0, [], 3, 1, @"C:\skills\a.py"));

        Assert.True(controller.IsProgressVisible);
        Assert.False(controller.IsIndeterminate);
        Assert.Equal(33.333333333333336d, controller.ProgressPercentage, precision: 6);
        Assert.Equal("掃描中: 1/3 (33%)", controller.StatusText);
        Assert.Single(controller.Files);
        Assert.Equal(@"C:\skills\a.py", controller.Files[0].FilePath);
    }

    [Fact(DisplayName = "掃描進度控制器測試：Apply Snapshot Restores Existing Scan State")]
    public void ApplySnapshot_RestoresExistingScanState()
    {
        var controller = new ScanProgressController(action => action());
        var snapshot = new ScanProgressSnapshot(
            new ScanProgressInfo("manual", "all", "Completed", 2, [@"C:\skills\a.py"], 2, 2),
            [@"C:\skills\a.py", @"C:\skills\b.py"]);

        controller.ApplySnapshot(snapshot);

        Assert.False(controller.IsProgressVisible);
        Assert.False(controller.IsIndeterminate);
        Assert.Equal(100d, controller.ProgressPercentage);
        Assert.Equal("掃描完成: 2/2，總大小 0 B | 2 筆風險", controller.StatusText);
        Assert.Equal(2, controller.Files.Count);
    }

    [Fact(DisplayName = "掃描進度控制器測試：Apply Progress For Started And Completed Transitions Updates Visibility And Status")]
    public void ApplyProgress_ForStartedAndCompletedTransitions_UpdatesVisibilityAndStatus()
    {
        var controller = new ScanProgressController(action => action());

        controller.ApplyProgress(new ScanProgressInfo("manual", "all", "Started", 0, [], 0, 0));

        Assert.True(controller.IsProgressVisible);
        Assert.True(controller.IsIndeterminate);
        Assert.Equal("掃描中", controller.StatusText);

        controller.ApplyProgress(new ScanProgressInfo("manual", "all", "Completed", 0, [], 0, 0));

        Assert.False(controller.IsProgressVisible);
        Assert.False(controller.IsIndeterminate);
        Assert.Equal("掃描完成", controller.StatusText);
    }

    [Fact(DisplayName = "掃描進度控制器測試：Attach Scheduler Restores Scheduler Snapshot For Late Subscribers")]
    public async Task AttachScheduler_RestoresSchedulerSnapshotForLateSubscribers()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"philche-progress-{Guid.NewGuid():N}.db");
        var tempDir = Path.Combine(Path.GetTempPath(), $"philche-progress-files-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "alpha.py"), "print('alpha')");
            File.WriteAllText(Path.Combine(tempDir, "beta.py"), "print('beta')");

            var store = new PhilcheDataStore(dbPath);
            await store.MigrationRunner.ApplyAsync();

            var scheduler = new ScanScheduler(
                new ScanSchedulerOptions
                {
                    PeriodicInterval = TimeSpan.FromHours(1),
                    DebounceWindow = TimeSpan.Zero,
                    MinimumAcceleratedInterval = TimeSpan.Zero,
                },
                store.ScanRuns,
                (_, _, _) => Task.FromResult(new ScanExecutionResult(1, 0, 0)),
                codeScanExecutor: null,
                agentCodeCollector: new AgentCodeCollector(),
                catalogEntries: [new KnownAgentCatalogEntry
                {
                    AgentKey = "agent-a",
                    DisplayName = "Agent A",
                    SkillsPaths = [new SkillsPathEntry(tempDir, false)],
                }]);

            var ran = await scheduler.TryRunManualAsync(DateTimeOffset.UtcNow);
            var controller = new ScanProgressController(action => action());

            controller.AttachScheduler(scheduler);

            Assert.True(ran);
            Assert.Equal("掃描完成: 2/2，總大小 0 B", controller.StatusText);
            Assert.Equal(2, controller.Files.Count);
            Assert.Contains(controller.Files, static file => file.FilePath.EndsWith("alpha.py", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(controller.Files, static file => file.FilePath.EndsWith("beta.py", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }

            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact(DisplayName = "掃描進度控制器測試：Toggle Pause Resume Synchronizes With Scheduler State")]
    public async Task TogglePauseResume_SynchronizesWithSchedulerState()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"philche-progress-toggle-{Guid.NewGuid():N}.db");

        try
        {
            var store = new PhilcheDataStore(dbPath);
            await store.MigrationRunner.ApplyAsync();

            var scheduler = new ScanScheduler(
                new ScanSchedulerOptions
                {
                    PeriodicInterval = TimeSpan.FromHours(1),
                    DebounceWindow = TimeSpan.Zero,
                    MinimumAcceleratedInterval = TimeSpan.Zero,
                },
                store.ScanRuns,
                (_, _, _) => Task.FromResult(new ScanExecutionResult(0, 0, 0)));

            var controller = new ScanProgressController(action => action());
            controller.AttachScheduler(scheduler);

            Assert.True(controller.TogglePauseResume());
            Assert.True(scheduler.IsPaused);
            Assert.True(controller.IsPaused);
            Assert.Equal("繼續掃描", controller.PauseResumeButtonText);

            Assert.True(controller.TogglePauseResume());
            Assert.False(scheduler.IsPaused);
            Assert.False(controller.IsPaused);
            Assert.Equal("暫停掃描", controller.PauseResumeButtonText);
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


