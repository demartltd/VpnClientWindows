using DemarVpn.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DemarVpn.Views;

public sealed partial class ServiceMenuPage : OverlayPage
{
    public ServiceMenuPage()
    {
        InitializeComponent();
        BuildAbout();
        EgressStatus.Text = "TCP 1.1.1.1:443 — 42 ms (mock)";
    }

    private void BuildAbout()
    {
        void Row(string k, string v)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var key = new TextBlock { Text = k, Opacity = 0.6 };
            var val = new TextBlock { Text = v, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(val, 1);
            grid.Children.Add(key);
            grid.Children.Add(val);
            AboutPanel.Children.Add(grid);
        }
        Row("Name", Strings.AppName);
        Row("Package", "DemarVpn");
        Row("Version", Strings.VersionName);
        Row("Platform", Strings.PlatformWindows);
        Row("Sing-box", AppServices.SingBoxLabel);
    }

    private void OnBack(object sender, RoutedEventArgs e) => GoBack();

    private void OnEgressRefresh(object sender, RoutedEventArgs e) =>
        EgressStatus.Text = $"TCP 1.1.1.1:443 — {Random.Shared.Next(20, 120)} ms (mock)";

    private void OnSnackInfo(object sender, RoutedEventArgs e) => ShowSnack(SnackKind.Info);
    private void OnSnackSuccess(object sender, RoutedEventArgs e) => ShowSnack(SnackKind.Success);
    private void OnSnackError(object sender, RoutedEventArgs e) => ShowSnack(SnackKind.Error);
    private void OnSnackWarning(object sender, RoutedEventArgs e) => ShowSnack(SnackKind.Warning);

    private static void ShowSnack(SnackKind kind) =>
        AppServices.Snackbar.Show(kind, $"{kind} snack demo");
}
