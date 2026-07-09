namespace DemarVpn.Domain.Vpn;

public abstract record ServerProfileKind
{
    public sealed record Naive(NaiveProfile Profile) : ServerProfileKind;
}

public sealed record ServerProfile(string Id, string DisplayName, ServerProfileKind Kind)
{
    public SingBoxConnectPlan ConnectPlan => Kind switch
    {
        ServerProfileKind.Naive n => SingBoxConnectPlan.Naive(n.Profile),
        _ => throw new InvalidOperationException("unknown profile kind"),
    };

    public string ListTitle => Kind switch
    {
        ServerProfileKind.Naive n => $"{n.Profile.Server}:{n.Profile.Port}",
        _ => string.Empty,
    };

    public string ListSubtitle => Kind switch
    {
        ServerProfileKind.Naive => "Naive (test)",
        _ => string.Empty,
    };

    public int? UplinkPort => Kind switch
    {
        ServerProfileKind.Naive n => n.Profile.Port,
        _ => null,
    };

    public static ServerProfile FromNaive(NaiveProfile profile) => new(
        $"naive:{profile.DisplayName}",
        profile.DisplayName,
        new ServerProfileKind.Naive(profile));
}
