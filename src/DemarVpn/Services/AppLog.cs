using System.Text;

namespace DemarVpn.Services;

public static class AppLog
{
    public const int MaxLines = 512;
    private static readonly Queue<string> Lines = new(MaxLines);
    private static readonly object Gate = new();
    private static readonly List<string> PendingDisk = new();
    private static string? _logPath;
    private static int _appendsSinceTrim;
    private static int _flushScheduled;
    private static CancellationTokenSource? _notifyCts;

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
        FlushForTest();
        lock (Gate)
        {
            Lines.Clear();
            PendingDisk.Clear();
            if (_logPath is not null) try { File.WriteAllText(_logPath, string.Empty); } catch { }
        }
        Changed?.Invoke();
    }

    public static string ReadPersistedUtf8()
    {
        FlushForTest();
        if (_logPath is null || !File.Exists(_logPath)) return string.Empty;
        try { return File.ReadAllText(_logPath, Encoding.UTF8); }
        catch { return string.Empty; }
    }

    internal static void BindForTest(string path) => Bind(path);

    /// <summary>Tests / Clear: drain pending disk writes synchronously.</summary>
    internal static void FlushForTest()
    {
        for (var i = 0; i < 100; i++)
        {
            List<string>? batch = null;
            var trim = false;
            lock (Gate)
            {
                if (PendingDisk.Count > 0)
                {
                    batch = [.. PendingDisk];
                    PendingDisk.Clear();
                    if (Lines.Count >= MaxLines || ++_appendsSinceTrim >= 64)
                    {
                        _appendsSinceTrim = 0;
                        trim = true;
                    }
                }
            }

            if (batch is not null)
            {
                WriteDisk(batch, trim);
                continue;
            }

            if (Volatile.Read(ref _flushScheduled) == 0) return;
            Thread.Sleep(10);
        }
    }

    internal static void ResetForTest()
    {
        FlushForTest();
        lock (Gate)
        {
            _logPath = null;
            Lines.Clear();
            PendingDisk.Clear();
            _appendsSinceTrim = 0;
            _flushScheduled = 0;
        }
        try { _notifyCts?.Cancel(); } catch { /* ignore */ }
        _notifyCts?.Dispose();
        _notifyCts = null;
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
        FlushForTest();
        _logPath = path;
        lock (Gate)
        {
            Lines.Clear();
            PendingDisk.Clear();
            if (File.Exists(path))
                foreach (var line in Capped(ReadFileLines(path)))
                    Lines.Enqueue(line);
        }
        TrimDisk();
        Changed?.Invoke();
    }

    private static void PushRows(IReadOnlyList<string> rows, bool notify = true)
    {
        lock (Gate)
        {
            foreach (var row in rows)
            {
                Lines.Enqueue(row);
                PendingDisk.Add(row);
            }
            while (Lines.Count > MaxLines && Lines.TryDequeue(out _)) { }
        }

        ScheduleFlush();
        if (notify) ScheduleNotify();
    }

    private static void ScheduleFlush()
    {
        if (Interlocked.CompareExchange(ref _flushScheduled, 1, 0) != 0) return;
        _ = Task.Run(() =>
        {
            try
            {
                while (true)
                {
                    List<string> batch;
                    var trim = false;
                    lock (Gate)
                    {
                        if (PendingDisk.Count == 0)
                        {
                            Interlocked.Exchange(ref _flushScheduled, 0);
                            if (PendingDisk.Count == 0) return;
                            if (Interlocked.CompareExchange(ref _flushScheduled, 1, 0) != 0) return;
                            continue;
                        }
                        batch = [.. PendingDisk];
                        PendingDisk.Clear();
                        if (Lines.Count >= MaxLines || ++_appendsSinceTrim >= 64)
                        {
                            _appendsSinceTrim = 0;
                            trim = true;
                        }
                    }
                    WriteDisk(batch, trim);
                }
            }
            catch
            {
                Interlocked.Exchange(ref _flushScheduled, 0);
            }
        });
    }

    private static void WriteDisk(List<string> batch, bool trim)
    {
        _logPath ??= Path.Combine(JournalDir, "demar_app.log");
        try
        {
            Directory.CreateDirectory(JournalDir);
            File.AppendAllText(
                _logPath,
                string.Join(Environment.NewLine, batch) + Environment.NewLine,
                Encoding.UTF8);
        }
        catch { /* keep in-memory even if disk fails */ }

        if (trim) TrimDisk();
    }

    private static void ScheduleNotify()
    {
        var cts = new CancellationTokenSource();
        var prev = Interlocked.Exchange(ref _notifyCts, cts);
        try { prev?.Cancel(); } catch { /* ignore */ }
        prev?.Dispose();

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, cts.Token);
                if (ReferenceEquals(Volatile.Read(ref _notifyCts), cts))
                    Changed?.Invoke();
            }
            catch (OperationCanceledException) { }
        });
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
            lock (Gate)
            {
                Lines.Clear();
                foreach (var line in capped) Lines.Enqueue(line);
            }
        }
        catch { /* best effort */ }
    }
}
