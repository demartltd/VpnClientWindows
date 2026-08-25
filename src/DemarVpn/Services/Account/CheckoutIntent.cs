namespace DemarVpn.Services.Account;

public enum CheckoutIntent
{
    Buy,
    Renew,
    UpgradeTraffic,
}

public static class CheckoutIntentExt
{
    public static string DefaultTier(this CheckoutIntent intent, string? current)
    {
        var t = (current ?? "free").ToLowerInvariant();
        return intent switch
        {
            CheckoutIntent.Buy => "plus",
            CheckoutIntent.Renew => t is "plus" or "vip" ? t : "plus",
            CheckoutIntent.UpgradeTraffic => t switch
            {
                "vip" or "dev" => "vip",
                "plus" or "tester" => "vip",
                _ => "plus",
            },
            _ => "plus",
        };
    }
}
