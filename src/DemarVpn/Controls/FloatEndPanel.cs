using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace DemarVpn.Controls;

/// <summary>Leading flow with last child floated top-trailing (CSS float:right / Apple FloatEndFlow).</summary>
public sealed class FloatEndPanel : Panel
{
    public static readonly DependencyProperty FullRowProperty =
        DependencyProperty.RegisterAttached(
            "FullRow", typeof(bool), typeof(FloatEndPanel),
            new PropertyMetadata(false));

    public static void SetFullRow(DependencyObject el, bool value) => el.SetValue(FullRowProperty, value);
    public static bool GetFullRow(DependencyObject el) => (bool)el.GetValue(FullRowProperty);

    public double Spacing { get; set; } = 12;
    public double LineSpacing { get; set; } = 10;
    public double Gap { get; set; } = 12;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        var n = Children.Count;
        if (n == 0) return new Size(width, 0);

        Children[n - 1].Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        for (var i = 0; i < n - 1; i++)
        {
            var child = Children[i];
            var constraint = GetFullRow(child) && width > 0
                ? new Size(width, double.PositiveInfinity)
                : new Size(double.PositiveInfinity, double.PositiveInfinity);
            child.Measure(constraint);
        }
        return Layout(width).size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var result = Layout(finalSize.Width);
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            var origin = result.origins[i];
            var desired = child.DesiredSize;
            var w = i == Children.Count - 1 || GetFullRow(child)
                ? (i == Children.Count - 1 ? desired.Width : finalSize.Width)
                : desired.Width;
            child.Arrange(new Rect(origin.X, origin.Y, w, desired.Height));
        }
        return new Size(finalSize.Width, result.size.Height);
    }

    private (Size size, Point[] origins) Layout(double width)
    {
        var n = Children.Count;
        if (n == 0) return (new Size(width, 0), []);

        var floatIndex = n - 1;
        var floatSize = Children[floatIndex].DesiredSize;
        var floatX = Math.Max(0, width - floatSize.Width);
        var origins = new Point[n];
        origins[floatIndex] = new Point(floatX, 0);

        double x = 0, y = 0, rowH = 0, maxY = floatSize.Height;

        for (var i = 0; i < floatIndex; i++)
        {
            var child = Children[i];
            var s = child.DesiredSize;

            if (GetFullRow(child))
            {
                if (x > 0)
                {
                    x = 0;
                    y += rowH + LineSpacing;
                    rowH = 0;
                }
                origins[i] = new Point(0, y);
                y += s.Height + LineSpacing;
                rowH = 0;
                x = 0;
                maxY = Math.Max(maxY, y);
                continue;
            }

            var avail = y < floatSize.Height ? Math.Max(0, floatX - Gap) : width;
            if (x > 0 && x + Spacing + s.Width > avail)
            {
                x = 0;
                y += rowH + LineSpacing;
                rowH = 0;
            }
            var lineAvail = y < floatSize.Height ? Math.Max(0, floatX - Gap) : width;
            if (s.Width > lineAvail && lineAvail < width)
            {
                x = 0;
                y = Math.Max(y, floatSize.Height + LineSpacing);
                rowH = 0;
            }
            origins[i] = new Point(x, y);
            rowH = Math.Max(rowH, s.Height);
            x += s.Width + Spacing;
            maxY = Math.Max(maxY, y + s.Height);
        }

        return (new Size(width, maxY), origins);
    }
}
