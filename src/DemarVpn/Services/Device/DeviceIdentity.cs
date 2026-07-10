using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace DemarVpn.Services.Device;

public static class DeviceIdentity
{
    private static IReadOnlyList<(string Key, string Value)>? _cache;

    public static Task<IReadOnlyList<(string Key, string Value)>> LoadAsync(bool forceRefresh = false)
    {
        if (!forceRefresh && _cache is not null) return Task.FromResult(_cache);
        return Task.Run(() =>
        {
            var fields = Collect();
            fields["fingerprint_sha256"] = Fingerprint(fields);
            _cache = Rows(fields);
            return _cache;
        });
    }

    public static string Fingerprint(IReadOnlyDictionary<string, string> fields)
    {
        var parts = fields
            .Where(e => e.Key != "fingerprint_sha256" && !string.IsNullOrEmpty(e.Value) && e.Value != "—")
            .Select(e => $"{e.Key}={e.Value}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();
        return Sha256Prefix32(string.Join('|', parts));
    }

    private static Dictionary<string, string> Collect()
    {
        var name = Environment.MachineName;
        return new Dictionary<string, string>
        {
            ["windows_machine_guid"] = ReadMachineGuid() ?? "—",
            ["windows_computer_name"] = string.IsNullOrWhiteSpace(name) ? "—" : name,
        };
    }

    private static string? ReadMachineGuid()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return key?.GetValue("MachineGuid") as string;
        }
        catch
        {
            return null;
        }
    }

    private static string Sha256Prefix32(string input)
    {
        var hex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
        return hex.Length <= 32 ? hex : hex[..32];
    }

    private static IReadOnlyList<(string Key, string Value)> Rows(Dictionary<string, string> fields)
    {
        string[] order = ["windows_machine_guid", "windows_computer_name", "fingerprint_sha256"];
        var seen = new HashSet<string>();
        var rows = new List<(string, string)>();
        foreach (var k in order)
        {
            if (!fields.TryGetValue(k, out var v)) continue;
            seen.Add(k);
            rows.Add((Label(k), v));
        }
        foreach (var k in fields.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (seen.Contains(k)) continue;
            rows.Add((Label(k), fields[k]));
        }
        return rows;
    }

    private static string Label(string key) => key switch
    {
        "windows_machine_guid" => "MachineGuid",
        "windows_computer_name" => "PC Name",
        "fingerprint_sha256" => "Fingerprint",
        _ => key,
    };
}
