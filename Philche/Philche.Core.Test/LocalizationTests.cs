using Philche.Core.Localization;

namespace Philche.Core.Test;

public sealed class LocalizationTests
{
    [Fact]
    public void LocaleResources_ShouldHaveKeyParityBetweenZhTwAndEnUs()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"philche-locale-{Guid.NewGuid():N}.txt");
        try
        {
            var localizer = new AppLocalizer(tempPath);
            var zhTw = localizer.GetLocaleMap("zh-TW").Keys.OrderBy(x => x).ToArray();
            var enUs = localizer.GetLocaleMap("en-US").Keys.OrderBy(x => x).ToArray();

            Assert.Equal(zhTw, enUs);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public void GetForLocale_WhenUnsupportedLocale_UsesFallbackBehavior()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"philche-locale-{Guid.NewGuid():N}.txt");
        try
        {
            var localizer = new AppLocalizer(tempPath);
            var text = localizer.GetForLocale("findings.header", "fr-FR");

            Assert.Equal(localizer.GetForLocale("findings.header", "zh-TW"), text);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public void SetLocale_ShouldPersistPreferenceAndLoadOnNextStartup()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"philche-locale-{Guid.NewGuid():N}.txt");
        try
        {
            var localizer = new AppLocalizer(tempPath);
            localizer.SetLocale("en-US");

            var next = new AppLocalizer(tempPath);
            Assert.Equal("en-US", next.CurrentLocale);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
