using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DemarVpn.Controls;

public sealed partial class SettingsActionButton : UserControl
{
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(SettingsActionButton),
            new PropertyMetadata(string.Empty, OnVisualChanged));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(SettingsActionButton),
            new PropertyMetadata(string.Empty, OnVisualChanged));

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(SettingsActionButton),
            new PropertyMetadata(string.Empty, OnVisualChanged));

    public static readonly DependencyProperty IsDangerProperty =
        DependencyProperty.Register(nameof(IsDanger), typeof(bool), typeof(SettingsActionButton),
            new PropertyMetadata(false, OnVisualChanged));

    public static readonly DependencyProperty ProgressProperty =
        DependencyProperty.Register(nameof(Progress), typeof(double), typeof(SettingsActionButton),
            new PropertyMetadata(double.NaN, OnVisualChanged));

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public bool IsDanger
    {
        get => (bool)GetValue(IsDangerProperty);
        set => SetValue(IsDangerProperty, value);
    }

    /// <summary>0..1 usage fraction; NaN hides the bar.</summary>
    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public event EventHandler? Click;

    public SettingsActionButton() => InitializeComponent();

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SettingsActionButton b) return;
        b.ApplyVisual();
    }

    private void ApplyVisual()
    {
        Icon.Glyph = Glyph;
        TitleBlock.Text = Title;
        SubtitleBlock.Text = Subtitle;
        var res = Application.Current.Resources;
        var accent = (Brush)res[IsDanger ? "DemarErrorBrush" : "DemarPrimaryBrush"];
        var muted = (Brush)res[IsDanger ? "DemarErrorMutedBrush" : "DemarPrimaryMutedBrush"];
        Icon.Foreground = accent;
        IconWell.Background = muted;
        if (!double.IsNaN(Progress))
        {
            UsageBar.Visibility = Visibility.Visible;
            UsageBar.Value = Math.Clamp(Progress, 0, 1) * 100;
            UsageBar.Foreground = accent;
            UsageBar.Background = muted;
        }
        else
            UsageBar.Visibility = Visibility.Collapsed;
    }

    private void OnClick(object sender, RoutedEventArgs e) => Click?.Invoke(this, EventArgs.Empty);
}
