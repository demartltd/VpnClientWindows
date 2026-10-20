using DemarVpn.Domain.Vpn;
using DemarVpn.Services.Servers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DemarVpn.Controls;

public sealed partial class ServerFiltersSheet : UserControl
{
    private IReadOnlyList<ServerProfile> _servers = [];

    public ServerFiltersSheet()
    {
        InitializeComponent();
        Chrome.Dismissed += () => Visibility = Visibility.Collapsed;
    }

    public void Show(IReadOnlyList<ServerProfile> servers)
    {
        _servers = servers;
        Rebuild();
        Visibility = Visibility.Visible;
        Chrome.Show();
    }

    public void Dismiss()
    {
        Chrome.Dismiss();
        Visibility = Visibility.Collapsed;
    }

    private void Rebuild()
    {
        RowsHost.Children.Clear();
        var items = ServerProfile.VisibleTiers(_servers)
            .Select(t => (Label: t.Label(), Kind: FilterKind.Tier, Id: t.ToString()))
            .Concat(ServerProfile.VisibleProtocols(_servers)
                .Select(p => (Label: p.Label(), Kind: FilterKind.Proto, Id: p.ToString())))
            .ToList();
        for (var i = 0; i < items.Count; i += 3)
        {
            var n = Math.Min(3, items.Count - i);
            var row = new Grid { ColumnSpacing = 8 };
            for (var c = 0; c < n; c++)
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var c = 0; c < n; c++)
            {
                var item = items[i + c];
                var chip = MakeChip(item.Label, item.Kind, item.Id);
                Grid.SetColumn(chip, c);
                row.Children.Add(chip);
            }
            RowsHost.Children.Add(row);
        }
    }

    private Button MakeChip(string label, FilterKind kind, string id)
    {
        var on = kind == FilterKind.Tier
            ? Enum.TryParse<ServerTier>(id, out var t) && ServersPreferencesStore.Tiers.Contains(t)
            : Enum.TryParse<ServerProtocol>(id, out var p) && ServersPreferencesStore.Protocols.Contains(p);
        var btn = new Button
        {
            Style = (Style)Application.Current.Resources["DemarQuietButtonStyle"],
            Content = new TextBlock
            {
                Text = label,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
            },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(0, 14, 0, 14),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(0),
            Tag = $"{(int)kind}|{id}",
        };
        StyleChip(btn, on);
        btn.Click += OnChipClick;
        return btn;
    }

    private void OnChipClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }) return;
        var pipe = tag.IndexOf('|');
        if (pipe <= 0) return;
        var kind = (FilterKind)int.Parse(tag[..pipe]);
        var id = tag[(pipe + 1)..];
        if (kind == FilterKind.Tier && Enum.TryParse<ServerTier>(id, out var tier))
        {
            var next = ServersPreferencesStore.Tiers.ToHashSet();
            if (!next.Remove(tier)) next.Add(tier);
            if (!ServerProfile.MatchesFilters(_servers, next, ServersPreferencesStore.Protocols)) return;
            ServersPreferencesStore.SetTiers(next);
        }
        else if (kind == FilterKind.Proto && Enum.TryParse<ServerProtocol>(id, out var proto))
        {
            var next = ServersPreferencesStore.Protocols.ToHashSet();
            if (!next.Remove(proto)) next.Add(proto);
            if (!ServerProfile.MatchesFilters(_servers, ServersPreferencesStore.Tiers, next)) return;
            ServersPreferencesStore.SetProtocols(next);
        }
        else return;
        Rebuild();
    }

    private static void StyleChip(Button btn, bool on)
    {
        var res = Application.Current.Resources;
        var baseBrush = (SolidColorBrush)res["DemarSurfaceHighBrush"];
        btn.Background = new SolidColorBrush(baseBrush.Color) { Opacity = on ? 1 : 0.45 };
        if (btn.Content is TextBlock tb)
        {
            tb.Foreground = on
                ? (Brush)res["DemarSecondaryBrush"]
                : new SolidColorBrush(((SolidColorBrush)res["DemarSecondaryBrush"]).Color) { Opacity = 0.65 };
        }
    }

    private enum FilterKind { Tier, Proto }
}
