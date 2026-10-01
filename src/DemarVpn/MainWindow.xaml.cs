using DemarVpn.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Composition.SystemBackdrops;
using Windows.Graphics;
using Windows.UI;

namespace DemarVpn;

public sealed partial class MainWindow : Window
{
    public Views.ShellPage? Shell => Root.Content as Views.ShellPage;
    public Frame RootFrame => Root;

    public MainWindow()
    {
        InitializeComponent();
        Title = Strings.AppName;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ApplyDarkCaptionButtons();
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(iconPath))
            AppWindow.SetIcon(iconPath);
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
        AppWindow.Changed += (_, e) =>
        {
            if (!e.DidSizeChange && !e.DidVisibilityChange) return;
            if (AppWindow.Presenter is OverlappedPresenter { State: not OverlappedPresenterState.Restored })
                return;
            DispatcherQueue.TryEnqueue(() => Root.InvalidateArrange());
        };
        TryMicaBackdrop();
        Root.Navigate(typeof(Views.ShellPage));
    }

    private void TryMicaBackdrop()
    {
        if (MicaController.IsSupported())
            SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
    }

    // Caption glyphs follow OS theme; force light icons for our dark-only chrome.
    private void ApplyDarkCaptionButtons()
    {
        if (!AppWindowTitleBar.IsCustomizationSupported()) return;
        var tb = AppWindow.TitleBar;
        var fg = Colors.White;
        var hover = Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF);
        var pressed = Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF);
        tb.ButtonBackgroundColor = Colors.Transparent;
        tb.ButtonInactiveBackgroundColor = Colors.Transparent;
        tb.ButtonForegroundColor = fg;
        tb.ButtonInactiveForegroundColor = Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF);
        tb.ButtonHoverForegroundColor = fg;
        tb.ButtonPressedForegroundColor = fg;
        tb.ButtonHoverBackgroundColor = hover;
        tb.ButtonPressedBackgroundColor = pressed;
    }

    private void CenterOnScreen(SizeInt32 size, RectInt32 work)
    {
        AppWindow.Move(new PointInt32(
            work.X + (work.Width - size.Width) / 2,
            work.Y + (work.Height - size.Height) / 2));
    }
}
