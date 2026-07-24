using System.Text.Json.Nodes;

namespace DemarVpn.Services.Auth;

public sealed record SessionExchange(
    string UserId,
    string LoginAt,
    string? Username,
    string? VpnSubscription = null,
    string? VpnExpiresAt = null);

public abstract record EmailConfirmResult
{
    public sealed record NeedsTotp(string ChallengeToken) : EmailConfirmResult;
    public sealed record Ready(string ExchangeCode) : EmailConfirmResult;
}

public static class AuthApi
{
    public static string TelegramOAuthStartUrl
    {
        get
        {
            var returnTo = Uri.EscapeDataString(DemarHttp.AppOauthReturn);
            return $"{DemarHttp.BaseUrl}/auth/oauth/telegram/start?return_to={returnTo}&client={DemarHttp.ClientId}";
        }
    }

    public static Task SendEmailAsync(string email, string purpose = "login", CancellationToken ct = default) =>
        DemarHttp.PostJsonAsync(
            $"{DemarHttp.BaseUrl}/auth/mobile/email/send",
            new JsonObject { ["email"] = email, ["purpose"] = purpose },
            ct);

    public static async Task<EmailConfirmResult> ConfirmEmailAsync(string email, string code, CancellationToken ct = default)
    {
        var json = await DemarHttp.PostJsonAsync(
            $"{DemarHttp.BaseUrl}/auth/mobile/email/confirm",
            new JsonObject { ["email"] = email, ["code"] = code },
            ct);
        if (DemarHttp.OptBool(json, "requiresTotp"))
        {
            var token = DemarHttp.OptString(json, "challengeToken")
                        ?? throw new AuthException("Missing challengeToken");
            return new EmailConfirmResult.NeedsTotp(token);
        }
        var exchange = DemarHttp.OptString(json, "exchangeCode")
                       ?? throw new AuthException("Missing exchangeCode");
        return new EmailConfirmResult.Ready(exchange);
    }

    public static async Task<string> VerifyTotpAsync(string challengeToken, string code, CancellationToken ct = default)
    {
        var json = await DemarHttp.PostJsonAsync(
            $"{DemarHttp.BaseUrl}/auth/mobile/totp/verify",
            new JsonObject { ["challengeToken"] = challengeToken, ["code"] = code },
            ct);
        return DemarHttp.OptString(json, "exchangeCode")
               ?? throw new AuthException("Missing exchangeCode");
    }

    public static async Task<SessionExchange> ExchangeSessionAsync(string code, CancellationToken ct = default)
    {
        var json = await DemarHttp.PostJsonAsync(
            $"{DemarHttp.BaseUrl}/auth/mobile/session/exchange",
            new JsonObject { ["code"] = code },
            ct);
        return ParseSession(json);
    }

    public static async Task<SessionExchange> CheckSessionAsync(string userId, string loginAt, CancellationToken ct = default)
    {
        var json = await DemarHttp.PostJsonAsync(
            $"{DemarHttp.BaseUrl}/auth/mobile/session/check",
            new JsonObject { ["userId"] = userId, ["loginAt"] = loginAt },
            ct);
        return ParseSession(json);
    }

    public static Task LogoutSessionAsync(string userId, string loginAt, CancellationToken ct = default) =>
        DemarHttp.PostJsonAsync(
            $"{DemarHttp.BaseUrl}/auth/mobile/session/logout",
            new JsonObject { ["userId"] = userId, ["loginAt"] = loginAt },
            ct);

    public static async Task<SessionExchange> SessionFromClientTokenAsync(string token, CancellationToken ct = default)
    {
        var json = await DemarHttp.GetJsonAsync(
            $"{DemarHttp.AccountBaseUrl}/api/auth/client/me", token, ct);
        var loginAt = DemarHttp.OptString(json, "loginAt")
                      ?? throw new AuthException("Missing loginAt");
        if (json["profile"] is not JsonObject profile)
            throw new AuthException("Missing profile");
        var userId = DemarHttp.OptString(profile, "id")
                     ?? throw new AuthException("Missing userId");
        var username = DemarHttp.OptString(profile, "username");
        string? vpnSub = null, vpnExp = null;
        if (profile["vpn"] is JsonObject vpn)
        {
            vpnSub = DemarHttp.OptString(vpn, "subscription");
            vpnExp = DemarHttp.OptString(vpn, "expiresAt");
        }
        return new SessionExchange(userId, loginAt, username, vpnSub, vpnExp);
    }

    private static SessionExchange ParseSession(JsonObject json)
    {
        var userId = DemarHttp.OptString(json, "userId") ?? throw new AuthException("Missing userId");
        var loginAt = DemarHttp.OptString(json, "loginAt") ?? throw new AuthException("Missing loginAt");
        return new SessionExchange(
            userId,
            loginAt,
            DemarHttp.OptString(json, "username"),
            DemarHttp.OptString(json, "vpnSubscription"),
            DemarHttp.OptString(json, "vpnExpiresAt"));
    }
}
