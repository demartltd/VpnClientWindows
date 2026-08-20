using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using DemarVpn.Services;
using DemarVpn.Services.Support;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;

namespace DemarVpn.Controls;

public sealed partial class SupportBubble : UserControl
{
    private const double BubbleMax = 260;
    private const double AlbumGap = 3;

    private SupportMessage? _msg;
    private string? _ticketId;
    private string? _userId;
    private string? _loginAt;
    private bool _canWrite;
    private int _bindEpoch;

    public event Action<SupportMessage>? EditRequested;
    public event Action<SupportMessage>? DeleteRequested;

    public SupportBubble() => InitializeComponent();

    public void Bind(SupportMessage msg, string ticketId, string userId, string loginAt, bool canWrite)
    {
        var epoch = ++_bindEpoch;
        _msg = msg;
        _ticketId = ticketId;
        _userId = userId;
        _loginAt = loginAt;
        _canWrite = canWrite;

        var isUser = msg.IsUser;
        Root.HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        Root.MaxWidth = BubbleMax + 24;
        BubbleBorder.Background = (Brush)Application.Current.Resources[
            isUser ? "DemarPrimaryMutedBrush" : "DemarSurfaceHighBrush"];
        BubbleBorder.CornerRadius = isUser
            ? new CornerRadius(16, 4, 16, 16)
            : new CornerRadius(4, 16, 16, 16);

        MediaHost.Children.Clear();
        BodyBlock.Inlines.Clear();
        BodyBlock.Visibility = Visibility.Collapsed;

        if (msg.Deleted)
        {
            TimeBlock.Text = "";
            TimeBlock.Visibility = Visibility.Collapsed;
            BodyBlock.Visibility = Visibility.Visible;
            BodyBlock.Inlines.Add(new Run
            {
                Text = Strings.SupportDeleted,
                FontStyle = Windows.UI.Text.FontStyle.Italic,
            });
            BodyBlock.Opacity = 0.6;
            return;
        }
        BodyBlock.Opacity = 1;
        TimeBlock.Visibility = Visibility.Visible;
        TimeBlock.Text = SupportApi.FormatMsgTime(msg.CreatedAt) ?? "";

        if (!string.IsNullOrWhiteSpace(msg.Text))
        {
            BodyBlock.Visibility = Visibility.Visible;
            FillLinkified(msg.Text!);
        }

        var photos = msg.Media.Where(m => m.IsPhoto && !string.IsNullOrWhiteSpace(m.Url)).ToList();
        var files = msg.Media.Where(m => !(m.IsPhoto && !string.IsNullOrWhiteSpace(m.Url))).ToList();
        if (photos.Count > 0)
            MediaHost.Children.Add(BuildAlbum(photos, epoch));
        foreach (var f in files)
            MediaHost.Children.Add(BuildFileRow(f));
    }

    private void FillLinkified(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0) BodyBlock.Inlines.Add(new LineBreak());
            AppendLinkifiedLine(lines[i]);
        }
    }

    private void AppendLinkifiedLine(string text)
    {
        var last = 0;
        foreach (Match m in SupportUrl.Pattern().Matches(text))
        {
            if (m.Index > last)
                BodyBlock.Inlines.Add(new Run { Text = text[last..m.Index] });
            var url = m.Value.TrimEnd('.', ',', ')', ']');
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                var link = new Hyperlink { NavigateUri = uri };
                link.Inlines.Add(new Run { Text = url });
                BodyBlock.Inlines.Add(link);
            }
            else
                BodyBlock.Inlines.Add(new Run { Text = m.Value });
            last = m.Index + m.Length;
        }
        if (last < text.Length)
            BodyBlock.Inlines.Add(new Run { Text = text[last..] });
        else if (text.Length == 0 && last == 0)
            BodyBlock.Inlines.Add(new Run { Text = "" });
    }

    private UIElement BuildAlbum(List<SupportMedia> photos, int epoch)
    {
        var album = new StackPanel { Spacing = AlbumGap, Margin = new Thickness(0, 4, 0, 0) };
        if (photos.Count == 1)
        {
            album.Children.Add(MakeThumbSlot(photos[0], BubbleMax, 320, epoch));
            return album;
        }

        var cols = photos.Count switch
        {
            <= 4 => 2,
            <= 9 => 3,
            _ => 4,
        };
        var thumbH = photos.Count switch
        {
            2 => 160.0,
            3 or 4 => 130.0,
            5 or 6 => 100.0,
            >= 7 and <= 9 => 84.0,
            _ => 68.0,
        };
        var thumbW = (BubbleMax - AlbumGap * (cols - 1)) / cols;

        for (var i = 0; i < photos.Count; i += cols)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = AlbumGap };
            foreach (var photo in photos.Skip(i).Take(cols))
                row.Children.Add(MakeThumbSlot(photo, thumbW, thumbH, epoch));
            album.Children.Add(row);
        }
        return album;
    }

    /// <summary>Reserve frame from metadata; refine after decode.</summary>
    private UIElement MakeThumbSlot(SupportMedia media, double maxW, double maxH, int epoch)
    {
        var (w, h) = AlbumFrame(maxW, maxH, PhotoAspect(media));
        var host = new Border
        {
            Width = w,
            Height = h,
            CornerRadius = new CornerRadius(6),
            Background = (Brush)Application.Current.Resources["DemarSurfaceBrush"],
            Tag = (maxW, maxH),
        };
        var ring = new ProgressRing
        {
            Width = 22,
            Height = 22,
            IsActive = true,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        host.Child = ring;
        _ = LoadThumbAsync(host, media, epoch);
        return host;
    }

    private async Task LoadThumbAsync(Border host, SupportMedia media, int epoch)
    {
        if (_userId is null || _loginAt is null || string.IsNullOrWhiteSpace(media.Url)) return;
        var (maxW, maxH) = host.Tag is ValueTuple<double, double> t ? t : (BubbleMax, 320.0);
        try
        {
            var bytes = await SupportApi.LoadMediaBytesAsync(_userId, _loginAt, media.Url!, _ticketId);
            if (epoch != _bindEpoch) return;
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(bytes.AsBuffer());
            stream.Seek(0);
            var bmp = new BitmapImage();
            await bmp.SetSourceAsync(stream);
            if (epoch != _bindEpoch) return;

            var aspect = bmp.PixelWidth > 0 && bmp.PixelHeight > 0
                ? (double)bmp.PixelWidth / bmp.PixelHeight
                : PhotoAspect(media);
            var (w, h) = AlbumFrame(maxW, maxH, aspect);
            host.Width = w;
            host.Height = h;
            host.Child = new Image
            {
                Source = bmp,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }
        catch
        {
            if (epoch != _bindEpoch) return;
            host.Child = new FontIcon
            {
                Glyph = "\uE91B",
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.5,
            };
        }
    }

    private static double PhotoAspect(SupportMedia media)
    {
        if (media.Width is > 0 && media.Height is > 0)
            return (double)media.Width.Value / media.Height.Value;
        return 4.0 / 3.0;
    }

    /** Fit inside maxW×maxH, keep aspect (iOS SupportMediaThumb.frameSize). */
    private static (double W, double H) AlbumFrame(double maxW, double maxH, double aspect)
    {
        var r = Math.Max(0.25, aspect);
        var w = maxW;
        var h = w / r;
        if (h > maxH)
        {
            h = maxH;
            w = h * r;
            if (w > maxW)
            {
                w = maxW;
                h = w / r;
            }
        }
        return (w, h);
    }

    private static UIElement BuildFileRow(SupportMedia media)
    {
        var name = media.FileName ?? media.Type ?? Strings.SupportAttachment;
        var res = Application.Current.Resources;
        var surface = ((SolidColorBrush)res["DemarSurfaceBrush"]).Color;
        var chipBg = new SolidColorBrush(Windows.UI.Color.FromArgb(0x73, surface.R, surface.G, surface.B));
        var row = new Border
        {
            Margin = new Thickness(0, 6, 0, 0),
            CornerRadius = new CornerRadius(12),
            Background = chipBg,
            Padding = new Thickness(10, 8, 10, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsHitTestVisible = false,
        };
        var panel = new Grid { ColumnSpacing = 10 };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var iconWell = new Border
        {
            Width = 42,
            Height = 42,
            CornerRadius = new CornerRadius(21),
            Background = (Brush)res["DemarPrimaryMutedBrush"],
            Child = new FontIcon
            {
                Glyph = "\uE8A5",
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 18,
                Foreground = (Brush)res["DemarPrimaryBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        var title = new TextBlock
        {
            Text = name,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(title, 1);
        panel.Children.Add(iconWell);
        panel.Children.Add(title);
        row.Child = panel;
        return row;
    }

    private void OnContextRequested(UIElement sender, Microsoft.UI.Xaml.Input.ContextRequestedEventArgs e)
    {
        e.Handled = true;
        if (e.TryGetPosition(BubbleBorder, out var pt))
            ShowMenu(pt);
        else
            ShowMenu(new Windows.Foundation.Point(BubbleBorder.ActualWidth / 2, BubbleBorder.ActualHeight / 2));
    }

    private void ShowMenu(Windows.Foundation.Point at)
    {
        if (_msg is null || _msg.Deleted) return;
        var flyout = new MenuFlyout();
        var hasText = !string.IsNullOrWhiteSpace(_msg.Text);
        var hasMedia = _msg.Media.Count > 0;

        if (hasText)
        {
            var copy = new MenuFlyoutItem { Text = Strings.SupportCopy };
            copy.Click += (_, _) =>
            {
                var data = new DataPackage();
                data.SetText(_msg.Text!);
                Clipboard.SetContent(data);
                AppServices.Snackbar.ShowInfo(Strings.SupportCopied);
            };
            flyout.Items.Add(copy);
        }

        if (_canWrite && _msg.IsUser)
        {
            if (hasText && !hasMedia)
            {
                var edit = new MenuFlyoutItem { Text = Strings.SupportEdit };
                edit.Click += (_, _) => EditRequested?.Invoke(_msg);
                flyout.Items.Add(edit);
            }
            var del = new MenuFlyoutItem { Text = Strings.SupportDelete };
            del.Click += (_, _) => DeleteRequested?.Invoke(_msg);
            flyout.Items.Add(del);
        }

        if (flyout.Items.Count > 0)
            flyout.ShowAt(BubbleBorder, at);
    }
}
