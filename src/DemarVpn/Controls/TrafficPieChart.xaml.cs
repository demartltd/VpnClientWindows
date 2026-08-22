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
    private const double ChartSize = 144;
    private const double Stroke = 26;

    public TrafficPieChart() => InitializeComponent();

    public void Bind(IReadOnlyList<VpnTrafficServer> servers, string otherLabel, string centerPrimary, string accountId)
    {
        Flow.Children.Clear();

        var id = new TextBlock
        {
            Text = accountId,
            FontSize = 14,
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap,
        };
        FloatEndPanel.SetFullRow(id, true);
        Flow.Children.Add(id);

        var slices = VpnAccountApi.PieSlices(servers, otherLabel);
        var palette = BuildPalette();
        if (slices.Any(s => s.Bytes > 0))
        {
            for (var i = 0; i < slices.Count; i++)
                Flow.Children.Add(LegendRow(palette[i % palette.Count], slices[i].Name, VpnAccountApi.FormatBytes(slices[i].Bytes)));
        }

        Flow.Children.Add(BuildChart(slices, palette, centerPrimary));
    }

    private static FrameworkElement BuildChart(
        IReadOnlyList<VpnTrafficServer> slices, List<Brush> palette, string centerPrimary)
    {
        var canvas = new Canvas { Width = ChartSize, Height = ChartSize };
        var total = Math.Max(1L, slices.Sum(s => s.Bytes));
        var radius = (ChartSize - Stroke) / 2;
        var center = new Point(ChartSize / 2, ChartSize / 2);

        if (slices.Any(s => s.Bytes > 0))
        {
            var start = -90.0;
            for (var i = 0; i < slices.Count; i++)
            {
                var sweep = slices[i].Bytes / (double)total * 360.0;
                if (sweep > 0.01)
                    canvas.Children.Add(ArcPath(center, radius, start, Math.Min(359.9, Math.Max(0.5, sweep)), palette[i % palette.Count]));
                start += sweep;
            }
        }

        var host = new Grid { Width = ChartSize, Height = ChartSize };
        host.Children.Add(canvas);
        host.Children.Add(new TextBlock
        {
            Text = centerPrimary,
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = ChartSize - 28,
        });
        return host;
    }

    private static Microsoft.UI.Xaml.Shapes.Path ArcPath(
        Point center, double radius, double startDeg, double sweepDeg, Brush stroke)
    {
        var start = Polar(center, radius, startDeg);
        var end = Polar(center, radius, startDeg + sweepDeg);
        var fig = new PathFigure { StartPoint = start, IsClosed = false };
        fig.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Size(radius, radius),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = sweepDeg > 180,
        });
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        return new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = geo,
            Stroke = stroke,
            StrokeThickness = Stroke,
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
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = color, VerticalAlignment = VerticalAlignment.Center });
        var texts = new StackPanel();
        texts.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 12,
            MaxWidth = 140,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
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
}
