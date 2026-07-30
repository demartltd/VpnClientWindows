using System.Text.Json;
using System.Text.Json.Serialization;
using DemarVpn.Domain.Vpn;

namespace DemarVpn.Services.Vpn;

public static class ClashTrafficMonitor
{
    private static readonly HttpClient Http = new(new HttpClientHandler { UseProxy = false })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    private static readonly Uri TrafficUrl =
        new($"http://{SingBoxTags.ClashApiHost}:{SingBoxTags.ClashApiPort}/traffic");

    public static async Task RunAsync(Action<long, long> onSample, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var resp = await Http.GetAsync(TrafficUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                resp.EnsureSuccessStatusCode();
                await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                using var reader = new StreamReader(stream);
                while (!ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(ct);
                    if (line is null) break;
                    if (TryParse(line) is not { } sample) continue;
                    onSample(sample.Up, sample.Down);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch
            {
                try { await Task.Delay(1000, ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            }
        }
    }

    public static TrafficSample? TryParse(string line)
    {
        try { return JsonSerializer.Deserialize(line, ClashTrafficJson.Default.TrafficSample); }
        catch { return null; }
    }
}

public readonly record struct TrafficSample(
    [property: JsonPropertyName("up")] long Up,
    [property: JsonPropertyName("down")] long Down);

[JsonSerializable(typeof(TrafficSample))]
internal partial class ClashTrafficJson : JsonSerializerContext;
