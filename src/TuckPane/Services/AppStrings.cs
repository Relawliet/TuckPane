using System.Globalization;
using System.Xml.Linq;
using TuckPane.Models;
using Microsoft.Windows.ApplicationModel.Resources;
using Microsoft.Windows.Globalization;

namespace TuckPane.Services;

public static class AppStrings
{
    private static ResourceLoader? _loader;
    private static IReadOnlyDictionary<string, string> _fallback = new Dictionary<string, string>();

    public static AppLanguage Language { get; private set; } = AppLanguage.English;
    public static int CharacterSpacing => Language == AppLanguage.English ? 0 : 30;
    public static string FontFamily => Language switch
    {
        AppLanguage.English => "Segoe UI Variable",
        AppLanguage.Japanese => "Yu Gothic UI",
        _ => "Microsoft YaHei UI"
    };

    public static void SetLanguage(AppLanguage language)
    {
        if (!Enum.IsDefined(language)) language = AppLanguage.English;
        Language = language;
        string tag = GetLanguageTag(language);
        ApplicationLanguages.PrimaryLanguageOverride = tag;
        CultureInfo culture = CultureInfo.GetCultureInfo(tag);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        try { _loader = new ResourceLoader(); }
        catch { _loader = null; }
        _fallback = LoadFallbackCatalog(tag);
    }

    public static string Get(string key)
    {
        try
        {
            _loader ??= new ResourceLoader();
            string value = _loader.GetString(key);
            if (!string.IsNullOrEmpty(value)) return value;
        }
        catch
        {
        }
        if (_fallback.Count == 0) _fallback = LoadFallbackCatalog(GetLanguageTag(Language));
        return _fallback.TryGetValue(key, out string? fallback) ? fallback : key;
    }

    public static string Format(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), arguments);

    public static string DefaultOrganizerName => Get("DefaultOrganizerName");

    public static string FormatItemCount(int count) => Format(count == 1 ? "ItemCountOne" : "ItemCountMany", count);

    public static string FormatDate(DateTimeOffset value) => value.LocalDateTime.ToString("g", CultureInfo.CurrentCulture);

    public static string GetLanguageTag(AppLanguage language) => language switch
    {
        AppLanguage.ChineseSimplified => "zh-CN",
        AppLanguage.Japanese => "ja-JP",
        _ => "en-US"
    };

    private static IReadOnlyDictionary<string, string> LoadFallbackCatalog(string languageTag)
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Strings", languageTag, "Resources.resw");
            if (!File.Exists(path)) return new Dictionary<string, string>();
            return XDocument.Load(path).Descendants("data")
                .Where(node => node.Attribute("name") is not null && node.Element("value") is not null)
                .ToDictionary(node => (string)node.Attribute("name")!, node => (string)node.Element("value")!, StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }
}
