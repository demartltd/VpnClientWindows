using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DemarVpn.Controls;

public sealed partial class DemarKvRow : UserControl
{
    public static readonly DependencyProperty KeyProperty =
        DependencyProperty.Register(nameof(Key), typeof(string), typeof(DemarKvRow),
            new PropertyMetadata(string.Empty, OnVisualChanged));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(string), typeof(DemarKvRow),
            new PropertyMetadata(string.Empty, OnVisualChanged));

    public static readonly DependencyProperty KeyWidthProperty =
        DependencyProperty.Register(nameof(KeyWidth), typeof(double), typeof(DemarKvRow),
            new PropertyMetadata(120.0, OnKeyWidthChanged));

    public string Key
    {
        get => (string)GetValue(KeyProperty);
        set => SetValue(KeyProperty, value);
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double KeyWidth
    {
        get => (double)GetValue(KeyWidthProperty);
        set => SetValue(KeyWidthProperty, value);
    }

    public DemarKvRow() => InitializeComponent();

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DemarKvRow row) return;
        row.KeyBlock.Text = row.Key;
        row.ValueBlock.Text = row.Value;
    }

    private static void OnKeyWidthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DemarKvRow row) return;
        row.KeyColumn.Width = new GridLength((double)e.NewValue);
    }
}
