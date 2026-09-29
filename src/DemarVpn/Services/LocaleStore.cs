using System.Globalization;

namespace DemarVpn.Services;

/// <summary>App UI language. First launch follows OS if supported; else en. Currency: ru→RUB, else EUR.</summary>
public static class LocaleStore
{
    public const string En = "en";
    public const string Ru = "ru";
    public const string Uk = "uk";
    public const string Tr = "tr";
    public const string Uz = "uz";
    public const string ZhHans = "zh-Hans";
    public const string ZhHant = "zh-Hant";
    public const string Ko = "ko";
    public const string Ja = "ja";
    public const string Es = "es";
    public const string De = "de";
    public const string Fr = "fr";
    public const string Hi = "hi";

    public static readonly string[] Supported =
        [En, Ru, Uk, Tr, Uz, ZhHans, ZhHant, Ko, Ja, Es, De, Fr, Hi];

    static readonly Dictionary<string, (string Culture, string Name)> Meta = new(StringComparer.OrdinalIgnoreCase)
    {
        [En] = ("en-US", "English"),
        [Ru] = ("ru-RU", "Русский"),
        [Uk] = ("uk-UA", "Українська"),
        [Tr] = ("tr-TR", "Türkçe"),
        [Uz] = ("uz-Latn-UZ", "Oʻzbekcha"),
        [ZhHans] = ("zh-CN", "简体中文"),
        [ZhHant] = ("zh-TW", "繁體中文"),
        [Ko] = ("ko-KR", "한국어"),
        [Ja] = ("ja-JP", "日本語"),
        [Es] = ("es-ES", "Español"),
        [De] = ("de-DE", "Deutsch"),
        [Fr] = ("fr-FR", "Français"),
        [Hi] = ("hi-IN", "हिन्दी"),
    };

    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DemarVpn", "locale");

    static string _tag = En;

    public static string Tag => _tag;
    public static int Index => Array.IndexOf(Supported, _tag) is >= 0 and var i ? i : 0;
    public static bool IsRu => _tag == Ru;
    public static bool UsesSlavicPlurals => _tag is Ru or Uk;
    public static string DisplayName => Meta[_tag].Name;
    public static string DefaultCurrency => IsRu ? "RUB" : "EUR";

    public static event Action? Changed;

    /// <summary>Canonical tag from saved value or OS culture name (e.g. zh-CN → zh-Hans).</summary>
    public static string? Match(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim().Replace('_', '-');

        foreach (var tag in Supported)
            if (string.Equals(tag, s, StringComparison.OrdinalIgnoreCase))
                return tag;

        if (IsZhHans(s)) return ZhHans;
        if (IsZhHant(s)) return ZhHant;
        if (string.Equals(s, "zh", StringComparison.OrdinalIgnoreCase)) return ZhHans;

        try
        {
            for (var c = CultureInfo.GetCultureInfo(s); !string.IsNullOrEmpty(c.Name); c = c.Parent)
            {
                if (IsZhHans(c.Name)) return ZhHans;
                if (IsZhHant(c.Name)) return ZhHant;
                foreach (var tag in Supported)
                {
                    if (string.Equals(tag, c.Name, StringComparison.OrdinalIgnoreCase)) return tag;
                    if (tag.Length == 2 &&
                        string.Equals(tag, c.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase))
                        return tag;
                }
            }
        }
        catch (CultureNotFoundException) { /* ignore */ }

        if (s.Length >= 2)
        {
            var two = s[..2];
            foreach (var tag in Supported)
                if (tag.Length == 2 && string.Equals(tag, two, StringComparison.OrdinalIgnoreCase))
                    return tag;
        }
        return null;
    }

    static bool IsZhHans(string n) =>
        n.StartsWith("zh-Hans", StringComparison.OrdinalIgnoreCase) ||
        n.Equals("zh-CN", StringComparison.OrdinalIgnoreCase) ||
        n.Equals("zh-SG", StringComparison.OrdinalIgnoreCase);

    static bool IsZhHant(string n) =>
        n.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase) ||
        n.Equals("zh-TW", StringComparison.OrdinalIgnoreCase) ||
        n.Equals("zh-HK", StringComparison.OrdinalIgnoreCase) ||
        n.Equals("zh-MO", StringComparison.OrdinalIgnoreCase);

    public static string ResolveInitial(string? saved, string uiCulture) =>
        Match(saved) ?? Match(uiCulture) ?? En;

    public static string DisplayNameOf(string tag) =>
        Meta.TryGetValue(tag, out var m) ? m.Name : tag;

    public static void Init()
    {
        string? saved = null;
        try
        {
            if (File.Exists(FilePath))
                saved = File.ReadAllText(FilePath).Trim();
        }
        catch { /* ignore */ }

        var tag = ResolveInitial(saved, CultureInfo.CurrentUICulture.Name);
        Apply(tag, persist: Match(saved) is null);
    }

    public static void Set(string tag)
    {
        var next = Match(tag);
        if (next is null || next == _tag) return;
        Apply(next, persist: true);
        Changed?.Invoke();
    }

    static void Apply(string tag, bool persist)
    {
        _tag = tag;
        var culture = CultureInfo.GetCultureInfo(Meta[tag].Culture);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        if (!persist) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, tag);
        }
        catch { /* ignore */ }
    }
}
