using System.Collections.Concurrent;
using Philche.Core.Discovery;

namespace Philche.Core.Orchestration;

public sealed class SkillsFileWatcher : IDisposable
{
    private readonly TimeSpan debounceWindow;
    private readonly ConcurrentDictionary<string, HashSet<string>> pendingFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, System.Timers.Timer> debounceTimers = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FileSystemWatcher> watchers = [];
    private readonly object sync = new();
    private bool disposed;

    public event Action<string, IReadOnlyList<string>>? FilesChanged;

    public SkillsFileWatcher(IReadOnlyList<KnownAgentCatalogEntry> catalogEntries, TimeSpan? debounceWindow = null)
    {
        this.debounceWindow = debounceWindow ?? TimeSpan.FromSeconds(3);

        foreach (var entry in catalogEntries)
        {
            foreach (var skillsPathEntry in entry.SkillsPaths.Where(static path => !path.Trusted))
            {
                var path = Environment.ExpandEnvironmentVariables(skillsPathEntry.Path);
                if (!Directory.Exists(path))
                {
                    continue;
                }

                var watcher = new FileSystemWatcher(path)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size,
                    EnableRaisingEvents = true,
                };

                watcher.Created += (_, args) => OnFileCreatedOrChanged(entry.AgentKey, args.FullPath);
                watcher.Changed += (_, args) => OnFileCreatedOrChanged(entry.AgentKey, args.FullPath);
                watcher.Renamed += (_, args) => OnFileCreatedOrChanged(entry.AgentKey, args.FullPath);
                watcher.Deleted += (_, args) => OnFileDeleted(entry.AgentKey, args.FullPath);

                lock (sync)
                {
                    watchers.Add(watcher);
                }
            }
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        foreach (var timer in debounceTimers.Values)
        {
            timer.Stop();
            timer.Dispose();
        }

        lock (sync)
        {
            foreach (var watcher in watchers)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }

            watchers.Clear();
        }
    }

    private void OnFileCreatedOrChanged(string agentKey, string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return;
        }

        var set = pendingFiles.GetOrAdd(agentKey, static _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        lock (set)
        {
            set.Add(fullPath);
        }

        ResetDebounce(agentKey);
    }

    private void OnFileDeleted(string agentKey, string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return;
        }

        if (pendingFiles.TryGetValue(agentKey, out var set))
        {
            lock (set)
            {
                set.Remove(fullPath);
            }
        }

        ResetDebounce(agentKey);
    }

    private void ResetDebounce(string agentKey)
    {
        var timer = debounceTimers.GetOrAdd(agentKey, _ =>
        {
            var created = new System.Timers.Timer(Math.Max(1, debounceWindow.TotalMilliseconds))
            {
                AutoReset = false,
            };
            created.Elapsed += (_, _) => Flush(agentKey);
            return created;
        });

        timer.Stop();
        timer.Start();
    }

    private void Flush(string agentKey)
    {
        if (!pendingFiles.TryGetValue(agentKey, out var set))
        {
            return;
        }

        List<string> files;
        lock (set)
        {
            files = set.Where(File.Exists).ToList();
            set.Clear();
        }

        if (files.Count == 0)
        {
            return;
        }

        FilesChanged?.Invoke(agentKey, files);
    }
}
