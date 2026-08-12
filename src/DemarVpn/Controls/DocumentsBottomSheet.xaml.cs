using DemarVpn.Domain.Content;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DemarVpn.Controls;

public sealed partial class DocumentsBottomSheet : UserControl
{
    public event Action<LegalDocument>? DocumentSelected;

    public DocumentsBottomSheet()
    {
        InitializeComponent();
        Chrome.Dismissed += () => Visibility = Visibility.Collapsed;
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
        Chrome.Show();
    }

    public void Dismiss()
    {
        Chrome.Dismiss();
        Visibility = Visibility.Collapsed;
    }
}
