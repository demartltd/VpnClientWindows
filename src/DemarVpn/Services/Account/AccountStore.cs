using DemarVpn.Services.Auth;
using DemarVpn.Services.Servers;

namespace DemarVpn.Services.Account;

/// <summary>Account + traffic snapshot. Refreshed on app start / servers refresh — not on tab focus.</summary>
public static class AccountStore
{
    private static readonly object Gate = new();
    private static VpnAccount? _account;
    private static VpnTraffic? _traffic;
    private static bool _loading;
    private static int _refreshGen;

    public static VpnAccount? Account { get { lock (Gate) return _account; } }
    public static VpnTraffic? Traffic { get { lock (Gate) return _traffic; } }
    public static bool Loading { get { lock (Gate) return _loading; } }

    public static event Action? Changed;

    public static void Clear()
    {
        lock (Gate)
        {
            _refreshGen++;
            _account = null;
            _traffic = null;
            _loading = false;
        }
        Changed?.Invoke();
    }

    public static void Hydrate(VpnAccount? account, VpnTraffic? traffic)
    {
        lock (Gate)
        {
            if (account is not null) _account = account;
            if (traffic is not null) _traffic = traffic;
        }
        Changed?.Invoke();
    }

    public static void ApplyAccount(VpnAccount account)
    {
        lock (Gate) _account = account;
        PersistAccount(account);
        Changed?.Invoke();
    }

    /// <returns>true if account and/or traffic updated (parity Apple refreshAccount).</returns>
    public static async Task<bool> RefreshAsync(CancellationToken ct = default)
    {
        var session = AuthSessionStore.Current;
        if (session is null)
        {
            Clear();
            return false;
        }

        int gen;
        lock (Gate)
        {
            gen = ++_refreshGen;
            _loading = true;
        }
        Changed?.Invoke();

        try
        {
            var accountTask = VpnAccountApi.FetchAccountAsync(session.UserId, session.LoginAt, ct);
            var trafficTask = VpnAccountApi.FetchTrafficAsync(session.UserId, session.LoginAt, ct);
            VpnAccount? account = null;
            VpnTraffic? traffic = null;
            Exception? err = null;
            try { account = await accountTask.ConfigureAwait(false); }
            catch (Exception e) { err = e; }
            try { traffic = await trafficTask.ConfigureAwait(false); }
            catch (Exception e) { err ??= e; }

            lock (Gate)
            {
                if (gen != _refreshGen) return false;
                if (account is not null) _account = account;
                if (traffic is not null) _traffic = traffic;
            }

            if (account is not null)
            {
                PersistAccount(account);
                AuthRepository.UpdateVpnProfile(account.VpnSubscription, account.VpnExpiresAt);
                AppServices.Auth.NotifyChanged();
            }
            if (traffic is not null)
                SessionCacheStore.SaveTraffic(traffic, session.UserId);

            if (err is AuthException { IsSessionInvalid: true })
            {
                AuthRepository.ClearLocal();
                AppServices.Auth.NotifyChanged();
                Clear();
                AppLog.Append("account: session revoked");
                return false;
            }
            if (err is not null)
                AppLog.Append($"account: refresh failed — {err.Message}");
            else
                AppLog.Append("account: refreshed");

            return account is not null || traffic is not null;
        }
        finally
        {
            lock (Gate)
            {
                if (gen == _refreshGen) _loading = false;
            }
            Changed?.Invoke();
        }
    }

    private static void PersistAccount(VpnAccount account)
    {
        var uid = AuthSessionStore.Current?.UserId;
        if (uid is not null) SessionCacheStore.SaveAccount(account, uid);
    }
}
