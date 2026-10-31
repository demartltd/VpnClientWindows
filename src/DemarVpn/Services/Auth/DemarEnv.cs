namespace DemarVpn.Services.Auth;

/// <summary>API bases from repo-root / output <c>.env</c> (see <c>.env.example</c>).</summary>
public static class DemarEnv
{
    public static string AuthApiBase { get; } = Required("DEMAR_AUTH_API_BASE");
    public static string VpnApiBase { get; } = Required("DEMAR_VPN_API_BASE");
    public static string AccountApiBase { get; } = Required("DEMAR_ACCOUNT_API_BASE");
    public static string SupportApiBase { get; } = Required("DEMAR_SUPPORT_API_BASE");

    static string Required(string key)
    {
        if (Map.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
            return v.Trim().Trim('"').Trim('\'');
        throw new InvalidOperationException(
            $"DemarEnv: missing {key} — copy .env.example → .env next to the project or app output.");
    }

    static readonly Dictionary<string, string> Map = Load();

    static Dictionary<string, string> Load()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in CandidatePaths())
        {
            if (!File.Exists(path)) continue;
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                map[line[..eq].Trim()] = line[(eq + 1)..].Trim();
            }
            break;
        }
        foreach (var key in new[]
                 {
                     "DEMAR_AUTH_API_BASE", "DEMAR_VPN_API_BASE", "DEMAR_ACCOUNT_API_BASE",
                     "DEMAR_SUPPORT_API_BASE",
                 })
        {
            var env = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(env))
                map[key] = env;
        }
        return map;
    }

    static IEnumerable<string> CandidatePaths()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            yield return Path.Combine(dir.FullName, ".env");
    }
}
