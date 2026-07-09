using DemarVpn.Domain.Vpn;
using DemarVpn.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DemarVpn.Views;

public sealed partial class ServersPage : Page
{
    public ServersPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Rebuild();
        AppServices.Vpn.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Services.Vpn.VpnController.Selected))
                DispatcherQueue.TryEnqueue(Rebuild);
        };
    }

    private void Rebuild()
    {
        ListHost.Children.Clear();
        var selected = AppServices.Vpn.Selected;
        foreach (var profile in AppServices.Vpn.Servers)
        {
            var isSelected = profile.Id == selected.Id;
            var card = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = (Brush)Application.Current.Resources[isSelected ? "DemarPrimaryContainerBrush" : "DemarSurfaceHighBrush"],
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(14),
                Tag = profile,
            };
            var grid = new Grid { Padding = new Thickness(16) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var texts = new StackPanel { Spacing = 4 };
            texts.Children.Add(new TextBlock { Text = profile.ListTitle, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            texts.Children.Add(new TextBlock { Text = profile.ListSubtitle, Opacity = 0.7 });
            grid.Children.Add(texts);
            var icon = new FontIcon
            {
                Glyph = isSelected ? "\uE73E" : "\uE739",
                Foreground = (Brush)Application.Current.Resources[isSelected ? "DemarSeedBrush" : "DemarSurfaceHighBrush"],
            };
            Grid.SetColumn(icon, 1);
            grid.Children.Add(icon);
            card.Content = grid;
            card.Click += OnSelect;
            ListHost.Children.Add(card);
        }
    }

    private void OnSelect(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ServerProfile profile }) return;
        AppServices.Vpn.Select(profile);
        AppServices.Snackbar.ShowSuccess(Strings.ServerSelected);
        Rebuild();
    }
}
