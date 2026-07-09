using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Composition.SystemBackdrops;
using Windows.Graphics;

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
        {
            var res = Application.Current.Resources;
            presenter.PreferredMinimumWidth = (int)(double)res["AppMinWidth"];
            var size = new SizeInt32(
                (int)(double)res["AppDefaultWidth"],
                (int)(double)res["AppDefaultHeight"]);
            AppWindow.Resize(size);
            CenterOnScreen(size);
        }
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

    private void CenterOnScreen(SizeInt32 size)
    {
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.Move(new PointInt32(
            work.X + (work.Width - size.Width) / 2,
            work.Y + (work.Height - size.Height) / 2));
    }
}
