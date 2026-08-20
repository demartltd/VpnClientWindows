using DemarVpn.Services;
using DemarVpn.Services.Auth;
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

    public App() => InitializeComponent();

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
        _window.Activate();

        var launchArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
        var uri = SingleInstance.TryGetAuthUri(launchArgs)
                  ?? ProtocolRegistration.ConsumePendingUri();
        if (uri is not null && DemarNavigation.TryParseSupportRoute(uri, out var supportRoute))
            _main.Shell?.OpenSupport(supportRoute);
        else
            _ = HandleAuthUriAsync(uri);
        _ = BootstrapAsync();
    }

    private async Task BootstrapAsync()
    {
        await AppServices.BootstrapSessionAsync();
        AppServices.Update.StartPeriodicChecks();
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
            if (uri is not null && DemarNavigation.TryParseSupportRoute(uri, out var supportRoute))
                _main?.Shell?.OpenSupport(supportRoute);
            else
                _ = HandleAuthUriAsync(uri);
        });
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
