namespace DemarVpn.Services.Auth;

public static class AuthRepository
{
    public static AuthSessionData? Session => AuthSessionStore.Current;
    public static bool IsSignedIn => Session is not null;

    public static async Task<bool> RefreshSessionAsync(CancellationToken ct = default)
    {
        var current = AuthSessionStore.Current;
        if (current is null) return false;
        try
        {
            var checkedSession = await AuthApi.CheckSessionAsync(current.UserId, current.LoginAt, ct);
            AuthSessionStore.Save(checkedSession);
            return true;
        }
        catch (AuthException e) when (e.StatusCode == 401 || e.Message == "session_revoked")
        {
            SignOut();
            AppLog.Append("auth: session inactive — signed out");
            return false;
        }
        catch (Exception e)
        {
            AppLog.Append($"auth: session check failed — {e.Message}");
            return true;
        }
    }

    public static async Task SendEmailAsync(string email, string purpose = "login", CancellationToken ct = default)
    {
        await AuthApi.SendEmailAsync(email.Trim().ToLowerInvariant(), purpose, ct);
        AppLog.Append($"auth: email OTP sent ({purpose})");
    }

    public static async Task<EmailConfirmResult> ConfirmEmailAsync(string email, string code, CancellationToken ct = default)
    {
        var result = await AuthApi.ConfirmEmailAsync(email.Trim().ToLowerInvariant(), code.Trim(), ct);
        if (result is EmailConfirmResult.Ready ready)
            await CompleteExchangeAsync(ready.ExchangeCode, ct);
        return result;
    }

    public static async Task VerifyTotpAsync(string challengeToken, string code, CancellationToken ct = default)
    {
        var exchangeCode = await AuthApi.VerifyTotpAsync(challengeToken, code.Trim(), ct);
        await CompleteExchangeAsync(exchangeCode, ct);
    }

    public static async Task CompleteExchangeAsync(string code, CancellationToken ct = default)
    {
        var exchange = await AuthApi.ExchangeSessionAsync(code.Trim(), ct);
        AuthSessionStore.Save(exchange);
        AppLog.Append($"auth: signed in as {exchange.UserId}");
    }

    public static async Task CompleteClientTokenAsync(string token, CancellationToken ct = default)
    {
        var exchange = await AuthApi.SessionFromClientTokenAsync(token.Trim(), ct);
        AuthSessionStore.Save(exchange);
        AppLog.Append($"auth: signed in as {exchange.UserId} (oauth token)");
    }

    public static void SignOut()
    {
        AuthSessionStore.Clear();
        AppLog.Append("auth: signed out");
    }
}
