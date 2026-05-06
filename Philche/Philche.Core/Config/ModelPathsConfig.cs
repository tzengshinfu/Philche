namespace Philche.Core.Config;

public sealed class ModelPathsConfig
{
    public string ModelName { get; init; } = HuggingFaceGuardModelLocator.DefaultModelName;
    public string GuardModelPath { get; init; } = string.Empty;
    public string CveSummaryModelPath { get; init; } = string.Empty;
}

public sealed class ScanningConfig
{
    public List<string> CodeFileExtensions { get; init; } = [];
    public bool EnableYaraScan { get; init; } = true;
    public bool EnableGuardModelScan { get; init; } = true;
    public bool EnableMaliciousWordGroupList { get; init; } = true;
    public bool EnableInvisibleCharacterDetection { get; init; } = true;
    public bool EnableLlmIntentRecognition { get; init; } = true;
    public bool EnableRegexScan { get; init; } = true;
    public bool EnableCveCorrelation { get; init; } = true;
}

public sealed class SchedulerConfig
{
    public int IntervalHours { get; init; } = 4;
    public bool IsPaused { get; init; }
    public bool ScanOnStartup { get; init; }
    public bool PeriodicScanEnabled { get; init; } = true;
    public bool RealtimeScanEnabled { get; init; } = true;
}

public sealed class ShellContextMenuConfig
{
    public bool FileContextMenuEnabled { get; init; }
    public bool DirectoryContextMenuEnabled { get; init; }
}
