using System.Text.Json;
using System.Text.Json.Nodes;
using DemarVpn.Services.Auth;

namespace DemarVpn.Services.Support;

/// <summary>Pure JSON codec for support tickets (no WinRT — unit-testable).</summary>
public static class SupportJson
{
    public static string EncodeSummaries(IReadOnlyList<SupportTicketSummary> list)
    {
        var arr = new JsonArray();
        foreach (var t in list)
        {
            var o = new JsonObject
            {
                ["id"] = t.Id,
                ["status"] = t.Status,
                ["hasUnread"] = t.HasUnread,
            };
            PutOpt(o, "openedAt", t.OpenedAt);
            PutOpt(o, "closedAt", t.ClosedAt);
            PutOpt(o, "channel", t.Channel);
            PutOpt(o, "sourceId", t.SourceId);
            PutOpt(o, "themeId", t.ThemeId);
            PutOpt(o, "preview", t.Preview);
            PutOpt(o, "lastMessageRole", t.LastMessageRole);
            arr.Add(o);
        }
        return arr.ToJsonString();
    }

    public static IReadOnlyList<SupportTicketSummary>? DecodeSummaries(string raw)
    {
        try { return ParseSummaries(JsonNode.Parse(raw) as JsonArray); }
        catch { return null; }
    }

    public static string EncodeTicket(SupportTicket ticket)
    {
        var o = new JsonObject { ["id"] = ticket.Id, ["status"] = ticket.Status };
        PutOpt(o, "channel", ticket.Channel);
        PutOpt(o, "sourceId", ticket.SourceId);
        PutOpt(o, "themeId", ticket.ThemeId);
        PutOpt(o, "openedAt", ticket.OpenedAt);
        PutOpt(o, "closedAt", ticket.ClosedAt);
        var msgs = new JsonArray();
        foreach (var m in ticket.Messages) msgs.Add(EncodeMessage(m));
        o["messages"] = msgs;
        return o.ToJsonString();
    }

    public static SupportTicket? DecodeTicket(string raw)
    {
        try
        {
            return JsonNode.Parse(raw) is JsonObject o ? ParseTicket(o) : null;
        }
        catch { return null; }
    }

    public static SupportTicket ParseTicketPayload(JsonObject json)
    {
        if (json["ticket"] is JsonObject t) return ParseTicket(t);
        if (AnyId(json, "id", "_id") is not null || json.ContainsKey("messages"))
            return ParseTicket(json);
        throw new AuthException("missing ticket", 500);
    }

    public static IReadOnlyList<SupportTicketSummary> ParseSummaries(JsonArray? arr)
    {
        if (arr is null) return [];
        var list = new List<SupportTicketSummary>();
        foreach (var node in arr)
        {
            if (node is not JsonObject o) continue;
            var id = AnyId(o, "id", "_id");
            if (id is null) continue;
            list.Add(new SupportTicketSummary(
                id,
                DemarHttp.OptString(o, "status") ?? "",
                Blank(DemarHttp.OptString(o, "openedAt")),
                Blank(DemarHttp.OptString(o, "closedAt")),
                Blank(DemarHttp.OptString(o, "channel")),
                Blank(DemarHttp.OptString(o, "sourceId")),
                Blank(DemarHttp.OptString(o, "themeId")),
                Blank(DemarHttp.OptString(o, "preview")),
                Blank(DemarHttp.OptString(o, "lastMessageRole")),
                DemarHttp.OptBool(o, "hasUnread")));
        }
        return list;
    }

    public static IReadOnlyList<SupportTheme> ParseThemes(JsonArray? arr)
    {
        if (arr is null) return [];
        var list = new List<SupportTheme>();
        foreach (var node in arr)
        {
            if (node is not JsonObject o) continue;
            var id = AnyId(o, "id", "_id");
            if (id is null) continue;
            var name = DemarHttp.OptString(o, "name");
            list.Add(new SupportTheme(id, string.IsNullOrWhiteSpace(name) ? id : name));
        }
        return list;
    }

    private static JsonObject EncodeMessage(SupportMessage m)
    {
        var o = new JsonObject
        {
            ["id"] = m.Id,
            ["role"] = m.Role,
            ["edited"] = m.Edited,
            ["deleted"] = m.Deleted,
        };
        PutOpt(o, "createdAt", m.CreatedAt);
        if (m.Resolved is { } r)
        {
            var resolved = new JsonObject();
            PutOpt(resolved, "text", r.Text);
            var media = new JsonArray();
            foreach (var med in r.Media)
            {
                var item = new JsonObject();
                PutOpt(item, "type", med.Type);
                PutOpt(item, "url", med.Url);
                PutOpt(item, "fileName", med.FileName);
                PutOpt(item, "mimeType", med.MimeType);
                if (med.FileSize is > 0) item["fileSize"] = med.FileSize.Value;
                if (med.Width is > 0) item["width"] = med.Width.Value;
                if (med.Height is > 0) item["height"] = med.Height.Value;
                media.Add(item);
            }
            resolved["media"] = media;
            o["resolved"] = resolved;
        }
        return o;
    }

    private static SupportTicket ParseTicket(JsonObject o)
    {
        var messages = new List<SupportMessage>();
        if (o["messages"] is JsonArray msgs)
        {
            foreach (var node in msgs)
                if (node is JsonObject m) messages.Add(ParseMessage(m));
        }
        return new SupportTicket(
            AnyId(o, "id", "_id") ?? "",
            DemarHttp.OptString(o, "status") ?? "",
            Blank(DemarHttp.OptString(o, "channel")),
            Blank(DemarHttp.OptString(o, "sourceId")),
            Blank(DemarHttp.OptString(o, "themeId")),
            Blank(DemarHttp.OptString(o, "openedAt")),
            Blank(DemarHttp.OptString(o, "closedAt")),
            messages);
    }

    private static SupportMessage ParseMessage(JsonObject o)
    {
        SupportResolved? resolved = null;
        if (o["resolved"] is JsonObject r)
        {
            var media = new List<SupportMedia>();
            if (r["media"] is JsonArray mediaArr)
            {
                foreach (var node in mediaArr)
                {
                    if (node is not JsonObject m) continue;
                    var fs = OptLong(m, "fileSize");
                    var w = DemarHttp.OptInt(m, "width");
                    var h = DemarHttp.OptInt(m, "height");
                    media.Add(new SupportMedia(
                        Blank(DemarHttp.OptString(m, "type")),
                        Blank(DemarHttp.OptString(m, "url")),
                        Blank(DemarHttp.OptString(m, "fileName")),
                        Blank(DemarHttp.OptString(m, "mimeType")),
                        fs > 0 ? fs : null,
                        w > 0 ? w : null,
                        h > 0 ? h : null));
                }
            }
            resolved = new SupportResolved(Blank(DemarHttp.OptString(r, "text")), media);
        }
        return new SupportMessage(
            AnyId(o, "id", "_id") ?? "",
            DemarHttp.OptString(o, "role") ?? "",
            DemarHttp.OptBool(o, "edited"),
            DemarHttp.OptBool(o, "deleted"),
            Blank(DemarHttp.OptString(o, "createdAt")),
            resolved);
    }

    private static string? AnyId(JsonObject o, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!o.TryGetPropertyValue(key, out var n) || n is null || n.GetValueKind() == JsonValueKind.Null)
                continue;
            string s;
            try
            {
                s = n.GetValueKind() switch
                {
                    JsonValueKind.String => n.GetValue<string>(),
                    JsonValueKind.Number => n.ToJsonString(),
                    _ => n.ToJsonString().Trim('"'),
                };
            }
            catch { continue; }
            s = s.Trim();
            if (s.Length > 0 && s != "null") return s;
        }
        return null;
    }

    private static string? Blank(string? s) =>
        string.IsNullOrWhiteSpace(s) || s == "null" ? null : s;

    private static long OptLong(JsonObject json, string key)
    {
        if (!json.TryGetPropertyValue(key, out var n) || n is null) return -1;
        try { return n.GetValue<long>(); }
        catch
        {
            try { return (long)n.GetValue<double>(); }
            catch { return -1; }
        }
    }

    private static void PutOpt(JsonObject o, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) o[key] = value;
    }
}
