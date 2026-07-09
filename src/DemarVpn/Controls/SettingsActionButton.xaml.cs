using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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

    public event EventHandler? Click;

    public SettingsActionButton() => InitializeComponent();

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SettingsActionButton b) return;
        b.Icon.Glyph = b.Glyph;
        b.TitleBlock.Text = b.Title;
        b.SubtitleBlock.Text = b.Subtitle;
    }

    private void OnClick(object sender, RoutedEventArgs e) => Click?.Invoke(this, EventArgs.Empty);
}
