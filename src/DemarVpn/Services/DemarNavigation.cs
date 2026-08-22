using DemarVpn.Services.Support;

namespace DemarVpn.Services;

/// <summary>Deep-link parsers — parity with Android DemarNavigation / Apple DemarURLScheme.</summary>
public static class DemarNavigation
{
    public const string Scheme = "demarvpn";

    /// <summary>
    /// demarvpn://settings/support · …/ticket/{id} · …/new/{themeId}
    /// (optional open/ prefix).
    /// </summary>
    public static bool TryParseSupportRoute(Uri uri, out SupportRoute? route)
    {
        route = null;
        if (!uri.Scheme.Equals(Scheme, StringComparison.OrdinalIgnoreCase))
            return false;

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(uri.Host))
            parts.Add(uri.Host);
        parts.AddRange(uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries));
        if (parts.Count > 0 && parts[0].Equals("open", StringComparison.OrdinalIgnoreCase))
            parts.RemoveAt(0);
        if (parts.Count < 2) return false;
        if (!parts[0].Equals("settings", StringComparison.OrdinalIgnoreCase)) return false;
        if (!parts[1].Equals("support", StringComparison.OrdinalIgnoreCase)) return false;

        if (parts.Count == 2)
        {
            route = null;
            return true;
        }
        if (parts.Count >= 4 && parts[2].Equals("ticket", StringComparison.OrdinalIgnoreCase))
        {
            route = new SupportRoute.Chat(parts[3]);
            return true;
        }
        if (parts.Count >= 4 && parts[2].Equals("new", StringComparison.OrdinalIgnoreCase))
        {
            route = new SupportRoute.Draft(parts[3]);
            return true;
        }
        return false;
    }

    /// <summary>
    /// demarvpn://settings/traffic (optional open/ prefix).
    /// </summary>
    public static bool TryParseTrafficRoute(Uri uri)
    {
        if (!uri.Scheme.Equals(Scheme, StringComparison.OrdinalIgnoreCase))
            return false;

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(uri.Host))
            parts.Add(uri.Host);
        parts.AddRange(uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries));
        if (parts.Count > 0 && parts[0].Equals("open", StringComparison.OrdinalIgnoreCase))
            parts.RemoveAt(0);
        return parts.Count == 2
            && parts[0].Equals("settings", StringComparison.OrdinalIgnoreCase)
            && parts[1].Equals("traffic", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryParseAuthHandoff(Uri uri, out string? token, out string? code)
    {
        token = null;
        code = null;
        if (!string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.Equals(uri.Host, "auth", StringComparison.OrdinalIgnoreCase)) return false;
        var path = uri.AbsolutePath.Trim('/');
        if (path.Length > 0 && !string.Equals(path, "callback", StringComparison.OrdinalIgnoreCase))
            return false;
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            var key = Uri.UnescapeDataString(part[..eq]);
            var val = Uri.UnescapeDataString(part[(eq + 1)..]);
            if (string.IsNullOrWhiteSpace(val)) continue;
            if (key.Equals("token", StringComparison.OrdinalIgnoreCase))
            {
                token = val;
                code = null;
                return true;
            }
            if (key.Equals("code", StringComparison.OrdinalIgnoreCase) && token is null)
                code = val;
        }
        return code is not null;
    }
}
