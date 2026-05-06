using Philche.Core.Config;

namespace Philche.Core.Test;

public sealed class SettingsChangeNotifierTests
{
    [Fact]
    public async Task RaisesSingleEvent_WhenFileChangesRapidly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"philche-settings-watch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var settingsPath = Path.Combine(tempDir, "Settings.yaml");
        File.WriteAllText(settingsPath, "version: 1\n", System.Text.Encoding.UTF8);

        try
        {
            var count = 0;
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            using var notifier = new SettingsChangeNotifier(settingsPath, TimeSpan.FromMilliseconds(200));
            notifier.SettingsChanged += () =>
            {
                Interlocked.Increment(ref count);
                tcs.TrySetResult(true);
            };

            File.AppendAllText(settingsPath, "#a\n", System.Text.Encoding.UTF8);
            await Task.Delay(50);
            File.AppendAllText(settingsPath, "#b\n", System.Text.Encoding.UTF8);
            await Task.Delay(50);
            File.AppendAllText(settingsPath, "#c\n", System.Text.Encoding.UTF8);

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(3000));
            Assert.Same(tcs.Task, completed);

            await Task.Delay(400);
            Assert.Equal(1, Volatile.Read(ref count));
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
    public void InitializationFailure_DisablesMonitoring_AndLogsWarning()
    {
        string? warning = null;
        var invalidPath = "\0invalid-path";

        using var notifier = new SettingsChangeNotifier(
            invalidPath,
            TimeSpan.FromMilliseconds(100),
            message => warning = message);

        Assert.False(notifier.IsMonitoring);
        Assert.False(string.IsNullOrWhiteSpace(warning));
    }
}
