using System.Collections.Concurrent;
using Philche.Core.Config;
using Philche.Tray;

namespace Philche.Core.Test;

public sealed class TrayIntegrationTests
{
    [Fact]
    public void IsCliMode_ReturnsTrue_WhenCliFlagPresent()
    {
        Assert.True(Program.IsCliMode(["philche.exe", "--cli", "--scan", @"C:\temp\a.py"]));
    }

    [Fact]
    public void IsHelpMode_ReturnsTrue_WhenHelpFlagPresent()
    {
        Assert.True(Program.IsHelpMode(["philche.exe", "--help"]));
        Assert.True(Program.IsHelpMode(["philche.exe", "-h"]));
    }

    [Fact]
    public void ExtractFormat_DefaultsToText_WhenFormatFlagMissing()
    {
        Assert.Equal("text", Program.ExtractFormat(["philche.exe", "--cli", "--scan", @"C:\temp\a.py"]));
    }

    [Fact]
    public void ExtractFormat_ReturnsJson_WhenJsonFormatSpecified()
    {
        Assert.Equal("json", Program.ExtractFormat(["philche.exe", "--cli", "--scan", @"C:\temp\a.py", "--format", "json"]));
    }

    [Fact]
    public void ExtractScanPaths_ReturnsDistinctTrimmedPathsAfterScanFlag()
    {
        var paths = Program.ExtractScanPaths(
        [
            "philche.exe",
            "--scan",
            "  C:\\temp\\a.py  ",
            "C:\\temp\\b.md",
            "C:\\temp\\a.py",
        ]);

        Assert.Equal(2, paths.Count);
        Assert.Equal(@"C:\temp\a.py", paths[0]);
        Assert.Equal(@"C:\temp\b.md", paths[1]);
    }

    [Fact]
    public void ExtractScanPaths_StopsAtNextFlag()
    {
        var paths = Program.ExtractScanPaths(
        [
            "philche.exe",
            "--cli",
            "--scan",
            @"\\wsl.localhost\Ubuntu-22.04\home\y1938\.openclaw\workspace\skills",
            "--format",
            "json",
        ]);

        Assert.Single(paths);
        Assert.Equal(@"\\wsl.localhost\Ubuntu-22.04\home\y1938\.openclaw\workspace\skills", paths[0]);
    }

    [Fact]
    public void ExtractScanPaths_ReturnsEmpty_WhenScanFlagMissing()
    {
        var paths = Program.ExtractScanPaths(["philche.exe", @"C:\temp\a.py"]);

        Assert.Empty(paths);
    }

    [Fact]
    public void ExtractCliScanOptions_ParsesMethodFlagsAndApiKey()
    {
        var options = Program.ExtractCliScanOptions(
        [
            "philche.exe",
            "--cli",
            "--scan",
            @"C:\temp\a.py",
            "--semantic",
            "--virustotal",
            "--gguf",
            "--all",
            "--virustotal-api-key",
            "vt-key",
        ]);

        Assert.True(options.EnableSemantic);
        Assert.True(options.EnableVirusTotal);
        Assert.True(options.EnableGguf);
        Assert.True(options.EnableAll);
        Assert.Equal("vt-key", options.VirusTotalApiKey);
    }

    [Fact]
    public void CliRunner_BuildHelpText_ListsNewFlags()
    {
        var help = CliRunner.BuildHelpText();

        Assert.Contains("--semantic", help, StringComparison.Ordinal);
        Assert.Contains("--gguf", help, StringComparison.Ordinal);
        Assert.Contains("--virustotal", help, StringComparison.Ordinal);
        Assert.Contains("--all", help, StringComparison.Ordinal);
        Assert.Contains("--virustotal-api-key", help, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildLaunchCommand_IncludesQuotedScanArguments()
    {
        var command = Program.BuildLaunchCommand("--scan", "%1");

        Assert.False(string.IsNullOrWhiteSpace(command));
        Assert.Contains("\"--scan\"", command, StringComparison.Ordinal);
        Assert.Contains("\"%1\"", command, StringComparison.Ordinal);
    }

    [Fact]
    public void ScanQueueIpc_EnqueueAndDrainPaths_RoundTripsDistinctValues()
    {
        ScanQueueIpc.DrainPaths();

        try
        {
            ScanQueueIpc.EnqueuePaths([@"C:\temp\a.py", @"C:\temp\b.md", @"C:\temp\a.py"]);

            var drained = ScanQueueIpc.DrainPaths();

            Assert.Equal(2, drained.Count);
            Assert.Contains(@"C:\temp\a.py", drained);
            Assert.Contains(@"C:\temp\b.md", drained);
            Assert.Empty(ScanQueueIpc.DrainPaths());
        }
        finally
        {
            ScanQueueIpc.DrainPaths();
        }
    }

    [Fact]
    public async Task ScanQueueIpc_RegisterListener_ReceivesQueuedPathsAfterSignal()
    {
        ScanQueueIpc.DrainPaths();
        var received = new ConcurrentQueue<IReadOnlyList<string>>();
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        ScanQueueIpc.RegisterListener(paths =>
        {
            received.Enqueue(paths);
            signal.TrySetResult();
        });

        try
        {
            ScanQueueIpc.EnqueuePaths([@"C:\temp\listener.py", @"C:\temp\listener.md"]);
            ScanQueueIpc.Signal();

            var completed = await Task.WhenAny(signal.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.Same(signal.Task, completed);

            Assert.True(received.TryDequeue(out var paths));
            Assert.Contains(@"C:\temp\listener.py", paths);
            Assert.Contains(@"C:\temp\listener.md", paths);
        }
        finally
        {
            ScanQueueIpc.DrainPaths();
        }
    }

    [Fact]
    public void ResolveScannableFiles_ExpandsDirectories_AndIncludesMarkdownFiles()
    {
        var tempDir = CreateTempDirectory();

        try
        {
            var nestedDir = Path.Combine(tempDir, "skills");
            Directory.CreateDirectory(nestedDir);

            var markdownFile = Path.Combine(nestedDir, "SKILL.md");
            var codeFile = Path.Combine(nestedDir, "script.py");
            var ignoredFile = Path.Combine(nestedDir, "image.png");

            File.WriteAllText(markdownFile, "safe prompt content");
            File.WriteAllText(codeFile, "print('hello')");
            File.WriteAllText(ignoredFile, "not scannable");

            var files = CliRunner.ResolveScannableFiles([tempDir], out var errors);

            Assert.Empty(errors);
            Assert.Equal(2, files.Count);
            Assert.Contains(markdownFile, files);
            Assert.Contains(codeFile, files);
            Assert.DoesNotContain(ignoredFile, files);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task CliRunner_RunAsync_ScansCleanMarkdownFile_AndReturnsZero()
    {
        var tempDir = CreateTempDirectory();

        try
        {
            var markdownFile = Path.Combine(tempDir, "SKILL.md");
            File.WriteAllText(markdownFile, "Summarize this guide in one sentence.");

            var exitCode = await CliRunner.RunAsync([markdownFile], "text");

            Assert.Equal(0, exitCode);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task CliRunner_RunAsync_ReturnsThree_WhenVirusTotalRequestedWithoutApiKey()
    {
        var tempDir = CreateTempDirectory();

        try
        {
            var markdownFile = Path.Combine(tempDir, "SKILL.md");
            File.WriteAllText(markdownFile, "visit https://example.test");

            var output = new StringWriter();
            var error = new StringWriter();

            var exitCode = await CliRunner.RunAsync(
                [markdownFile],
                "text",
                new CliRunner.CliScanOptions(false, true, false, false, false, false, false, string.Empty),
                new FakeSettingsStore(),
                new FakeModelDownloader(tempDir),
                new StringReader(string.Empty),
                output,
                error);

            Assert.Equal(3, exitCode);
            Assert.Contains("virustotal.com/gui/sign-in", error.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task CliRunner_RunAsync_DownloadsGuardModel_WhenRequestedAndConfirmed()
    {
        var tempDir = CreateTempDirectory();

        try
        {
            var markdownFile = Path.Combine(tempDir, "SKILL.md");
            File.WriteAllText(markdownFile, "Summarize this guide in one sentence.");

            var settingsStore = new FakeSettingsStore
            {
                ModelPaths = new ModelPathsConfig
                {
                    ModelName = HuggingFaceGuardModelLocator.DefaultModelName,
                    GuardModelPath = Path.Combine(tempDir, "missing.gguf"),
                },
                Scanning = new ScanningConfig
                {
                    EnableGuardModelScan = false,
                    EnableLlmIntentRecognition = false,
                },
            };

            var output = new StringWriter();
            var error = new StringWriter();
            var downloader = new FakeModelDownloader(tempDir);

            var exitCode = await CliRunner.RunAsync(
                [markdownFile],
                "text",
                new CliRunner.CliScanOptions(false, false, true, false, false, false, false, string.Empty),
                settingsStore,
                downloader,
                new StringReader("y" + Environment.NewLine),
                output,
                error);

            Assert.Equal(0, exitCode);
            Assert.True(downloader.DownloadCalled);
            Assert.True(File.Exists(settingsStore.ModelPaths.GuardModelPath));
            Assert.Contains("GGUF download completed", output.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"philche-tray-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeSettingsStore : ISettingsYamlStore
    {
        public string FilePath => Path.Combine(Path.GetTempPath(), "Philche", "Settings.yaml");

        public ModelPathsConfig ModelPaths { get; set; } = new();
        public ScanningConfig Scanning { get; set; } = new();

        public IReadOnlyList<Philche.Core.Discovery.KnownAgentCatalogEntry> LoadCatalog() => [];

        public ModelPathsConfig LoadModelPaths() => ModelPaths;

        public ScanningConfig LoadScanningConfig() => Scanning;

        public SchedulerConfig LoadSchedulerConfig() => new();

        public ShellContextMenuConfig LoadShellContextMenuConfig() => new();

        public void SaveModelPaths(ModelPathsConfig modelPaths)
        {
            ModelPaths = modelPaths;
        }

        public void SaveScanningConfig(ScanningConfig scanningConfig)
        {
            Scanning = scanningConfig;
        }

        public void SaveSchedulerConfig(SchedulerConfig schedulerConfig)
        {
        }

        public void SaveShellContextMenuConfig(ShellContextMenuConfig shellContextMenuConfig)
        {
        }

        public void SaveCatalog(IReadOnlyList<Philche.Core.Discovery.KnownAgentCatalogEntry> entries)
        {
        }
    }

    private sealed class FakeModelDownloader(string rootDir) : IModelDownloader
    {
        public bool DownloadCalled { get; private set; }

        public Task<string> DownloadAsync(
            Uri sourceUrl,
            string targetDir,
            string expectedSha256,
            IProgress<(long downloaded, long total)>? progress = null,
            CancellationToken cancellationToken = default)
        {
            DownloadCalled = true;
            Directory.CreateDirectory(targetDir);
            progress?.Report((50, 100));
            progress?.Report((100, 100));

            var filePath = Path.Combine(targetDir, Path.GetFileName(sourceUrl.LocalPath));
            File.WriteAllText(filePath, $"fake gguf from {rootDir}");
            return Task.FromResult(filePath);
        }
    }
}