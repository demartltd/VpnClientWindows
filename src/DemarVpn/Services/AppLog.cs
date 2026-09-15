using System.Text;

namespace DemarVpn.Services;

public static class AppLog
{
    public const int MaxLines = 512;
    private static readonly Queue<string> Lines = new(MaxLines);
    private static readonly object Gate = new();
    private static string? _logPath;
    private static int _appendsSinceTrim;

    public static event Action? Changed;

    public static void Init()
    {
        Directory.CreateDirectory(JournalDir);
        Bind(Path.Combine(JournalDir, "demar_app.log"));
        Append("[app] Demar VPN Windows started");
    }

    public static string? LogPath => _logPath;

    public static string JournalDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DemarVpn", "demar");

    public static IReadOnlyList<string> Snapshot()
    {
        lock (Gate) return Lines.ToArray();
    }

    public static void Append(string line)
    {
        var t = line.Trim();
        if (t.Length == 0 || IsVpnTrafficAccessNoise(t)) return;
        PushRows([$"{DateTime.UtcNow:yyyy-MM-dd-HH-mm-ss} {t}"]);
    }

    /// <summary>Crash breadcrumbs — no noise filter, no UI notify.</summary>
    public static void AppendCrash(string line)
    {
        var t = line.Trim();
        if (t.Length == 0) return;
        PushRows([$"{DateTime.UtcNow:yyyy-MM-dd-HH-mm-ss} {t}"], notify: false);
    }

    public static void AppendMany(IEnumerable<string> raw, bool prependStamp = true)
    {
        var rows = raw.Select(line =>
        {
            var t = line.Trim();
            if (t.Length == 0 || IsVpnTrafficAccessNoise(t)) return null;
            return prependStamp ? $"{DateTime.UtcNow:yyyy-MM-dd-HH-mm-ss} {t}" : t;
        }).Where(r => r is not null).Cast<string>().ToList();
        if (rows.Count > 0) PushRows(rows);
    }

    public static void Clear()
    {
        lock (Gate)
        {
            Lines.Clear();
            if (_logPath is not null) try { File.WriteAllText(_logPath, string.Empty); } catch { }
        }
        Changed?.Invoke();
    }

    public static string ReadPersistedUtf8()
    {
        if (_logPath is null || !File.Exists(_logPath)) return string.Empty;
        try { return File.ReadAllText(_logPath, Encoding.UTF8); }
        catch { return string.Empty; }
    }

    internal static void BindForTest(string path) => Bind(path);

    internal static void ResetForTest()
    {
        lock (Gate)
        {
            _logPath = null;
            Lines.Clear();
            _appendsSinceTrim = 0;
        }
        Changed?.Invoke();
    }

    public static bool IsVpnTrafficAccessNoise(string line)
    {
        var s = line.Trim();
        if (!s.Contains(" accepted ") || !s.Contains(" from ") || !s.Contains("->") || !s.Contains(']'))
            return false;
        var lower = s.ToLowerInvariant();
        return lower.Contains("tcp:") || lower.Contains("udp:");
    }

    private static void Bind(string path)
    {
        _logPath = path;
        lock (Gate)
        {
            Lines.Clear();
            if (File.Exists(path))
                foreach (var line in Capped(ReadFileLines(path)))
                    Lines.Enqueue(line);
            TrimDisk();
        }
        Changed?.Invoke();
    }

    private static void PushRows(IReadOnlyList<string> rows, bool notify = true)
    {
        lock (Gate)
        {
            foreach (var row in rows) Lines.Enqueue(row);
            var overCap = false;
            while (Lines.Count > MaxLines && Lines.TryDequeue(out _))
                overCap = true;

            _logPath ??= Path.Combine(JournalDir, "demar_app.log");
            try
            {
                Directory.CreateDirectory(JournalDir);
                var payload = string.Join(Environment.NewLine, rows) + Environment.NewLine;
                using var fs = new FileStream(
                    _logPath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite,
                    bufferSize: 4096);
                var bytes = Encoding.UTF8.GetBytes(payload);
                fs.Write(bytes, 0, bytes.Length);
            }
            catch { /* keep in-memory even if disk fails */ }

            if (overCap || ++_appendsSinceTrim >= 64)
            {
                _appendsSinceTrim = 0;
                TrimDisk();
            }
        }
        if (notify) Changed?.Invoke();
    }

    private static List<string> ReadFileLines(string path) =>
        File.ReadAllLines(path, Encoding.UTF8)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

    private static List<string> Capped(List<string> all) =>
        all.Count > MaxLines ? all[^MaxLines..] : all;

    private static void TrimDisk()
    {
        if (_logPath is null || !File.Exists(_logPath)) return;
        try
        {
            var all = ReadFileLines(_logPath);
            if (all.Count <= MaxLines) return;
            var capped = Capped(all);
            File.WriteAllText(_logPath, string.Join(Environment.NewLine, capped) + Environment.NewLine, Encoding.UTF8);
            Lines.Clear();
            foreach (var line in capped) Lines.Enqueue(line);
        }
        catch { /* best effort */ }
    }
}
