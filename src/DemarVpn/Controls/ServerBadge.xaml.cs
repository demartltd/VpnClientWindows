using DemarVpn.Domain.Vpn;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace DemarVpn.Controls;

public sealed partial class ServerBadge : UserControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(ServerBadge),
            new PropertyMetadata("", OnChanged));

    public static readonly DependencyProperty TierProperty =
        DependencyProperty.Register(nameof(Tier), typeof(string), typeof(ServerBadge),
            new PropertyMetadata("", OnChanged));

    public static readonly DependencyProperty LargeProperty =
        DependencyProperty.Register(nameof(Large), typeof(bool), typeof(ServerBadge),
            new PropertyMetadata(false, OnChanged));

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>Optional tier name for Plus/Vip colors; empty = neutral (protocol).</summary>
    public string Tier
    {
        get => (string)GetValue(TierProperty);
        set => SetValue(TierProperty, value ?? "");
    }

    public bool Large
    {
        get => (bool)GetValue(LargeProperty);
        set => SetValue(LargeProperty, value);
    }

    public ServerBadge()
    {
        InitializeComponent();
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Center;
    }

    public void BindTier(ServerTier tier, bool large = false)
    {
        Large = large;
        Tier = tier.ToString();
        Label = tier.Label();
    }

    public void BindProtocol(ServerProtocol protocol, bool large = false, bool fullLabel = false)
    {
        Large = large;
        Tier = "";
        Label = fullLabel ? protocol.FullLabel() : protocol.Label();
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ServerBadge b) b.Apply();
    }

    private void Apply()
    {
        LabelBlock.Text = Label ?? "";
        var large = Large;
        LabelBlock.FontSize = large ? 12 : 10;
        Root.Padding = large ? new Thickness(8, 0, 8, 0) : new Thickness(4, 1, 4, 1);
        Root.CornerRadius = new CornerRadius(large ? 6 : 4);
        Root.Height = large ? 24 : double.NaN;
        Root.HorizontalAlignment = HorizontalAlignment.Left;
        var (bg, fg) = ColorsFor(Tier);
        Root.Background = new SolidColorBrush(bg);
        LabelBlock.Foreground = new SolidColorBrush(fg);
    }

    private static (Color Bg, Color Fg) ColorsFor(string? tier) => tier switch
    {
        nameof(ServerTier.Plus) => (C(0x47, 0x43, 0xA0, 0x47), C(0xF2, 0x43, 0xA0, 0x47)),
        nameof(ServerTier.Vip) => (C(0x47, 0x8E, 0x24, 0xAA), C(0xF2, 0x8E, 0x24, 0xAA)),
        _ => (C(0x1A, 0xFF, 0xFF, 0xFF), C(0xB3, 0xB0, 0xB0, 0xB0)),
    };

    private static Color C(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);
}
