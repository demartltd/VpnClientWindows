using DemarVpn.Services;

namespace DemarVpn.Tests;

public class LocaleStoreTest
{
    [Theory]
    [InlineData(null, "ru", "ru")]
    [InlineData(null, "en", "en")]
    [InlineData(null, "de", "en")]
    [InlineData(null, "fr", "en")]
    [InlineData("en", "ru", "en")]
    [InlineData("ru", "en", "ru")]
    [InlineData("xx", "ru", "ru")]
    [InlineData("xx", "de", "en")]
    [InlineData("", "ru", "ru")]
    public void ResolveInitial_firstLaunchAndSaved(string? saved, string ui, string expected) =>
        Assert.Equal(expected, LocaleStore.ResolveInitial(saved, ui));
}
