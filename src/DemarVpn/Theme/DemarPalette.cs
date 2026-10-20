using MaterialColorUtilities.Palettes;
using MaterialColorUtilities.Schemes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace DemarVpn.Theme;

public static class DemarPalette
{
    private const uint Seed = 0xFF3A6EA5;

    public static void Apply(ResourceDictionary resources)
    {
        var core = new CorePalette();
        core.Fill(Seed, MaterialColorUtilities.Palettes.Style.TonalSpot);
        var s = new DarkSchemeMapper().Map(core);

        Set(resources, "DemarSeedBrush", Seed);
        Set(resources, "DemarBackgroundBrush", s.Background);
        Set(resources, "DemarSurfaceBrush", s.Surface);
        Set(resources, "DemarSurfaceHighBrush", s.SurfaceVariant);
        Set(resources, "DemarPrimaryBrush", s.Primary);
        Set(resources, "DemarOnPrimaryBrush", s.OnPrimary);
        Set(resources, "DemarPrimaryContainerBrush", s.PrimaryContainer);
        Set(resources, "DemarOnPrimaryContainerBrush", s.OnPrimaryContainer);
        Set(resources, "DemarSecondaryBrush", s.Secondary);
        Set(resources, "DemarTertiaryBrush", s.Tertiary);
        Set(resources, "DemarOnTertiaryBrush", s.OnTertiary);
        Set(resources, "DemarErrorBrush", s.Error);
        Set(resources, "DemarPrimaryMutedBrush", WithAlpha(s.Primary, 0.10));
        Set(resources, "DemarSuccessMutedBrush", WithAlpha(s.Secondary, 0.20));
        Set(resources, "DemarErrorMutedBrush", WithAlpha(s.Error, 0.20));
        Set(resources, "DemarWarningMutedBrush", WithAlpha(s.Tertiary, 0.20));
        Set(resources, "DemarSuccessBrush", 0xFF81C784);
        Set(resources, "DemarWarningBrush", 0xFFFFB74D);
    }

    public static string ManifestBackgroundHex()
    {
        var core = new CorePalette();
        core.Fill(Seed, MaterialColorUtilities.Palettes.Style.TonalSpot);
        var bg = new DarkSchemeMapper().Map(core).Background;
        return $"#{(bg & 0xFFFFFF):X6}";
    }

    private static void Set(ResourceDictionary r, string key, uint argb) =>
        r[key] = new SolidColorBrush(FromArgb(argb));

    private static Windows.UI.Color FromArgb(uint argb) => Windows.UI.Color.FromArgb(
        (byte)((argb >> 24) & 0xFF),
        (byte)((argb >> 16) & 0xFF),
        (byte)((argb >> 8) & 0xFF),
        (byte)(argb & 0xFF));

    private static uint WithAlpha(uint argb, double alpha)
    {
        var a = (byte)(alpha * 255);
        return (uint)(a << 24) | (argb & 0x00FFFFFF);
    }
}
