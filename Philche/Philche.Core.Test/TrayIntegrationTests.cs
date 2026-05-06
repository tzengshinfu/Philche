using System.Collections.Concurrent;
using Philche.Tray;

namespace Philche.Core.Test;

public sealed class TrayIntegrationTests
{
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
}