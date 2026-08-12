using DemarVpn.Services.Auth;
using Xunit;

namespace DemarVpn.Tests;

public class DemarHttpTest
{
    [Fact]
    public void ControlHosts_matchApiSurface()
    {
        Assert.True(DemarHttp.IsControlHost("auth.example.invalid"));
        Assert.True(DemarHttp.IsControlHost("vpn.example.invalid"));
        Assert.True(DemarHttp.IsControlHost("account.example.invalid"));
        Assert.True(DemarHttp.IsControlHost("API.demar.cc"));
        Assert.False(DemarHttp.IsControlHost("demar.cc"));
        Assert.False(DemarHttp.IsControlHost("github.com"));
    }
}
