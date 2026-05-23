using YamlDotNet.Serialization;

namespace Philche.Core.Config;

internal sealed class SettingsYamlDocument
{
    [YamlMember(Alias = "version")]
    public int Version { get; init; } = 1;

    [YamlMember(Alias = "agents")]
    public List<SettingsYamlAgent> Agents { get; init; } = [];

    [YamlMember(Alias = "models")]
    public SettingsYamlModels? Models { get; init; }

    [YamlMember(Alias = "scanning")]
    public SettingsYamlScanning? Scanning { get; init; }

    [YamlMember(Alias = "scheduler")]
    public SettingsYamlScheduler? Scheduler { get; init; }

    [YamlMember(Alias = "shell")]
    public SettingsYamlShell? Shell { get; init; }
}

internal sealed class SettingsYamlAgent
{
    [YamlMember(Alias = "agentKey")]
    public string? AgentKey { get; init; }

    [YamlMember(Alias = "displayName")]
    public string? DisplayName { get; init; }

    [YamlMember(Alias = "name")]
    public string? LegacyName { get; init; }

    [YamlMember(Alias = "hostExecutablePaths")]
    public List<string>? HostExecutablePaths { get; init; }

    [YamlMember(Alias = "host_executable_paths")]
    public List<string>? LegacyHostExecutablePaths { get; init; }

    [YamlMember(Alias = "executableNames")]
    public List<string>? ExecutableNames { get; init; }

    [YamlMember(Alias = "executable_names")]
    public List<string>? LegacyExecutableNames { get; init; }

    [YamlMember(Alias = "skillsPaths")]
    public List<object>? SkillsPaths { get; init; }

    [YamlMember(Alias = "skills_path")]
    public string? LegacySkillsPath { get; init; }

    [YamlMember(Alias = "wslUserRelativePath")]
    public string? WslUserRelativePath { get; init; }
}

internal sealed class SettingsYamlSkillsPath
{
    [YamlMember(Alias = "path")]
    public string? Path { get; init; }

    [YamlMember(Alias = "trusted")]
    public bool? Trusted { get; init; }
}

internal sealed class SettingsYamlModels
{
    [YamlMember(Alias = "modelName")]
    public string? ModelName { get; init; }

    [YamlMember(Alias = "guardModelPath")]
    public string? GuardModelPath { get; init; }

    [YamlMember(Alias = "cveSummaryModelPath")]
    public string? CveSummaryModelPath { get; init; }
}

internal sealed class SettingsYamlScanning
{
    [YamlMember(Alias = "codeFileExtensions")]
    public List<string>? CodeFileExtensions { get; init; }

    [YamlMember(Alias = "virusTotalApiKey")]
    public string? VirusTotalApiKey { get; init; }

    [YamlMember(Alias = "enableSemanticScan")]
    public bool? EnableSemanticScan { get; init; }

    [YamlMember(Alias = "enableYaraScan")]
    public bool? EnableYaraScan { get; init; }

    [YamlMember(Alias = "enableGuardModelScan")]
    public bool? EnableGuardModelScan { get; init; }

    [YamlMember(Alias = "enableMaliciousWordGroupList")]
    public bool? EnableMaliciousWordGroupList { get; init; }

    [YamlMember(Alias = "enableInvisibleCharacterDetection")]
    public bool? EnableInvisibleCharacterDetection { get; init; }

    [YamlMember(Alias = "enableLlmIntentRecognition")]
    public bool? EnableLlmIntentRecognition { get; init; }

    [YamlMember(Alias = "enableRegexScan")]
    public bool? EnableRegexScan { get; init; }

    [YamlMember(Alias = "enableVirusTotalSkillUrlScan")]
    public bool? EnableVirusTotalSkillUrlScan { get; init; }

    [YamlMember(Alias = "enableVirusTotalScriptUrlScan")]
    public bool? EnableVirusTotalScriptUrlScan { get; init; }

    [YamlMember(Alias = "enableCveCorrelation")]
    public bool? EnableCveCorrelation { get; init; }
}

internal sealed class SettingsYamlScheduler
{
    [YamlMember(Alias = "intervalHours")]
    public int? IntervalHours { get; init; }

    [YamlMember(Alias = "isPaused")]
    public bool? IsPaused { get; init; }

    [YamlMember(Alias = "scanOnStartup")]
    public bool? ScanOnStartup { get; init; }

    [YamlMember(Alias = "periodicScanEnabled")]
    public bool? PeriodicScanEnabled { get; init; }

    [YamlMember(Alias = "realtimeScanEnabled")]
    public bool? RealtimeScanEnabled { get; init; }
}

internal sealed class SettingsYamlShell
{
    [YamlMember(Alias = "fileContextMenuEnabled")]
    public bool? FileContextMenuEnabled { get; init; }

    [YamlMember(Alias = "directoryContextMenuEnabled")]
    public bool? DirectoryContextMenuEnabled { get; init; }
}
