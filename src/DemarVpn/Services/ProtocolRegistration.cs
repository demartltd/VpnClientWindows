using Microsoft.Win32;

namespace DemarVpn.Services;

/// <summary>Registers demar:// and stages OAuth callback URI for single-instance handoff.</summary>
public static class ProtocolRegistration
{
    const string Scheme = "demar";
    static string KeyPath => $@"Software\Classes\{Scheme}";

    public static string HandoffPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DemarVpn", "oauth_handoff.uri");

    public static void EnsureRegistered()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            if (key is null) return;
            key.SetValue(null, "URL:Demar VPN");
            key.SetValue("URL Protocol", "");
            using var icon = key.CreateSubKey("DefaultIcon");
            icon?.SetValue(null, $"\"{exe}\",0");
            using var cmd = key.CreateSubKey(@"shell\open\command");
            // "%1" = full demar://… URI from browser handoff page
            cmd?.SetValue(null, $"\"{exe}\" \"%1\"");
            AppLog.Append($"protocol: demar:// → {exe}");
        }
        catch (Exception ex)
        {
            AppLog.Append($"protocol register failed: {ex.Message}");
        }
    }

    public static void Unregister()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(KeyPath, throwOnMissingSubKey: false); }
        catch { /* ignore */ }
    }

    public static void WritePendingUri(Uri uri)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(HandoffPath)!);
            File.WriteAllText(HandoffPath, uri.AbsoluteUri);
        }
        catch (Exception ex)
        {
            AppLog.Append($"protocol handoff write failed: {ex.Message}");
        }
    }

    public static Uri? ConsumePendingUri()
    {
        try
        {
            if (!File.Exists(HandoffPath)) return null;
            var raw = File.ReadAllText(HandoffPath).Trim();
            File.Delete(HandoffPath);
            if (raw.Length == 0) return null;
            return Uri.TryCreate(raw, UriKind.Absolute, out var uri) ? uri : null;
        }
        catch
        {
            return null;
        }
    }

    public static Uri? FindDemarUri(IEnumerable<string> args)
    {
        foreach (var arg in args)
        {
            var t = arg.Trim().Trim('"');
            if (t.StartsWith("demar:", StringComparison.OrdinalIgnoreCase)
                && Uri.TryCreate(t, UriKind.Absolute, out var uri))
                return uri;
        }
        return null;
    }
}
