using DemarVpn.Services;

namespace DemarVpn.Locales;

/// <summary>Per-language string tables (Apple .lproj / Android values-* parity). Key = English source.</summary>
static class Catalog
{
    static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Tables =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [LocaleStore.En] = En.Map,
            [LocaleStore.Ru] = Ru.Map,
            [LocaleStore.Uk] = Uk.Map,
            [LocaleStore.Tr] = Tr.Map,
            [LocaleStore.Uz] = Uz.Map,
            [LocaleStore.ZhHans] = ZhHans.Map,
            [LocaleStore.ZhHant] = ZhHant.Map,
            [LocaleStore.Ko] = Ko.Map,
            [LocaleStore.Ja] = Ja.Map,
            [LocaleStore.Es] = Es.Map,
            [LocaleStore.De] = De.Map,
            [LocaleStore.Fr] = Fr.Map,
            [LocaleStore.Hi] = Hi.Map,
        };

    public static string Get(string key)
    {
        if (Tables.TryGetValue(LocaleStore.Tag, out var map) && map.TryGetValue(key, out var v))
            return v;
        return En.Map.TryGetValue(key, out var en) ? en : key;
    }
}
