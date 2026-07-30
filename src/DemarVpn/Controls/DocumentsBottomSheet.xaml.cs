using DemarVpn.Domain.Content;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;

namespace DemarVpn.Controls;

public sealed partial class DocumentsBottomSheet : UserControl
{
    public event Action<LegalDocument>? DocumentSelected;

    public DocumentsBottomSheet()
    {
        InitializeComponent();
        foreach (var doc in LegalDocuments.All)
        {
            var row = new SettingsActionButton
            {
                Glyph = doc.Glyph(),
                Title = doc.Title(),
                Subtitle = doc.Subtitle(),
            };
            var captured = doc;
            row.Click += (_, _) =>
            {
                Dismiss();
                DocumentSelected?.Invoke(captured);
            };
            ItemsHost.Children.Add(row);
        }
    }

    public void Show()
    {
        Visibility = Visibility.Visible;
        SheetSlide.Y = 320;
        var anim = new DoubleAnimation
        {
            From = 320,
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

    public void Dismiss() => Visibility = Visibility.Collapsed;

    private void OnScrimPressed(object sender, PointerRoutedEventArgs e) => Dismiss();
}
