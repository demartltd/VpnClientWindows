using System.Globalization;

namespace DemarVpn.Services;

/// <summary>App UI language (en|ru). First launch follows OS UI culture; non-ru → en.</summary>
public static class LocaleStore
{
    public const string En = "en";
    public const string Ru = "ru";

    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DemarVpn", "locale");

    static string _tag = En;

    public static string Tag => _tag;
    public static bool IsRu => _tag == Ru;
    public static string DisplayName => IsRu ? "Русский" : "English";

    public static event Action? Changed;

    public static string ResolveInitial(string? saved, string uiTwoLetter) =>
        saved is En or Ru ? saved
        : string.Equals(uiTwoLetter, Ru, StringComparison.OrdinalIgnoreCase) ? Ru : En;

    public static void Init()
    {
        string? saved = null;
        try
        {
            if (File.Exists(FilePath))
                saved = File.ReadAllText(FilePath).Trim();
        }
        catch { /* ignore */ }

        var tag = ResolveInitial(saved, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        Apply(tag, persist: saved is not En and not Ru);
    }

    public static void Set(string tag)
    {
        if (tag is not (En or Ru) || tag == _tag) return;
        Apply(tag, persist: true);
        Changed?.Invoke();
    }

    static void Apply(string tag, bool persist)
    {
        _tag = tag;
        var culture = CultureInfo.GetCultureInfo(tag == Ru ? "ru-RU" : "en-US");
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
