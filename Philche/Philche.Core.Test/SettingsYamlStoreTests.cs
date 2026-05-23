using System.Text;
using Philche.Core.Config;
using Philche.Core.Discovery;
using Philche.Core.Domain.Models;

namespace Philche.Core.Test;

public sealed class SettingsYamlStoreTests
{
    [Fact]
    public void LoadCatalog_WhenFileMissing_BootstrapsDefaultCatalog()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"philche-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var settingsPath = Path.Combine(tempDir, "Settings.yaml");

        try
        {
            var store = new SettingsYamlStore(settingsPath);
            var entries = store.LoadCatalog();

            Assert.NotEmpty(entries);
            Assert.True(File.Exists(settingsPath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void SaveCatalog_RoundTripsCustomEntries()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"philche-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var settingsPath = Path.Combine(tempDir, "Settings.yaml");

        try
        {
            var store = new SettingsYamlStore(settingsPath);
            store.SaveCatalog([
                new KnownAgentCatalogEntry
                {
                    AgentKey = "custom-agent",
                    DisplayName = "Custom Agent",
                    HostExecutablePaths = [@"C:\tools\custom.exe"],
                    ExecutableNames = ["custom.exe"],
                    SkillsPaths = [new SkillsPathEntry(@"C:\tools\skills")],
                }
            ]);

            var entry = Assert.Single(store.LoadCatalog());
            Assert.Equal("custom-agent", entry.AgentKey);
            Assert.Equal("Custom Agent", entry.DisplayName);
            Assert.Equal(@"C:\tools\custom.exe", Assert.Single(entry.HostExecutablePaths));
            Assert.Equal("custom.exe", Assert.Single(entry.ExecutableNames));
            var skillsPath = Assert.Single(entry.SkillsPaths);
            Assert.Equal(@"C:\tools\skills", skillsPath.Path);
            Assert.False(skillsPath.Trusted);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void SaveCatalog_PreservesExistingModelPaths()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"philche-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var settingsPath = Path.Combine(tempDir, "Settings.yaml");

        try
        {
            var store = new SettingsYamlStore(settingsPath);
            store.SaveModelPaths(new ModelPathsConfig
            {
                ModelName = "Llama-Guard-3-8B-Q4_K_M-GGUF",
                GuardModelPath = @"C:\models\guard.gguf",
                CveSummaryModelPath = @"C:\models\cve.gguf",
            });

            store.SaveCatalog([
                new KnownAgentCatalogEntry
                {
                    AgentKey = "custom-agent",
                    DisplayName = "Custom Agent",
                    HostExecutablePaths = [@"C:\tools\custom.exe"],
                    ExecutableNames = ["custom.exe"],
                    SkillsPaths = [new SkillsPathEntry(@"C:\tools\skills")],
                }
            ]);

            var loaded = store.LoadModelPaths();
            Assert.Equal("Llama-Guard-3-8B-Q4_K_M-GGUF", loaded.ModelName);
            Assert.Equal(@"C:\models\guard.gguf", loaded.GuardModelPath);
            Assert.Equal(@"C:\models\cve.gguf", loaded.CveSummaryModelPath);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void LoadModelPaths_UsesDefaultModelNameWhenMissing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"philche-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var settingsPath = Path.Combine(tempDir, "Settings.yaml");

        try
        {
            var store = new SettingsYamlStore(settingsPath);

            var loaded = store.LoadModelPaths();

            Assert.Equal(HuggingFaceGuardModelLocator.DefaultModelName, loaded.ModelName);
            Assert.Equal(string.Empty, loaded.GuardModelPath);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void LoadAndSaveScanningConfig_RoundTripsToggleFlags()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"philche-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var settingsPath = Path.Combine(tempDir, "Settings.yaml");

        try
        {
            var store = new SettingsYamlStore(settingsPath);
            store.SaveScanningConfig(new ScanningConfig
            {
                CodeFileExtensions = [".cs", ".py"],
                VirusTotalApiKey = "vt-key",
                EnableSemanticScan = true,
                EnableYaraScan = false,
                EnableGuardModelScan = true,
                EnableMaliciousWordGroupList = false,
                EnableInvisibleCharacterDetection = false,
                EnableLlmIntentRecognition = true,
                EnableRegexScan = false,
                EnableVirusTotalSkillUrlScan = true,
                EnableVirusTotalScriptUrlScan = true,
                EnableCveCorrelation = true,
            });

            var loaded = store.LoadScanningConfig();
            Assert.Equal(2, loaded.CodeFileExtensions.Count);
            Assert.Equal("vt-key", loaded.VirusTotalApiKey);
            Assert.True(loaded.EnableSemanticScan);
            Assert.False(loaded.EnableYaraScan);
            Assert.True(loaded.EnableGuardModelScan);
            Assert.False(loaded.EnableMaliciousWordGroupList);
            Assert.False(loaded.EnableInvisibleCharacterDetection);
            Assert.True(loaded.EnableLlmIntentRecognition);
            Assert.False(loaded.EnableRegexScan);
            Assert.True(loaded.EnableVirusTotalSkillUrlScan);
            Assert.True(loaded.EnableVirusTotalScriptUrlScan);
            Assert.True(loaded.EnableCveCorrelation);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void LoadScanningConfig_UsesVirusTotalDefaultsWhenMissing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"philche-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var settingsPath = Path.Combine(tempDir, "Settings.yaml");

        try
        {
            var store = new SettingsYamlStore(settingsPath);

            var loaded = store.LoadScanningConfig();

            Assert.Equal(string.Empty, loaded.VirusTotalApiKey);
            Assert.False(loaded.EnableSemanticScan);
            Assert.False(loaded.EnableVirusTotalSkillUrlScan);
            Assert.False(loaded.EnableVirusTotalScriptUrlScan);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void LoadSchedulerConfig_UsesDefaultsWhenMissing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"philche-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var settingsPath = Path.Combine(tempDir, "Settings.yaml");

        try
        {
            var store = new SettingsYamlStore(settingsPath);
            var scheduler = store.LoadSchedulerConfig();

            Assert.Equal(4, scheduler.IntervalHours);
            Assert.False(scheduler.IsPaused);
            Assert.False(scheduler.ScanOnStartup);
            Assert.True(scheduler.PeriodicScanEnabled);
            Assert.True(scheduler.RealtimeScanEnabled);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void SaveSchedulerConfig_RoundTripsValues()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"philche-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var settingsPath = Path.Combine(tempDir, "Settings.yaml");

        try
        {
            var store = new SettingsYamlStore(settingsPath);
            store.SaveSchedulerConfig(new SchedulerConfig
            {
                IntervalHours = 8,
                IsPaused = true,
                ScanOnStartup = true,
                PeriodicScanEnabled = false,
                RealtimeScanEnabled = false,
            });

            var scheduler = store.LoadSchedulerConfig();
            Assert.Equal(8, scheduler.IntervalHours);
            Assert.True(scheduler.IsPaused);
            Assert.True(scheduler.ScanOnStartup);
            Assert.False(scheduler.PeriodicScanEnabled);
            Assert.False(scheduler.RealtimeScanEnabled);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void LoadCatalog_AcceptsLegacySkillsPathStringList()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"philche-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var settingsPath = Path.Combine(tempDir, "Settings.yaml");

        try
        {
            var yaml = """
                version: 1
                agents:
                  - agentKey: custom-agent
                    displayName: Custom Agent
                    hostExecutablePaths:
                      - C:\tools\custom.exe
                    executableNames:
                      - custom.exe
                    skillsPaths:
                      - C:\tools\skills
                """;

            File.WriteAllText(settingsPath, yaml, Encoding.UTF8);

            var entry = Assert.Single(new SettingsYamlStore(settingsPath).LoadCatalog());
            var skillsPath = Assert.Single(entry.SkillsPaths);
            Assert.Equal(@"C:\tools\skills", skillsPath.Path);
            Assert.False(skillsPath.Trusted);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void LoadCatalog_AcceptsStructuredSkillsPathEntries()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"philche-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var settingsPath = Path.Combine(tempDir, "Settings.yaml");

        try
        {
            var yaml = """
                version: 1
                agents:
                  - agentKey: openclaw
                    displayName: OpenClaw
                    hostExecutablePaths:
                      - C:\tools\openclaw.exe
                    executableNames:
                      - openclaw.exe
                    skillsPaths:
                      - path: C:\tools\trusted-skills
                        trusted: true
                      - path: C:\tools\custom-skills
                        trusted: false
                """;

            File.WriteAllText(settingsPath, yaml, Encoding.UTF8);

            var entry = Assert.Single(new SettingsYamlStore(settingsPath).LoadCatalog());
            Assert.Collection(
                entry.SkillsPaths,
                first =>
                {
                    Assert.Equal(@"C:\tools\trusted-skills", first.Path);
                    Assert.True(first.Trusted);
                },
                second =>
                {
                    Assert.Equal(@"C:\tools\custom-skills", second.Path);
                    Assert.False(second.Trusted);
                });
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void SaveCatalog_RoundTripsTrustedSkillsPathEntries()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"philche-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var settingsPath = Path.Combine(tempDir, "Settings.yaml");

        try
        {
            var store = new SettingsYamlStore(settingsPath);
            store.SaveCatalog([
                new KnownAgentCatalogEntry
                {
                    AgentKey = "openclaw",
                    DisplayName = "OpenClaw",
                    HostExecutablePaths = [@"C:\tools\openclaw.exe"],
                    ExecutableNames = ["openclaw.exe"],
                    WslUserRelativePath = ".openclaw/workspace/skills",
                    SkillsPaths =
                    [
                        new SkillsPathEntry(@"C:\tools\trusted-skills", Trusted: true),
                        new SkillsPathEntry(@"C:\tools\custom-skills", Trusted: false),
                    ],
                }
            ]);

            var reloaded = Assert.Single(store.LoadCatalog());
            Assert.Equal(".openclaw/workspace/skills", reloaded.WslUserRelativePath);
            Assert.Collection(
                reloaded.SkillsPaths,
                first =>
                {
                    Assert.Equal(@"C:\tools\trusted-skills", first.Path);
                    Assert.True(first.Trusted);
                },
                second =>
                {
                    Assert.Equal(@"C:\tools\custom-skills", second.Path);
                    Assert.False(second.Trusted);
                });
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void SaveShellContextMenuConfig_RoundTripsValues()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"philche-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var settingsPath = Path.Combine(tempDir, "Settings.yaml");

        try
        {
            var store = new SettingsYamlStore(settingsPath);
            store.SaveShellContextMenuConfig(new ShellContextMenuConfig
            {
                FileContextMenuEnabled = true,
                DirectoryContextMenuEnabled = false,
            });

            var loaded = store.LoadShellContextMenuConfig();
            Assert.True(loaded.FileContextMenuEnabled);
            Assert.False(loaded.DirectoryContextMenuEnabled);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
