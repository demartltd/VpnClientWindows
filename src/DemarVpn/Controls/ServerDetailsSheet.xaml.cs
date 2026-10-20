using DemarVpn.Core;
using DemarVpn.Domain.Vpn;
using DemarVpn.Services;
using DemarVpn.Services.Account;
using DemarVpn.Services.Servers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;

namespace DemarVpn.Controls;

public sealed partial class ServerDetailsSheet : UserControl
{
    private ServerProfile? _profile;

    public ServerDetailsSheet()
    {
        InitializeComponent();
        Chrome.Dismissed += () => Visibility = Visibility.Collapsed;
        FlagImage.ImageFailed += (_, _) => FlagImage.Source = new BitmapImage(CountryFlags.UnknownUri());
        ServersPreferencesStore.Changed += OnPrefsChanged;
    }

    public void Show(ServerProfile profile, ServerReachState reach, long? trafficBytes)
    {
        _profile = profile;
        NameBlock.Text = profile.DisplayName;
        FlagImage.Source = new BitmapImage(CountryFlags.AssetUri(profile.CountryCode));
        TierBadge.BindTier(profile.Tier, large: true);
        ProtoBadge.BindProtocol(profile.Protocol, large: true, fullLabel: true);
        SyncFavorite();
        BuildMetrics(profile, reach, trafficBytes);
        Visibility = Visibility.Visible;
        Chrome.Show();
    }

    public void Dismiss()
    {
        Chrome.Dismiss();
        Visibility = Visibility.Collapsed;
        _profile = null;
    }

    private void OnPrefsChanged() =>
        Ui.Run(DispatcherQueue, () =>
        {
            if (_profile is not null && Visibility == Visibility.Visible) SyncFavorite();
        });

    private void SyncFavorite()
    {
        if (_profile is null) return;
        var on = ServersPreferencesStore.IsFavorite(_profile.Id);
        FavIcon.Glyph = on ? "\uEB52" : "\uEB51";
        FavIcon.Foreground = on
            ? (Brush)Application.Current.Resources["DemarErrorBrush"]
            : (Brush)Application.Current.Resources["DemarSecondaryBrush"];
    }

    private void OnFavClick(object sender, RoutedEventArgs e)
    {
        if (_profile is null) return;
        ServersPreferencesStore.ToggleFavorite(_profile.Id);
    }

    private void BuildMetrics(ServerProfile profile, ServerReachState reach, long? trafficBytes)
    {
        MetricsHost.Children.Clear();
        MetricsHost.Children.Add(LoadBlock(profile.EffectiveLoadPercent));
        MetricsHost.Children.Add(MetricRow("TCP", ServerReachState.FormatMs(reach.LatencyMs), first: true));
        var hsLabel = profile.Protocol == ServerProtocol.Hysteria2 ? "QUIC" : "TLS";
        MetricsHost.Children.Add(MetricRow(hsLabel, ServerReachState.FormatMs(reach.TlsMs)));
        var traffic = trafficBytes is long b ? VpnAccountApi.FormatBytes(b) : "—";
        MetricsHost.Children.Add(MetricRow(Strings.PayCompareTraffic, traffic));

        var directs = ApiBypassRuleSet.DirectCountryCodes(profile.BypassRuleSets);
        if (directs.Count == 0) return;
        MetricsHost.Children.Add(Divider(inset: true));
        var row = new Grid { Padding = new Thickness(16, 14, 16, 14) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock
        {
            Text = Strings.ServerDirect,
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var flags = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var code in directs)
        {
            var img = new Image
            {
                Width = 28,
                Height = 28,
                Stretch = Stretch.Uniform,
                Source = new BitmapImage(CountryFlags.AssetUri(code)),
            };
            flags.Children.Add(img);
        }
        Grid.SetColumn(flags, 1);
        row.Children.Add(flags);
        MetricsHost.Children.Add(row);
    }

    private static UIElement LoadBlock(int percent)
    {
        var (fill, track) = LoadColors(percent);
        var panel = new StackPanel { Spacing = 10, Padding = new Thickness(16) };
        panel.Children.Add(new TextBlock
        {
            Text = Strings.ServerLoad,
            FontSize = 14,
        });
        var trackBorder = new Border
        {
            Height = 12,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(track),
        };
        var fillBorder = new Border
        {
            Height = 12,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(fill),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 0,
        };
        // Width set after measure via SizeChanged on host
        var host = new Grid { Height = 12 };
        host.Children.Add(trackBorder);
        host.Children.Add(fillBorder);
        host.SizeChanged += (_, e) =>
            fillBorder.Width = Math.Max(0, e.NewSize.Width * percent / 100.0);
        panel.Children.Add(host);
        return panel;
    }

    private static (Color Fill, Color Track) LoadColors(int percent)
    {
        var res = Application.Current.Resources;
        Color Base(string key) => ((SolidColorBrush)res[key]).Color;
        if (percent <= 50)
        {
            var c = Base("DemarSecondaryBrush");
            return (c, Color.FromArgb(0x40, c.R, c.G, c.B));
        }
        if (percent <= 80)
        {
            var c = Base("DemarTertiaryBrush");
            return (c, Color.FromArgb(0x40, c.R, c.G, c.B));
        }
        var err = Base("DemarErrorBrush");
        return (err, Color.FromArgb(0x40, err.R, err.G, err.B));
    }

    private static UIElement MetricRow(string label, string value, bool first = false)
    {
        var panel = new StackPanel();
        panel.Children.Add(Divider(inset: !first, strong: first));
        var row = new Grid { Padding = new Thickness(16, 14, 16, 14) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock { Text = label, FontSize = 16, VerticalAlignment = VerticalAlignment.Center });
        var val = new TextBlock
        {
            Text = value,
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(val, 1);
        row.Children.Add(val);
        panel.Children.Add(row);
        return panel;
    }

    private static Border Divider(bool inset, bool strong = false) => new()
    {
        Height = 1,
        Margin = inset ? new Thickness(12, 0, 12, 0) : new Thickness(0),
        Background = new SolidColorBrush(((SolidColorBrush)Application.Current.Resources["DemarOutlineVariantBrush"]).Color)
        {
            Opacity = strong ? 0.6 : 0.45,
        },
    };
}
