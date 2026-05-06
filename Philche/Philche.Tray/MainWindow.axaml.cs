using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.Controls;
using Philche.Core.Config;
using Philche.Core.Data;
using Philche.Core.Discovery;
using Philche.Core.Domain.Enums;
using Philche.Core.Domain.Models;
using Philche.Core.Localization;
using Philche.Core.Orchestration;
using Philche.Core.Review;
using System.Text.Json;

namespace Philche.Tray;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<KnownAgentCatalogEntry> agents = [];
    private readonly ObservableCollection<EditableSkillsPathItem> editableSkillsPaths = [];
    private readonly ObservableCollection<FindingListItem> findings = [];
    private readonly WslCatalogResolver wslCatalogResolver = new();

    private readonly ISettingsYamlStore settingsYamlStore;
    private PhilcheDataStore? dataStore;
    private readonly AppLocalizer localizer;
    private readonly SettingsChangeNotifier settingsChangeNotifier;
    private readonly EvaluatorFactory evaluatorFactory;
    private readonly IModelDownloader modelDownloader;
    private readonly HuggingFaceGuardModelDownloader guardModelDownloader;
    private readonly ScanProgressController scanProgressController;

    private readonly string baseWindowTitle;
    private bool isDirty;
    private bool suppressDirtyTracking;
    private bool suppressShellContextMenuEvents;
    private bool bypassCloseConfirmation;
    private bool isLicenseAcceptedThisSession;

    public MainWindow()
    {
        InitializeComponent();

        settingsYamlStore = new SettingsYamlStore();
        localizer = new AppLocalizer();
        evaluatorFactory = new EvaluatorFactory(settingsYamlStore);
        modelDownloader = new HttpModelDownloader();
        guardModelDownloader = new HuggingFaceGuardModelDownloader(modelDownloader);
        scanProgressController = new ScanProgressController(action => Dispatcher.UIThread.Post(action));
        settingsChangeNotifier = new SettingsChangeNotifier(settingsYamlStore.FilePath);
        settingsChangeNotifier.SettingsChanged += OnSettingsChanged;

        baseWindowTitle = Title ?? "Philche Settings";

        AgentsListBox.ItemsSource = agents;
        SkillsPathsListBox.ItemsSource = editableSkillsPaths;
        FindingsListBox.ItemsSource = findings;
        ScanProgressBar.DataContext = scanProgressController;
        ScanProgressStatusTextBlock.DataContext = scanProgressController;
        ScanProgressListBox.DataContext = scanProgressController;
        PauseResumeScanButton.DataContext = scanProgressController;

        LocaleComboBox.ItemsSource = AppLocalizer.SupportedLocales;
        LocaleComboBox.SelectedItem = localizer.CurrentLocale;

        AgentsListBox.SelectionChanged += (_, _) => DisplaySelectedAgent();
        FindingsListBox.SelectionChanged += FindingsListBox_SelectionChanged;

        AgentKeyTextBox.TextChanged += EditorTextChanged;
        DisplayNameTextBox.TextChanged += EditorTextChanged;
        GuardModelNameTextBox.TextChanged += EditorTextChanged;
        GuardModelPathTextBox.TextChanged += EditorTextChanged;
        EnableMaliciousWordsScanCheckBox.IsCheckedChanged += EditorToggleChanged;
        EnableInvisibleCharsScanCheckBox.IsCheckedChanged += EditorToggleChanged;
        EnableLlmIntentScanCheckBox.IsCheckedChanged += EditorToggleChanged;
        EnableYaraScanCheckBox.IsCheckedChanged += EditorToggleChanged;
        EnableRegexScanCheckBox.IsCheckedChanged += EditorToggleChanged;

        Opened += async (_, _) =>
        {
            dataStore ??= await CreateDataStoreAsync();
            await ReloadFromYamlAsync();
            LoadModelPathsToEditor();
            LoadScanningConfigToEditor();
            LoadScanOptionsToEditor();
            RefreshModelStatus();
            await LoadFindingsAsync();
            ApplyLocalizedUiText();
        };
        Closing += OnWindowClosing;
        Closed += (_, _) =>
        {
            settingsChangeNotifier.Dispose();
            scanProgressController.Dispose();
        };
    }

    internal ScanProgressController ScanProgressController => scanProgressController;

    internal IReadOnlyList<(string KindLabel, string Title, string Subtitle)> GetRiskReviewItemsSnapshot()
    {
        return findings.Select(static item => (item.KindLabel, item.Title, item.Subtitle)).ToList();
    }

    internal void SetDataStoreForTesting(PhilcheDataStore store)
    {
        dataStore = store;
    }

    public void SetScanScheduler(ScanScheduler? scheduler)
    {
        scanProgressController.AttachScheduler(scheduler);
    }

    private void PauseResumeScan_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        scanProgressController.TogglePauseResume();
    }

    public void OpenModelsTab()
    {
        if (RootTabControl is null)
        {
            return;
        }

        RootTabControl.SelectedItem = ModelsTabItem;
    }

    public void OpenScanProgressTab()
    {
        if (RootTabControl is null)
        {
            return;
        }

        RootTabControl.SelectedItem = ScanProgressTabItem;
    }

    private static async Task<PhilcheDataStore> CreateDataStoreAsync()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Philche");
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "philche.db");
        var store = new PhilcheDataStore(dbPath);
        await store.MigrationRunner.ApplyAsync();
        return store;
    }

    private void SaveLocale_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var selected = LocaleComboBox.SelectedItem?.ToString() ?? "zh-TW";
        localizer.SetLocale(selected);

        if (Avalonia.Application.Current is App app)
        {
            app.RefreshTrayLocalization();
        }

        ApplyLocalizedUiText();
        StatusTextBlock.Text = localizer.Get("locale.saved");
    }

    private async void Reload_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await ReloadFromYamlAsync();
    }

    private void AddAgent_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var newEntry = new KnownAgentCatalogEntry
        {
            AgentKey = string.Empty,
            DisplayName = localizer.Get("status.agentDraftName"),
            HostExecutablePaths = [],
            ExecutableNames = [],
            SkillsPaths = [],
        };

        agents.Add(newEntry);
        AgentsListBox.SelectedIndex = agents.Count - 1;
        StatusTextBlock.Text = localizer.Get("status.agentDraftAdded");
        MarkDirty();
    }

    private void DeleteAgent_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (AgentsListBox.SelectedIndex < 0)
        {
            StatusTextBlock.Text = localizer.Get("status.selectAgentToDelete");
            return;
        }

        var index = AgentsListBox.SelectedIndex;
        agents.RemoveAt(index);
        AgentsListBox.SelectedIndex = Math.Min(index, agents.Count - 1);
        DisplaySelectedAgent();
        StatusTextBlock.Text = localizer.Get("status.agentRemoved");
        MarkDirty();
    }

    private void MoveAgentUp_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        MoveAgentByOffset(sender, -1);
    }

    private void MoveAgentDown_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        MoveAgentByOffset(sender, 1);
    }

    private void ApplySelected_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ApplyEditorToSelected())
        {
            StatusTextBlock.Text = localizer.Get("status.appliedSelectedAgent");
            MarkDirty();
        }
    }

    private void MoveAgentByOffset(object? sender, int offset)
    {
        if (sender is not MenuItem { CommandParameter: KnownAgentCatalogEntry entry })
        {
            StatusTextBlock.Text = localizer.Get("status.agentMoveUnknown");
            return;
        }

        var currentIndex = agents.IndexOf(entry);
        if (currentIndex < 0)
        {
            StatusTextBlock.Text = localizer.Get("status.agentNoLongerExists");
            return;
        }

        var targetIndex = currentIndex + offset;
        if (targetIndex < 0 || targetIndex >= agents.Count)
        {
            return;
        }

        agents.Move(currentIndex, targetIndex);
        AgentsListBox.SelectedItem = entry;
        StatusTextBlock.Text = offset < 0
            ? localizer.Get("status.agentMovedUp")
            : localizer.Get("status.agentMovedDown");
        MarkDirty(localizer.Get("status.unsavedApplySaveSettings"));
    }

    private void Save_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            if (!ApplyEditorToSelected())
            {
                return;
            }

            settingsYamlStore.SaveCatalog(agents.ToList());
            settingsYamlStore.SaveScanningConfig(BuildScanningConfigFromEditor());
            StatusTextBlock.Text = string.Format(localizer.Get("status.savedTo"), settingsYamlStore.FilePath);
            ClearDirty();
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = string.Format(localizer.Get("status.saveFailed"), ex.Message);
        }
    }

    private async void ReloadFindings_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await LoadFindingsAsync();
    }

    internal async Task LoadFindingsAsync()
    {
        if (dataStore is null)
        {
            findings.Clear();
            ClearFindingDetails();
            StatusTextBlock.Text = localizer.Get("status.findingsUnavailableUntilDb");
            return;
        }

        findings.Clear();
        ClearFindingDetails();

        var host = await dataStore.ScanTargets.ListBySurfaceAsync(SurfaceType.Host);
        var wsl = await dataStore.ScanTargets.ListBySurfaceAsync(SurfaceType.Wsl);
        var targets = host.Concat(wsl).ToList();

        foreach (var target in targets)
        {
            var targetFindings = await dataStore.Findings.ListByTargetAsync(target.Id);
            foreach (var finding in targetFindings)
            {
                findings.Add(FindingListItem.FromModels(target, finding));
            }
        }

        var recentRuns = await dataStore.ScanRuns.ListRecentAsync(10);
        var latestCompleted = recentRuns.FirstOrDefault(static run => run.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(latestCompleted?.HighRiskPathsJson))
        {
            var riskyPaths = DeserializePaths(latestCompleted.HighRiskPathsJson!);
            var detectedAt = latestCompleted.EndedAt ?? latestCompleted.StartedAt;
            foreach (var riskyPath in riskyPaths)
            {
                findings.Add(FindingListItem.FromRiskyPath(riskyPath, detectedAt));
            }
        }

        if (findings.Count == 0)
        {
            FindingSummaryTextBlock.Text = localizer.Get("findings.empty");
        }

        StatusTextBlock.Text = string.Format(localizer.Get("status.findings.loaded"), findings.Count);
    }

    private void FindingsListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (FindingsListBox.SelectedItem is not FindingListItem selected)
        {
            ClearFindingDetails();
            return;
        }

        if (selected.IsCodeScanRisk)
        {
            FindingSummaryTextBlock.Text = selected.RiskyFilePath ?? string.Empty;
            FindingDescriptionTextBlock.Text = "由程式碼掃描標記為風險的檔案。";
            FindingProvenanceTextBlock.Text = selected.Subtitle;
            FindingGuidanceTextBlock.Text = "請優先檢查此檔案內容、來源與最近變更。";
            return;
        }

        if (selected.Finding is null)
        {
            ClearFindingDetails();
            return;
        }

        FindingSummaryTextBlock.Text = selected.Finding.Summary ?? selected.Finding.CanonicalVulnerabilityId;
        FindingDescriptionTextBlock.Text = selected.Finding.Description ?? string.Empty;

        var provenanceLines = selected.Finding.Provenance.Select(p =>
            $"- {p.Field}: {p.Source}{(string.IsNullOrWhiteSpace(p.SourceId) ? string.Empty : $" ({p.SourceId})")}");
        var combinedSources = selected.Finding.Provenance.Select(x => x.Source).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var mixedSourceText = combinedSources.Count > 1
            ? $"{Environment.NewLine}- mixed-source: {string.Join(", ", combinedSources)}"
            : string.Empty;
        FindingProvenanceTextBlock.Text = string.Join(Environment.NewLine, provenanceLines) + mixedSourceText;

        FindingGuidanceTextBlock.Text = GetGuidanceText(selected.Finding);
    }

    private string GetGuidanceText(Finding finding)
    {
        return localizer.Get(RiskReviewUiLogic.ResolveGuidanceKey(finding));
    }

    private static IReadOnlyList<string> DeserializePaths(string json)
    {
        try
        {
            return (JsonSerializer.Deserialize<List<string>>(json) ?? [])
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private void ClearFindingDetails()
    {
        FindingSummaryTextBlock.Text = string.Empty;
        FindingDescriptionTextBlock.Text = string.Empty;
        FindingProvenanceTextBlock.Text = string.Empty;
        FindingGuidanceTextBlock.Text = string.Empty;
    }

    private void ApplyLocalizedUiText()
    {
        TitleTextBlock.Text = localizer.Get("title.main");

        AgentCatalogTabItem.Header = localizer.Get("tab.agentCatalog");
        RiskReviewTabItem.Header = localizer.Get("tab.riskReview");
        ScanProgressTabItem.Header = localizer.Get("tab.scanProgress");
        ModelsTabItem.Header = localizer.Get("tab.scanMethod");
        ScanOptionsTabItem.Header = localizer.Get("tab.scanOptions");

        AgentsHeaderTextBlock.Text = localizer.Get("agents.header");
        AddAgentButton.Content = localizer.Get("agents.add");
        DeleteAgentButton.Content = localizer.Get("agents.delete");
        ReloadAgentButton.Content = localizer.Get("agents.reload");

        AgentKeyLabelTextBlock.Text = localizer.Get("agents.field.agentKey");
        DisplayNameLabelTextBlock.Text = localizer.Get("agents.field.displayName");
        SkillsPathsLabelTextBlock.Text = localizer.Get("agents.field.skillsPaths");
        AddSkillsPathButton.Content = localizer.Get("agents.skillsPaths.add");
        RemoveSkillsPathButton.Content = localizer.Get("agents.skillsPaths.remove");
        ApplySelectedButton.Content = localizer.Get("agents.applySelected");
        SaveSettingsButton.Content = localizer.Get("agents.saveSettings");

        LocaleLabelTextBlock.Text = localizer.Get("locale.label");
        SaveLocaleButton.Content = localizer.Get("locale.save");

        FindingsHeaderTextBlock.Text = localizer.Get("findings.header");
        ReloadFindingsButton.Content = localizer.Get("findings.reload");
        FindingSummaryLabelTextBlock.Text = localizer.Get("findings.detail.summary");
        FindingDescriptionLabelTextBlock.Text = localizer.Get("findings.detail.description");
        FindingProvenanceLabelTextBlock.Text = localizer.Get("findings.detail.provenance");
        FindingGuidanceLabelTextBlock.Text = localizer.Get("findings.detail.guidance");

        ScanOptionsHeaderTextBlock.Text = localizer.Get("tab.scanOptions");
        ScanOptionsDescriptionTextBlock.Text = localizer.Get("scanOptions.description");
        ScanFileContextMenuCheckBox.Content = localizer.Get("scanOptions.fileContextMenu");
        ScanDirectoryContextMenuCheckBox.Content = localizer.Get("scanOptions.dirContextMenu");
    }

    private async Task ReloadFromYamlAsync()
    {
        try
        {
            var loaded = await wslCatalogResolver.ExpandAsync(settingsYamlStore.LoadCatalog());
            agents.Clear();
            foreach (var item in loaded)
            {
                agents.Add(item);
            }

            AgentsListBox.SelectedIndex = agents.Count > 0 ? 0 : -1;
            DisplaySelectedAgent();
            LoadScanningConfigToEditor();
            StatusTextBlock.Text = string.Format(localizer.Get("status.agentsLoadedFrom"), agents.Count, settingsYamlStore.FilePath);
            ClearDirty();
        }
        catch (Exception ex)
        {
            agents.Clear();
            ClearEditor();
            StatusTextBlock.Text = string.Format(localizer.Get("status.reloadFailed"), ex.Message);
        }
    }

    private bool ApplyEditorToSelected()
    {
        if (AgentsListBox.SelectedIndex < 0)
        {
            StatusTextBlock.Text = localizer.Get("status.selectAgentFirst");
            return false;
        }

        var agentKey = AgentKeyTextBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(agentKey))
        {
            StatusTextBlock.Text = localizer.Get("status.agentKeyRequired");
            return false;
        }

        var displayName = DisplayNameTextBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(displayName))
        {
            StatusTextBlock.Text = localizer.Get("status.displayNameRequired");
            return false;
        }

        var updated = new KnownAgentCatalogEntry
        {
            AgentKey = agentKey,
            DisplayName = displayName,
            SkillsPaths = BuildSkillsPathEntriesFromEditor(),
        };

        var index = AgentsListBox.SelectedIndex;
        agents[index] = updated;
        AgentsListBox.SelectedIndex = index;
        return true;
    }

    private void DisplaySelectedAgent()
    {
        if (AgentsListBox.SelectedItem is not KnownAgentCatalogEntry selected)
        {
            ClearEditor();
            return;
        }

        suppressDirtyTracking = true;
        AgentKeyTextBox.Text = selected.AgentKey;
        DisplayNameTextBox.Text = selected.DisplayName;
        ResetEditableSkillsPaths(selected.SkillsPaths);
        suppressDirtyTracking = false;
    }

    private void ClearEditor()
    {
        suppressDirtyTracking = true;
        AgentKeyTextBox.Text = string.Empty;
        DisplayNameTextBox.Text = string.Empty;
        ResetEditableSkillsPaths([]);
        suppressDirtyTracking = false;
    }

    private void AddSkillsPath_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var item = new EditableSkillsPathItem();
        SubscribeToSkillsPathItem(item);
        editableSkillsPaths.Add(item);
        SkillsPathsListBox.SelectedItem = item;
        MarkDirty(localizer.Get("status.unsavedApplySaveSettings"));
    }

    private void RemoveSkillsPath_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (SkillsPathsListBox.SelectedItem is not EditableSkillsPathItem selected)
        {
            StatusTextBlock.Text = localizer.Get("status.selectSkillsPathToRemove");
            return;
        }

        if (selected.Default)
        {
            StatusTextBlock.Text = localizer.Get("status.cannotRemoveDefaultSkillsPath");
            return;
        }

        UnsubscribeFromSkillsPathItem(selected);
        editableSkillsPaths.Remove(selected);
        MarkDirty(localizer.Get("status.unsavedApplySaveSettings"));
    }

    private void EditorTextChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (suppressDirtyTracking)
        {
            return;
        }

        MarkDirty(localizer.Get("status.unsavedApplySaveSettings"));
    }

    private void EditorToggleChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (suppressDirtyTracking)
        {
            return;
        }

        MarkDirty(localizer.Get("status.unsavedSaveSettingsOrModels"));
    }

    private void MarkDirty(string? message = null)
    {
        if (isDirty)
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                StatusTextBlock.Text = message;
            }

            return;
        }

        isDirty = true;
        Title = $"* {baseWindowTitle}";
        if (!string.IsNullOrWhiteSpace(message))
        {
            StatusTextBlock.Text = message;
        }
    }

    private void ClearDirty()
    {
        isDirty = false;
        Title = baseWindowTitle;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (bypassCloseConfirmation || !isDirty)
        {
            return;
        }

        e.Cancel = true;
        var shouldDiscard = await ConfirmDiscardChangesAsync();
        if (!shouldDiscard)
        {
            StatusTextBlock.Text = localizer.Get("status.closeCanceled");
            return;
        }

        bypassCloseConfirmation = true;
        Close();
    }

    private async void SaveModels_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            await SaveModelPathsAndInvalidatePromptCacheAsync();
            settingsYamlStore.SaveScanningConfig(BuildScanningConfigFromEditor());

            StatusTextBlock.Text = localizer.Get("status.modelPathsSaved");
            RefreshModelStatus();
            await LoadFindingsAsync();
            ClearDirty();
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = string.Format(localizer.Get("status.saveModelPathsFailed"), ex.Message);
        }
    }

    private async void RefreshModels_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        RefreshModelStatus();
        await LoadFindingsAsync();
    }

    private async void BrowseGuardModel_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var selectedPath = await PickGgufFileAsync();
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            GuardModelPathTextBox.Text = selectedPath;
        }
    }

    private async void DownloadGuardModel_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await DownloadGuardModelFromHuggingFaceAsync();
    }

    private async Task DownloadGuardModelFromHuggingFaceAsync()
    {
        if (!await EnsureLlamaLicenseAcceptedAsync())
        {
            StatusTextBlock.Text = localizer.Get("status.downloadCanceledNoModel");
            return;
        }

        try
        {
            var modelName = HuggingFaceGuardModelLocator.NormalizeModelName(GuardModelNameTextBox.Text ?? string.Empty);

            var modelDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Philche",
                "models");

            GuardDownloadProgressBar.IsVisible = true;
            GuardDownloadProgressBar.Value = 0;

            var progress = new Progress<(long downloaded, long total)>(state =>
            {
                if (state.total <= 0)
                {
                    return;
                }

                var percentage = Math.Clamp(state.downloaded * 100.0 / state.total, 0, 100);
                GuardDownloadProgressBar.Value = percentage;
            });

            var downloadedPath = await guardModelDownloader.DownloadAsync(
                modelName,
                modelDir,
                progress,
                UpdateGuardDownloadStatus,
                CancellationToken.None);

            GuardModelNameTextBox.Text = modelName;
            GuardModelPathTextBox.Text = downloadedPath;
            await SaveModelPathsAndInvalidatePromptCacheAsync();

            RefreshModelStatus();
            StatusTextBlock.Text = string.Format(localizer.Get("status.modelDownloaded"), Path.GetFileName(downloadedPath));
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = localizer.Get("status.modelDownloadCanceled");
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = string.Format(localizer.Get("status.modelDownloadFailed"), ex.Message);
        }
        finally
        {
            GuardDownloadProgressBar.IsVisible = false;
        }
    }

    private void UpdateGuardDownloadStatus(GuardModelDownloadStatus status)
    {
        var sourceLabel = HuggingFaceGuardModelDownloader.BuildSourceLabel(status.DownloadUri);

        if (status.Kind == GuardModelDownloadStatusKind.Attempting)
        {
            StatusTextBlock.Text = string.Format(
                localizer.Get("status.guardModelDownloading"),
                status.ModelName,
                status.AttemptNumber,
                status.AttemptCount,
                sourceLabel);
            return;
        }

        var nextSourceLabel = status.NextDownloadUri is null
            ? "next source"
            : HuggingFaceGuardModelDownloader.BuildSourceLabel(status.NextDownloadUri);

        StatusTextBlock.Text = string.Format(
            localizer.Get("status.guardModelRetrying"),
            sourceLabel,
            status.Error?.Message,
            nextSourceLabel);
    }

    private async Task<bool> EnsureLlamaLicenseAcceptedAsync()
    {
        if (isLicenseAcceptedThisSession)
        {
            return true;
        }

        var accepted = await ConfirmDialog.ShowAsync(
            this,
            title: localizer.Get("dialog.llamaLicense.title"),
            message: localizer.Get("dialog.llamaLicense.message"),
            confirmText: localizer.Get("dialog.llamaLicense.confirm"),
            cancelText: localizer.Get("dialog.llamaLicense.cancel"));

        isLicenseAcceptedThisSession = accepted;
        return accepted;
    }

    private async Task<string?> PickGgufFileAsync()
    {
        if (StorageProvider is null)
        {
            return null;
        }

        var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select GGUF model",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("GGUF model")
                {
                    Patterns = ["*.gguf"],
                },
            ],
        });

        var file = result.FirstOrDefault();
        return file?.TryGetLocalPath();
    }

    private async Task SaveModelPathsAndInvalidatePromptCacheAsync()
    {
        var previous = settingsYamlStore.LoadModelPaths();
        var updated = new ModelPathsConfig
        {
            ModelName = HuggingFaceGuardModelLocator.NormalizeModelName(GuardModelNameTextBox.Text ?? string.Empty),
            GuardModelPath = GuardModelPathTextBox.Text?.Trim() ?? string.Empty,
            CveSummaryModelPath = previous.CveSummaryModelPath,
        };

        settingsYamlStore.SaveModelPaths(updated);

        if (dataStore is not null &&
            !string.Equals(previous.GuardModelPath, updated.GuardModelPath, StringComparison.OrdinalIgnoreCase))
        {
            await dataStore.ScanCache.DeleteByScanTypeAsync(ScanCacheTypes.Prompt);
        }
    }

    private void LoadModelPathsToEditor()
    {
        var modelPaths = settingsYamlStore.LoadModelPaths();
        suppressDirtyTracking = true;
        GuardModelNameTextBox.Text = modelPaths.ModelName;
        GuardModelPathTextBox.Text = modelPaths.GuardModelPath;
        suppressDirtyTracking = false;
    }

    private void LoadScanningConfigToEditor()
    {
        var scanning = settingsYamlStore.LoadScanningConfig();
        suppressDirtyTracking = true;
        EnableYaraScanCheckBox.IsChecked = scanning.EnableYaraScan;
        EnableMaliciousWordsScanCheckBox.IsChecked = scanning.EnableMaliciousWordGroupList;
        EnableInvisibleCharsScanCheckBox.IsChecked = scanning.EnableInvisibleCharacterDetection;
        EnableLlmIntentScanCheckBox.IsChecked = scanning.EnableLlmIntentRecognition;
        EnableRegexScanCheckBox.IsChecked = scanning.EnableRegexScan;
        suppressDirtyTracking = false;
    }

    private ScanningConfig BuildScanningConfigFromEditor()
    {
        var existing = settingsYamlStore.LoadScanningConfig();
        return new ScanningConfig
        {
            CodeFileExtensions = existing.CodeFileExtensions,
            EnableYaraScan = EnableYaraScanCheckBox.IsChecked ?? true,
            EnableGuardModelScan = EnableLlmIntentScanCheckBox.IsChecked ?? true,
            EnableMaliciousWordGroupList = EnableMaliciousWordsScanCheckBox.IsChecked ?? true,
            EnableInvisibleCharacterDetection = EnableInvisibleCharsScanCheckBox.IsChecked ?? true,
            EnableLlmIntentRecognition = EnableLlmIntentScanCheckBox.IsChecked ?? true,
            EnableRegexScan = EnableRegexScanCheckBox.IsChecked ?? true,
        };
    }

    private void RefreshModelStatus()
    {
        using var snapshot = evaluatorFactory.Build();

        if (snapshot.GuardProvider is null)
        {
            GuardModelStatusTextBlock.Text = localizer.Get("status.guardModel.notConfigured");
        }
        else if (snapshot.GuardProvider.IsAvailable)
        {
            GuardModelStatusTextBlock.Text = localizer.Get("status.guardModel.available");
        }
        else
        {
            GuardModelStatusTextBlock.Text = localizer.Get("status.guardModel.notFound");
        }
    }

    private void OnSettingsChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _ = HandleSettingsChangedAsync();
        });
    }

    private async Task HandleSettingsChangedAsync()
    {
        try
        {
            await ReloadFromYamlAsync();
            LoadModelPathsToEditor();
            LoadScanningConfigToEditor();
            LoadScanOptionsToEditor();
            RefreshModelStatus();
            await LoadFindingsAsync();
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = string.Format(localizer.Get("status.settingsReloadFailed"), ex.Message);
        }
    }

    private void LoadScanOptionsToEditor()
    {
        suppressShellContextMenuEvents = true;
        try
        {
            var config = settingsYamlStore.LoadShellContextMenuConfig();
            ScanFileContextMenuCheckBox.IsChecked = config.FileContextMenuEnabled && WindowsShellContextMenu.IsFileContextMenuRegistered(GetShellContextMenuExtensions());
            ScanDirectoryContextMenuCheckBox.IsChecked = config.DirectoryContextMenuEnabled && WindowsShellContextMenu.IsDirectoryContextMenuRegistered();
        }
        finally
        {
            suppressShellContextMenuEvents = false;
        }
    }

    private async void ScanFileContextMenuCheckBox_Changed(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (suppressShellContextMenuEvents)
        {
            return;
        }

        await UpdateShellContextMenuAsync(isFileMenu: true, ScanFileContextMenuCheckBox.IsChecked ?? false);
    }

    private async void ScanDirectoryContextMenuCheckBox_Changed(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (suppressShellContextMenuEvents)
        {
            return;
        }

        await UpdateShellContextMenuAsync(isFileMenu: false, ScanDirectoryContextMenuCheckBox.IsChecked ?? false);
    }

    private async Task UpdateShellContextMenuAsync(bool isFileMenu, bool enabled)
    {
        try
        {
            var launchCommand = Program.BuildLaunchCommand("--scan", "%1");
            var iconPath = Program.GetPreferredExecutablePath();
            if (string.IsNullOrWhiteSpace(launchCommand) && enabled)
            {
                throw new InvalidOperationException(localizer.Get("status.executablePathUnavailable"));
            }

            await Task.Run(() =>
            {
                if (isFileMenu)
                {
                    if (enabled)
                    {
                        WindowsShellContextMenu.RegisterFileContextMenu(launchCommand!, iconPath, GetShellContextMenuExtensions());
                    }
                    else
                    {
                        WindowsShellContextMenu.UnregisterFileContextMenu(GetShellContextMenuExtensions());
                    }
                }
                else
                {
                    if (enabled)
                    {
                        WindowsShellContextMenu.RegisterDirectoryContextMenu(launchCommand!, iconPath);
                    }
                    else
                    {
                        WindowsShellContextMenu.UnregisterDirectoryContextMenu();
                    }
                }
            });

            var current = settingsYamlStore.LoadShellContextMenuConfig();
            var updated = new ShellContextMenuConfig
            {
                FileContextMenuEnabled = isFileMenu ? enabled : current.FileContextMenuEnabled,
                DirectoryContextMenuEnabled = isFileMenu ? current.DirectoryContextMenuEnabled : enabled,
            };
            settingsYamlStore.SaveShellContextMenuConfig(updated);
            LoadScanOptionsToEditor();
            StatusTextBlock.Text = enabled
                ? localizer.Get("status.contextMenuRegistered")
                : localizer.Get("status.contextMenuRemoved");
        }
        catch (Exception ex)
        {
            LoadScanOptionsToEditor();
            StatusTextBlock.Text = string.Format(localizer.Get("status.contextMenuUpdateFailed"), ex.Message);
        }
    }

    private IReadOnlyList<string> GetShellContextMenuExtensions()
    {
        var configured = settingsYamlStore.LoadScanningConfig().CodeFileExtensions;
        return configured.Count > 0
            ? configured
            : WindowsShellContextMenu.GetDefaultFileExtensions();
    }

    private async Task<bool> ConfirmDiscardChangesAsync()
    {
        return await ConfirmDialog.ShowAsync(
            this,
            title: localizer.Get("dialog.unsavedChanges.title"),
            message: localizer.Get("dialog.unsavedChanges.message"),
            confirmText: localizer.Get("dialog.unsavedChanges.confirm"),
            cancelText: localizer.Get("dialog.unsavedChanges.cancel"));
    }

    private static IReadOnlyList<string> ParseLines(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .Split(['\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string JoinLines(IReadOnlyList<string> values)
    {
        return string.Join(Environment.NewLine, values);
    }

    private IReadOnlyList<SkillsPathEntry> BuildSkillsPathEntriesFromEditor()
    {
        return editableSkillsPaths
            .Where(static item => !string.IsNullOrWhiteSpace(item.Path))
            .Where(static item => !item.Default || !item.Trusted) // Only save custom paths, or default paths where user unchecked Trusted
            .Select(static item => new SkillsPathEntry(item.Path.Trim(), item.Trusted))
            .GroupBy(static item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToList();
    }

    private void ResetEditableSkillsPaths(IReadOnlyList<SkillsPathEntry> values)
    {
        foreach (var item in editableSkillsPaths)
        {
            UnsubscribeFromSkillsPathItem(item);
        }

        editableSkillsPaths.Clear();

        foreach (var value in values)
        {
            var item = new EditableSkillsPathItem
            {
                Path = value.Path,
                Trusted = value.Trusted,
                Default = value.Default,
            };
            SubscribeToSkillsPathItem(item);
            editableSkillsPaths.Add(item);
        }
    }

    private void SubscribeToSkillsPathItem(EditableSkillsPathItem item)
    {
        item.PropertyChanged += SkillsPathItem_PropertyChanged;
    }

    private void UnsubscribeFromSkillsPathItem(EditableSkillsPathItem item)
    {
        item.PropertyChanged -= SkillsPathItem_PropertyChanged;
    }

    private void SkillsPathItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (suppressDirtyTracking)
        {
            return;
        }

        if (e.PropertyName is nameof(EditableSkillsPathItem.Path) or nameof(EditableSkillsPathItem.Trusted))
        {
            MarkDirty(localizer.Get("status.unsavedApplySaveSettings"));
        }
    }

    public void TriggerManualScanFromTray()
    {
        StatusTextBlock.Text = localizer.Get("status.trayManualScanRequested");
    }

    public void PauseScanningFromTray()
    {
        StatusTextBlock.Text = localizer.Get("status.trayScanPaused");
    }

    public void ResumeScanningFromTray()
    {
        StatusTextBlock.Text = localizer.Get("status.trayScanResumed");
    }

    private sealed class EditableSkillsPathItem : INotifyPropertyChanged
    {
        private string path = string.Empty;
        private bool trusted;
        private bool isDefault;

        public bool Default
        {
            get => isDefault;
            set
            {
                if (isDefault == value)
                {
                    return;
                }

                isDefault = value;
                OnPropertyChanged();
            }
        }

        public string Path
        {
            get => path;
            set
            {
                if (string.Equals(path, value, StringComparison.Ordinal))
                {
                    return;
                }

                path = value;
                OnPropertyChanged();
            }
        }

        public bool Trusted
        {
            get => trusted;
            set
            {
                if (trusted == value)
                {
                    return;
                }

                trusted = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    private sealed class FindingListItem
    {
        public required string KindLabel { get; init; }
        public required string Title { get; init; }
        public required string Subtitle { get; init; }
        public Finding? Finding { get; init; }
        public string? RiskyFilePath { get; init; }
        public bool IsCodeScanRisk => !string.IsNullOrWhiteSpace(RiskyFilePath);

        public static FindingListItem FromModels(ScanTarget target, Finding finding)
        {
            var risk = RiskReviewUiLogic.ResolveRiskLevel(finding);
            var title = $"[{risk}] {target.DisplayName} · {finding.FindingType}";
            var subtitle = $"{finding.CanonicalVulnerabilityId} · {finding.UpdatedAt:yyyy-MM-dd HH:mm}";
            return new FindingListItem
            {
                KindLabel = "Finding",
                Title = title,
                Subtitle = subtitle,
                Finding = finding,
            };
        }

        public static FindingListItem FromRiskyPath(string filePath, DateTimeOffset detectedAt)
        {
            return new FindingListItem
            {
                KindLabel = "Code Scan",
                Title = $"[RISK] {Path.GetFileName(filePath)}",
                Subtitle = $"{filePath} · 掃描時間: {detectedAt.LocalDateTime:yyyy-MM-dd HH:mm}",
                RiskyFilePath = filePath,
            };
        }
    }
}
