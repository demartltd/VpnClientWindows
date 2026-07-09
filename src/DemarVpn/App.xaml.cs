using DemarVpn.Services;
using DemarVpn.Theme;
using Microsoft.UI.Xaml;

namespace DemarVpn;

public partial class App : Application
{
    private Window? _window;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (!SingleInstance.TryClaim(FocusMainWindow)) return;

        AppServices.Init();
        DemarPalette.Apply(Resources);
        _window = new MainWindow();
        _window.Activate();
    }

    private void FocusMainWindow()
    {
        _window?.AppWindow.Show(true);
    }
}
