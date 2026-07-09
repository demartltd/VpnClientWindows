namespace DemarVpn.Core;

public static class CountryFlags
{
    public const string Unknown = "unknown";

    public static Uri AssetUri(string? countryCode)
    {
        var code = countryCode?.Trim().ToLowerInvariant() ?? "";
        var file = code.Length == 2 && code.All(char.IsAsciiLetter) ? code : Unknown;
        return new Uri($"ms-appx:///Assets/Flags/{file}.png");
    }

    public static Uri UnknownUri() => new($"ms-appx:///Assets/Flags/{Unknown}.png");
}
