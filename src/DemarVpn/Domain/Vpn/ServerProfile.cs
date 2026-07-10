using System.Globalization;

namespace DemarVpn.Domain.Vpn;

public abstract record ServerProfileKind
{
    public sealed record Naive(NaiveProfile Profile) : ServerProfileKind;
}

public sealed record ServerProfile(
    string Id,
    string DisplayName,
    ServerProfileKind Kind,
    string? CountryCode = null,
    string? CountryName = null)
{
    public SingBoxConnectPlan ConnectPlan => Kind switch
    {
        ServerProfileKind.Naive n => SingBoxConnectPlan.Naive(n.Profile),
        _ => throw new InvalidOperationException("unknown profile kind"),
    };

    public int? UplinkPort => Kind switch
    {
        ServerProfileKind.Naive n => n.Profile.Port,
        _ => null,
    };

    public static ServerProfile Naive(NaiveProfile profile, string? countryCode = null, string? countryName = null) => new(
        $"naive:{profile.DisplayName}",
        profile.DisplayName,
        new ServerProfileKind.Naive(profile),
        countryCode,
        countryName);

    public static ServerProfile NaiveFromTag(NaiveProfile profile)
    {
        var countryCode = CountryCodeFromTag(profile.DisplayName);
        return Naive(profile, countryCode, CountryNameFromCode(countryCode));
    }

    public static string? CountryCodeFromTag(string tag)
    {
        var dash = tag.IndexOf('-');
        if (dash <= 0) return null;
        var code = tag[..dash];
        return code.Length == 2 && code.All(char.IsAsciiLetter) ? code.ToUpperInvariant() : null;
    }

    public static string? CountryNameFromCode(string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode)) return null;
        try
        {
            return new RegionInfo(countryCode).NativeName;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
