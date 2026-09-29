using DemarVpn.Services;

namespace DemarVpn.Tests;

public class LocaleStoreTest
{
    [Theory]
    [InlineData(null, "ru", "ru")]
    [InlineData(null, "en", "en")]
    [InlineData(null, "uk", "uk")]
    [InlineData(null, "tr", "tr")]
    [InlineData(null, "uz", "uz")]
    [InlineData(null, "zh-CN", "zh-Hans")]
    [InlineData(null, "zh-TW", "zh-Hant")]
    [InlineData(null, "ko", "ko")]
    [InlineData(null, "ja-JP", "ja")]
    [InlineData(null, "es", "es")]
    [InlineData(null, "es-ES", "es")]
    [InlineData(null, "de", "de")]
    [InlineData(null, "de-DE", "de")]
    [InlineData(null, "fr", "fr")]
    [InlineData(null, "fr-FR", "fr")]
    [InlineData(null, "hi", "hi")]
    [InlineData(null, "hi-IN", "hi")]
    [InlineData(null, "pt", "en")]
    [InlineData("es", "ru", "es")]
    [InlineData("de", "en", "de")]
    [InlineData("fr", "ja", "fr")]
    [InlineData("hi", "de", "hi")]
    [InlineData("xx", "es-MX", "es")]
    [InlineData("UK", "en", "uk")]
    public void ResolveInitial_firstLaunchAndSaved(string? saved, string ui, string expected) =>
        Assert.Equal(expected, LocaleStore.ResolveInitial(saved, ui));

    [Theory]
    [InlineData("ru", "RUB")]
    [InlineData("es", "EUR")]
    [InlineData("de", "EUR")]
    [InlineData("fr", "EUR")]
    [InlineData("hi", "EUR")]
    public void DefaultCurrency_rule(string tag, string currency) =>
        Assert.Equal(currency, tag == LocaleStore.Ru ? "RUB" : "EUR");

    [Fact]
    public void Supported_hasThirteen() =>
        Assert.Equal(13, LocaleStore.Supported.Length);
}
