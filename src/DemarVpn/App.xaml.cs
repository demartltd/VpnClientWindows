using DemarVpn.Services;
using DemarVpn.Services.Auth;
using DemarVpn.Services.Vpn;
using DemarVpn.Theme;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using WinRT.Interop;

namespace DemarVpn;

public partial class App : Application
{
    private Window? _window;
    private MainWindow? _main;
    private int _handoffInFlight;

    /// <summary>HWND for FileOpenPicker InitializeWithWindow (unpackaged WinUI).</summary>
    public static nint MainHwnd { get; private set; }

    public static App? CurrentApp => Current as App;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            try { AppLog.Append($"unhandled: {e.Exception}"); } catch { /* best-effort */ }
            // Do not set Handled — keep crash visible; log is the breadcrumb.
        };
    }

    /// <summary>Recreate shell after locale change so UI strings refresh.</summary>
    public static void RemountShell(bool openAppSettings = false)
    {
        var main = CurrentApp?._main;
        if (main is null) return;
        main.RootFrame.Navigate(typeof(Views.ShellPage));
        if (openAppSettings)
            main.DispatcherQueue.TryEnqueue(() => main.Shell?.OpenAppSettings());
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (!SingleInstance.TryClaim(OnRedirectedActivation)) return;

        AppServices.Init();
        if (AppServices.Update.TryApplyPending()) return;

        ProtocolRegistration.EnsureRegistered();
        DemarPalette.Apply(Resources);
        _main = new MainWindow();
        _window = _main;
        MainHwnd = WindowNative.GetWindowHandle(_main);
        VpnLifecycle.Attach(_main);
        _window.Activate();

        var launchArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
        var uri = ResolveLaunchUri(launchArgs);
        HandleIncomingUri(uri);
        _ = BootstrapAsync();
    }

    /** Cold start: only handle URI from activation args; never replay stale oauth_handoff.uri. */
    private static Uri? ResolveLaunchUri(AppActivationArguments launchArgs)
    {
        var uri = SingleInstance.TryGetAuthUri(launchArgs);
        if (uri is not null)
        {
            ProtocolRegistration.ClearPendingUri();
            return uri;
        }
        if (SingleInstance.IsProtocolActivation(launchArgs))
            return ProtocolRegistration.ConsumePendingUri();
        ProtocolRegistration.ClearPendingUri();
        return null;
    }

    private async Task BootstrapAsync()
    {
        await AppServices.BootstrapSessionAsync();
        AppServices.StartBackgroundRefresh();
    }

    private void OnRedirectedActivation(AppActivationArguments args)
    {
        _window?.DispatcherQueue.TryEnqueue(() =>
        {
            _window?.AppWindow.Show(true);
            _window?.Activate();
            WindowFocus.BringToFront(MainHwnd);
            var uri = SingleInstance.TryGetAuthUri(args)
                      ?? ProtocolRegistration.ConsumePendingUri();
            ProtocolRegistration.ClearPendingUri();
            HandleIncomingUri(uri);
        });
    }

    private void HandleIncomingUri(Uri? uri)
    {
        if (uri is null) return;
        if (DemarNavigation.TryParsePayReturn(uri, out var payOk))
        {
            _main?.Shell?.HandlePayReturn(payOk);
            return;
        }
        if (DemarNavigation.TryParseAccountSheet(uri, out var accountSheet))
        {
            _main?.Shell?.OpenAccountSheet(accountSheet);
            return;
        }
        if (DemarNavigation.TryParseTrafficRoute(uri))
        {
            AppServices.Vpn.RaiseTrafficLimitReached();
            return;
        }
        if (DemarNavigation.TryParseSupportRoute(uri, out var supportRoute))
        {
            _main?.Shell?.OpenSupport(supportRoute);
            return;
        }
        _ = HandleAuthUriAsync(uri);
    }

    private async Task HandleAuthUriAsync(Uri? uri)
    {
        if (uri is null || !DemarNavigation.TryParseAuthHandoff(uri, out var token, out var code)) return;
        if (Interlocked.Exchange(ref _handoffInFlight, 1) == 1) return;
        try
        {
            AppLog.Append($"auth: oauth callback {uri.GetLeftPart(UriPartial.Path)}");
            if (token is not null)
                await AuthRepository.CompleteClientTokenAsync(token);
            else if (code is not null)
                await AuthRepository.CompleteExchangeAsync(code);
            else return;

            AppServices.Auth.NotifyChanged();
            await AppServices.Vpn.RefreshServersAsync();
            _main?.Shell?.OnExternalSignIn();
        }
        catch (Exception ex)
        {
            AppLog.Append($"auth: oauth handoff failed — {ex.Message}");
            _main?.Shell?.ShowLogin();
        }
        finally
        {
            Interlocked.Exchange(ref _handoffInFlight, 0);
        }
    }
}
