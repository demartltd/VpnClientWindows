namespace DemarVpn.Domain.Content;

public enum LegalDocument
{
    Terms,
    Privacy,
}

public static class LegalDocuments
{
    public static IReadOnlyList<LegalDocument> All { get; } = [LegalDocument.Terms, LegalDocument.Privacy];

    public static string Title(this LegalDocument d) => d switch
    {
        LegalDocument.Terms => Strings.SettingsTerms,
        LegalDocument.Privacy => Strings.SettingsPrivacy,
        _ => d.ToString(),
    };

    public static string Subtitle(this LegalDocument d) => d switch
    {
        LegalDocument.Terms => Strings.SettingsTermsSub,
        LegalDocument.Privacy => Strings.SettingsPrivacySub,
        _ => string.Empty,
    };

    public static string Glyph(this LegalDocument d) => d switch
    {
        LegalDocument.Terms => "\uE8A5",
        LegalDocument.Privacy => "\uE72E",
        _ => "\uE8A5",
    };

    public static string Url(this LegalDocument d) => d switch
    {
        LegalDocument.Terms => "https://raw.githubusercontent.com/demartltd/tos-pp/refs/heads/main/ToS.md",
        LegalDocument.Privacy => "https://raw.githubusercontent.com/demartltd/tos-pp/refs/heads/main/pp.md",
        _ => throw new ArgumentOutOfRangeException(nameof(d)),
    };
}
