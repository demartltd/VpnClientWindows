namespace DemarVpn.Services.Support;

public sealed record SupportTicketSummary(
    string Id,
    string Status,
    string? OpenedAt,
    string? ClosedAt,
    string? Channel,
    string? SourceId,
    string? ThemeId,
    string? Preview,
    string? LastMessageRole,
    bool HasUnread = false)
{
    public bool IsOpen => Status == "open";
}

public sealed record SupportTheme(string Id, string Name);

public sealed record SupportMedia(
    string? Type,
    string? Url,
    string? FileName,
    string? MimeType,
    long? FileSize,
    int? Width = null,
    int? Height = null)
{
    public bool IsPhoto
    {
        get
        {
            if (Type == "photo") return true;
            if (MimeType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true) return true;
            var n = FileName?.ToLowerInvariant() ?? "";
            return n.EndsWith(".jpg") || n.EndsWith(".jpeg") || n.EndsWith(".png")
                   || n.EndsWith(".webp") || n.EndsWith(".gif") || n.EndsWith(".heic");
        }
    }
}

public sealed record SupportResolved(string? Text, IReadOnlyList<SupportMedia> Media);

public sealed record SupportMessage(
    string Id,
    string Role,
    bool Edited,
    bool Deleted,
    string? CreatedAt,
    SupportResolved? Resolved)
{
    public bool IsUser => Role == "user";
    public string? Text => string.IsNullOrWhiteSpace(Resolved?.Text) ? null : Resolved!.Text;
    public IReadOnlyList<SupportMedia> Media => Resolved?.Media ?? [];
}

public sealed record SupportTicket(
    string Id,
    string Status,
    string? Channel,
    string? SourceId,
    string? ThemeId,
    string? OpenedAt,
    string? ClosedAt,
    IReadOnlyList<SupportMessage> Messages)
{
    public bool IsOpen => Status == "open";
}

public sealed record SupportUploadMedia(string DataBase64, string? FileName, string? MimeType);

public sealed class SupportMediaTooLargeException() : Exception("media_too_large");

/// <summary>Internal list / draft / chat navigation (parity Android SupportRoute).</summary>
public abstract record SupportRoute
{
    public sealed record Draft(string ThemeId) : SupportRoute;
    public sealed record Chat(string TicketId) : SupportRoute;
}
