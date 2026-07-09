using DemarVpn.Controls;
using DemarVpn.Domain.Vpn;
using DemarVpn.Models;
using Microsoft.UI.Xaml.Controls;

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
            var row = new ServerRow { Profile = profile, IsSelected = profile.Id == selected.Id };
            row.Click += (_, _) =>
            {
                AppServices.Vpn.Select(profile);
                AppServices.Snackbar.ShowSuccess(Strings.ServerSelected);
                Rebuild();
            };
            ListHost.Children.Add(row);
        }
    }
}
