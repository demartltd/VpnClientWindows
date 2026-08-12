using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media.Animation;

namespace DemarVpn.Controls;

[ContentProperty(Name = nameof(Body))]
public sealed partial class BottomSheetChrome : UserControl
{
    private const double FromY = 360;

    public static readonly DependencyProperty BodyProperty =
        DependencyProperty.Register(nameof(Body), typeof(UIElement), typeof(BottomSheetChrome),
            new PropertyMetadata(null, OnBodyChanged));

    public static readonly DependencyProperty BlockDismissProperty =
        DependencyProperty.Register(nameof(BlockDismiss), typeof(bool), typeof(BottomSheetChrome),
            new PropertyMetadata(false));

    public event Action? Dismissed;

    public UIElement? Body
    {
        get => (UIElement?)GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    public bool BlockDismiss
    {
        get => (bool)GetValue(BlockDismissProperty);
        set => SetValue(BlockDismissProperty, value);
    }

    public BottomSheetChrome() => InitializeComponent();

    public void Show()
    {
        Visibility = Visibility.Visible;
        SheetSlide.Y = FromY;
        var anim = new DoubleAnimation
        {
            From = FromY,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(280),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(anim, SheetSlide);
        Storyboard.SetTargetProperty(anim, "Y");
        var sb = new Storyboard();
        sb.Children.Add(anim);
        sb.Begin();
    }

    public void Dismiss()
    {
        Visibility = Visibility.Collapsed;
        Dismissed?.Invoke();
    }

    private static void OnBodyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BottomSheetChrome chrome)
            chrome.BodyHost.Content = e.NewValue;
    }

    private void OnScrimPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!BlockDismiss) Dismiss();
    }
}
