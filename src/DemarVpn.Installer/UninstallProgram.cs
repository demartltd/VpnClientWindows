using System.Runtime.InteropServices;
using System.Threading;

namespace DemarVpn.Installer;

static class UninstallProgram
{
    static int Main(string[] args)
    {
        var silent = args.Any(a =>
            a.Equals("/silent", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("/s", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("/verysilent", StringComparison.OrdinalIgnoreCase));
        if (silent)
        {
            try { InstallService.Uninstall(null); return 0; }
            catch { return 1; }
        }
        return UninstallUi.Run();
    }
}

/// <summary>Demar-styled Win32 uninstall window (AOT-safe, mirrors Updater chrome).</summary>
static unsafe class UninstallUi
{
    const string ClassName = "DemarVpnUninstall";
    const int W = 480, H = 320;
    const uint WmPaint = 0x000F, WmDestroy = 0x0002, WmClose = 0x0010;
    const uint WmLDown = 0x0201, WmLUp = 0x0202, WmMove = 0x0200, WmSetCursor = 0x0020;
    const uint MsgStatus = 0x0400, MsgDone = 0x0401, MsgFail = 0x0402;
    const int IdcHand = 32649, IdcArrow = 32512;

    static readonly uint Bg = Rgb(0x12, 0x18, 0x20), Surface = Rgb(0x1A, 0x22, 0x2C);
    static readonly uint SurfHover = Rgb(0x2A, 0x34, 0x40), Primary = Rgb(0xA5, 0xC8, 0xFF);
    static readonly uint Btn = Rgb(0x2A, 0x4A, 0x6E), BtnHover = Rgb(0x3A, 0x5A, 0x7E);
    static readonly uint BtnPress = Rgb(0x1E, 0x38, 0x55), OnBtn = Rgb(0xD8, 0xE8, 0xF8);
    static readonly uint Secondary = Rgb(0xB8, 0xC8, 0xDA), Outline = Rgb(0x4A, 0x55, 0x64);

    static nint _hwnd, _fontUi, _fontTitle, _fontBtn;
    static string _status = "", _action = InstallerStrings.Uninstall;
    static bool _busy, _done, _btnHover, _btnDown, _closeHover;
    static int _exit;
    static readonly WndProcDel _proc = WndProc;

    public static int Run()
    {
        var inst = GetModuleHandleW(null);
        var clsName = Marshal.StringToHGlobalUni(ClassName);
        var wc = new WndClassExW
        {
            cbSize = (uint)sizeof(WndClassExW),
            style = 3,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
            hInstance = inst,
            hCursor = LoadCursorW(0, IdcArrow),
            lpszClassName = clsName,
        };
        RegisterClassExW(ref wc);

        _fontUi = CreateFontW(-13, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        _fontTitle = CreateFontW(-26, 0, 0, 0, 600, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI Semibold");
        _fontBtn = CreateFontW(-15, 0, 0, 0, 600, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI Semibold");

        var x = (GetSystemMetrics(0) - W) / 2;
        var y = (GetSystemMetrics(1) - H) / 2;
        _hwnd = CreateWindowExW(0, ClassName, "Demar VPN", 0x80000000, x, y, W, H, 0, 0, inst, 0);
        if (_hwnd == 0) return 1;
        ShowWindow(_hwnd, 5);

        Msg msg;
        while (GetMessageW(out msg, 0, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
        return _exit;
    }

    static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WmPaint: Paint(hwnd); return 0;
            case WmLDown: OnDown(hwnd, (short)lParam, (short)((long)lParam >> 16)); return 0;
            case WmLUp: OnUp(hwnd, (short)lParam, (short)((long)lParam >> 16)); return 0;
            case WmMove: OnMove(hwnd, (short)lParam, (short)((long)lParam >> 16)); return 0;
            case WmSetCursor when (ushort)lParam == 1:
                GetCursorPos(out var pt); ScreenToClient(hwnd, ref pt);
                SetCursor(LoadCursorW(0, InBtn(pt.x, pt.y) || InClose(pt.x, pt.y) ? IdcHand : IdcArrow));
                return 1;
            case MsgStatus:
                _status = Marshal.PtrToStringUni(wParam) ?? "";
                if (wParam != 0) Marshal.FreeHGlobal(wParam);
                InvalidateRect(hwnd, 0, 0); return 0;
            case MsgDone:
                _busy = false; _done = true; _action = InstallerStrings.Close; _status = InstallerStrings.Uninstalled; _exit = 0;
                InvalidateRect(hwnd, 0, 0);
                return 0;
            case MsgFail:
                _busy = false; _action = InstallerStrings.Uninstall; _exit = 1;
                _status = Marshal.PtrToStringUni(wParam) ?? InstallerStrings.Error;
                if (wParam != 0) Marshal.FreeHGlobal(wParam);
                InvalidateRect(hwnd, 0, 0); return 0;
            case WmClose: DestroyWindow(hwnd); return 0;
            case WmDestroy: PostQuitMessage(0); return 0;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    static void Paint(nint hwnd)
    {
        PaintStruct ps;
        var hdc = BeginPaint(hwnd, out ps);
        var mem = CreateCompatibleDC(hdc);
        var bmp = CreateCompatibleBitmap(hdc, W, H);
        var oldBmp = SelectObject(mem, bmp);

        Fill(mem, 0, 0, W, H, Bg);
        Fill(mem, 0, 0, W, 40, Surface);
        TextAt(mem, 16, 11, InstallerStrings.ChromeUninstall, _fontUi, Primary);
        // Close inset so hover never covers the 1px outline
        Fill(mem, W - 41, 1, 39, 38, _closeHover ? SurfHover : Surface);
        Center(mem, W - 41, 1, 39, 38, "✕", _fontUi, Secondary);
        TextAt(mem, 28, 64, $"Demar VPN  {InstallService.AppVersion}", _fontTitle, Primary);
        TextAt(mem, 28, 108, InstallerStrings.SubtitleUninstall, _fontUi, Secondary);
        if (_status.Length > 0) TextAt(mem, 28, 160, _status, _fontUi, Secondary);
        if (_busy) Fill(mem, 28, 220, 424, 4, BtnHover);

        var bc = _btnDown || (_busy && !_done) ? BtnPress : _btnHover ? BtnHover : Btn;
        Fill(mem, 28, 244, 424, 44, bc);
        Center(mem, 28, 244, 424, 44, _action, _fontBtn, OnBtn);
        Frame(mem, Outline);

        BitBlt(hdc, 0, 0, W, H, mem, 0, 0, 0x00CC0020);
        SelectObject(mem, oldBmp);
        DeleteObject(bmp);
        DeleteDC(mem);
        EndPaint(hwnd, ref ps);
    }

    static void OnDown(nint hwnd, int x, int y)
    {
        if (InClose(x, y)) { PostMessageW(hwnd, WmClose, 0, 0); return; }
        if (y < 40) { ReleaseCapture(); SendMessageW(hwnd, 0xA1, 2, 0); return; }
        if (InBtn(x, y)) { _btnDown = true; InvalidateRect(hwnd, 0, 0); }
    }

    static void OnUp(nint hwnd, int x, int y)
    {
        if (_btnDown && InBtn(x, y)) Click(hwnd);
        _btnDown = false;
        InvalidateRect(hwnd, 0, 0);
    }

    static void OnMove(nint hwnd, int x, int y)
    {
        var bh = InBtn(x, y); var ch = InClose(x, y);
        if (bh == _btnHover && ch == _closeHover) return;
        _btnHover = bh; _closeHover = ch;
        InvalidateRect(hwnd, 0, 0);
    }

    static void Click(nint hwnd)
    {
        if (_done) { PostMessageW(hwnd, WmClose, 0, 0); return; }
        if (_busy) return;
        _busy = true; _status = InstallerStrings.Stopping;
        InvalidateRect(hwnd, 0, 0);
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                InstallService.Uninstall(new Progress<string>(s =>
                    PostMessageW(hwnd, MsgStatus, Marshal.StringToHGlobalUni(s), 0)));
                PostMessageW(hwnd, MsgDone, 0, 0);
            }
            catch (Exception ex)
            {
                PostMessageW(hwnd, MsgFail, Marshal.StringToHGlobalUni(ex.Message), 0);
            }
        });
    }

    static bool InBtn(int x, int y) => x is >= 28 and < 452 && y is >= 244 and < 288;
    static bool InClose(int x, int y) => x >= W - 41 && x < W - 1 && y >= 1 && y < 39;
    static uint Rgb(int r, int g, int b) => (uint)(r | (g << 8) | (b << 16));

    static void Fill(nint hdc, int x, int y, int w, int h, uint c)
    {
        var r = new Rect(x, y, x + w, y + h);
        var br = CreateSolidBrush(c);
        FillRect(hdc, ref r, br);
        DeleteObject(br);
    }

    static void Frame(nint hdc, uint c)
    {
        var pen = CreatePen(0, 1, c);
        var old = SelectObject(hdc, pen);
        MoveToEx(hdc, 0, 0, 0); LineTo(hdc, W - 1, 0); LineTo(hdc, W - 1, H - 1); LineTo(hdc, 0, H - 1); LineTo(hdc, 0, 0);
        SelectObject(hdc, old); DeleteObject(pen);
    }

    static void TextAt(nint hdc, int x, int y, string t, nint font, uint c)
    {
        var old = SelectObject(hdc, font);
        SetBkMode(hdc, 1); SetTextColor(hdc, c);
        TextOutW(hdc, x, y, t, t.Length);
        SelectObject(hdc, old);
    }

    static void Center(nint hdc, int x, int y, int w, int h, string t, nint font, uint c)
    {
        var old = SelectObject(hdc, font);
        SetBkMode(hdc, 1); SetTextColor(hdc, c);
        var r = new Rect(x, y, x + w, y + h);
        DrawTextW(hdc, t, t.Length, ref r, 0x25);
        SelectObject(hdc, old);
    }

    delegate nint WndProcDel(nint h, uint m, nint w, nint l);

    [StructLayout(LayoutKind.Sequential)]
    struct WndClassExW
    {
        public uint cbSize, style;
        public nint lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public nint hInstance, hIcon, hCursor, hbrBackground, lpszMenuName, lpszClassName, hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PaintStruct
    {
        public nint hdc;
        public int fErase;
        public Rect rcPaint;
        public int fRestore, fIncUpdate;
        public fixed byte rgbReserved[32];
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Rect
    {
        public int left, top, right, bottom;
        public Rect(int l, int t, int r, int b) { left = l; top = t; right = r; bottom = b; }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Point { public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    struct Msg
    {
        public nint hwnd; public uint message; public nint wParam, lParam; public uint time; public Point pt;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern ushort RegisterClassExW(ref WndClassExW c);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern nint CreateWindowExW(uint ex, string cn, string t, uint s, int x, int y, int w, int h, nint p, nint m, nint i, nint lp);
    [DllImport("user32.dll")] static extern bool ShowWindow(nint h, int c);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetMessageW(out Msg m, nint h, uint min, uint max);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref Msg m);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern nint DispatchMessageW(ref Msg m);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern nint DefWindowProcW(nint h, uint m, nint w, nint l);
    [DllImport("user32.dll")] static extern void PostQuitMessage(int c);
    [DllImport("user32.dll")] static extern bool DestroyWindow(nint h);
    [DllImport("user32.dll")] static extern bool PostMessageW(nint h, uint m, nint w, nint l);
    [DllImport("user32.dll")] static extern nint SendMessageW(nint h, uint m, nint w, nint l);
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern bool InvalidateRect(nint h, nint r, int e);
    [DllImport("user32.dll")] static extern nint BeginPaint(nint h, out PaintStruct ps);
    [DllImport("user32.dll")] static extern bool EndPaint(nint h, ref PaintStruct ps);
    [DllImport("user32.dll")] static extern int FillRect(nint d, ref Rect r, nint b);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Point p);
    [DllImport("user32.dll")] static extern bool ScreenToClient(nint h, ref Point p);
    [DllImport("user32.dll")] static extern nint SetCursor(nint c);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern nint LoadCursorW(nint i, int id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int DrawTextW(nint d, string t, int n, ref Rect r, uint f);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern nint GetModuleHandleW(string? n);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    static extern nint CreateFontW(int h, int w, int e, int o, int wt, uint i, uint u, uint s, uint c, uint o2, uint q, uint p, uint f, string face);
    [DllImport("gdi32.dll")] static extern nint CreateSolidBrush(uint c);
    [DllImport("gdi32.dll")] static extern nint CreatePen(int s, int w, uint c);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(nint o);
    [DllImport("gdi32.dll")] static extern nint SelectObject(nint d, nint o);
    [DllImport("gdi32.dll")] static extern nint CreateCompatibleDC(nint d);
    [DllImport("gdi32.dll")] static extern nint CreateCompatibleBitmap(nint d, int w, int h);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(nint d);
    [DllImport("gdi32.dll")] static extern bool BitBlt(nint d, int x, int y, int w, int h, nint s, int sx, int sy, uint r);
    [DllImport("gdi32.dll")] static extern int SetBkMode(nint d, int m);
    [DllImport("gdi32.dll")] static extern uint SetTextColor(nint d, uint c);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern bool TextOutW(nint d, int x, int y, string t, int n);
    [DllImport("gdi32.dll")] static extern bool MoveToEx(nint d, int x, int y, nint p);
    [DllImport("gdi32.dll")] static extern bool LineTo(nint d, int x, int y);
}
