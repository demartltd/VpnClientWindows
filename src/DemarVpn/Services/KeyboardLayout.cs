using System.Runtime.InteropServices;

namespace DemarVpn.Services;

/// <summary>Win32 input locale (hardware keyboard). WinUI has no InputLanguageManager.</summary>
internal static class KeyboardLayout
{
    const string EnUs = "00000409";
    const uint KlfActivate = 0x00000001;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern nint LoadKeyboardLayout(string pwszKLID, uint flags);

    [DllImport("user32.dll")]
    static extern nint ActivateKeyboardLayout(nint hkl, uint flags);

    [DllImport("user32.dll")]
    static extern nint GetKeyboardLayout(uint idThread);

    public static nint Current() => GetKeyboardLayout(0);

    public static void PreferUsEnglish()
    {
        var hkl = LoadKeyboardLayout(EnUs, KlfActivate);
        if (hkl != 0)
            ActivateKeyboardLayout(hkl, 0);
    }

    public static void Activate(nint hkl)
    {
        if (hkl != 0)
            ActivateKeyboardLayout(hkl, 0);
    }
}
