using DemarVpn.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Composition.SystemBackdrops;
using Windows.Graphics;

namespace DemarVpn;

public sealed partial class MainWindow : Window
{
    public Views.ShellPage? Shell => Root.Content as Views.ShellPage;

    public MainWindow()
    {
        InitializeComponent();
        Title = Strings.AppName;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            var res = Application.Current.Resources;
            var minW = (int)(double)res["AppMinWidth"];
            var minH = (int)(double)res["AppMinHeight"];
            presenter.PreferredMinimumWidth = minW;
            presenter.PreferredMinimumHeight = minH;
            var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            if (!WindowLayoutStore.TryLoad(out var w, out var h))
            {
                w = (int)(double)res["AppDefaultWidth"];
                h = (int)(double)res["AppDefaultHeight"];
            }
            var size = new SizeInt32(
                Math.Clamp(w, minW, work.Width),
                Math.Clamp(h, minH, work.Height));
            AppWindow.Resize(size);
            CenterOnScreen(size, work);
            Closed += (_, _) =>
            {
                if (AppWindow.Presenter is OverlappedPresenter p
                    && p.State == OverlappedPresenterState.Restored)
                    WindowLayoutStore.Save(AppWindow.Size.Width, AppWindow.Size.Height);
            };
        }
        TryMicaBackdrop();
        Root.Navigate(typeof(Views.ShellPage));
    }

    private void TryMicaBackdrop()
    {
        if (MicaController.IsSupported())
            SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
    }

    private void CenterOnScreen(SizeInt32 size, RectInt32 work)
    {
        AppWindow.Move(new PointInt32(
            work.X + (work.Width - size.Width) / 2,
            work.Y + (work.Height - size.Height) / 2));
    }
}
