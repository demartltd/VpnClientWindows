using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;

namespace DemarVpn.Services;

internal static class SingleInstance
{
    const string Key = "DemarVpn";

    static string PidPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DemarVpn", "instance.pid");

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

            // This process briefly owns FG (browser/shortcut launched us) — unlock main instance.
            if (TryReadOwnerPid(out var ownerPid))
                WindowFocus.AllowForeground(ownerPid);

            instance.RedirectActivationToAsync(args).AsTask().GetAwaiter().GetResult();
            Application.Current.Exit();
            return false;
        }

        WriteOwnerPid();
        if (onActivated != null)
            instance.Activated += (_, e) => onActivated(e);
        return true;
    }

    static void WriteOwnerPid()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PidPath)!);
            File.WriteAllText(PidPath, Environment.ProcessId.ToString());
        }
        catch { /* ignore */ }
    }

    static bool TryReadOwnerPid(out int pid)
    {
        pid = 0;
        try
        {
            if (!File.Exists(PidPath)) return false;
            return int.TryParse(File.ReadAllText(PidPath).Trim(), out pid) && pid > 0;
        }
        catch
        {
            return false;
        }
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
