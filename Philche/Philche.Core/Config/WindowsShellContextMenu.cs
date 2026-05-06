using Microsoft.Win32;

namespace Philche.Core.Config;

public static class WindowsShellContextMenu
{
    private const string DirectoryMenuKeyPath = @"Software\Classes\Directory\shell\Philche.ScanDirectory";
    private const string MenuLabel = "Scan by Philche";
    private static readonly string[] DefaultFileExtensions =
    [
        ".py", ".js", ".ts", ".jsx", ".tsx",
        ".sh", ".bash", ".ps1", ".psm1",
        ".cs", ".java", ".go", ".rb", ".rs",
        ".yaml", ".yml", ".json", ".toml", ".md",
    ];

    public static IReadOnlyList<string> GetDefaultFileExtensions() => DefaultFileExtensions;

    internal static IReadOnlyList<string> NormalizeFileExtensionsForTest(IEnumerable<string>? fileExtensions) => NormalizeFileExtensions(fileExtensions);

    internal static string BuildFileMenuKeyPathForTest(string fileExtension) => BuildFileMenuKeyPath(fileExtension);

    public static bool IsFileContextMenuRegistered(IEnumerable<string>? fileExtensions = null) =>
        NormalizeFileExtensions(fileExtensions).All(IsFileExtensionRegistered);

    public static bool IsDirectoryContextMenuRegistered() => IsRegistered(DirectoryMenuKeyPath);

    public static void RegisterFileContextMenu(string command, string? iconPath, IEnumerable<string>? fileExtensions = null)
    {
        foreach (var fileExtension in NormalizeFileExtensions(fileExtensions))
        {
            Register(BuildFileMenuKeyPath(fileExtension), command, iconPath);
        }
    }

    public static void RegisterDirectoryContextMenu(string command, string? iconPath)
    {
        Register(DirectoryMenuKeyPath, command, iconPath);
    }

    public static void UnregisterFileContextMenu(IEnumerable<string>? fileExtensions = null)
    {
        foreach (var fileExtension in NormalizeFileExtensions(fileExtensions))
        {
            Unregister(BuildFileMenuKeyPath(fileExtension));
        }
    }

    public static void UnregisterDirectoryContextMenu()
    {
        Unregister(DirectoryMenuKeyPath);
    }

    private static void Register(string keyPath, string command, string? iconPath)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(keyPath);
        if (key is null)
        {
            return;
        }

        key.SetValue("", MenuLabel);
        if (!string.IsNullOrWhiteSpace(iconPath))
        {
            key.SetValue("Icon", iconPath);
        }

        using var commandKey = key.CreateSubKey("command");
        commandKey?.SetValue("", command);
    }

    private static void Unregister(string keyPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
        }
        catch
        {
        }
    }

    private static bool IsRegistered(string keyPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key is not null;
    }

    private static bool IsFileExtensionRegistered(string fileExtension)
    {
        return IsRegistered(BuildFileMenuKeyPath(fileExtension));
    }

    private static string BuildFileMenuKeyPath(string fileExtension)
    {
        return $@"Software\Classes\SystemFileAssociations\{fileExtension}\shell\Philche.ScanFile";
    }

    private static IReadOnlyList<string> NormalizeFileExtensions(IEnumerable<string>? fileExtensions)
    {
        var candidates = fileExtensions ?? DefaultFileExtensions;
        return candidates
            .Where(static ext => !string.IsNullOrWhiteSpace(ext))
            .Select(static ext =>
            {
                var normalized = ext.Trim();
                return normalized.StartsWith('.') ? normalized : $".{normalized}";
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}