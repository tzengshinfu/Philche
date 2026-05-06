using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Philche.Core.Orchestration;

namespace Philche.Tray;

internal sealed class ScanProgressController : INotifyPropertyChanged, IDisposable
{
    private readonly Action<Action> marshal;
    private readonly ObservableCollection<ScanProgressFileEntry> files = [];
    private readonly HashSet<string> knownFiles = new(StringComparer.OrdinalIgnoreCase);

    private ScanScheduler? scheduler;
    private string statusText = "閒置";
    private double progressPercentage;
    private bool isProgressVisible;
    private bool isIndeterminate;
    private bool isPaused;

    public ScanProgressController(Action<Action>? marshal = null)
    {
        this.marshal = marshal ?? (static action => action());
        Files = new ReadOnlyObservableCollection<ScanProgressFileEntry>(files);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ReadOnlyObservableCollection<ScanProgressFileEntry> Files { get; }

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public double ProgressPercentage
    {
        get => progressPercentage;
        private set => SetProperty(ref progressPercentage, value);
    }

    public bool IsProgressVisible
    {
        get => isProgressVisible;
        private set => SetProperty(ref isProgressVisible, value);
    }

    public bool IsIndeterminate
    {
        get => isIndeterminate;
        private set => SetProperty(ref isIndeterminate, value);
    }

    public bool IsPaused
    {
        get => isPaused;
        private set
        {
            if (!SetProperty(ref isPaused, value))
            {
                return;
            }

            OnPropertyChanged(nameof(PauseResumeButtonText));
        }
    }

    public string PauseResumeButtonText => IsPaused ? "繼續掃描" : "暫停掃描";

    public void AttachScheduler(ScanScheduler? scanScheduler)
    {
        if (ReferenceEquals(scheduler, scanScheduler))
        {
            marshal(RefreshFromScheduler);
            return;
        }

        if (scheduler is not null)
        {
            scheduler.ScanProgress -= OnScanProgress;
        }

        scheduler = scanScheduler;

        if (scheduler is not null)
        {
            scheduler.ScanProgress += OnScanProgress;
        }

        marshal(RefreshFromScheduler);
    }

    public bool TogglePauseResume()
    {
        if (scheduler is null)
        {
            return false;
        }

        if (scheduler.IsPaused)
        {
            scheduler.Resume();
        }
        else
        {
            scheduler.Pause();
        }

        marshal(() => IsPaused = scheduler.IsPaused);
        return true;
    }

    public void Dispose()
    {
        if (scheduler is not null)
        {
            scheduler.ScanProgress -= OnScanProgress;
            scheduler = null;
        }
    }

    internal void ApplySnapshot(ScanProgressSnapshot? snapshot)
    {
        ResetFiles();

        if (snapshot is null)
        {
            StatusText = "閒置";
            ProgressPercentage = 0;
            IsProgressVisible = false;
            IsIndeterminate = false;
            return;
        }

        foreach (var filePath in snapshot.Files)
        {
            AddFile(filePath);
        }

        ApplyProgress(snapshot.Progress, preserveExistingFiles: true);
    }

    internal void ApplyProgress(ScanProgressInfo progress)
    {
        ApplyProgress(progress, preserveExistingFiles: false);
    }

    private void RefreshFromScheduler()
    {
        IsPaused = scheduler?.IsPaused ?? false;
        ApplySnapshot(scheduler?.CurrentProgressSnapshot);
    }

    private void OnScanProgress(ScanProgressInfo progress)
    {
        marshal(() => ApplyProgress(progress));
    }

    private void ApplyProgress(ScanProgressInfo progress, bool preserveExistingFiles)
    {
        IsPaused = scheduler?.IsPaused ?? false;

        if (!preserveExistingFiles && progress.Status.Equals("Started", StringComparison.OrdinalIgnoreCase))
        {
            ResetFiles();
        }

        if (!string.IsNullOrWhiteSpace(progress.CurrentFile))
        {
            AddFile(progress.CurrentFile);
        }

        switch (progress.Status)
        {
            case "Started":
                IsProgressVisible = true;
                IsIndeterminate = progress.TotalFiles <= 0;
                ProgressPercentage = 0;
                StatusText = BuildActiveStatus(progress);
                break;
            case "Progress":
                IsProgressVisible = true;
                IsIndeterminate = progress.TotalFiles <= 0;
                ProgressPercentage = progress.TotalFiles > 0
                    ? Math.Clamp((double)progress.ScannedFiles / progress.TotalFiles * 100d, 0d, 100d)
                    : 0d;
                StatusText = BuildActiveStatus(progress);
                break;
            case "Completed":
                IsProgressVisible = false;
                IsIndeterminate = false;
                ProgressPercentage = progress.TotalFiles > 0 ? 100d : 0d;
                MarkRiskyFiles(progress.HighRiskPaths);
                StatusText = BuildCompletedStatus(progress);
                break;
            case "Cancelled":
                IsProgressVisible = false;
                IsIndeterminate = false;
                StatusText = "掃描已取消";
                break;
            case "Failed":
                IsProgressVisible = false;
                IsIndeterminate = false;
                StatusText = string.IsNullOrWhiteSpace(progress.ErrorMessage)
                    ? "掃描失敗"
                    : $"掃描失敗: {progress.ErrorMessage}";
                break;
            default:
                StatusText = string.IsNullOrWhiteSpace(progress.Status) ? "閒置" : progress.Status;
                break;
        }
    }

    private static string BuildActiveStatus(ScanProgressInfo progress)
    {
        var targetLabel = string.IsNullOrWhiteSpace(progress.ScanTargetDisplayName)
            ? string.Empty
            : $"{progress.ScanTargetDisplayName} ";

        if (progress.TotalFiles > 0)
        {
            var percentage = (int)Math.Round((double)progress.ScannedFiles / progress.TotalFiles * 100d);
            return $"掃描中: {targetLabel}{progress.ScannedFiles}/{progress.TotalFiles} ({percentage}%)";
        }

        return string.IsNullOrWhiteSpace(progress.CurrentFile)
            ? "掃描中"
            : $"掃描中: {progress.CurrentFile}";
    }

    private static string BuildCompletedStatus(ScanProgressInfo progress)
    {
        if (progress.TotalFiles > 0)
        {
            var sizeText = FormatSize(progress.TotalSizeBytes);
            var findingText = progress.FindingsCount > 0 ? $" | {progress.FindingsCount} 筆風險" : string.Empty;
            return $"掃描完成: {progress.ScannedFiles}/{progress.TotalFiles}，總大小 {sizeText}{findingText}";
        }

        return progress.FindingsCount > 0
            ? $"掃描完成: {progress.FindingsCount} 筆風險"
            : "掃描完成";
    }

    private static string FormatSize(long totalSizeBytes)
    {
        if (totalSizeBytes >= 1024L * 1024L)
        {
            return $"{totalSizeBytes / 1024d / 1024d:0.##} MB";
        }

        if (totalSizeBytes >= 1024L)
        {
            return $"{totalSizeBytes / 1024d:0.##} KB";
        }

        return $"{Math.Max(0, totalSizeBytes)} B";
    }

    private void AddFile(string filePath)
    {
        if (!knownFiles.Add(filePath))
        {
            return;
        }

        files.Add(new ScanProgressFileEntry(filePath));
    }

    private void MarkRiskyFiles(IReadOnlyList<string> highRiskPaths)
    {
        if (highRiskPaths.Count == 0)
        {
            return;
        }

        var riskySet = new HashSet<string>(highRiskPaths, StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            file.IsRisky = riskySet.Contains(file.FilePath);
        }
    }

    private void ResetFiles()
    {
        knownFiles.Clear();
        files.Clear();
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

internal sealed class ScanProgressFileEntry : INotifyPropertyChanged
{
    private bool isRisky;

    public ScanProgressFileEntry(string filePath)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }

    public string DisplayText => IsRisky ? $"[RISK] {FilePath}" : FilePath;

    public bool IsRisky
    {
        get => isRisky;
        set
        {
            if (isRisky == value)
            {
                return;
            }

            isRisky = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRisky)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayText)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}