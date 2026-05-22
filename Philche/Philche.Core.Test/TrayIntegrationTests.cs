using System.Collections.Concurrent;
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

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"philche-tray-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}