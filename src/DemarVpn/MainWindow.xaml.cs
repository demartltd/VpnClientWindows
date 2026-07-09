using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Composition.SystemBackdrops;

namespace DemarVpn;

public sealed partial class MainWindow : Window
{
    private readonly DispatcherTimer _throughputTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public MainWindow()
    {
        InitializeComponent();
        Title = Strings.AppName;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.PreferredMinimumWidth = (int)(double)Application.Current.Resources["AppMinWidth"];
        TryMicaBackdrop();
        Root.Navigate(typeof(Views.ShellPage));
        _throughputTimer.Tick += (_, _) => AppServices.Vpn.TickThroughput();
        _throughputTimer.Start();
        Activated += (_, _) => AppServices.Vpn.TickThroughput();
    }

    private void TryMicaBackdrop()
    {
        if (MicaController.IsSupported())
            SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
    }
}
