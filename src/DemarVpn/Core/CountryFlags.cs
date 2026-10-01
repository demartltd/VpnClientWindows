using System.Collections.Concurrent;

namespace DemarVpn.Core;

public static class CountryFlags
{
    public const string Unknown = "unknown";
    private static readonly ConcurrentDictionary<string, Uri> Cache = new(StringComparer.Ordinal);

    public static Uri AssetUri(string? countryCode)
    {
        var code = countryCode?.Trim().ToLowerInvariant() ?? "";
        var file = code.Length == 2 && code.All(char.IsAsciiLetter) ? code : Unknown;
        return Resolve(file);
    }

    public static Uri UnknownUri() => Resolve(Unknown);

    static Uri Resolve(string file) =>
        Cache.GetOrAdd(file, static f =>
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Flags", $"{f}.png");
            return File.Exists(path)
                ? new Uri(path, UriKind.Absolute)
                : new Uri($"ms-appx:///Assets/Flags/{f}.png");
        });
}
