using Philche.Core.Data.Repositories;
using Philche.Core.Discovery;
using Philche.Core.Domain.Models;
using Philche.Core.SkillsRisk;
using System.Text.Json;

namespace Philche.Core.Orchestration;

public sealed class ScanScheduler
{
    private readonly ScanSchedulerOptions options;
    private readonly IScanRunRepository scanRunRepository;
    private readonly Func<IReadOnlyList<KnownAgentCatalogEntry>, string, CancellationToken, Task<ScanExecutionResult>> promptScanExecutor;
    private readonly Func<string, CancellationToken, Task<ScanExecutionResult>>? codeScanExecutor;
    private readonly AgentCodeCollector? agentCodeCollector;
    private readonly IReadOnlyList<KnownAgentCatalogEntry> catalogEntries;
    private readonly IScanCacheRepository? scanCacheRepository;

    private DateTimeOffset? lastPeriodicRunAt;
    private DateTimeOffset? lastAcceleratedRunAt;
    private DateTimeOffset? lastEventQueuedAt;
    private readonly HashSet<string> pendingReasons = [];
    private readonly Dictionary<string, HashSet<string>> pendingScopedFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly object runLock = new();
    private readonly object progressStateLock = new();
    private CancellationTokenSource? activeScanCts;
    private int activeScanCount;
    private bool isFullScanRunning;
    private ScanProgressInfo? latestProgress;
    private readonly List<string> currentScanFiles = [];

    public bool IsPaused { get; private set; }
    public int ActiveScanCount => activeScanCount;
    public ScanProgressSnapshot? CurrentProgressSnapshot
    {
        get
        {
            lock (progressStateLock)
            {
                return latestProgress is null
                    ? null
                    : new ScanProgressSnapshot(latestProgress, currentScanFiles.ToList());
            }
        }
    }

    public event Action<ScanProgressInfo>? ScanProgress;

    public ScanScheduler(
        ScanSchedulerOptions options,
        IScanRunRepository scanRunRepository,
        Func<IReadOnlyList<KnownAgentCatalogEntry>, string, CancellationToken, Task<ScanExecutionResult>> promptScanExecutor,
        Func<string, CancellationToken, Task<ScanExecutionResult>>? codeScanExecutor = null,
        AgentCodeCollector? agentCodeCollector = null,
        IReadOnlyList<KnownAgentCatalogEntry>? catalogEntries = null,
        IScanCacheRepository? scanCacheRepository = null)
    {
        options.Validate();
        this.options = options;
        this.scanRunRepository = scanRunRepository;
        this.promptScanExecutor = promptScanExecutor;
        this.codeScanExecutor = codeScanExecutor;
        this.agentCodeCollector = agentCodeCollector;
        this.catalogEntries = catalogEntries ?? [];
        this.scanCacheRepository = scanCacheRepository;
    }

    public bool CanRunPeriodic(DateTimeOffset now) =>
        !IsPaused && (lastPeriodicRunAt is null || (now - lastPeriodicRunAt.Value) >= options.PeriodicInterval);

    public async Task<bool> TryRunPeriodicAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (!CanRunPeriodic(now))
        {
            return false;
        }

        if (!TryEnterFullScan())
        {
            lastPeriodicRunAt = now;
            return false;
        }

        try
        {
            await ExecuteAndRecordAsync(ScanTriggerReason.Periodic, now, ScanScope.Full(), cancellationToken, bypassCache: false);
            lastPeriodicRunAt = now;
            return true;
        }
        catch (OperationCanceledException)
        {
            lastPeriodicRunAt = now;
            return false;
        }
    }

    public void QueueAcceleratedEvent(TriggerEventKind eventKind, DateTimeOffset now, ScanScope? scope = null)
    {
        pendingReasons.Add(TriggerEventAdapter.ToReason(eventKind));
        lastEventQueuedAt = now;

        if (scope is { IsFullScan: false, FilePaths: not null } && !string.IsNullOrWhiteSpace(scope.AgentKey))
        {
            if (!pendingScopedFiles.TryGetValue(scope.AgentKey, out var files))
            {
                files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                pendingScopedFiles[scope.AgentKey] = files;
            }

            foreach (var filePath in scope.FilePaths.Where(static p => !string.IsNullOrWhiteSpace(p)))
            {
                if (File.Exists(filePath))
                {
                    files.Add(filePath);
                }
            }
        }
    }

    public bool CanRunAccelerated(DateTimeOffset now)
    {
        if (IsPaused || pendingReasons.Count == 0 || lastEventQueuedAt is null)
        {
            return false;
        }

        var debounceReached = (now - lastEventQueuedAt.Value) >= options.DebounceWindow;
        var throttleReached =
            lastAcceleratedRunAt is null ||
            (now - lastAcceleratedRunAt.Value) >= options.MinimumAcceleratedInterval;

        return debounceReached && throttleReached;
    }

    public async Task<bool> TryRunAcceleratedAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (!CanRunAccelerated(now))
        {
            return false;
        }

        var mergedReason = string.Join(",", pendingReasons.OrderBy(static x => x, StringComparer.Ordinal));

        var scope = BuildAcceleratedScope();
        if (scope.IsFullScan && !TryEnterFullScan())
        {
            pendingReasons.Clear();
            pendingScopedFiles.Clear();
            lastAcceleratedRunAt = now;
            return false;
        }

        try
        {
            if (scanCacheRepository is not null &&
                mergedReason.Contains(ScanTriggerReason.RuleSetVersionChanged, StringComparison.OrdinalIgnoreCase))
            {
                await scanCacheRepository.DeleteByScanTypeAsync(ScanCacheTypes.Code, cancellationToken);
            }

            if (scanCacheRepository is not null &&
                mergedReason.Contains(ScanTriggerReason.AgentVersionChanged, StringComparison.OrdinalIgnoreCase))
            {
                await scanCacheRepository.DeleteByScanTypeAsync(ScanCacheTypes.Posture, cancellationToken);
            }

            await ExecuteAndRecordAsync(mergedReason, now, scope, cancellationToken, bypassCache: false);
        }
        catch (OperationCanceledException)
        {
            pendingReasons.Clear();
            pendingScopedFiles.Clear();
            lastAcceleratedRunAt = now;
            return false;
        }

        pendingReasons.Clear();
        pendingScopedFiles.Clear();
        lastAcceleratedRunAt = now;
        return true;
    }

    public void Pause()
    {
        IsPaused = true;
    }

    public void Resume()
    {
        IsPaused = false;
    }

    public async Task<bool> TryRunManualAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (!TryEnterFullScan())
        {
            return false;
        }

        try
        {
            await ExecuteAndRecordAsync(ScanTriggerReason.Manual, now, ScanScope.Full(), cancellationToken, bypassCache: true);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public async Task<bool> TryRunScopedAsync(
        string triggerReason,
        string? agentKey,
        IReadOnlyList<string> filePaths,
        DateTimeOffset now,
        string? displayName = null,
        CancellationToken cancellationToken = default)
    {
        if (filePaths.Count == 0)
        {
            return false;
        }

        var scope = ScanScope.Scoped(agentKey, filePaths);
        try
        {
            await ExecuteAndRecordAsync(triggerReason, now, scope, cancellationToken, bypassCache: false, displayName);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public void StopAllScans()
    {
        lock (runLock)
        {
            activeScanCts?.Cancel();
        }
    }

    private async Task ExecuteAndRecordAsync(
        string triggerReason,
        DateTimeOffset startedAt,
        ScanScope scope,
        CancellationToken cancellationToken,
        bool bypassCache,
        string? displayName = null)
    {
        using var linkedTokenSource = CreateLinkedTokenSource(cancellationToken);
        var linkedToken = linkedTokenSource.Token;
        Interlocked.Increment(ref activeScanCount);
        var totalFiles = 0;
        var scannedFiles = 0;
        long totalSizeBytes = 0;

        PublishProgress(new ScanProgressInfo(
            triggerReason,
            scope.AgentKey ?? "all",
            "Started",
            0,
            [],
            0,
            0,
            null,
            displayName));

        try
        {
            ScanExecutionResult result;
            var riskyFiles = new List<string>();

            if (scope.IsFullScan)
            {
                var promptResult = await promptScanExecutor(BuildPromptScanCatalogEntries(), triggerReason, linkedToken);
                result = promptResult;

                if (codeScanExecutor is not null)
                {
                    var codeResult = await codeScanExecutor(triggerReason, linkedToken);
                    result = MergeResults(promptResult, codeResult);
                }
                else if (agentCodeCollector is not null)
                {
                    var roots = BuildFullScanRoots();
                    totalFiles = roots.Sum(static root => root.Files.Count);

                    foreach (var root in roots)
                    {
                        linkedToken.ThrowIfCancellationRequested();

                        var rootRiskyFiles = await GetRiskyFilesForRootAsync(root, bypassCache, linkedToken);
                        foreach (var filePath in root.Files)
                        {
                            scannedFiles++;
                            PublishProgress(new ScanProgressInfo(
                                triggerReason,
                                root.AgentKey,
                                "Progress",
                                riskyFiles.Count + rootRiskyFiles.Count,
                                riskyFiles.Concat(rootRiskyFiles).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                                totalFiles,
                                scannedFiles,
                                filePath,
                                root.SkillsPath));
                        }

                        riskyFiles.AddRange(rootRiskyFiles);
                    }

                    result = MergeResults(
                        promptResult,
                        new ScanExecutionResult(totalFiles, riskyFiles.Distinct(StringComparer.OrdinalIgnoreCase).Count(), 0, "Completed"));
                }

            }
            else
            {
                var filePaths = scope.FilePaths?.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
                if (filePaths.Count == 0)
                {
                    result = new ScanExecutionResult(0, 0, 0, "Completed");
                }
                else if (codeScanExecutor is not null)
                {
                    result = await codeScanExecutor(triggerReason, linkedToken);
                }
                else if (agentCodeCollector is not null)
                {
                    totalFiles = filePaths.Count(path =>
                        !string.IsNullOrWhiteSpace(path) &&
                        File.Exists(path) &&
                        agentCodeCollector.IsScannableFilePath(path));

                    PublishProgress(new ScanProgressInfo(
                        triggerReason,
                        scope.AgentKey ?? "all",
                        "Started",
                        0,
                        [],
                        totalFiles,
                        0,
                        null,
                        displayName));

                    foreach (var filePath in filePaths)
                    {
                        linkedToken.ThrowIfCancellationRequested();

                        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath) || !agentCodeCollector.IsScannableFilePath(filePath))
                        {
                            continue;
                        }

                        totalSizeBytes += new FileInfo(filePath).Length;
                        var fileRiskyFiles = await GetRiskyFilesForFileAsync(filePath, bypassCache, linkedToken);
                        riskyFiles.AddRange(fileRiskyFiles);
                        scannedFiles++;

                        PublishProgress(new ScanProgressInfo(
                            triggerReason,
                            scope.AgentKey ?? "all",
                            "Progress",
                            riskyFiles.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                            riskyFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                            totalFiles,
                            scannedFiles,
                            filePath,
                            displayName,
                            totalSizeBytes));
                    }

                    result = new ScanExecutionResult(
                        totalFiles,
                        riskyFiles.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                        0,
                        "Completed");
                }
                else
                {
                    result = new ScanExecutionResult(0, 0, 0, "Completed");
                }
            }

            var run = new ScanRun
            {
                Id = Guid.NewGuid().ToString("N"),
                TriggerReason = triggerReason,
                StartedAt = startedAt,
                EndedAt = DateTimeOffset.UtcNow,
                InventoryCount = result.InventoryCount,
                FindingCount = result.FindingCount,
                WarningCount = result.WarningCount,
                Status = result.Status,
                HighRiskPathsJson = JsonSerializer.Serialize(riskyFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToList()),
            };

            await scanRunRepository.UpsertAsync(run, linkedToken);
            PublishProgress(new ScanProgressInfo(
                triggerReason,
                scope.AgentKey ?? "all",
                "Completed",
                result.FindingCount,
                riskyFiles,
                totalFiles,
                scannedFiles,
                null,
                displayName,
                totalSizeBytes));
        }
        catch (OperationCanceledException)
        {
            PublishProgress(new ScanProgressInfo(triggerReason, scope.AgentKey ?? "all", "Cancelled", 0, [], totalFiles, scannedFiles, null, displayName, totalSizeBytes));
            throw;
        }
        catch (Exception ex)
        {
            PublishProgress(new ScanProgressInfo(triggerReason, scope.AgentKey ?? "all", "Failed", 0, [], totalFiles, scannedFiles, null, displayName, totalSizeBytes, ex.Message));
            throw;
        }
        finally
        {
            if (scope.IsFullScan)
            {
                lock (runLock)
                {
                    isFullScanRunning = false;
                }
            }

            Interlocked.Decrement(ref activeScanCount);
        }
    }

    private List<FullScanRoot> BuildFullScanRoots()
    {
        var roots = new List<FullScanRoot>();

        foreach (var entry in catalogEntries)
        {
            foreach (var skillsPathEntry in entry.SkillsPaths.Where(static path => !path.Trusted))
            {
                var skillsPath = Environment.ExpandEnvironmentVariables(skillsPathEntry.Path);
                var files = agentCodeCollector?.GetScannableFiles([skillsPath]) ?? [];
                roots.Add(new FullScanRoot(entry.AgentKey, skillsPath, files));
            }
        }

        return roots;
    }

    private IReadOnlyList<KnownAgentCatalogEntry> BuildPromptScanCatalogEntries()
    {
        return catalogEntries
            .Select(static entry => entry with
            {
                SkillsPaths = entry.SkillsPaths
                    .Where(static path => !path.Trusted)
                    .ToList(),
            })
            .Where(static entry => entry.SkillsPaths.Count > 0)
            .ToList();
    }

    private async Task<List<string>> GetRiskyFilesForRootAsync(
        FullScanRoot root,
        bool bypassCache,
        CancellationToken cancellationToken)
    {
        if (root.Files.Count == 0)
        {
            return [];
        }

        var rootResult = await GetRiskyFilesForPathsAsync(root.Files, bypassCache, cancellationToken);
        return rootResult.RiskyFiles;
    }

    private async Task<(List<string> RiskyFiles, int TotalFiles)> GetRiskyFilesForPathsAsync(
        IReadOnlyList<string> filePaths,
        bool bypassCache,
        CancellationToken cancellationToken)
    {
        var riskyFiles = new List<string>();
        var totalFiles = 0;

        foreach (var filePath in filePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath) || !agentCodeCollector!.IsScannableFilePath(filePath))
            {
                continue;
            }

            totalFiles++;
            riskyFiles.AddRange(await GetRiskyFilesForFileAsync(filePath, bypassCache, cancellationToken));
        }

        return (riskyFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), totalFiles);
    }

    private async Task<IReadOnlyList<string>> GetRiskyFilesForFileAsync(
        string filePath,
        bool bypassCache,
        CancellationToken cancellationToken)
    {
        var contentHash = await AgentCodeCollector.ComputeSingleFileHashAsync(filePath, cancellationToken);

        if (scanCacheRepository is not null && !bypassCache)
        {
            var cached = await scanCacheRepository.GetAsync(filePath, ScanCacheTypes.Code, cancellationToken);
            if (cached is not null && string.Equals(cached.ContentHash, contentHash, StringComparison.Ordinal))
            {
                return DeserializeRiskyFiles(cached.FindingsJson)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        var scanResult = await agentCodeCollector!.ScanFilesAsync([filePath], cancellationToken);
        var riskyFiles = scanResult.FileResults
            .Select(static x => x.FilePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (scanCacheRepository is not null)
        {
            await scanCacheRepository.UpsertAsync(
                new ScanCacheEntry
                {
                    SkillPath = filePath,
                    ScanType = ScanCacheTypes.Code,
                    ContentHash = contentHash,
                    AgentVersion = null,
                    LastScannedAt = DateTimeOffset.UtcNow,
                    FindingsJson = JsonSerializer.Serialize(riskyFiles),
                },
                cancellationToken);
        }

        return riskyFiles;
    }

    private bool TryEnterFullScan()
    {
        lock (runLock)
        {
            if (isFullScanRunning)
            {
                return false;
            }

            isFullScanRunning = true;
            return true;
        }
    }

    private CancellationTokenSource CreateLinkedTokenSource(CancellationToken external)
    {
        lock (runLock)
        {
            if (activeScanCts is null || activeScanCts.IsCancellationRequested)
            {
                activeScanCts?.Dispose();
                activeScanCts = new CancellationTokenSource();
            }

            return CancellationTokenSource.CreateLinkedTokenSource(activeScanCts.Token, external);
        }
    }

    private ScanScope BuildAcceleratedScope()
    {
        if (pendingScopedFiles.Count == 0)
        {
            return ScanScope.Full();
        }

        var merged = pendingScopedFiles
            .SelectMany(static pair => pair.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(File.Exists)
            .ToList();

        if (merged.Count == 0)
        {
            return ScanScope.Full();
        }

        return ScanScope.Scoped("all", merged);
    }

    private void PublishProgress(ScanProgressInfo progress)
    {
        lock (progressStateLock)
        {
            if (progress.Status.Equals("Started", StringComparison.OrdinalIgnoreCase))
            {
                currentScanFiles.Clear();
            }

            if (!string.IsNullOrWhiteSpace(progress.CurrentFile) &&
                !currentScanFiles.Contains(progress.CurrentFile, StringComparer.OrdinalIgnoreCase))
            {
                currentScanFiles.Add(progress.CurrentFile);
            }

            latestProgress = progress;
        }

        ScanProgress?.Invoke(progress);
    }

    private static List<string> DeserializeRiskyFiles(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static ScanExecutionResult MergeResults(ScanExecutionResult prompt, ScanExecutionResult code)
    {
        return new ScanExecutionResult(
            Math.Max(prompt.InventoryCount, code.InventoryCount),
            prompt.FindingCount + code.FindingCount,
            prompt.WarningCount + code.WarningCount,
            code.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase) ? prompt.Status : code.Status);
    }

    private sealed record FullScanRoot(string AgentKey, string SkillsPath, IReadOnlyList<string> Files);
}
