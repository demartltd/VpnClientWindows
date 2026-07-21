namespace DemarVpn.Core;

public static class CountryFlags
{
    public const string Unknown = "unknown";

    public static Uri AssetUri(string? countryCode)
    {
        var code = countryCode?.Trim().ToLowerInvariant() ?? "";
        var file = code.Length == 2 && code.All(char.IsAsciiLetter) ? code : Unknown;
        return Resolve(file);
    }

    public static Uri UnknownUri() => Resolve(Unknown);

    static Uri Resolve(string file)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Flags", $"{file}.png");
        if (File.Exists(path)) return new Uri(path, UriKind.Absolute);
        return new Uri($"ms-appx:///Assets/Flags/{file}.png");
    }
}
