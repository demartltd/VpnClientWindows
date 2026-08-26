using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace DemarVpn.Controls;

/// <summary>Wraps children left-to-right using each child's desired size (unlike ItemsWrapGrid).</summary>
public sealed class TunChipWrapPanel : Panel
{
    public static readonly DependencyProperty ItemSpacingProperty =
        DependencyProperty.Register(nameof(ItemSpacing), typeof(double), typeof(TunChipWrapPanel),
            new PropertyMetadata(6.0, OnLayoutChanged));

    public static readonly DependencyProperty LineSpacingProperty =
        DependencyProperty.Register(nameof(LineSpacing), typeof(double), typeof(TunChipWrapPanel),
            new PropertyMetadata(6.0, OnLayoutChanged));

    public double ItemSpacing
    {
        get => (double)GetValue(ItemSpacingProperty);
        set => SetValue(ItemSpacingProperty, value);
    }

    public double LineSpacing
    {
        get => (double)GetValue(LineSpacingProperty);
        set => SetValue(LineSpacingProperty, value);
    }

    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TunChipWrapPanel panel) panel.InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var maxWidth = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : availableSize.Width;
        var itemSpacing = ItemSpacing;
        var lineSpacing = LineSpacing;
        double x = 0, y = 0, lineHeight = 0, usedWidth = 0;

        foreach (var child in Children)
        {
            if (child.Visibility != Visibility.Visible) continue;
            child.Measure(new Size(maxWidth, availableSize.Height));
            var size = child.DesiredSize;
            if (size.Width <= 0 && size.Height <= 0) continue;
            if (x > 0 && x + size.Width > maxWidth)
            {
                usedWidth = Math.Max(usedWidth, x - itemSpacing);
                y += lineHeight + lineSpacing;
                x = 0;
                lineHeight = 0;
            }

            x += size.Width + itemSpacing;
            lineHeight = Math.Max(lineHeight, size.Height);
        }

        if (x > 0) usedWidth = Math.Max(usedWidth, x - itemSpacing);
        var height = y + lineHeight;
        return new Size(
            double.IsInfinity(maxWidth) ? usedWidth : Math.Min(usedWidth, maxWidth),
            height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var itemSpacing = ItemSpacing;
        var lineSpacing = LineSpacing;
        double x = 0, y = 0, lineHeight = 0;

        foreach (var child in Children)
        {
            if (child.Visibility != Visibility.Visible) continue;
            var size = child.DesiredSize;
            if (size.Width <= 0 && size.Height <= 0) continue;
            if (x > 0 && x + size.Width > finalSize.Width)
            {
                y += lineHeight + lineSpacing;
                x = 0;
                lineHeight = 0;
            }

            child.Arrange(new Rect(x, y, size.Width, size.Height));
            x += size.Width + itemSpacing;
            lineHeight = Math.Max(lineHeight, size.Height);
        }

        return finalSize;
    }
}
