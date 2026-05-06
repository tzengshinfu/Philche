namespace Philche.Core.Config;

public sealed class SettingsChangeNotifier : IDisposable
{
    private readonly object syncLock = new();
    private readonly FileSystemWatcher? watcher;
    private readonly System.Timers.Timer debounceTimer;
    private bool disposed;

    public event Action? SettingsChanged;

    public bool IsMonitoring { get; }

    public SettingsChangeNotifier(
        string yamlFilePath,
        TimeSpan? debounceWindow = null,
        Action<string>? warningLogger = null)
    {
        warningLogger ??= static message => Console.Error.WriteLine(message);

        var window = debounceWindow ?? TimeSpan.FromMilliseconds(500);
        debounceTimer = new System.Timers.Timer(Math.Max(1, window.TotalMilliseconds))
        {
            AutoReset = false,
        };
        debounceTimer.Elapsed += (_, _) => SettingsChanged?.Invoke();

        try
        {
            if (string.IsNullOrWhiteSpace(yamlFilePath))
            {
                throw new InvalidOperationException("settings.yaml path is empty");
            }

            var fullPath = Path.GetFullPath(yamlFilePath);
            var directory = Path.GetDirectoryName(fullPath);
            var fileName = Path.GetFileName(fullPath);
            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
            {
                throw new InvalidOperationException($"invalid settings path: {yamlFilePath}");
            }

            Directory.CreateDirectory(directory);

            watcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };

            watcher.Changed += OnFileChanged;
            watcher.Created += OnFileChanged;
            watcher.Renamed += OnFileChanged;
            watcher.Deleted += OnFileChanged;

            IsMonitoring = true;
        }
        catch (Exception ex)
        {
            warningLogger($"[SettingsChangeNotifier] monitoring disabled: {ex.Message}");
            IsMonitoring = false;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        lock (syncLock)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (watcher is not null)
            {
                watcher.Changed -= OnFileChanged;
                watcher.Created -= OnFileChanged;
                watcher.Renamed -= OnFileChanged;
                watcher.Deleted -= OnFileChanged;
                watcher.Dispose();
            }

            debounceTimer.Stop();
            debounceTimer.Dispose();
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (disposed)
        {
            return;
        }

        lock (syncLock)
        {
            if (disposed)
            {
                return;
            }

            debounceTimer.Stop();
            debounceTimer.Start();
        }
    }
}
