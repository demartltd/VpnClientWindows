using DemarVpn.Services.Account;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace DemarVpn.Controls;

public sealed partial class TrafficPieChart : UserControl
{
    public TrafficPieChart() => InitializeComponent();

    public void Bind(IReadOnlyList<VpnTrafficServer> servers, string otherLabel, string centerPrimary)
    {
        CenterLabel.Text = centerPrimary;
        RingCanvas.Children.Clear();
        LegendHost.Items.Clear();

        var slices = VpnAccountApi.PieSlices(servers, otherLabel);
        var total = Math.Max(1L, slices.Sum(s => s.Bytes));
        var palette = BuildPalette();
        const double size = 200;
        const double stroke = 36;
        const double radius = (size - stroke) / 2;
        var center = new Point(size / 2, size / 2);

        var track = new Ellipse
        {
            Width = radius * 2,
            Height = radius * 2,
            Stroke = TrackBrush(),
            StrokeThickness = stroke,
            Fill = null,
        };
        Canvas.SetLeft(track, (size - radius * 2) / 2);
        Canvas.SetTop(track, (size - radius * 2) / 2);
        RingCanvas.Children.Add(track);

        if (slices.Any(s => s.Bytes > 0))
        {
            var start = -90.0;
            for (var i = 0; i < slices.Count; i++)
            {
                var sweep = slices[i].Bytes / (double)total * 360.0;
                if (sweep > 0.01)
                    RingCanvas.Children.Add(ArcPath(center, radius, start, Math.Min(359.9, Math.Max(0.5, sweep)), palette[i % palette.Count], stroke));
                start += sweep;
                LegendHost.Items.Add(LegendRow(palette[i % palette.Count], slices[i].Name, VpnAccountApi.FormatBytes(slices[i].Bytes)));
            }
        }
    }

    private static Microsoft.UI.Xaml.Shapes.Path ArcPath(
        Point center, double radius, double startDeg, double sweepDeg, Brush stroke, double thickness)
    {
        var start = Polar(center, radius, startDeg);
        var end = Polar(center, radius, startDeg + sweepDeg);
        var large = sweepDeg > 180;
        var fig = new PathFigure { StartPoint = start, IsClosed = false };
        fig.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Size(radius, radius),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = large,
        });
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        return new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = geo,
            Stroke = stroke,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Flat,
            StrokeEndLineCap = PenLineCap.Flat,
        };
    }

    private static Point Polar(Point c, double r, double deg)
    {
        var rad = deg * Math.PI / 180.0;
        return new Point(c.X + r * Math.Cos(rad), c.Y + r * Math.Sin(rad));
    }

    private static FrameworkElement LegendRow(Brush color, string name, string value)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 16, 10) };
        row.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = color, VerticalAlignment = VerticalAlignment.Center });
        var texts = new StackPanel();
        texts.Children.Add(new TextBlock { Text = name, FontSize = 12 });
        texts.Children.Add(new TextBlock { Text = value, FontSize = 11, Opacity = 0.7 });
        row.Children.Add(texts);
        return row;
    }

    private static List<Brush> BuildPalette()
    {
        var res = Application.Current.Resources;
        Brush B(string key) => (Brush)res[key];
        Brush Fade(string key, byte a)
        {
            if (res[key] is not SolidColorBrush scb) return B(key);
            var c = scb.Color;
            return new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B));
        }
        return
        [
            B("DemarPrimaryBrush"),
            B("DemarTertiaryBrush"),
            B("DemarSecondaryBrush"),
            Fade("DemarPrimaryBrush", 0x8C),
            Fade("DemarTertiaryBrush", 0x8C),
            Fade("DemarSecondaryBrush", 0x8C),
        ];
    }

    private static Brush TrackBrush() => (Brush)Application.Current.Resources["DemarSurfaceHighBrush"];
}
