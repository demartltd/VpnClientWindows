using System.Collections.Concurrent;
using System.Text;

namespace DemarVpn.Services;

public static class AppLog
{
    public const int MaxLines = 512;
    private static readonly ConcurrentQueue<string> Lines = new();
    private static readonly object Gate = new();
    private static string? _logPath;

    public static event Action? Changed;

    public static void Init()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DemarVpn", "demar");
        Directory.CreateDirectory(dir);
        _logPath = Path.Combine(dir, "demar_app.log");
        Append("[app] Demar VPN Windows started");
    }

    public static string? LogPath => _logPath;

    public static IReadOnlyList<string> Snapshot()
    {
        lock (Gate) return Lines.ToArray();
    }

    public static void Append(string line)
    {
        var stamped = $"{DateTime.UtcNow:yyyy-MM-dd-HH-mm} {line}";
        lock (Gate)
        {
            Lines.Enqueue(stamped);
            while (Lines.Count > MaxLines && Lines.TryDequeue(out _)) { }
            if (_logPath is not null)
            {
                try { File.AppendAllText(_logPath, stamped + Environment.NewLine, Encoding.UTF8); }
                catch { /* best effort */ }
            }
        }
        Changed?.Invoke();
    }

    public static void Clear()
    {
        lock (Gate)
        {
            while (Lines.TryDequeue(out _)) { }
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
}
