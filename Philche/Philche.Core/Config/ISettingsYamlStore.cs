using Philche.Core.Discovery;

namespace Philche.Core.Config;

public interface ISettingsYamlStore
{
    string FilePath { get; }

    IReadOnlyList<KnownAgentCatalogEntry> LoadCatalog();

    ModelPathsConfig LoadModelPaths();

    ScanningConfig LoadScanningConfig();

    SchedulerConfig LoadSchedulerConfig();

    ShellContextMenuConfig LoadShellContextMenuConfig();

    void SaveModelPaths(ModelPathsConfig modelPaths);

    void SaveScanningConfig(ScanningConfig scanningConfig);

    void SaveSchedulerConfig(SchedulerConfig schedulerConfig);

    void SaveShellContextMenuConfig(ShellContextMenuConfig shellContextMenuConfig);

    void SaveCatalog(IReadOnlyList<KnownAgentCatalogEntry> entries);
}
