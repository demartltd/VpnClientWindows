using System.Runtime.InteropServices;

namespace DemarVpn.Services;

/// <summary>Bring the main HWND to the foreground after protocol activation (browser holds FG lock).</summary>
internal static class WindowFocus
{
    const int SwRestore = 9;
    static readonly nint HwndTopmost = -1;
    static readonly nint HwndNotopmost = -2;
    const uint SwpNomove = 0x0002;
    const uint SwpNosize = 0x0001;
    const uint SwpShowwindow = 0x0040;

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [DllImport("user32.dll")]
    static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(int dwProcessId);

    [DllImport("kernel32.dll")]
    static extern uint GetCurrentThreadId();

    /// <summary>Call from the short-lived protocol process so the running app may steal focus.</summary>
    public static void AllowForeground(int processId)
    {
        if (processId > 0)
            AllowSetForegroundWindow(processId);
    }

    public static void BringToFront(nint hwnd)
    {
        if (hwnd == 0) return;
        if (IsIconic(hwnd))
            ShowWindow(hwnd, SwRestore);

        var fg = GetForegroundWindow();
        var fgThread = GetWindowThreadProcessId(fg, out _);
        var curThread = GetCurrentThreadId();
        var attached = fgThread != 0 && fgThread != curThread && AttachThreadInput(curThread, fgThread, true);
        try
        {
            SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNomove | SwpNosize | SwpShowwindow);
            SetWindowPos(hwnd, HwndNotopmost, 0, 0, 0, 0, SwpNomove | SwpNosize | SwpShowwindow);
            SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached)
                AttachThreadInput(curThread, fgThread, false);
        }
    }
}
