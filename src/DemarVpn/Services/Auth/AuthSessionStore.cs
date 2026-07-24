using System.Text.Json;

namespace DemarVpn.Services.Auth;

public sealed record AuthSessionData(
    string UserId,
    string LoginAt,
    string Username,
    string? VpnSubscription = null,
    string? VpnExpiresAt = null);

public static class AuthSessionStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DemarVpn", "demar_auth_session.json");

    private static readonly object Gate = new();
    private static AuthSessionData? _session;

    public static AuthSessionData? Current
    {
        get { lock (Gate) return _session; }
    }

    public static void Load()
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(FilePath)) { _session = null; return; }
                var raw = File.ReadAllText(FilePath);
                var data = JsonSerializer.Deserialize<AuthSessionData>(raw);
                if (data is null || string.IsNullOrWhiteSpace(data.UserId) || string.IsNullOrWhiteSpace(data.LoginAt))
                {
                    _session = null;
                    return;
                }
                _session = data with
                {
                    Username = string.IsNullOrWhiteSpace(data.Username) ? data.UserId : data.Username,
                };
            }
            catch
            {
                _session = null;
            }
        }
    }

    public static void Save(SessionExchange exchange)
    {
        var session = new AuthSessionData(
            exchange.UserId,
            exchange.LoginAt,
            string.IsNullOrWhiteSpace(exchange.Username) ? exchange.UserId : exchange.Username!,
            exchange.VpnSubscription,
            exchange.VpnExpiresAt);
        lock (Gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(session));
            _session = session;
        }
    }

    public static void Clear()
    {
        lock (Gate)
        {
            _session = null;
            try { if (File.Exists(FilePath)) File.Delete(FilePath); }
            catch { /* ignore */ }
        }
    }
}
