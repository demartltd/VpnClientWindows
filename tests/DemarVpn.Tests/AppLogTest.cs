using DemarVpn.Services;

namespace DemarVpn.Tests;

public class AppLogTest : IDisposable
{
    private readonly string _dir;

    public AppLogTest()
    {
        _dir = Path.Combine(Path.GetTempPath(), "demar-applog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        AppLog.ResetForTest();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private void Bind() => AppLog.BindForTest(Path.Combine(_dir, "demar_app.log"));

    [Fact]
    public void Append_keepsOnlyLastMaxLinesInMemory()
    {
        Bind();
        for (var i = 0; i < AppLog.MaxLines + 40; i++) AppLog.Append($"line-{i}");
        var lines = AppLog.Snapshot();
        Assert.Equal(AppLog.MaxLines, lines.Count);
        Assert.Contains($"line-{AppLog.MaxLines + 39}", lines[^1]);
        Assert.DoesNotContain(lines, l => l.Contains("line-0"));
    }

    [Fact]
    public void Append_trimsPersistedFileToMaxLines()
    {
        Bind();
        for (var i = 0; i < AppLog.MaxLines + 10; i++) AppLog.Append($"disk-{i}");
        var persisted = AppLog.ReadPersistedUtf8().Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(AppLog.MaxLines, persisted.Length);
        Assert.Contains($"disk-{AppLog.MaxLines + 9}", persisted[^1]);
    }

    [Fact]
    public void Bind_trimsPreexistingOversizedJournal()
    {
        var path = Path.Combine(_dir, "demar_app.log");
        File.WriteAllText(path, string.Join('\n', Enumerable.Range(1, 600).Select(i => $"seed-{i:D4}")) + '\n');
        Bind();
        var lines = AppLog.Snapshot();
        Assert.Equal(AppLog.MaxLines, lines.Count);
        Assert.Equal(AppLog.MaxLines, File.ReadAllLines(path).Length);
        Assert.Contains("seed-0600", lines[^1]);
        Assert.DoesNotContain(lines, l => l.Contains("seed-0001"));
    }

    [Fact]
    public void Bind_loadsPersistedJournalAcrossRestart()
    {
        Bind();
        AppLog.Append("before-restart");
        AppLog.ResetForTest();
        Bind();
        Assert.Single(AppLog.Snapshot());
        Assert.Contains("before-restart", AppLog.Snapshot()[0]);
    }

    [Fact]
    public void Append_prependsSecondPrecisionStamp()
    {
        Bind();
        AppLog.Append("probe");
        Assert.Matches(@"\d{4}-\d{2}-\d{2}-\d{2}-\d{2}-\d{2} probe", AppLog.Snapshot().Single());
    }

    [Fact]
    public void AppendMany_trimsWhenBatchExceedsMaxLines()
    {
        Bind();
        AppLog.AppendMany(Enumerable.Range(0, AppLog.MaxLines + 50).Select(i => $"batch-{i}"), prependStamp: false);
        Assert.Equal(AppLog.MaxLines, AppLog.Snapshot().Count);
        Assert.Equal(AppLog.MaxLines, AppLog.ReadPersistedUtf8().Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("batch-561", AppLog.Snapshot()[^1]);
        Assert.DoesNotContain(AppLog.Snapshot(), l => l == "batch-0");
    }

    [Fact]
    public void IsVpnTrafficAccessNoise_filtersSingBoxAccessLines()
    {
        var noise = "[tcp] 127.0.0.1:1234 accepted tcp:1.1.1.1:443 from 10.0.0.1:5555 -> 1.1.1.1:443";
        Assert.True(AppLog.IsVpnTrafficAccessNoise(noise));
        Assert.False(AppLog.IsVpnTrafficAccessNoise("[app] connect started"));
    }
}
