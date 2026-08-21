using System.Globalization;
using System.Runtime.InteropServices;
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
        var (osName, osDisplay, osBuild) = ReadOs();
        var (boardMfr, boardProduct, sysMfr, sysProduct) = ReadBoard();
        var board = JoinNonEmpty(" / ", boardMfr, boardProduct)
                    ?? JoinNonEmpty(" / ", sysMfr, sysProduct)
                    ?? "—";
        return new Dictionary<string, string>
        {
            ["windows_computer_name"] = string.IsNullOrWhiteSpace(name) ? "—" : name,
            ["windows_os"] = FormatWindowsProduct(osName, osBuild),
            ["windows_display"] = osDisplay ?? "—",
            ["windows_build"] = osBuild ?? "—",
            ["windows_arch"] = RuntimeInformation.OSArchitecture.ToString(),
            ["windows_cpu"] = RegString(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString") ?? "—",
            ["windows_gpu"] = ReadGpu() ?? "—",
            ["windows_board"] = board,
            ["windows_ram"] = FormatRamGb(TotalPhysBytes()),
            ["windows_user"] = string.IsNullOrWhiteSpace(Environment.UserName) ? "—" : Environment.UserName,
            ["windows_locale"] = CultureInfo.CurrentUICulture.Name,
            ["windows_timezone"] = TimeZoneInfo.Local.Id,
            ["windows_machine_guid"] = ReadMachineGuid() ?? "—",
        };
    }

    /// <summary>ProductName stays "Windows 10 …" on Win11; map by build ≥ 22000.</summary>
    private static string FormatWindowsProduct(string? productName, string? build)
    {
        var name = string.IsNullOrWhiteSpace(productName) ? "Windows" : productName.Trim();
        if (int.TryParse(build, out var n) && n >= 22000)
            name = name.Replace("Windows 10", "Windows 11", StringComparison.OrdinalIgnoreCase);
        return name;
    }

    private static string? ReadGpu()
    {
        const string root = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(root);
            if (key is null) return null;
            var names = new List<string>();
            foreach (var sub in key.GetSubKeyNames().OrderBy(s => s, StringComparer.Ordinal))
            {
                if (sub.Length != 4 || !sub.All(char.IsDigit)) continue;
                using var adapter = key.OpenSubKey(sub);
                var desc = adapter?.GetValue("DriverDesc")?.ToString()?.Trim();
                if (string.IsNullOrEmpty(desc)) continue;
                if (desc.Contains("Microsoft Basic Display", StringComparison.OrdinalIgnoreCase)) continue;
                if (desc.Contains("Remote Desktop", StringComparison.OrdinalIgnoreCase)) continue;
                if (!names.Contains(desc, StringComparer.OrdinalIgnoreCase))
                    names.Add(desc);
            }
            return names.Count == 0 ? null : string.Join(", ", names);
        }
        catch
        {
            return null;
        }
    }

    private static (string? Name, string? Display, string? Build) ReadOs()
    {
        const string path = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        return (
            RegString(path, "ProductName"),
            RegString(path, "DisplayVersion") ?? RegString(path, "ReleaseId"),
            RegString(path, "CurrentBuild") ?? RegString(path, "CurrentBuildNumber"));
    }

    private static (string? BoardMfr, string? BoardProduct, string? SysMfr, string? SysProduct) ReadBoard()
    {
        const string bios = @"HARDWARE\DESCRIPTION\System\BIOS";
        return (
            RegString(bios, "BaseBoardManufacturer"),
            RegString(bios, "BaseBoardProduct"),
            RegString(bios, "SystemManufacturer"),
            RegString(bios, "SystemProductName"));
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

    private static string? RegString(string path, string name)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(path);
            var v = key?.GetValue(name)?.ToString()?.Trim();
            return string.IsNullOrEmpty(v) ? null : v;
        }
        catch
        {
            return null;
        }
    }

    private static string? JoinNonEmpty(string sep, params string?[] parts)
    {
        var ok = parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()).ToArray();
        return ok.Length == 0 ? null : string.Join(sep, ok);
    }

    private static string FormatRamGb(ulong? bytes)
    {
        if (bytes is null or 0) return "—";
        var gb = Math.Round(bytes.Value / (1024d * 1024d * 1024d), 1);
        return $"{gb.ToString(CultureInfo.InvariantCulture)} GB";
    }

    private static ulong? TotalPhysBytes()
    {
        try
        {
            var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            return GlobalMemoryStatusEx(ref status) ? status.ullTotalPhys : null;
        }
        catch
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    private static string Sha256Prefix32(string input)
    {
        var hex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
        return hex.Length <= 32 ? hex : hex[..32];
    }

    private static IReadOnlyList<(string Key, string Value)> Rows(Dictionary<string, string> fields)
    {
        string[] order =
        [
            "windows_computer_name",
            "windows_os", "windows_display", "windows_build", "windows_arch",
            "windows_cpu", "windows_gpu", "windows_board", "windows_ram",
            "windows_user", "windows_locale", "windows_timezone",
            "windows_machine_guid", "fingerprint_sha256",
        ];
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
        "windows_computer_name" => "PC Name",
        "windows_os" => "Windows",
        "windows_display" => "Release",
        "windows_build" => "Build",
        "windows_arch" => "Arch",
        "windows_cpu" => "CPU",
        "windows_gpu" => "GPU",
        "windows_board" => "Board",
        "windows_ram" => "RAM",
        "windows_user" => "User",
        "windows_locale" => "Locale",
        "windows_timezone" => "Timezone",
        "windows_machine_guid" => "MachineGuid",
        "fingerprint_sha256" => "Fingerprint",
        _ => key,
    };
}
