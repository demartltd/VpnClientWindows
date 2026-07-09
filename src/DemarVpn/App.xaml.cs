using DemarVpn.Theme;
using Microsoft.UI.Xaml;

namespace DemarVpn;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        AppServices.Init();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        DemarPalette.Apply(Resources);
        new MainWindow().Activate();
    }
}
