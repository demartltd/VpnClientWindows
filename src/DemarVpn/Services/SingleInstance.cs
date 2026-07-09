using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace DemarVpn.Services;

internal static class SingleInstance
{
    const string Key = "DemarVpn";
    const uint MbOkInfo = 0x40;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    public static bool TryClaim(Action? onActivated = null)
    {
        var instance = AppInstance.FindOrRegisterForKey(Key);
        if (!instance.IsCurrent)
        {
            instance.RedirectActivationToAsync(AppInstance.GetCurrent().GetActivatedEventArgs())
                .AsTask().GetAwaiter().GetResult();
            MessageBoxW(IntPtr.Zero, Strings.AlreadyRunningMessage, Strings.AppName, MbOkInfo);
            Application.Current.Exit();
            return false;
        }

        if (onActivated != null)
            instance.Activated += (_, _) => onActivated();
        return true;
    }
}
