using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;

namespace DemarVpn.Services;

/// <summary>Bounds, persist, Maximize→toggle max↔default (short anim; content hidden → solid bg).</summary>
internal sealed class WindowSizing
{
    const int AnimMs = 150;
    const uint WmSyscommand = 0x0112;
    const nint ScMaximize = 0xF030;

    static readonly string SizePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DemarVpn", "window_size");

    readonly Window _window;
    readonly AppWindow _app;
    readonly FrameworkElement _content;
    readonly SizeInt32 _min, _max, _def;
    readonly SubclassProc _proc; // keep alive
    readonly DispatcherQueueTimer _timer;

    bool _enlarged, _animating;
    SystemBackdrop? _backdrop;
    RectInt32 _from, _to;
    long _t0;

    public WindowSizing(Window window, FrameworkElement content)
    {
        _window = window;
        _app = window.AppWindow;
        _content = content;
        _min = Res("AppMin", "Width", "Height");
        _max = Res("AppMax", "Width", "Height");
        _def = Res("AppDefault", "Width", "Height");
        _timer = window.DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(8);
        _timer.Tick += OnTick;

        if (_app.Presenter is OverlappedPresenter p)
        {
            p.PreferredMinimumWidth = _min.Width;
            p.PreferredMinimumHeight = _min.Height;
            p.PreferredMaximumWidth = _max.Width;
            p.PreferredMaximumHeight = _max.Height;
            var work = Work();
            var size = Clamp(TryLoad() ?? _def, work);
            _enlarged = size.Width >= _max.Width && size.Height >= _max.Height;
            _app.MoveAndResize(Center(size, work));
            window.Closed += (_, _) =>
            {
                if (_app.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Restored })
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(SizePath)!);
                        File.WriteAllText(SizePath, $"{_app.Size.Width},{_app.Size.Height}");
                    }
                    catch { /* ignore */ }
            };
        }

        _proc = (h, msg, w, l, _, _) =>
        {
            if (msg == WmSyscommand && (w & 0xFFF0) == ScMaximize) { Toggle(); return 0; }
            return DefSubclassProc(h, msg, w, l);
        };
        SetWindowSubclass(WindowNative.GetWindowHandle(window), _proc, 1, 0);
        _app.Changed += OnChanged;
    }

    void Toggle()
    {
        var work = Work();
        _enlarged = !_enlarged;
        Animate(Center(Clamp(_enlarged ? _max : _def, work), work));
    }

    void Animate(RectInt32 to)
    {
        if (!_animating)
        {
            _animating = true;
            _backdrop = _window.SystemBackdrop;
            _window.SystemBackdrop = null; // opaque DemarBackground, no Mica black edges
            _content.Visibility = Visibility.Collapsed;
        }
        _from = new(_app.Position.X, _app.Position.Y, _app.Size.Width, _app.Size.Height);
        _to = to;
        _t0 = Environment.TickCount64;
        if (!_timer.IsRunning) _timer.Start();
    }

    void OnTick(DispatcherQueueTimer t, object _)
    {
        var u = Math.Clamp((Environment.TickCount64 - _t0) / (double)AnimMs, 0, 1);
        var e = 1 - Math.Pow(1 - u, 3);
        _app.MoveAndResize(new(
            Lerp(_from.X, _to.X, e), Lerp(_from.Y, _to.Y, e),
            Lerp(_from.Width, _to.Width, e), Lerp(_from.Height, _to.Height, e)));
        if (u < 1) return;
        t.Stop();
        _app.MoveAndResize(_to);
        _content.Visibility = Visibility.Visible;
        _window.SystemBackdrop = _backdrop;
        _animating = false;
    }

    void OnChanged(AppWindow _, AppWindowChangedEventArgs e)
    {
        if (_animating || (!e.DidSizeChange && !e.DidVisibilityChange)) return;
        if (_app.Presenter is OverlappedPresenter { State: not OverlappedPresenterState.Restored }) return;
        Ui.Run(_content.DispatcherQueue, () => _content.InvalidateArrange());
    }

    SizeInt32 Clamp(SizeInt32 s, RectInt32 w) => new(
        Math.Clamp(s.Width, _min.Width, Math.Min(_max.Width, w.Width)),
        Math.Clamp(s.Height, _min.Height, Math.Min(_max.Height, w.Height)));

    RectInt32 Work() => DisplayArea.GetFromWindowId(_app.Id, DisplayAreaFallback.Nearest).WorkArea;

    static RectInt32 Center(SizeInt32 s, RectInt32 w) => new(
        w.X + (w.Width - s.Width) / 2, w.Y + (w.Height - s.Height) / 2, s.Width, s.Height);

    static SizeInt32? TryLoad()
    {
        try
        {
            if (!File.Exists(SizePath)) return null;
            var p = File.ReadAllText(SizePath).Split(',');
            if (p.Length == 2 && int.TryParse(p[0], out var w) && int.TryParse(p[1], out var h) && w > 0 && h > 0)
                return new(w, h);
        }
        catch { /* ignore */ }
        return null;
    }

    static SizeInt32 Res(string prefix, string w, string h)
    {
        var r = Application.Current.Resources;
        return new((int)(double)r[prefix + w], (int)(double)r[prefix + h]);
    }

    static int Lerp(int a, int b, double t) => (int)Math.Round(a + (b - a) * t);

    delegate nint SubclassProc(nint h, uint m, nint w, nint l, nuint id, nint data);

    [DllImport("comctl32.dll", ExactSpelling = true)]
    static extern bool SetWindowSubclass(nint hWnd, SubclassProc fn, nuint id, nint data);

    [DllImport("comctl32.dll", ExactSpelling = true)]
    static extern nint DefSubclassProc(nint hWnd, uint msg, nint wParam, nint lParam);
}
