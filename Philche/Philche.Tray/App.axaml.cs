using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Win32;
using Philche.Core.Config;
using Philche.Core.Data;
using Philche.Core.Discovery;
using Philche.Core.Localization;
using Philche.Core.Orchestration;
using Philche.Core.SkillsRisk;

namespace Philche.Tray;

public partial class App : Application
{
    private readonly WslCatalogResolver wslCatalogResolver = new();
    private readonly object contextMenuQueueLock = new();
    private Window? trayHostWindow;
    private MainWindow? mainWindow;
    private SettingsYamlStore? settingsStore;
    private ScanScheduler? scanScheduler;
    private PhilcheDataStore? dataStore;
    private AgentCodeCollector? codeCollector;
    private SkillsFileWatcher? skillsFileWatcher;
    private System.Threading.PeriodicTimer? periodicTimer;
    private CancellationTokenSource? periodicLoopCts;
    private Task? periodicLoopTask;
    private SchedulerConfig schedulerConfig = new();
    private TrayIcon? mainTrayIcon;
    private NativeMenuItem? scanOnStartupMenuItem;
    private NativeMenuItem? periodicScanMenuItem;
    private NativeMenuItem? realtimeScanMenuItem;
    private NativeMenuItem? scanActionMenuItem;
    private NativeMenuItem? openSettingsMenuItem;
    private NativeMenuItem? exitMenuItem;
    private PowerModeChangedEventHandler? powerModeChangedHandler;
    private DateTimeOffset? lastFullScanCompletedAt;
    private int cleanupPerformed;
    private int pendingTrayClickCount;
    private bool trayClickTimerInitialized;
    private bool isContextMenuScanDraining;
    private AppLocalizer trayLocalizer = new();
    private readonly DispatcherTimer trayClickTimer = new();
    private readonly HashSet<string> pendingContextMenuPaths = new(StringComparer.OrdinalIgnoreCase);

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += (_, _) => CleanupResources();
            ToastNotifier.Initialize(HandleToastAction);
            Program.RegisterOpenModelsListener(() => ToastNotifier.InvokeAction(ToastNotifier.OpenModelsAction));
            EnsureTrayHostWindow();
            BindTrayMenuItems();
            InitializeTrayClickBehavior();
            RefreshTrayLocalization();
            ApplyMenuState();
            _ = InitializeAppAsync();

            if (Program.OpenModelsOnStartup)
            {
                HandleToastAction(ToastNotifier.OpenModelsAction);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void EnsureTrayHostWindow()
    {
        if (trayHostWindow is not null)
        {
            return;
        }

        var hostWindow = new Window
        {
            Width = 1,
            Height = 1,
            Opacity = 0,
            ShowInTaskbar = false,
            CanResize = false,
            SystemDecorations = SystemDecorations.None,
            WindowStartupLocation = WindowStartupLocation.Manual,
        };

        trayHostWindow = hostWindow;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = hostWindow;
        }

        hostWindow.Show();
        hostWindow.WindowState = WindowState.Minimized;
    }

    private void HandleToastAction(string action)
    {
        if (!string.Equals(action, ToastNotifier.OpenModelsAction, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            ShowMainWindow();
            mainWindow?.OpenModelsTab();
        });
    }

    private void TrayIcon_Clicked(object? sender, EventArgs e)
    {
        pendingTrayClickCount = Math.Min(2, pendingTrayClickCount + 1);
        trayClickTimer.Stop();
        trayClickTimer.Start();
    }

    private void OpenSettings_Click(object? sender, EventArgs e)
    {
        CancelPendingTrayClick();
        Dispatcher.UIThread.Post(ShowMainWindow);
    }

    private void CancelPendingTrayClick()
    {
        pendingTrayClickCount = 0;
        trayClickTimer.Stop();
    }

    private void InitializeTrayClickBehavior()
    {
        if (trayClickTimerInitialized)
        {
            return;
        }

        trayClickTimerInitialized = true;
        trayClickTimer.Interval = TimeSpan.FromMilliseconds(275);
        trayClickTimer.Tick += (_, _) =>
        {
            trayClickTimer.Stop();

            var clickCount = pendingTrayClickCount;
            pendingTrayClickCount = 0;

            if (clickCount >= 2)
            {
                ShowMainWindow();
                return;
            }

            ToggleMainWindowFromTray();
        };
    }

    private async void ScanAction_Click(object? sender, EventArgs e)
    {
        if (scanScheduler is null)
        {
            return;
        }

        try
        {
            if (scanScheduler.ActiveScanCount > 0)
            {
                scanScheduler.StopAllScans();
                return;
            }

            await scanScheduler.TryRunManualAsync(DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[App] manual scan action failed: {ex}");
            ToastNotifier.WarningLogger($"Manual scan action failed: {ex.Message}");
        }
    }

    private async Task InitializeAppAsync()
    {
        try
        {
            await InitializeSchedulerAsync();
            Program.RegisterScanRequestListener(paths => Dispatcher.UIThread.Post(() => _ = EnqueueContextMenuScanAsync(paths)));
            var queuedScanPaths = ScanQueueIpc.DrainPaths();
            RegisterPowerResumeHandler();
            RefreshTrayLocalization();
            ApplyMenuState();
            await InitializeBackgroundAsync();

            if (queuedScanPaths.Count > 0)
            {
                await EnqueueContextMenuScanAsync(queuedScanPaths);
            }

            if (Program.ScanPathsOnStartup.Count > 0)
            {
                await EnqueueContextMenuScanAsync(Program.ScanPathsOnStartup);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[App] initialization failed: {ex}");
            ToastNotifier.WarningLogger($"Initialization failed: {ex.Message}");
        }
    }

    private void ScanOnStartupToggle_Click(object? sender, EventArgs e)
    {
        if (sender is not NativeMenuItem menuItem || settingsStore is null)
        {
            return;
        }

        schedulerConfig = new SchedulerConfig
        {
            IntervalHours = schedulerConfig.IntervalHours,
            IsPaused = schedulerConfig.IsPaused,
            ScanOnStartup = menuItem.IsChecked,
            PeriodicScanEnabled = schedulerConfig.PeriodicScanEnabled,
            RealtimeScanEnabled = schedulerConfig.RealtimeScanEnabled,
        };
        settingsStore.SaveSchedulerConfig(schedulerConfig);
    }

    private void PeriodicScanToggle_Click(object? sender, EventArgs e)
    {
        if (sender is not NativeMenuItem menuItem || settingsStore is null)
        {
            return;
        }

        schedulerConfig = new SchedulerConfig
        {
            IntervalHours = schedulerConfig.IntervalHours,
            IsPaused = schedulerConfig.IsPaused,
            ScanOnStartup = schedulerConfig.ScanOnStartup,
            PeriodicScanEnabled = menuItem.IsChecked,
            RealtimeScanEnabled = schedulerConfig.RealtimeScanEnabled,
        };
        settingsStore.SaveSchedulerConfig(schedulerConfig);

        if (schedulerConfig.PeriodicScanEnabled)
        {
            StartPeriodicLoop(TimeSpan.FromHours(Math.Max(1, schedulerConfig.IntervalHours)));
        }
        else
        {
            StopPeriodicLoop();
        }
    }

    private void RealtimeScanToggle_Click(object? sender, EventArgs e)
    {
        if (sender is not NativeMenuItem menuItem || settingsStore is null)
        {
            return;
        }

        schedulerConfig = new SchedulerConfig
        {
            IntervalHours = schedulerConfig.IntervalHours,
            IsPaused = schedulerConfig.IsPaused,
            ScanOnStartup = schedulerConfig.ScanOnStartup,
            PeriodicScanEnabled = schedulerConfig.PeriodicScanEnabled,
            RealtimeScanEnabled = menuItem.IsChecked,
        };
        settingsStore.SaveSchedulerConfig(schedulerConfig);

        if (schedulerConfig.RealtimeScanEnabled)
        {
            StartRealtimeWatcher();
        }
        else
        {
            StopRealtimeWatcher();
        }
    }

    private void Exit_Click(object? sender, EventArgs e)
    {
        Program.AllowWindowClose = true;
        CleanupResources();
        mainWindow?.Close();
        trayHostWindow?.Close();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private void ShowMainWindow()
    {
        try
        {
            var window = EnsureMainWindow();
            SetDesktopMainWindow(window);
            window.ShowInTaskbar = true;

            if (!window.IsVisible)
            {
                window.Show();
            }

            window.WindowState = WindowState.Normal;
            window.Topmost = true;
            window.Activate();
            window.Topmost = false;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[App] failed to show main window: {ex}");

            try
            {
                mainWindow = null;
                var recreatedWindow = EnsureMainWindow();
                SetDesktopMainWindow(recreatedWindow);
                recreatedWindow.ShowInTaskbar = true;
                recreatedWindow.Show();
                recreatedWindow.WindowState = WindowState.Normal;
                recreatedWindow.Topmost = true;
                recreatedWindow.Activate();
                recreatedWindow.Topmost = false;
            }
            catch (Exception retryEx)
            {
                Console.Error.WriteLine($"[App] failed to recreate main window: {retryEx}");
            }
        }
    }

    private void ToggleMainWindowFromTray()
    {
        if (mainWindow is not null && mainWindow.IsVisible && mainWindow.WindowState != WindowState.Minimized)
        {
            HideMainWindowToTray(mainWindow);
            return;
        }

        ShowMainWindow();
    }

    private void HideMainWindowToTray(MainWindow window)
    {
        window.ShowInTaskbar = false;
        window.Hide();
        RestoreTrayHostAsDesktopMainWindow();
    }

    private MainWindow EnsureMainWindow()
    {
        if (mainWindow is not null)
        {
            mainWindow.SetScanScheduler(scanScheduler);
            return mainWindow;
        }

        var window = new MainWindow();
        window.SetScanScheduler(scanScheduler);
        window.Closing += MainWindow_Closing;
        window.Closed += MainWindow_Closed;
        mainWindow = window;

        return window;
    }

    private void MainWindow_Closing(object? sender, WindowClosingEventArgs args)
    {
        if (sender is not MainWindow window || Program.AllowWindowClose)
        {
            return;
        }

        args.Cancel = true;
        HideMainWindowToTray(window);
    }

    private void MainWindow_Closed(object? sender, EventArgs args)
    {
        if (ReferenceEquals(sender, mainWindow))
        {
            mainWindow = null;
        }

        RestoreTrayHostAsDesktopMainWindow();
    }

    private void SetDesktopMainWindow(Window window)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = window;
        }
    }

    private void RestoreTrayHostAsDesktopMainWindow()
    {
        if (trayHostWindow is null)
        {
            return;
        }

        SetDesktopMainWindow(trayHostWindow);
    }

    public void RefreshTrayLocalization()
    {
        trayLocalizer = new AppLocalizer();
        BindTrayMenuItems();
        ApplyMenuLocalization();

        if (mainTrayIcon is not null && (scanScheduler?.ActiveScanCount ?? 0) == 0)
        {
            mainTrayIcon.ToolTipText = trayLocalizer.Get("tray.tooltip.protected");
        }
    }

    private async Task InitializeSchedulerAsync()
    {
        settingsStore = new SettingsYamlStore();
        schedulerConfig = settingsStore.LoadSchedulerConfig();
        var scanningConfig = settingsStore.LoadScanningConfig();

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = Path.Combine(localAppData, "Philche");
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "philche.db");

        dataStore = new PhilcheDataStore(dbPath);
        await dataStore.MigrationRunner.ApplyAsync();

        var allCatalogEntries = settingsStore.LoadCatalog();
        var enabledAgents = (await wslCatalogResolver.ExpandAsync(allCatalogEntries)).ToList();
        var llmIntentEnabled = scanningConfig.EnableGuardModelScan && scanningConfig.EnableLlmIntentRecognition;
        var rulesStageEnabled = scanningConfig.EnableMaliciousWordGroupList || scanningConfig.EnableInvisibleCharacterDetection;

        if (!scanningConfig.EnableYaraScan &&
            !llmIntentEnabled &&
            !rulesStageEnabled &&
            !scanningConfig.EnableVirusTotalSkillUrlScan &&
            !scanningConfig.EnableVirusTotalScriptUrlScan &&
            !scanningConfig.EnableRegexScan &&
            !scanningConfig.EnableCveCorrelation)
        {
            Console.Error.WriteLine("[App] all scan methods are disabled in settings.");
        }

        codeCollector = scanningConfig.EnableYaraScan ? new AgentCodeCollector() : null;

        scanScheduler = new ScanScheduler(
            new ScanSchedulerOptions
            {
                PeriodicInterval = TimeSpan.FromHours(Math.Max(1, schedulerConfig.IntervalHours)),
                DebounceWindow = TimeSpan.FromSeconds(3),
                MinimumAcceleratedInterval = TimeSpan.FromSeconds(10),
            },
            dataStore.ScanRuns,
            (_, _, _) => Task.FromResult(new ScanExecutionResult(0, 0, 0)),
            codeScanExecutor: null,
            agentCodeCollector: codeCollector,
            catalogEntries: enabledAgents);

        scanScheduler.ScanProgress += OnScanProgress;
    }

    private async Task InitializeBackgroundAsync()
    {
        if (scanScheduler is null || settingsStore is null)
        {
            return;
        }

        NotifyMissingModelsOnStartup(settingsStore.LoadModelPaths());

        if (schedulerConfig.RealtimeScanEnabled)
        {
            StartRealtimeWatcher();
        }

        if (schedulerConfig.ScanOnStartup)
        {
            await scanScheduler.TryRunManualAsync(DateTimeOffset.UtcNow);
        }

        if (schedulerConfig.PeriodicScanEnabled)
        {
            StartPeriodicLoop(TimeSpan.FromHours(Math.Max(1, schedulerConfig.IntervalHours)));
        }

        UpdateScanActionMenuText();
    }

    private static void NotifyMissingModelsOnStartup(ModelPathsConfig modelPaths)
    {
        var guardMissing = string.IsNullOrWhiteSpace(modelPaths.GuardModelPath) || !File.Exists(modelPaths.GuardModelPath);
        var cveMissing = string.IsNullOrWhiteSpace(modelPaths.CveSummaryModelPath) || !File.Exists(modelPaths.CveSummaryModelPath);

        if (!guardMissing && !cveMissing)
        {
            return;
        }

        var missingSummary = guardMissing && cveMissing
            ? "Guard 與 CVE Summary 模型皆未設定，請在 Models 分頁下載 GGUF 模型。"
            : guardMissing
                ? "Guard 模型未設定，請在 Models 分頁下載 GGUF 模型。"
                : "CVE Summary 模型未設定，請在 Models 分頁下載 GGUF 模型。";

        ToastNotifier.TryShowModelMissing(missingSummary);
    }

    private async void StartRealtimeWatcher()
    {
        StopRealtimeWatcher();

        if (settingsStore is null || scanScheduler is null)
        {
            return;
        }

        var catalog = settingsStore.LoadCatalog()
            .ToList();

        catalog = (await wslCatalogResolver.ExpandAsync(catalog)).ToList();

        if (catalog.Count == 0)
        {
            return;
        }

        skillsFileWatcher = new SkillsFileWatcher(catalog, TimeSpan.FromSeconds(3));
        skillsFileWatcher.FilesChanged += OnSkillsFilesChanged;
    }

    private void StopRealtimeWatcher()
    {
        if (skillsFileWatcher is null)
        {
            return;
        }

        skillsFileWatcher.FilesChanged -= OnSkillsFilesChanged;
        skillsFileWatcher.Dispose();
        skillsFileWatcher = null;
    }

    private void OnSkillsFilesChanged(string agentKey, IReadOnlyList<string> filePaths)
    {
        _ = HandleSkillsFilesChangedAsync(agentKey, filePaths);
    }

    private async Task HandleSkillsFilesChangedAsync(string agentKey, IReadOnlyList<string> filePaths)
    {
        if (scanScheduler is null)
        {
            return;
        }

        try
        {
            await scanScheduler.TryRunScopedAsync(
                ScanTriggerReason.SkillsFolderChanged,
                agentKey,
                filePaths,
                DateTimeOffset.UtcNow,
                displayName: agentKey);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[App] realtime watcher scan failed: {ex}");
            ToastNotifier.WarningLogger($"Realtime watcher scan failed: {ex.Message}");
        }
    }

    private async Task EnqueueContextMenuScanAsync(IReadOnlyList<string> paths)
    {
        var normalized = paths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Count == 0)
        {
            return;
        }

        var shouldStartDrain = false;
        lock (contextMenuQueueLock)
        {
            foreach (var path in normalized)
            {
                pendingContextMenuPaths.Add(path);
            }

            if (!isContextMenuScanDraining)
            {
                isContextMenuScanDraining = true;
                shouldStartDrain = true;
            }
        }

        if (shouldStartDrain)
        {
            await DrainContextMenuScanQueueAsync();
        }
    }

    private async Task DrainContextMenuScanQueueAsync()
    {
        try
        {
            while (true)
            {
                List<string> queuedPaths;
                lock (contextMenuQueueLock)
                {
                    if (pendingContextMenuPaths.Count == 0)
                    {
                        isContextMenuScanDraining = false;
                        return;
                    }

                    queuedPaths = pendingContextMenuPaths.ToList();
                    pendingContextMenuPaths.Clear();
                }

                var filesToScan = await Task.Run(() => ExpandContextMenuPaths(queuedPaths));
                if (filesToScan.Count == 0)
                {
                    continue;
                }

                var displayName = string.Join(", ", queuedPaths.Select(static path => Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))));

                Dispatcher.UIThread.Post(() =>
                {
                    ShowMainWindow();
                    mainWindow?.OpenScanProgressTab();
                });

                while ((scanScheduler?.ActiveScanCount ?? 0) > 0)
                {
                    await Task.Delay(250);
                }

                if (scanScheduler is null)
                {
                    return;
                }

                await scanScheduler.TryRunScopedAsync(
                    ScanTriggerReason.ContextMenuScan,
                    agentKey: null,
                    filePaths: filesToScan,
                    now: DateTimeOffset.UtcNow,
                    displayName: displayName);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[App] context menu scan failed: {ex}");
            ToastNotifier.WarningLogger($"Context menu scan failed: {ex.Message}");
            lock (contextMenuQueueLock)
            {
                isContextMenuScanDraining = false;
            }
        }
    }

    private List<string> ExpandContextMenuPaths(IReadOnlyList<string> paths)
    {
        if (codeCollector is null)
        {
            ToastNotifier.WarningLogger("YARA scan is disabled. Enable scan methods before using context-menu scan.");
            return [];
        }

        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                if (codeCollector.IsScannableFilePath(path))
                {
                    files.Add(path);
                }

                continue;
            }

            if (Directory.Exists(path))
            {
                foreach (var filePath in codeCollector.GetScannableFiles([path]))
                {
                    files.Add(filePath);
                }
            }
        }

        return files.ToList();
    }

    private void StartPeriodicLoop(TimeSpan interval)
    {
        StopPeriodicLoop();

        if (scanScheduler is null)
        {
            return;
        }

        periodicLoopCts = new CancellationTokenSource();
        periodicTimer = new System.Threading.PeriodicTimer(interval);
        periodicLoopTask = Task.Run(async () =>
        {
            try
            {
                while (periodicTimer is not null && periodicLoopCts is not null)
                {
                    var canTick = await periodicTimer.WaitForNextTickAsync(periodicLoopCts.Token);
                    if (!canTick)
                    {
                        break;
                    }

                    if (!schedulerConfig.PeriodicScanEnabled || scanScheduler is null)
                    {
                        continue;
                    }

                    await scanScheduler.TryRunPeriodicAsync(DateTimeOffset.UtcNow, periodicLoopCts.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[App] periodic loop failed: {ex}");
                ToastNotifier.WarningLogger($"Periodic loop failed: {ex.Message}");
            }
        }, periodicLoopCts.Token);
    }

    private void StopPeriodicLoop()
    {
        periodicLoopCts?.Cancel();
        periodicLoopCts?.Dispose();
        periodicLoopCts = null;

        periodicTimer?.Dispose();
        periodicTimer = null;

        if (periodicLoopTask is not null)
        {
            try
            {
                periodicLoopTask.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
            }

            periodicLoopTask = null;
        }
    }

    private void OnScanProgress(ScanProgressInfo progress)
    {
        Dispatcher.UIThread.Post(() =>
        {
            UpdateScanActionMenuText();

            if (progress.Status == "Completed")
            {
                _ = mainWindow?.LoadFindingsAsync();
            }

            if (mainTrayIcon is null)
            {
                return;
            }

            var isFullProgress = progress.ScanType.Equals(ScanTriggerReason.Periodic, StringComparison.OrdinalIgnoreCase) ||
                                 progress.ScanType.Equals(ScanTriggerReason.Manual, StringComparison.OrdinalIgnoreCase);

            if (!isFullProgress || progress.TotalFiles <= 0)
            {
                if (scanScheduler?.ActiveScanCount == 0)
                {
                    mainTrayIcon.ToolTipText = trayLocalizer.Get("tray.tooltip.protected");
                }

                return;
            }

            var percentage = (int)Math.Round((double)progress.ScannedFiles / progress.TotalFiles * 100);
            mainTrayIcon.ToolTipText = string.Format(
                trayLocalizer.Get("tray.tooltip.scanning"),
                progress.ScannedFiles,
                progress.TotalFiles,
                percentage);

            if (progress.Status is "Completed" or "Failed" or "Cancelled")
            {
                mainTrayIcon.ToolTipText = trayLocalizer.Get("tray.tooltip.protected");

                if (progress.Status == "Completed" && isFullProgress)
                {
                    lastFullScanCompletedAt = DateTimeOffset.UtcNow;
                    ToastNotifier.TryShowScanCompleted(progress);
                }

                if (isFullProgress && schedulerConfig.PeriodicScanEnabled)
                {
                    StartPeriodicLoop(TimeSpan.FromHours(Math.Max(1, schedulerConfig.IntervalHours)));
                }
            }
        });
    }

    private void UpdateScanActionMenuText()
    {
        if (scanActionMenuItem is null)
        {
            return;
        }

        scanActionMenuItem.Header = (scanScheduler?.ActiveScanCount ?? 0) > 0
            ? trayLocalizer.Get("tray.action.stop")
            : trayLocalizer.Get("tray.action.start");
    }

    private void ApplyMenuLocalization()
    {
        if (scanOnStartupMenuItem is not null)
        {
            scanOnStartupMenuItem.Header = trayLocalizer.Get("tray.menu.scanOnStartup");
        }

        if (periodicScanMenuItem is not null)
        {
            periodicScanMenuItem.Header = trayLocalizer.Get("tray.menu.periodicScan");
        }

        if (realtimeScanMenuItem is not null)
        {
            realtimeScanMenuItem.Header = trayLocalizer.Get("tray.menu.realtimeScan");
        }

        if (openSettingsMenuItem is not null)
        {
            openSettingsMenuItem.Header = trayLocalizer.Get("tray.menu.openSettings");
        }

        if (exitMenuItem is not null)
        {
            exitMenuItem.Header = trayLocalizer.Get("tray.menu.exit");
        }

        UpdateScanActionMenuText();
    }

    private void RegisterPowerResumeHandler()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        powerModeChangedHandler = (_, args) =>
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            if (args.Mode != PowerModes.Resume)
            {
                return;
            }

            _ = HandleSystemResumeAsync();
        };

        SystemEvents.PowerModeChanged += powerModeChangedHandler;
    }

    private async Task HandleSystemResumeAsync()
    {
        if (scanScheduler is null || !schedulerConfig.PeriodicScanEnabled)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var interval = TimeSpan.FromHours(Math.Max(1, schedulerConfig.IntervalHours));
        var elapsed = lastFullScanCompletedAt is null
            ? interval
            : now - lastFullScanCompletedAt.Value;

        if (elapsed < interval)
        {
            return;
        }

        await scanScheduler.TryRunPeriodicAsync(now);
    }

    private void CleanupResources()
    {
        if (Interlocked.Exchange(ref cleanupPerformed, 1) == 1)
        {
            return;
        }

        if (powerModeChangedHandler is not null)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    SystemEvents.PowerModeChanged -= powerModeChangedHandler;
                }
            }
            catch
            {
            }

            powerModeChangedHandler = null;
        }

        StopPeriodicLoop();
        StopRealtimeWatcher();
        scanScheduler?.StopAllScans();
        FlushSqlite();

        if (trayHostWindow is not null)
        {
            trayHostWindow.Close();
            trayHostWindow = null;
        }
    }

    private void FlushSqlite()
    {
        if (dataStore is null)
        {
            return;
        }

        try
        {
            using var connection = dataStore.ConnectionFactory.CreateOpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA wal_checkpoint(FULL);";
            command.ExecuteNonQuery();
        }
        catch
        {
        }
    }

    private void ApplyMenuState()
    {
        if (scanOnStartupMenuItem is not null)
        {
            scanOnStartupMenuItem.IsChecked = schedulerConfig.ScanOnStartup;
        }

        if (periodicScanMenuItem is not null)
        {
            periodicScanMenuItem.IsChecked = schedulerConfig.PeriodicScanEnabled;
        }

        if (realtimeScanMenuItem is not null)
        {
            realtimeScanMenuItem.IsChecked = schedulerConfig.RealtimeScanEnabled;
        }

        UpdateScanActionMenuText();
    }

    private void BindTrayMenuItems()
    {
        var icons = TrayIcon.GetIcons(this);
        mainTrayIcon = icons?.FirstOrDefault();
        if (mainTrayIcon is not null)
        {
            mainTrayIcon.Clicked -= TrayIcon_Clicked;
            mainTrayIcon.Clicked += TrayIcon_Clicked;
        }

        var menu = mainTrayIcon?.Menu;
        if (menu?.Items is null || menu.Items.Count < 9)
        {
            return;
        }

        scanOnStartupMenuItem = menu.Items[0] as NativeMenuItem;
        periodicScanMenuItem = menu.Items[1] as NativeMenuItem;
        realtimeScanMenuItem = menu.Items[2] as NativeMenuItem;
        scanActionMenuItem = menu.Items[4] as NativeMenuItem;
        openSettingsMenuItem = menu.Items[6] as NativeMenuItem;
        exitMenuItem = menu.Items[8] as NativeMenuItem;
    }
}
