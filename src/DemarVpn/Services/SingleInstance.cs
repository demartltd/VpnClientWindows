using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;

namespace DemarVpn.Services;

internal static class SingleInstance
{
    const string Key = "DemarVpn";
    const uint MbOkInfo = 0x40;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    public static bool TryClaim(Action<AppActivationArguments>? onActivated = null)
    {
        var instance = AppInstance.FindOrRegisterForKey(Key);
        var args = AppInstance.GetCurrent().GetActivatedEventArgs();
        var demarUri = TryGetAuthUri(args);

        if (!instance.IsCurrent)
        {
            // Unpackaged: protocol often arrives only as argv — stage for the running instance.
            if (demarUri is not null)
                ProtocolRegistration.WritePendingUri(demarUri);

            instance.RedirectActivationToAsync(args).AsTask().GetAwaiter().GetResult();
            if (demarUri is null && !IsProtocolActivation(args))
                MessageBoxW(IntPtr.Zero, Strings.AlreadyRunningMessage, Strings.AppName, MbOkInfo);
            Application.Current.Exit();
            return false;
        }

        if (onActivated != null)
            instance.Activated += (_, e) => onActivated(e);
        return true;
    }

    public static Uri? TryGetAuthUri(AppActivationArguments args)
    {
        if (TryGetProtocolUri(args) is { } protocol)
            return protocol;

        if (args.Kind == ExtendedActivationKind.Launch
            && args.Data is ILaunchActivatedEventArgs launch
            && !string.IsNullOrWhiteSpace(launch.Arguments))
        {
            var fromLaunch = ProtocolRegistration.FindDemarUri(
                launch.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            if (fromLaunch is not null) return fromLaunch;
        }

        return ProtocolRegistration.FindDemarUri(Environment.GetCommandLineArgs().Skip(1));
    }

    public static Uri? TryGetProtocolUri(AppActivationArguments args)
    {
        if (args.Kind != ExtendedActivationKind.Protocol) return null;
        if (args.Data is IProtocolActivatedEventArgs protocol)
            return protocol.Uri;
        return null;
    }

    public static bool IsProtocolActivation(AppActivationArguments args) =>
        args.Kind == ExtendedActivationKind.Protocol
        || TryGetAuthUri(args) is not null;
}
