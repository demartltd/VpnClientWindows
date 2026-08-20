using System.Text.Json.Nodes;
using DemarVpn.Services.Support;
using Xunit;

namespace DemarVpn.Tests;

public class SupportApiTest
{
    [Fact]
    public void ParseSummaries_readsIdAndUnread()
    {
        var arr = JsonNode.Parse(
            """
            [
              { "id": "42", "status": "open", "preview": "hi", "hasUnread": true, "channel": "windows" },
              { "_id": 7, "status": "closed", "hasUnread": false }
            ]
            """) as JsonArray;
        var list = SupportJson.ParseSummaries(arr);
        Assert.Equal(2, list.Count);
        Assert.Equal("42", list[0].Id);
        Assert.True(list[0].IsOpen);
        Assert.True(list[0].HasUnread);
        Assert.Equal("windows", list[0].Channel);
        Assert.Equal("7", list[1].Id);
        Assert.False(list[1].IsOpen);
    }

    [Fact]
    public void ParseTicketPayload_acceptsRootOrWrapped()
    {
        var wrapped = JsonNode.Parse(
            """
            { "ticket": {
                "id": "9", "status": "open", "channel": "windows",
                "messages": [{ "id": "1", "role": "user", "resolved": { "text": "hello", "media": [] } }]
            }}
            """) as JsonObject;
        var t = SupportJson.ParseTicketPayload(wrapped!);
        Assert.Equal("9", t.Id);
        Assert.Single(t.Messages);
        Assert.Equal("hello", t.Messages[0].Text);

        var root = JsonNode.Parse(
            """{ "_id": 3, "status": "open", "messages": [] }""") as JsonObject;
        Assert.Equal("3", SupportJson.ParseTicketPayload(root!).Id);
    }

    [Fact]
    public void EncodeDecode_roundTripsSummaryAndTicket()
    {
        var summaries = new[]
        {
            new SupportTicketSummary("1", "open", null, null, "windows", "2", "t", "preview", "user", true),
        };
        var decoded = SupportJson.DecodeSummaries(SupportJson.EncodeSummaries(summaries));
        Assert.NotNull(decoded);
        Assert.Equal("1", decoded![0].Id);
        Assert.True(decoded[0].HasUnread);

        var ticket = new SupportTicket("1", "open", "windows", "2", "t", null, null,
        [
            new SupportMessage("m1", "user", false, false, "2026-01-01T00:00:00Z",
                new SupportResolved("hi", [])),
        ]);
        var round = SupportJson.DecodeTicket(SupportJson.EncodeTicket(ticket));
        Assert.NotNull(round);
        Assert.Equal("1", round!.Id);
        Assert.Equal("hi", round.Messages[0].Text);
    }
}
