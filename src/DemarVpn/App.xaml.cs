using DemarVpn.Services;
using DemarVpn.Services.Auth;
using DemarVpn.Theme;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace DemarVpn;

public partial class App : Application
{
    private Window? _window;
    private MainWindow? _main;
    private int _handoffInFlight;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (!SingleInstance.TryClaim(OnRedirectedActivation)) return;

        AppServices.Init();
        ProtocolRegistration.EnsureRegistered();
        DemarPalette.Apply(Resources);
        _main = new MainWindow();
        _window = _main;
        _window.Activate();

        var launchArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
        _ = HandleAuthUriAsync(SingleInstance.TryGetAuthUri(launchArgs)
                               ?? ProtocolRegistration.ConsumePendingUri());
        _ = AppServices.BootstrapSessionAsync();
    }

    private void OnRedirectedActivation(AppActivationArguments args)
    {
        _window?.DispatcherQueue.TryEnqueue(() =>
        {
            _window?.AppWindow.Show(true);
            _window?.Activate();
            var uri = SingleInstance.TryGetAuthUri(args)
                      ?? ProtocolRegistration.ConsumePendingUri();
            _ = HandleAuthUriAsync(uri);
        });
    }

    private async Task HandleAuthUriAsync(Uri? uri)
    {
        if (uri is null || !TryParseAuthHandoff(uri, out var token, out var code)) return;
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

    internal static bool TryParseAuthHandoff(Uri uri, out string? token, out string? code)
    {
        token = null;
        code = null;
        if (!string.Equals(uri.Scheme, "demar", StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.Equals(uri.Host, "auth", StringComparison.OrdinalIgnoreCase)) return false;
        var path = uri.AbsolutePath.Trim('/');
        if (path.Length > 0 && !string.Equals(path, "callback", StringComparison.OrdinalIgnoreCase))
            return false;
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            var key = Uri.UnescapeDataString(part[..eq]);
            var val = Uri.UnescapeDataString(part[(eq + 1)..]);
            if (string.IsNullOrWhiteSpace(val)) continue;
            if (key.Equals("token", StringComparison.OrdinalIgnoreCase)) token = val;
            else if (key.Equals("code", StringComparison.OrdinalIgnoreCase)) code = val;
        }
        return token is not null || code is not null;
    }
}
