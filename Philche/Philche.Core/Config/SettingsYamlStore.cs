using System.Text;
using Philche.Core.Discovery;
using Philche.Core.Domain.Models;
using YamlDotNet.Serialization;

namespace Philche.Core.Config;

public sealed class SettingsYamlStore : ISettingsYamlStore
{
    private const string SettingsEnvVar = "PHILCHE_SETTINGS_YAML_PATH";

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    public string FilePath { get; }

    public SettingsYamlStore(string? filePath = null)
    {
        FilePath = ResolveFilePath(filePath);
    }

    public IReadOnlyList<KnownAgentCatalogEntry> LoadCatalog()
    {
        EnsureFileExists();

        var yaml = File.ReadAllText(FilePath, Encoding.UTF8);
        var document = Deserializer.Deserialize<SettingsYamlDocument>(yaml) ?? new SettingsYamlDocument();
        return MapToCatalog(document);
    }

    public ModelPathsConfig LoadModelPaths()
    {
        if (!File.Exists(FilePath))
        {
            return new ModelPathsConfig();
        }

        var yaml = File.ReadAllText(FilePath, Encoding.UTF8);
        var document = Deserializer.Deserialize<SettingsYamlDocument>(yaml) ?? new SettingsYamlDocument();
        return new ModelPathsConfig
        {
            ModelName = document.Models?.ModelName ?? HuggingFaceGuardModelLocator.DefaultModelName,
            GuardModelPath = document.Models?.GuardModelPath ?? string.Empty,
            CveSummaryModelPath = document.Models?.CveSummaryModelPath ?? string.Empty,
        };
    }

    public ScanningConfig LoadScanningConfig()
    {
        if (!File.Exists(FilePath))
        {
            return new ScanningConfig();
        }

        var yaml = File.ReadAllText(FilePath, Encoding.UTF8);
        var document = Deserializer.Deserialize<SettingsYamlDocument>(yaml) ?? new SettingsYamlDocument();
        return new ScanningConfig
        {
            CodeFileExtensions = document.Scanning?.CodeFileExtensions?.Where(e => !string.IsNullOrWhiteSpace(e)).ToList() ?? [],
            EnableYaraScan = document.Scanning?.EnableYaraScan ?? true,
            EnableGuardModelScan = document.Scanning?.EnableLlmIntentRecognition ?? document.Scanning?.EnableGuardModelScan ?? true,
            EnableMaliciousWordGroupList = document.Scanning?.EnableMaliciousWordGroupList ?? true,
            EnableInvisibleCharacterDetection = document.Scanning?.EnableInvisibleCharacterDetection ?? true,
            EnableLlmIntentRecognition = document.Scanning?.EnableLlmIntentRecognition ?? document.Scanning?.EnableGuardModelScan ?? true,
            EnableRegexScan = document.Scanning?.EnableRegexScan ?? true,
            EnableCveCorrelation = document.Scanning?.EnableCveCorrelation ?? true,
        };
    }

    public SchedulerConfig LoadSchedulerConfig()
    {
        if (!File.Exists(FilePath))
        {
            return new SchedulerConfig();
        }

        var yaml = File.ReadAllText(FilePath, Encoding.UTF8);
        var document = Deserializer.Deserialize<SettingsYamlDocument>(yaml) ?? new SettingsYamlDocument();
        return new SchedulerConfig
        {
            IntervalHours = document.Scheduler?.IntervalHours is > 0 ? document.Scheduler.IntervalHours.Value : 4,
            IsPaused = document.Scheduler?.IsPaused ?? false,
            ScanOnStartup = document.Scheduler?.ScanOnStartup ?? false,
            PeriodicScanEnabled = document.Scheduler?.PeriodicScanEnabled ?? true,
            RealtimeScanEnabled = document.Scheduler?.RealtimeScanEnabled ?? true,
        };
    }

    public ShellContextMenuConfig LoadShellContextMenuConfig()
    {
        if (!File.Exists(FilePath))
        {
            return new ShellContextMenuConfig();
        }

        var yaml = File.ReadAllText(FilePath, Encoding.UTF8);
        var document = Deserializer.Deserialize<SettingsYamlDocument>(yaml) ?? new SettingsYamlDocument();
        return new ShellContextMenuConfig
        {
            FileContextMenuEnabled = document.Shell?.FileContextMenuEnabled ?? false,
            DirectoryContextMenuEnabled = document.Shell?.DirectoryContextMenuEnabled ?? false,
        };
    }

    public void SaveCatalog(IReadOnlyList<KnownAgentCatalogEntry> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        ValidateEntries(entries);

        var existing = LoadDocumentOrDefault();

        var document = new SettingsYamlDocument
        {
            Version = existing.Version,
            Agents = entries.Select(ToYamlAgent).ToList(),
            Models = existing.Models,
            Scanning = existing.Scanning,
            Scheduler = existing.Scheduler,
        };

        var yaml = Serializer.Serialize(document);
        var tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, yaml, Encoding.UTF8);
        File.Move(tempPath, FilePath, overwrite: true);
    }

    public void SaveModelPaths(ModelPathsConfig modelPaths)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

        var existing = LoadDocumentOrDefault();
        var document = new SettingsYamlDocument
        {
            Version = existing.Version,
            Agents = existing.Agents,
            Scanning = existing.Scanning,
            Scheduler = existing.Scheduler,
            Models = new SettingsYamlModels
            {
                ModelName = modelPaths.ModelName,
                GuardModelPath = modelPaths.GuardModelPath,
                CveSummaryModelPath = modelPaths.CveSummaryModelPath,
            },
        };

        var yaml = Serializer.Serialize(document);
        var tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, yaml, Encoding.UTF8);
        File.Move(tempPath, FilePath, overwrite: true);
    }

    public void SaveScanningConfig(ScanningConfig scanningConfig)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

        var existing = LoadDocumentOrDefault();
        var document = new SettingsYamlDocument
        {
            Version = existing.Version,
            Agents = existing.Agents,
            Models = existing.Models,
            Scheduler = existing.Scheduler,
            Scanning = new SettingsYamlScanning
            {
                CodeFileExtensions = scanningConfig.CodeFileExtensions,
                EnableYaraScan = scanningConfig.EnableYaraScan,
                EnableGuardModelScan = scanningConfig.EnableLlmIntentRecognition,
                EnableMaliciousWordGroupList = scanningConfig.EnableMaliciousWordGroupList,
                EnableInvisibleCharacterDetection = scanningConfig.EnableInvisibleCharacterDetection,
                EnableLlmIntentRecognition = scanningConfig.EnableLlmIntentRecognition,
                EnableRegexScan = scanningConfig.EnableRegexScan,
                EnableCveCorrelation = scanningConfig.EnableCveCorrelation,
            },
        };

        var yaml = Serializer.Serialize(document);
        var tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, yaml, Encoding.UTF8);
        File.Move(tempPath, FilePath, overwrite: true);
    }

    public void SaveSchedulerConfig(SchedulerConfig schedulerConfig)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

        var existing = LoadDocumentOrDefault();
        var document = new SettingsYamlDocument
        {
            Version = existing.Version,
            Agents = existing.Agents,
            Models = existing.Models,
            Scanning = existing.Scanning,
            Scheduler = new SettingsYamlScheduler
            {
                IntervalHours = schedulerConfig.IntervalHours,
                IsPaused = schedulerConfig.IsPaused,
                ScanOnStartup = schedulerConfig.ScanOnStartup,
                PeriodicScanEnabled = schedulerConfig.PeriodicScanEnabled,
                RealtimeScanEnabled = schedulerConfig.RealtimeScanEnabled,
            },
        };

        var yaml = Serializer.Serialize(document);
        var tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, yaml, Encoding.UTF8);
        File.Move(tempPath, FilePath, overwrite: true);
    }

    public void SaveShellContextMenuConfig(ShellContextMenuConfig shellContextMenuConfig)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

        var existing = LoadDocumentOrDefault();
        var document = new SettingsYamlDocument
        {
            Version = existing.Version,
            Agents = existing.Agents,
            Models = existing.Models,
            Scanning = existing.Scanning,
            Scheduler = existing.Scheduler,
            Shell = new SettingsYamlShell
            {
                FileContextMenuEnabled = shellContextMenuConfig.FileContextMenuEnabled,
                DirectoryContextMenuEnabled = shellContextMenuConfig.DirectoryContextMenuEnabled,
            },
        };

        var yaml = Serializer.Serialize(document);
        var tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, yaml, Encoding.UTF8);
        File.Move(tempPath, FilePath, overwrite: true);
    }

    private static void ValidateEntries(IReadOnlyList<KnownAgentCatalogEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.AgentKey))
            {
                throw new InvalidOperationException("agentKey is required.");
            }

            if (string.IsNullOrWhiteSpace(entry.DisplayName))
            {
                throw new InvalidOperationException($"displayName is required for '{entry.AgentKey}'.");
            }
        }
    }

    private void EnsureFileExists()
    {
        if (File.Exists(FilePath))
        {
            return;
        }

        var defaults = KnownAgentCatalog.Entries;
        SaveCatalog(defaults);
    }

    private static IReadOnlyList<KnownAgentCatalogEntry> MapToCatalog(SettingsYamlDocument document)
    {
        var entries = new List<KnownAgentCatalogEntry>();

        foreach (var agent in document.Agents)
        {
            var displayName = agent.DisplayName ?? agent.LegacyName;
            if (string.IsNullOrWhiteSpace(displayName))
            {
                continue;
            }

            var hostPaths = NormalizeList(agent.HostExecutablePaths, agent.LegacyHostExecutablePaths);
            var executableNames = NormalizeList(agent.ExecutableNames, agent.LegacyExecutableNames);

            var agentKey = string.IsNullOrWhiteSpace(agent.AgentKey)
                ? ToAgentKey(displayName)
                : agent.AgentKey;

            entries.Add(new KnownAgentCatalogEntry
            {
                AgentKey = agentKey!,
                DisplayName = displayName,
                HostExecutablePaths = hostPaths,
                ExecutableNames = executableNames,
                SkillsPaths = NormalizeSkillsPaths(agent.SkillsPaths, ToList(agent.LegacySkillsPath)),
                WslUserRelativePath = agent.WslUserRelativePath?.Trim() ?? string.Empty,
            });
        }

        return entries;
    }

    private static SettingsYamlAgent ToYamlAgent(KnownAgentCatalogEntry entry)
    {
        return new SettingsYamlAgent
        {
            AgentKey = entry.AgentKey,
            DisplayName = entry.DisplayName,
            HostExecutablePaths = entry.HostExecutablePaths.ToList(),
            ExecutableNames = entry.ExecutableNames.ToList(),
            WslUserRelativePath = string.IsNullOrWhiteSpace(entry.WslUserRelativePath)
                ? null
                : entry.WslUserRelativePath,
            SkillsPaths = entry.SkillsPaths
                .Select(static skillsPath => (object)new Dictionary<string, object?>
                {
                    ["path"] = skillsPath.Path,
                    ["trusted"] = skillsPath.Trusted,
                })
                .ToList(),
        };
    }

    private static List<SkillsPathEntry> NormalizeSkillsPaths(List<object>? structuredSkillsPaths, List<string>? legacySkillsPaths)
    {
        var normalized = new List<SkillsPathEntry>();

        if (structuredSkillsPaths is not null)
        {
            foreach (var candidate in structuredSkillsPaths)
            {
                if (TryParseSkillsPath(candidate, out var skillsPath))
                {
                    normalized.Add(skillsPath);
                }
            }
        }

        if (legacySkillsPaths is not null)
        {
            foreach (var value in legacySkillsPaths)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    normalized.Add(new SkillsPathEntry(value.Trim(), Trusted: false));
                }
            }
        }

        return normalized
            .GroupBy(static entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToList();
    }

    private static bool TryParseSkillsPath(object? candidate, out SkillsPathEntry skillsPath)
    {
        switch (candidate)
        {
            case null:
                break;
            case string raw when !string.IsNullOrWhiteSpace(raw):
                skillsPath = new SkillsPathEntry(raw.Trim(), Trusted: false);
                return true;
            case SettingsYamlSkillsPath value when !string.IsNullOrWhiteSpace(value.Path):
                skillsPath = new SkillsPathEntry(value.Path.Trim(), value.Trusted ?? false);
                return true;
            case IDictionary<object, object> map:
                return TryParseSkillsPathMap(map, out skillsPath);
            case IDictionary<string, object> stringMap:
                return TryParseSkillsPathMap(stringMap.ToDictionary(static pair => (object)pair.Key, static pair => pair.Value), out skillsPath);
        }

        skillsPath = new SkillsPathEntry(string.Empty);
        return false;
    }

    private static bool TryParseSkillsPathMap(IDictionary<object, object> map, out SkillsPathEntry skillsPath)
    {
        skillsPath = new SkillsPathEntry(string.Empty);

        string? path = null;
        var trusted = false;

        foreach (var pair in map)
        {
            var key = pair.Key?.ToString();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (string.Equals(key, "path", StringComparison.OrdinalIgnoreCase))
            {
                path = pair.Value?.ToString();
                continue;
            }

            if (string.Equals(key, "trusted", StringComparison.OrdinalIgnoreCase) && pair.Value is not null)
            {
                trusted = ParseTrustedValue(pair.Value);
            }
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        skillsPath = new SkillsPathEntry(path.Trim(), trusted);
        return true;
    }

    private static bool ParseTrustedValue(object value)
    {
        return value switch
        {
            bool flag => flag,
            string raw when bool.TryParse(raw, out var parsed) => parsed,
            _ => false,
        };
    }

    private static List<string> NormalizeList(params List<string>?[] candidates)
    {
        return candidates
            .Where(list => list is not null)
            .SelectMany(list => list!)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string>? ToList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return [value.Trim()];
    }

    private static string ToAgentKey(string displayName)
    {
        var chars = displayName
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();

        var key = new string(chars);
        while (key.Contains("--", StringComparison.Ordinal))
        {
            key = key.Replace("--", "-", StringComparison.Ordinal);
        }

        return key.Trim('-');
    }

    private static string ResolveFilePath(string? filePath)
    {
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            return Path.GetFullPath(filePath);
        }

        var envPath = Environment.GetEnvironmentVariable(SettingsEnvVar);
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            return Path.GetFullPath(envPath);
        }

        var currentDirectoryPath = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "Settings.yaml"));
        if (File.Exists(currentDirectoryPath))
        {
            return currentDirectoryPath;
        }

        var desktopDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (!string.IsNullOrWhiteSpace(desktopDirectory))
        {
            return Path.GetFullPath(Path.Combine(desktopDirectory, "Settings.yaml"));
        }

        return currentDirectoryPath;
    }

    private SettingsYamlDocument LoadDocumentOrDefault()
    {
        if (!File.Exists(FilePath))
        {
            return new SettingsYamlDocument();
        }

        var yaml = File.ReadAllText(FilePath, Encoding.UTF8);
        return Deserializer.Deserialize<SettingsYamlDocument>(yaml) ?? new SettingsYamlDocument();
    }
}
