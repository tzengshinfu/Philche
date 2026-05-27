using Philche.Core.Config;

namespace Philche.Core.Test;

public sealed class WindowsShellContextMenuTests
{
    [Fact(DisplayName = "Windows Shell 內容選單測試：Get Default File Extensions Includes Expected Scannable Types")]
    public void GetDefaultFileExtensions_IncludesExpectedScannableTypes()
    {
        var extensions = WindowsShellContextMenu.GetDefaultFileExtensions();

        Assert.Contains(".md", extensions);
        Assert.Contains(".py", extensions);
        Assert.Contains(".js", extensions);
    }

    [Fact(DisplayName = "Windows Shell 內容選單測試：Normalize File Extensions For Test Trims Prefixes And Deduplicates")]
    public void NormalizeFileExtensionsForTest_TrimsPrefixesAndDeduplicates()
    {
        var extensions = WindowsShellContextMenu.NormalizeFileExtensionsForTest([
            " py ",
            ".md",
            " .MD ",
            "js",
            string.Empty,
            "  ",
        ]);

        Assert.Equal(3, extensions.Count);
        Assert.Equal(".py", extensions[0]);
        Assert.Equal(".md", extensions[1]);
        Assert.Equal(".js", extensions[2]);
    }

    [Fact(DisplayName = "Windows Shell 內容選單測試：Build File Menu Key Path For Test Returns Expected Registry Path")]
    public void BuildFileMenuKeyPathForTest_ReturnsExpectedRegistryPath()
    {
        var path = WindowsShellContextMenu.BuildFileMenuKeyPathForTest(".py");

        Assert.Equal(@"Software\Classes\SystemFileAssociations\.py\shell\Philche.ScanFile", path);
    }
}


