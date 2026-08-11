using DemarVpn.Services;

namespace DemarVpn.Tests;

public class RunAtStartupTest
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    public void PrefMeansEnabled_parsesPrefs(string? text, bool expected) =>
        Assert.Equal(expected, RunAtStartup.PrefMeansEnabled(text));
}
