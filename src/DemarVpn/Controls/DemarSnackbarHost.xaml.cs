using System.Collections.Generic;
using DemarVpn.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;

namespace DemarVpn.Controls;

public sealed partial class DemarSnackbarHost : UserControl
{
    private static readonly Dictionary<SnackKind, (string Glyph, uint Bg, uint Fg)> Styles = new()
    {
        [SnackKind.Success] = ("\uE73E", 0xFF1E3D2F, 0xFF81C784),
        [SnackKind.Error] = ("\uE783", 0xFF4A2328, 0xFFFFB4AB),
        [SnackKind.Warning] = ("\uE7BA", 0xFF3A3020, 0xFFFFB74D),
        [SnackKind.Info] = ("\uE946", 0xFF2A4A6E, 0xFFA5C8FF),
    };

    private static readonly Dictionary<uint, SolidColorBrush> Brushes = new();

    private readonly DispatcherTimer _timer = new();
    private SnackMessage? _shown;
    private bool _wide;

    public DemarSnackbarHost()
    {
        InitializeComponent();
        AppServices.Snackbar.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Services.SnackbarService.Current))
                DispatcherQueue.TryEnqueue(Refresh);
        };
        _timer.Tick += (_, _) => AppServices.Snackbar.Dismiss();
    }

    public void SetWideLayout(bool wide)
    {
        if (_wide == wide) return;
        _wide = wide;
        if (wide)
        {
            Width = (double)Application.Current.Resources["ToolbarTabWidth"] * 3;
            HorizontalAlignment = HorizontalAlignment.Left;
        }
        else
        {
            ClearValue(WidthProperty);
            HorizontalAlignment = HorizontalAlignment.Stretch;
        }
    }

    private void Refresh()
    {
        var msg = AppServices.Snackbar.Current;
        if (msg is null)
        {
            Visibility = Visibility.Collapsed;
            IsHitTestVisible = false;
            _shown = null;
            _timer.Stop();
            return;
        }
        if (msg == _shown) return;

        _shown = msg;
        var style = Styles[msg.Kind];
        Message.Text = msg.Text;
        Icon.Glyph = style.Glyph;
        Icon.Foreground = GetBrush(style.Fg);
        Host.Background = GetBrush(style.Bg);
        Message.Foreground = GetBrush(0xFFE8EEF4);
        Visibility = Visibility.Visible;
        IsHitTestVisible = true;
        _timer.Interval = msg.Kind == SnackKind.Error ? TimeSpan.FromSeconds(6) : TimeSpan.FromSeconds(3);
        _timer.Start();
    }

    private static SolidColorBrush GetBrush(uint argb)
    {
        if (Brushes.TryGetValue(argb, out var brush)) return brush;
        brush = new SolidColorBrush(Color.FromArgb(
            (byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        Brushes[argb] = brush;
        return brush;
    }

    private void OnDismiss(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        AppServices.Snackbar.Dismiss();
    }

    private void OnCopyTap(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (AppServices.Snackbar.Current is null) return;
        var data = new DataPackage();
        data.SetText(AppServices.Snackbar.Current.Text);
        Clipboard.SetContent(data);
    }
}
