using DemarVpn.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Composition.SystemBackdrops;
using Windows.UI;

namespace DemarVpn;

public sealed partial class MainWindow : Window
{
    public Views.ShellPage? Shell => Root.Content as Views.ShellPage;
    public Frame RootFrame => Root;

    // Keep alive: Maximize hook + size anim.
    readonly WindowSizing _sizing;

    public MainWindow()
    {
        InitializeComponent();
        // Without this, await after ConfigureAwait(false) resumes on the thread pool (APPCRASH on XAML).
        Ui.EnsureSyncContext(DispatcherQueue);
        Title = Strings.AppName;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppTitleBar.Height = AppWindow.TitleBar.Height;
        ApplyDarkCaptionButtons();
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(iconPath))
            AppWindow.SetIcon(iconPath);
        _sizing = new WindowSizing(this, Root);
        if (MicaController.IsSupported())
            SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
        Root.Navigate(typeof(Views.ShellPage));
        Activated += (_, e) =>
        {
            if (e.WindowActivationState != WindowActivationState.Deactivated)
                Shell?.EvaluateSubscriptionAlerts();
        };
    }

    // Caption glyphs follow OS theme; force light icons for our dark-only chrome.
    void ApplyDarkCaptionButtons()
    {
        if (!AppWindowTitleBar.IsCustomizationSupported()) return;
        var tb = AppWindow.TitleBar;
        var fg = Colors.White;
        var hover = Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF);
        var pressed = Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF);
        tb.ButtonBackgroundColor = tb.ButtonInactiveBackgroundColor = Colors.Transparent;
        tb.ButtonForegroundColor = tb.ButtonHoverForegroundColor = tb.ButtonPressedForegroundColor = fg;
        tb.ButtonInactiveForegroundColor = Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF);
        tb.ButtonHoverBackgroundColor = hover;
        tb.ButtonPressedBackgroundColor = pressed;
    }
}
