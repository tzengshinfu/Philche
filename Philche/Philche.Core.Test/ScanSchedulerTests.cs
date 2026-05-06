using Philche.Core.Discovery;
using Philche.Core.Data;
using Philche.Core.Domain.Models;
using Philche.Core.Orchestration;
using Philche.Core.SkillsRisk;

namespace Philche.Core.Test;

public sealed class ScanSchedulerTests
{
    [Fact]
    public async Task TryRunPeriodicAsync_RespectsMinimumOneHourInterval()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"philche-scheduler-{Guid.NewGuid():N}.db");
        try
        {
            var store = new PhilcheDataStore(dbPath);
            await store.MigrationRunner.ApplyAsync();

            var scheduler = new ScanScheduler(
                new ScanSchedulerOptions
                {
                    PeriodicInterval = TimeSpan.FromHours(1),
                    DebounceWindow = TimeSpan.FromSeconds(1),
                    MinimumAcceleratedInterval = TimeSpan.FromMinutes(10),
                },
                store.ScanRuns,
                (_, _, _) => Task.FromResult(new ScanExecutionResult(1, 2, 0)));

            var start = DateTimeOffset.UtcNow;
            var first = await scheduler.TryRunPeriodicAsync(start);
            var second = await scheduler.TryRunPeriodicAsync(start.AddMinutes(30));
            var third = await scheduler.TryRunPeriodicAsync(start.AddHours(1));

            Assert.True(first);
            Assert.False(second);
            Assert.True(third);

            var runs = await store.ScanRuns.ListRecentAsync(10);
            Assert.Equal(2, runs.Count);
            Assert.All(runs, x => Assert.Equal("periodic", x.TriggerReason));
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    [Fact]
    public async Task TryRunAcceleratedAsync_AppliesDebounceAndThrottleAndMergesReasons()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"philche-scheduler-{Guid.NewGuid():N}.db");
        try
        {
            var store = new PhilcheDataStore(dbPath);
            await store.MigrationRunner.ApplyAsync();

            var scheduler = new ScanScheduler(
                new ScanSchedulerOptions
                {
                    PeriodicInterval = TimeSpan.FromHours(1),
                    DebounceWindow = TimeSpan.FromSeconds(5),
                    MinimumAcceleratedInterval = TimeSpan.FromMinutes(10),
                },
                store.ScanRuns,
                (_, _, _) => Task.FromResult(new ScanExecutionResult(2, 3, 1)));

            var start = DateTimeOffset.UtcNow;
            scheduler.QueueAcceleratedEvent(TriggerEventKind.AgentVersionChanged, start);
            scheduler.QueueAcceleratedEvent(TriggerEventKind.SkillsFolderChanged, start.AddSeconds(1));

            var tooEarly = await scheduler.TryRunAcceleratedAsync(start.AddSeconds(3));
            var firstRun = await scheduler.TryRunAcceleratedAsync(start.AddSeconds(6));

            scheduler.QueueAcceleratedEvent(TriggerEventKind.ExtensionListChanged, start.AddSeconds(7));
            var throttled = await scheduler.TryRunAcceleratedAsync(start.AddSeconds(20));
            var secondRun = await scheduler.TryRunAcceleratedAsync(start.AddMinutes(11));

            Assert.False(tooEarly);
            Assert.True(firstRun);
            Assert.False(throttled);
            Assert.True(secondRun);

            var runs = await store.ScanRuns.ListRecentAsync(10);
            Assert.Equal(2, runs.Count);
            Assert.Contains("agent-version-changed", runs[1].TriggerReason);
            Assert.Contains("skills-folder-changed", runs[1].TriggerReason);
            Assert.Contains("extension-list-changed", runs[0].TriggerReason);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    [Fact]
    public async Task TryRunAcceleratedAsync_RunsCodeScanExecutor_ForSkillsChangeReason()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"philche-scheduler-{Guid.NewGuid():N}.db");
        try
        {
            var store = new PhilcheDataStore(dbPath);
            await store.MigrationRunner.ApplyAsync();

            var codeInvocations = 0;
            var scheduler = new ScanScheduler(
                new ScanSchedulerOptions
                {
                    PeriodicInterval = TimeSpan.FromHours(1),
                    DebounceWindow = TimeSpan.Zero,
                    MinimumAcceleratedInterval = TimeSpan.Zero,
                },
                store.ScanRuns,
                (_, _, _) => Task.FromResult(new ScanExecutionResult(1, 1, 0)),
                (_, _) =>
                {
                    codeInvocations++;
                    return Task.FromResult(new ScanExecutionResult(1, 2, 0));
                });

            var now = DateTimeOffset.UtcNow;
            scheduler.QueueAcceleratedEvent(TriggerEventKind.SkillsFolderChanged, now);

            var ran = await scheduler.TryRunAcceleratedAsync(now);

            Assert.True(ran);
            Assert.Equal(1, codeInvocations);

            var run = Assert.Single(await store.ScanRuns.ListRecentAsync(1));
            Assert.Equal(3, run.FindingCount);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    [Fact]
    public async Task TryRunAcceleratedAsync_UsesAgentCodeCollector_WhenCodeExecutorIsNull()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"philche-scheduler-{Guid.NewGuid():N}.db");
        var tempDir = Path.Combine(Path.GetTempPath(), $"philche-code-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "evil.py"), "import subprocess\nsubprocess.Popen('cmd.exe')");

            var store = new PhilcheDataStore(dbPath);
            await store.MigrationRunner.ApplyAsync();

            var collector = new AgentCodeCollector();
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
                agentCodeCollector: collector,
                catalogEntries: [BuildCatalogEntry("agent-a", tempDir)]);

            var now = DateTimeOffset.UtcNow;
            scheduler.QueueAcceleratedEvent(TriggerEventKind.SkillsFolderChanged, now);

            var ran = await scheduler.TryRunAcceleratedAsync(now);

            Assert.True(ran);
            var run = Assert.Single(await store.ScanRuns.ListRecentAsync(1));
            Assert.True(run.FindingCount >= 1);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task PauseAndResume_ControlPeriodicAndAcceleratedEligibility()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"philche-scheduler-{Guid.NewGuid():N}.db");
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
                (_, _, _) => Task.FromResult(new ScanExecutionResult(1, 1, 0)));

            var now = DateTimeOffset.UtcNow;
            scheduler.QueueAcceleratedEvent(TriggerEventKind.AgentVersionChanged, now);
            scheduler.Pause();

            Assert.False(scheduler.CanRunPeriodic(now));
            Assert.False(scheduler.CanRunAccelerated(now));

            scheduler.Resume();

            Assert.True(scheduler.CanRunPeriodic(now));
            Assert.True(scheduler.CanRunAccelerated(now));
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task TryRunManualAsync_BypassesDebounceAndThrottle()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"philche-scheduler-{Guid.NewGuid():N}.db");
        try
        {
            var store = new PhilcheDataStore(dbPath);
            await store.MigrationRunner.ApplyAsync();

            var scheduler = new ScanScheduler(
                new ScanSchedulerOptions
                {
                    PeriodicInterval = TimeSpan.FromHours(1),
                    DebounceWindow = TimeSpan.FromMinutes(5),
                    MinimumAcceleratedInterval = TimeSpan.FromHours(1),
                },
                store.ScanRuns,
                (_, _, _) => Task.FromResult(new ScanExecutionResult(1, 2, 0)));

            var now = DateTimeOffset.UtcNow;
            scheduler.QueueAcceleratedEvent(TriggerEventKind.AgentVersionChanged, now);
            Assert.False(await scheduler.TryRunAcceleratedAsync(now));

            var manualRan = await scheduler.TryRunManualAsync(now);
            Assert.True(manualRan);

            var run = Assert.Single(await store.ScanRuns.ListRecentAsync(1));
            Assert.Equal(ScanTriggerReason.Manual, run.TriggerReason);
            Assert.Equal(2, run.FindingCount);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task ScanProgress_RaisesStartedAndCompletedEvents()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"philche-scheduler-{Guid.NewGuid():N}.db");
        try
        {
            var store = new PhilcheDataStore(dbPath);
            await store.MigrationRunner.ApplyAsync();

            var events = new List<ScanProgressInfo>();
            var scheduler = new ScanScheduler(
                new ScanSchedulerOptions
                {
                    PeriodicInterval = TimeSpan.FromHours(1),
                    DebounceWindow = TimeSpan.Zero,
                    MinimumAcceleratedInterval = TimeSpan.Zero,
                },
                store.ScanRuns,
                (_, _, _) => Task.FromResult(new ScanExecutionResult(1, 3, 0)));

            scheduler.ScanProgress += progress => events.Add(progress);

            var now = DateTimeOffset.UtcNow;
            var ran = await scheduler.TryRunPeriodicAsync(now);

            Assert.True(ran);
            Assert.Equal(2, events.Count);
            Assert.Equal("Started", events[0].Status);
            Assert.Equal("Completed", events[1].Status);
            Assert.Equal(3, events[1].FindingsCount);
            Assert.Equal(ScanTriggerReason.Periodic, events[1].ScanType);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task TryRunManualAsync_SkipsWhenAnotherFullScanIsRunning()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"philche-scheduler-{Guid.NewGuid():N}.db");
        try
        {
            var store = new PhilcheDataStore(dbPath);
            await store.MigrationRunner.ApplyAsync();

            var blockTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var startedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var scheduler = new ScanScheduler(
                new ScanSchedulerOptions
                {
                    PeriodicInterval = TimeSpan.FromHours(1),
                    DebounceWindow = TimeSpan.Zero,
                    MinimumAcceleratedInterval = TimeSpan.Zero,
                },
                store.ScanRuns,
                async (_, _, cancellationToken) =>
                {
                    startedTcs.TrySetResult();
                    await blockTcs.Task.WaitAsync(cancellationToken);
                    return new ScanExecutionResult(1, 0, 0);
                });

            var firstRun = scheduler.TryRunManualAsync(DateTimeOffset.UtcNow);
            await startedTcs.Task.WaitAsync(CancellationToken.None);

            var secondRun = await scheduler.TryRunManualAsync(DateTimeOffset.UtcNow);
            Assert.False(secondRun);

            blockTcs.TrySetResult();
            Assert.True(await firstRun);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task TryRunScopedAsync_CanRunInParallelWithFullScan()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"philche-scheduler-{Guid.NewGuid():N}.db");
        var tempDir = Path.Combine(Path.GetTempPath(), $"philche-code-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var scopedFile = Path.Combine(tempDir, "changed.py");
            File.WriteAllText(scopedFile, "subprocess.Popen('cmd.exe')");

            var store = new PhilcheDataStore(dbPath);
            await store.MigrationRunner.ApplyAsync();

            var holdTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var startedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var scheduler = new ScanScheduler(
                new ScanSchedulerOptions
                {
                    PeriodicInterval = TimeSpan.FromHours(1),
                    DebounceWindow = TimeSpan.Zero,
                    MinimumAcceleratedInterval = TimeSpan.Zero,
                },
                store.ScanRuns,
                async (_, _, cancellationToken) =>
                {
                    startedTcs.TrySetResult();
                    await holdTcs.Task.WaitAsync(cancellationToken);
                    return new ScanExecutionResult(1, 0, 0);
                },
                codeScanExecutor: null,
                agentCodeCollector: new AgentCodeCollector(),
                catalogEntries: [BuildCatalogEntry("agent-a", tempDir)]);

            var fullTask = scheduler.TryRunManualAsync(DateTimeOffset.UtcNow);
            await startedTcs.Task.WaitAsync(CancellationToken.None);

            var scopedRan = await scheduler.TryRunScopedAsync(
                ScanTriggerReason.SkillsFolderChanged,
                "agent-a",
                [scopedFile],
                DateTimeOffset.UtcNow);

            Assert.True(scopedRan);

            holdTcs.TrySetResult();
            Assert.True(await fullTask);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task StopAllScans_CancelsRunningFullScan()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"philche-scheduler-{Guid.NewGuid():N}.db");
        try
        {
            var store = new PhilcheDataStore(dbPath);
            await store.MigrationRunner.ApplyAsync();

            var startedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var scheduler = new ScanScheduler(
                new ScanSchedulerOptions
                {
                    PeriodicInterval = TimeSpan.FromHours(1),
                    DebounceWindow = TimeSpan.Zero,
                    MinimumAcceleratedInterval = TimeSpan.Zero,
                },
                store.ScanRuns,
                async (_, _, cancellationToken) =>
                {
                    startedTcs.TrySetResult();
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                    return new ScanExecutionResult(1, 0, 0);
                });

            var runTask = scheduler.TryRunManualAsync(DateTimeOffset.UtcNow);
            await startedTcs.Task.WaitAsync(CancellationToken.None);

            scheduler.StopAllScans();
            var ran = await runTask;

            Assert.False(ran);
            Assert.Equal(0, scheduler.ActiveScanCount);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task FullScan_WithCollector_PublishesPerFileProgressEvents()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"philche-scheduler-{Guid.NewGuid():N}.db");
        var tempDir = Path.Combine(Path.GetTempPath(), $"philche-code-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "one.py"), "print('1')");
            File.WriteAllText(Path.Combine(tempDir, "two.py"), "subprocess.Popen('cmd.exe')");

            var store = new PhilcheDataStore(dbPath);
            await store.MigrationRunner.ApplyAsync();

            var events = new List<ScanProgressInfo>();
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
                catalogEntries: [BuildCatalogEntry("agent-a", tempDir)]);

            scheduler.ScanProgress += progress => events.Add(progress);

            var ran = await scheduler.TryRunManualAsync(DateTimeOffset.UtcNow);

            Assert.True(ran);
            var perFileProgress = events.Where(static e => e.Status == "Progress").ToList();
            Assert.Equal(2, perFileProgress.Count);
            Assert.All(perFileProgress, e => Assert.False(string.IsNullOrWhiteSpace(e.CurrentFile)));
            Assert.Equal(1, perFileProgress[0].ScannedFiles);
            Assert.Equal(2, perFileProgress[1].ScannedFiles);
            Assert.Equal(2, perFileProgress[0].TotalFiles);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task TryRunAcceleratedAsync_RuleSetVersionChanged_ClearsCodeCache()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"philche-scheduler-{Guid.NewGuid():N}.db");
        try
        {
            var store = new PhilcheDataStore(dbPath);
            await store.MigrationRunner.ApplyAsync();

            await store.ScanCache.UpsertAsync(new ScanCacheEntry
            {
                SkillPath = "skill-a",
                ScanType = ScanCacheTypes.Code,
                ContentHash = "hash-a",
                LastScannedAt = DateTimeOffset.UtcNow,
                FindingsJson = "[]",
            });

            var scheduler = new ScanScheduler(
                new ScanSchedulerOptions
                {
                    PeriodicInterval = TimeSpan.FromHours(1),
                    DebounceWindow = TimeSpan.Zero,
                    MinimumAcceleratedInterval = TimeSpan.Zero,
                },
                store.ScanRuns,
                (_, _, _) => Task.FromResult(new ScanExecutionResult(1, 0, 0)),
                scanCacheRepository: store.ScanCache);

            var now = DateTimeOffset.UtcNow;
            scheduler.QueueAcceleratedEvent(TriggerEventKind.RuleSetVersionChanged, now);

            var ran = await scheduler.TryRunAcceleratedAsync(now);

            Assert.True(ran);
            Assert.Null(await store.ScanCache.GetAsync("skill-a", ScanCacheTypes.Code));
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task TryRunAcceleratedAsync_AgentVersionChanged_ClearsPostureCache()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"philche-scheduler-{Guid.NewGuid():N}.db");
        try
        {
            var store = new PhilcheDataStore(dbPath);
            await store.MigrationRunner.ApplyAsync();

            await store.ScanCache.UpsertAsync(new ScanCacheEntry
            {
                SkillPath = "openclaw",
                ScanType = ScanCacheTypes.Posture,
                ContentHash = string.Empty,
                AgentVersion = "1.0.0",
                LastScannedAt = DateTimeOffset.UtcNow,
                FindingsJson = "[]",
            });

            var scheduler = new ScanScheduler(
                new ScanSchedulerOptions
                {
                    PeriodicInterval = TimeSpan.FromHours(1),
                    DebounceWindow = TimeSpan.Zero,
                    MinimumAcceleratedInterval = TimeSpan.Zero,
                },
                store.ScanRuns,
                (_, _, _) => Task.FromResult(new ScanExecutionResult(1, 0, 0)),
                scanCacheRepository: store.ScanCache);

            var now = DateTimeOffset.UtcNow;
            scheduler.QueueAcceleratedEvent(TriggerEventKind.AgentVersionChanged, now);

            var ran = await scheduler.TryRunAcceleratedAsync(now);

            Assert.True(ran);
            Assert.Null(await store.ScanCache.GetAsync("openclaw", ScanCacheTypes.Posture));
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task TryRunManualAsync_BypassesCodeCacheAndRefreshesTimestamp()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"philche-scheduler-{Guid.NewGuid():N}.db");
        var tempDir = Path.Combine(Path.GetTempPath(), $"philche-code-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var changedFile = Path.Combine(tempDir, "changed.py");
            var unchangedFile = Path.Combine(tempDir, "unchanged.py");
            File.WriteAllText(changedFile, "subprocess.Popen('cmd.exe')");
            File.WriteAllText(unchangedFile, "print('safe')");

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
                catalogEntries: [BuildCatalogEntry("agent-a", tempDir)],
                scanCacheRepository: store.ScanCache);

            var start = DateTimeOffset.UtcNow;
            Assert.True(await scheduler.TryRunPeriodicAsync(start));

            var changedFirst = await store.ScanCache.GetAsync(changedFile, ScanCacheTypes.Code);
            var unchangedFirst = await store.ScanCache.GetAsync(unchangedFile, ScanCacheTypes.Code);
            Assert.NotNull(changedFirst);
            Assert.NotNull(unchangedFirst);

            Assert.True(await scheduler.TryRunPeriodicAsync(start.AddHours(2)));

            var changedSecond = await store.ScanCache.GetAsync(changedFile, ScanCacheTypes.Code);
            var unchangedSecond = await store.ScanCache.GetAsync(unchangedFile, ScanCacheTypes.Code);
            Assert.NotNull(changedSecond);
            Assert.NotNull(unchangedSecond);
            Assert.Equal(changedFirst!.LastScannedAt, changedSecond!.LastScannedAt);
            Assert.Equal(unchangedFirst!.LastScannedAt, unchangedSecond!.LastScannedAt);

            await Task.Delay(50);
            File.WriteAllText(changedFile, "print('changed')");
            Assert.True(await scheduler.TryRunPeriodicAsync(start.AddHours(4)));

            var changedThird = await store.ScanCache.GetAsync(changedFile, ScanCacheTypes.Code);
            var unchangedThird = await store.ScanCache.GetAsync(unchangedFile, ScanCacheTypes.Code);
            Assert.NotNull(changedThird);
            Assert.NotNull(unchangedThird);
            Assert.True(changedThird!.LastScannedAt > changedSecond.LastScannedAt);
            Assert.Equal(unchangedSecond.LastScannedAt, unchangedThird!.LastScannedAt);

            await Task.Delay(50);
            Assert.True(await scheduler.TryRunManualAsync(start.AddHours(4).AddMinutes(1)));

            var changedFourth = await store.ScanCache.GetAsync(changedFile, ScanCacheTypes.Code);
            var unchangedFourth = await store.ScanCache.GetAsync(unchangedFile, ScanCacheTypes.Code);
            Assert.NotNull(changedFourth);
            Assert.NotNull(unchangedFourth);
            Assert.True(changedFourth!.LastScannedAt > changedThird.LastScannedAt);
            Assert.True(unchangedFourth!.LastScannedAt > unchangedThird.LastScannedAt);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task TryRunPeriodicAsync_SkipsTrustedSkillsPathsForPromptAndCodeScans()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"philche-scheduler-{Guid.NewGuid():N}.db");
        var trustedDir = Path.Combine(Path.GetTempPath(), $"philche-trusted-{Guid.NewGuid():N}");
        var untrustedDir = Path.Combine(Path.GetTempPath(), $"philche-untrusted-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(trustedDir);
            Directory.CreateDirectory(untrustedDir);
            File.WriteAllText(Path.Combine(trustedDir, "trusted.py"), "subprocess.Popen('cmd.exe')");
            File.WriteAllText(Path.Combine(untrustedDir, "untrusted.py"), "subprocess.Popen('cmd.exe')");

            var store = new PhilcheDataStore(dbPath);
            await store.MigrationRunner.ApplyAsync();

            IReadOnlyList<KnownAgentCatalogEntry>? promptCatalog = null;
            var scheduler = new ScanScheduler(
                new ScanSchedulerOptions
                {
                    PeriodicInterval = TimeSpan.FromHours(1),
                    DebounceWindow = TimeSpan.Zero,
                    MinimumAcceleratedInterval = TimeSpan.Zero,
                },
                store.ScanRuns,
                (catalog, _, _) =>
                {
                    promptCatalog = catalog;
                    return Task.FromResult(new ScanExecutionResult(0, 0, 0));
                },
                codeScanExecutor: null,
                agentCodeCollector: new AgentCodeCollector(),
                catalogEntries:
                [
                    new KnownAgentCatalogEntry
                    {
                        AgentKey = "openclaw",
                        DisplayName = "OpenClaw",
                        HostExecutablePaths = [@"C:\tools\openclaw.exe"],
                        ExecutableNames = ["openclaw.exe"],
                        SkillsPaths =
                        [
                            new SkillsPathEntry(trustedDir, Trusted: true),
                            new SkillsPathEntry(untrustedDir, Trusted: false),
                        ],
                    }
                ]);

            Assert.True(await scheduler.TryRunPeriodicAsync(DateTimeOffset.UtcNow));

            var promptEntry = Assert.Single(promptCatalog!);
            var remainingPath = Assert.Single(promptEntry.SkillsPaths);
            Assert.Equal(untrustedDir, remainingPath.Path);
            Assert.False(remainingPath.Trusted);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
            if (Directory.Exists(trustedDir)) Directory.Delete(trustedDir, true);
            if (Directory.Exists(untrustedDir)) Directory.Delete(untrustedDir, true);
        }
    }

    [Fact]
    public async Task TryRunPeriodicAsync_SkipsCodeScanWhenAllSkillsPathsAreTrusted()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"philche-scheduler-{Guid.NewGuid():N}.db");
        var trustedDir = Path.Combine(Path.GetTempPath(), $"philche-trusted-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(trustedDir);
            File.WriteAllText(Path.Combine(trustedDir, "trusted.py"), "subprocess.Popen('cmd.exe')");

            var store = new PhilcheDataStore(dbPath);
            await store.MigrationRunner.ApplyAsync();

            var progressEvents = new List<ScanProgressInfo>();
            var scheduler = new ScanScheduler(
                new ScanSchedulerOptions
                {
                    PeriodicInterval = TimeSpan.FromHours(1),
                    DebounceWindow = TimeSpan.Zero,
                    MinimumAcceleratedInterval = TimeSpan.Zero,
                },
                store.ScanRuns,
                (_, _, _) => Task.FromResult(new ScanExecutionResult(0, 0, 0)),
                codeScanExecutor: null,
                agentCodeCollector: new AgentCodeCollector(),
                catalogEntries:
                [
                    new KnownAgentCatalogEntry
                    {
                        AgentKey = "openclaw",
                        DisplayName = "OpenClaw",
                        HostExecutablePaths = [@"C:\tools\openclaw.exe"],
                        ExecutableNames = ["openclaw.exe"],
                        SkillsPaths = [new SkillsPathEntry(trustedDir, Trusted: true)],
                    }
                ]);
            scheduler.ScanProgress += progress => progressEvents.Add(progress);

            Assert.True(await scheduler.TryRunPeriodicAsync(DateTimeOffset.UtcNow));
            Assert.DoesNotContain(progressEvents, progress =>
                progress.AgentKey.Equals("openclaw", StringComparison.OrdinalIgnoreCase) &&
                progress.Status.Equals("Progress", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
            if (Directory.Exists(trustedDir)) Directory.Delete(trustedDir, true);
        }
    }

    private static KnownAgentCatalogEntry BuildCatalogEntry(string agentKey, string skillsPath)
    {
        return new KnownAgentCatalogEntry
        {
            AgentKey = agentKey,
            DisplayName = agentKey,
            HostExecutablePaths = [@"C:\tools\agent.exe"],
            ExecutableNames = ["agent.exe"],
            SkillsPaths = [new SkillsPathEntry(skillsPath)],
        };
    }
}
