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
}
