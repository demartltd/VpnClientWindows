using DemarVpn.Services.Vpn;

namespace DemarVpn.Tests;

public class CoreWatchArgsTest
{
    [Fact]
    public void TryParse_validArgs()
    {
        var args = new[]
        {
            @"C:\Program Files\Demar VPN\Demar VPN.exe",
            CoreWatchArgs.Flag,
            "1234",
            "--",
            @"C:\Program Files\Demar VPN\sing-box.exe",
            "run",
            "-c",
            @"C:\Users\me\AppData\Local\DemarVpn\demar\config.json",
        };

        Assert.True(CoreWatchArgs.TryParse(args, out var parentPid, out var exe, out var coreArgv));
        Assert.Equal(1234, parentPid);
        Assert.EndsWith("sing-box.exe", exe);
        Assert.Equal(["run", "-c", @"C:\Users\me\AppData\Local\DemarVpn\demar\config.json"], coreArgv);
    }

    [Fact]
    public void TryParse_withoutDoubleDash()
    {
        var args = new[] { "app.exe", CoreWatchArgs.Flag, "42", "sing-box.exe", "run", "-c", "cfg.json" };

        Assert.True(CoreWatchArgs.TryParse(args, out var parentPid, out var exe, out var coreArgv));
        Assert.Equal(42, parentPid);
        Assert.Equal("sing-box.exe", exe);
        Assert.Equal(["run", "-c", "cfg.json"], coreArgv);
    }

    [Fact]
    public void TryParse_invalidPid()
    {
        var args = new[] { "app.exe", CoreWatchArgs.Flag, "abc", "--", "sing-box.exe", "run" };

        Assert.False(CoreWatchArgs.TryParse(args, out _, out _, out _));
    }

    [Fact]
    public void TryParse_missingCore()
    {
        var args = new[] { "app.exe", CoreWatchArgs.Flag, "99", "--" };

        Assert.False(CoreWatchArgs.TryParse(args, out _, out _, out _));
    }

    [Fact]
    public void TryParse_noFlag()
    {
        var args = new[] { "app.exe", "--help" };

        Assert.False(CoreWatchArgs.TryParse(args, out _, out _, out _));
    }
}
