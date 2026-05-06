using System.Text;

namespace Philche.Tray;

internal static class ScanQueueIpc
{
    private const string SignalName = "Philche.Tray.ScanRequest";
    private const string QueueMutexName = "Philche.Tray.ScanQueue";
    private static readonly Lock QueueLock = new();
    private static EventWaitHandle? scanSignal;
    private static RegisteredWaitHandle? scanWaitHandle;

    public static void EnqueuePaths(IReadOnlyList<string> paths)
    {
        var normalized = paths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Count == 0)
        {
            return;
        }

        using var mutex = new Mutex(false, QueueMutexName);
        mutex.WaitOne();
        try
        {
            var queueFilePath = GetQueueFilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(queueFilePath)!);
            lock (QueueLock)
            {
                File.AppendAllLines(queueFilePath, normalized, Encoding.UTF8);
            }
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    public static IReadOnlyList<string> DrainPaths()
    {
        using var mutex = new Mutex(false, QueueMutexName);
        mutex.WaitOne();
        try
        {
            var queueFilePath = GetQueueFilePath();
            if (!File.Exists(queueFilePath))
            {
                return [];
            }

            lock (QueueLock)
            {
                var paths = File.ReadAllLines(queueFilePath, Encoding.UTF8)
                    .Where(static path => !string.IsNullOrWhiteSpace(path))
                    .Select(static path => path.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                File.Delete(queueFilePath);
                return paths;
            }
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    public static void Signal()
    {
        try
        {
            using var signal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName);
            signal.Set();
        }
        catch
        {
        }
    }

    public static void RegisterListener(Action<IReadOnlyList<string>> onPaths)
    {
        scanSignal ??= new EventWaitHandle(false, EventResetMode.AutoReset, SignalName);
        scanWaitHandle ??= ThreadPool.RegisterWaitForSingleObject(
            scanSignal,
            (_, _) =>
            {
                var queuedPaths = DrainPaths();
                if (queuedPaths.Count > 0)
                {
                    onPaths(queuedPaths);
                }
            },
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    private static string GetQueueFilePath()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Philche");
        return Path.Combine(root, "scan-queue.txt");
    }
}