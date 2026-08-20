using System.Security.Cryptography;
using System.Text;
using DemarVpn.Services.Auth;

namespace DemarVpn.Services.Support;

/// <summary>Disk cache + unread + media bytes — parity Android SupportCacheStore / UnreadStore.</summary>
public static class SupportStore
{
    private const string CacheDirName = "support_cache";
    private const string MediaDirName = "support_media";
    private const int MaxMediaFiles = 80;
    private const long MaxMediaBytes = 64L * 1024 * 1024;

    private static readonly object Gate = new();
    private static bool _hasUnread;

    public static bool HasUnread
    {
        get { lock (Gate) return _hasUnread; }
    }

    public static event Action? Changed;

    public static void Clear()
    {
        lock (Gate) _hasUnread = false;
        Changed?.Invoke();
    }

    public static void ClearAll()
    {
        Clear();
        try
        {
            var root = Path.Combine(AppDataRoot(), CacheDirName);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
        catch { /* best-effort */ }
    }

    public static void Apply(IReadOnlyList<SupportTicketSummary> tickets)
    {
        var unread = tickets.Any(t => t.HasUnread);
        lock (Gate) _hasUnread = unread;
        Changed?.Invoke();
    }

    public static async Task RefreshUnreadAsync(CancellationToken ct = default)
    {
        var session = AuthSessionStore.Current;
        if (session is null)
        {
            Clear();
            return;
        }
        try
        {
            var tickets = await SupportApi.ListTicketsAsync(session.UserId, session.LoginAt, ct)
                .ConfigureAwait(false);
            Apply(tickets);
            SaveList(session.UserId, tickets);
            _ = WarmTicketsAsync(session.UserId, session.LoginAt, tickets, ct);
        }
        catch (Exception ex)
        {
            AppLog.Append($"support unread: {ex.Message}");
        }
    }

    public static IReadOnlyList<SupportTicketSummary>? LoadList(string userId) =>
        Read(userId, "list.json") is { } raw ? SupportApi.DecodeSummaries(raw) : null;

    public static void SaveList(string userId, IReadOnlyList<SupportTicketSummary> tickets) =>
        Write(userId, "list.json", SupportApi.EncodeSummaries(tickets));

    public static SupportTicket? LoadTicket(string userId, string ticketId) =>
        Read(userId, $"ticket_{ticketId}.json") is { } raw ? SupportApi.DecodeTicket(raw) : null;

    public static void SaveTicket(string userId, SupportTicket ticket) =>
        Write(userId, $"ticket_{ticket.Id}.json", SupportApi.EncodeTicket(ticket));

    public static void RemoveTicket(string userId, string ticketId)
    {
        try { File.Delete(FilePath(userId, $"ticket_{ticketId}.json")); }
        catch { /* ignore */ }
    }

    public static byte[]? LoadMedia(string key)
    {
        try
        {
            var f = MediaFile(key);
            if (!f.Exists) return null;
            f.LastWriteTimeUtc = DateTime.UtcNow;
            return File.ReadAllBytes(f.FullName);
        }
        catch { return null; }
    }

    public static void SaveMedia(string key, byte[] bytes)
    {
        try
        {
            var f = MediaFile(key);
            f.Directory?.Create();
            var tmp = f.FullName + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            File.Move(tmp, f.FullName, overwrite: true);
            TrimMedia();
        }
        catch { /* ignore */ }
    }

    private static async Task WarmTicketsAsync(
        string userId, string loginAt, IReadOnlyList<SupportTicketSummary> summaries, CancellationToken ct)
    {
        var missing = summaries.Where(s => LoadTicket(userId, s.Id) is null).ToList();
        if (missing.Count == 0) return;
        await Task.WhenAll(missing.Select(async s =>
        {
            try
            {
                var fresh = await SupportApi.GetTicketAsync(userId, loginAt, s.Id, ct).ConfigureAwait(false);
                SaveTicket(userId, fresh);
            }
            catch { /* warm best-effort */ }
        })).ConfigureAwait(false);
    }

    private static string? Read(string userId, string name)
    {
        try
        {
            var path = FilePath(userId, name);
            if (!File.Exists(path)) return null;
            var raw = File.ReadAllText(path, Encoding.UTF8);
            return string.IsNullOrWhiteSpace(raw) ? null : raw;
        }
        catch { return null; }
    }

    private static void Write(string userId, string name, string body)
    {
        try
        {
            var path = FilePath(userId, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, body, Encoding.UTF8);
            File.Move(tmp, path, overwrite: true);
        }
        catch { /* ignore */ }
    }

    private static string FilePath(string userId, string name)
    {
        var safe = RegexSafe(userId);
        return Path.Combine(AppDataRoot(), CacheDirName, safe, name);
    }

    private static FileInfo MediaFile(string key)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant()[..16];
        return new FileInfo(Path.Combine(AppDataRoot(), MediaDirName, hash + ".bin"));
    }

    private static void TrimMedia()
    {
        try
        {
            var dir = new DirectoryInfo(Path.Combine(AppDataRoot(), MediaDirName));
            if (!dir.Exists) return;
            var files = dir.GetFiles().OrderBy(f => f.LastWriteTimeUtc).ToList();
            var total = files.Sum(f => f.Length);
            var i = 0;
            while ((files.Count - i > MaxMediaFiles || total > MaxMediaBytes) && i < files.Count)
            {
                total -= files[i].Length;
                try { files[i].Delete(); } catch { /* ignore */ }
                i++;
            }
        }
        catch { /* ignore */ }
    }

    private static string AppDataRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DemarVpn");

    private static string RegexSafe(string userId)
    {
        var sb = new StringBuilder(userId.Length);
        foreach (var c in userId)
        {
            if (char.IsLetterOrDigit(c) || c is '.' or '_' or '-') sb.Append(c);
            else sb.Append('_');
        }
        return sb.Length > 0 ? sb.ToString() : "_";
    }
}
