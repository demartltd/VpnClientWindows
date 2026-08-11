using DemarVpn.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace DemarVpn.Controls;

[ContentProperty(Name = nameof(Body))]
public sealed partial class OverlayChrome : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(OverlayChrome),
            new PropertyMetadata(string.Empty, OnTitleChanged));

    public static readonly DependencyProperty BodyProperty =
        DependencyProperty.Register(nameof(Body), typeof(UIElement), typeof(OverlayChrome),
            new PropertyMetadata(null, OnBodyChanged));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public UIElement? Body
    {
        get => (UIElement?)GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    public OverlayChrome() => InitializeComponent();

    private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is OverlayChrome chrome)
            chrome.TitleBlock.Text = (string)e.NewValue;
    }

    private static void OnBodyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is OverlayChrome chrome)
            chrome.BodyHost.Content = e.NewValue;
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        for (DependencyObject? p = this; p is not null; p = VisualTreeHelper.GetParent(p))
        {
            if (p is OverlayPage page)
            {
                page.GoBack();
                return;
            }
        }
    }
}
