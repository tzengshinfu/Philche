using System.Reflection;
using System.Text.Json;

namespace Philche.Core.Localization;

public sealed class AppLocalizer
{
    private static readonly string[] Supported = ["zh-TW", "en-US"];
    private const string DefaultLocale = "zh-TW";
    private const string FallbackLocale = "en-US";

    private readonly string localePreferencePath;
    private readonly IReadOnlyDictionary<string, string> zhTw;
    private readonly IReadOnlyDictionary<string, string> enUs;

    public AppLocalizer(string? localePreferencePath = null)
    {
        this.localePreferencePath = localePreferencePath ?? GetDefaultLocalePreferencePath();
        zhTw = LoadLocale("zh-TW");
        enUs = LoadLocale("en-US");

        CurrentLocale = ResolveInitialLocale();
    }

    public string CurrentLocale { get; private set; }

    public static IReadOnlyList<string> SupportedLocales => Supported;

    public string Get(string key)
    {
        return GetForLocale(key, CurrentLocale);
    }

    public string GetForLocale(string key, string locale)
    {
        var active = NormalizeLocale(locale);
        var activeMap = active.Equals("zh-TW", StringComparison.OrdinalIgnoreCase) ? zhTw : enUs;

        if (activeMap.TryGetValue(key, out var value))
        {
            return value;
        }

        if (enUs.TryGetValue(key, out var fallbackValue))
        {
            return fallbackValue;
        }

        return key;
    }

    public void SetLocale(string locale)
    {
        CurrentLocale = NormalizeLocale(locale);

        var directory = Path.GetDirectoryName(localePreferencePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(localePreferencePath, CurrentLocale);
    }

    public IReadOnlyDictionary<string, string> GetLocaleMap(string locale)
    {
        var normalized = NormalizeLocale(locale);
        return normalized.Equals("zh-TW", StringComparison.OrdinalIgnoreCase) ? zhTw : enUs;
    }

    private string ResolveInitialLocale()
    {
        if (!File.Exists(localePreferencePath))
        {
            return DefaultLocale;
        }

        var value = File.ReadAllText(localePreferencePath).Trim();
        return NormalizeLocale(value);
    }

    private static string NormalizeLocale(string locale)
    {
        if (Supported.Any(x => x.Equals(locale, StringComparison.OrdinalIgnoreCase)))
        {
            return Supported.First(x => x.Equals(locale, StringComparison.OrdinalIgnoreCase));
        }

        return DefaultLocale;
    }

    private static string GetDefaultLocalePreferencePath()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Philche");
        return Path.Combine(root, "ui-locale.txt");
    }

    private static IReadOnlyDictionary<string, string> LoadLocale(string locale)
    {
        var assembly = typeof(AppLocalizer).Assembly;
        var resourceName = $"Philche.Core.Localization.Resources.{locale}.json";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing localization resource: {resourceName}");
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();

        var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
            ?? throw new InvalidOperationException($"Invalid localization JSON for {locale}");

        return map;
    }
}
