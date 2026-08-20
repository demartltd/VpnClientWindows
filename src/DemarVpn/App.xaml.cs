using DemarVpn.Services;
using DemarVpn.Services.Auth;
using DemarVpn.Services.Support;
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
        if (uri is not null && TryParseSupportRoute(uri, out var supportRoute))
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
            var uri = SingleInstance.TryGetAuthUri(args)
                      ?? ProtocolRegistration.ConsumePendingUri();
            if (uri is not null && TryParseSupportRoute(uri, out var supportRoute))
                _main?.Shell?.OpenSupport(supportRoute);
            else
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

    /// <summary>
    /// demar://settings/support · …/ticket/{id} · …/new/{themeId}
    /// (also accepts demarvpn:// and optional open/ prefix).
    /// </summary>
    internal static bool TryParseSupportRoute(Uri uri, out SupportRoute? route)
    {
        route = null;
        if (!uri.Scheme.Equals("demar", StringComparison.OrdinalIgnoreCase)
            && !uri.Scheme.Equals("demarvpn", StringComparison.OrdinalIgnoreCase))
            return false;

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(uri.Host))
            parts.Add(uri.Host);
        parts.AddRange(uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries));
        if (parts.Count > 0 && parts[0].Equals("open", StringComparison.OrdinalIgnoreCase))
            parts.RemoveAt(0);
        if (parts.Count < 2) return false;
        if (!parts[0].Equals("settings", StringComparison.OrdinalIgnoreCase)) return false;
        if (!parts[1].Equals("support", StringComparison.OrdinalIgnoreCase)) return false;

        if (parts.Count == 2)
        {
            route = null;
            return true;
        }
        if (parts.Count >= 4 && parts[2].Equals("ticket", StringComparison.OrdinalIgnoreCase))
        {
            route = new SupportRoute.Chat(parts[3]);
            return true;
        }
        if (parts.Count >= 4 && parts[2].Equals("new", StringComparison.OrdinalIgnoreCase))
        {
            route = new SupportRoute.Draft(parts[3]);
            return true;
        }
        return false;
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
