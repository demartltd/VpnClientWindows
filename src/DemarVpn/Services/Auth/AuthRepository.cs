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
            var checkedSession = await AuthApi.CheckSessionAsync(current.UserId, current.LoginAt, ct)
                .ConfigureAwait(false);
            AuthSessionStore.Save(checkedSession);
            return true;
        }
        catch (AuthException e) when (e.IsSessionInvalid)
        {
            ClearLocal();
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
        await AuthApi.SendEmailAsync(email.Trim().ToLowerInvariant(), purpose, ct).ConfigureAwait(false);
        AppLog.Append($"auth: email OTP sent ({purpose})");
    }

    public static async Task<EmailConfirmResult> ConfirmEmailAsync(string email, string code, CancellationToken ct = default)
    {
        var result = await AuthApi.ConfirmEmailAsync(email.Trim().ToLowerInvariant(), code.Trim(), ct)
            .ConfigureAwait(false);
        if (result is EmailConfirmResult.Ready ready)
            await CompleteExchangeAsync(ready.ExchangeCode, ct).ConfigureAwait(false);
        return result;
    }

    public static async Task VerifyTotpAsync(string challengeToken, string code, CancellationToken ct = default)
    {
        var exchangeCode = await AuthApi.VerifyTotpAsync(challengeToken, code.Trim(), ct).ConfigureAwait(false);
        await CompleteExchangeAsync(exchangeCode, ct).ConfigureAwait(false);
    }

    public static async Task CompleteExchangeAsync(string code, CancellationToken ct = default)
    {
        var exchange = await AuthApi.ExchangeSessionAsync(code.Trim(), ct).ConfigureAwait(false);
        AuthSessionStore.Save(exchange);
        AppLog.Append($"auth: signed in as {exchange.UserId}");
    }

    public static async Task CompleteClientTokenAsync(string token, CancellationToken ct = default)
    {
        var exchange = await AuthApi.SessionFromClientTokenAsync(token.Trim(), ct).ConfigureAwait(false);
        AuthSessionStore.Save(exchange);
        AppLog.Append($"auth: signed in as {exchange.UserId} (oauth token)");
    }

    /** Best-effort server revoke, then always clear local session. */
    public static async Task SignOutAsync(CancellationToken ct = default)
    {
        var current = AuthSessionStore.Current;
        if (current is not null)
        {
            try { await AuthApi.LogoutSessionAsync(current.UserId, current.LoginAt, ct).ConfigureAwait(false); }
            catch (Exception e) { AppLog.Append($"auth: logout API failed — {e.Message}"); }
        }
        ClearLocal();
    }

    public static void UpdateVpnProfile(string? vpnSubscription, string? vpnExpiresAt) =>
        AuthSessionStore.UpdateVpnProfile(vpnSubscription, vpnExpiresAt);

    public static void ClearLocal()
    {
        AuthSessionStore.Clear();
        AppLog.Append("auth: signed out");
    }
}
