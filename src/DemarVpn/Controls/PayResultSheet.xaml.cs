using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DemarVpn.Controls;

public sealed partial class PayResultSheet : UserControl
{
    public PayResultSheet()
    {
        InitializeComponent();
        OkButton.Content = Strings.Ok;
        Chrome.Dismissed += () => Visibility = Visibility.Collapsed;
    }

    public void Show(bool success)
    {
        ResultIcon.Glyph = success ? "\uE73E" : "\uE711";
        ResultIcon.Foreground = (Brush)Application.Current.Resources[
            success ? "DemarPrimaryBrush" : "DemarErrorBrush"];
        ResultText.Text = success ? Strings.PaySuccess : Strings.PayFailed;
        Visibility = Visibility.Visible;
        Chrome.Show();
    }

    public void Dismiss()
    {
        Chrome.Dismiss();
        Visibility = Visibility.Collapsed;
    }

    private void OnOk(object sender, RoutedEventArgs e) => Dismiss();
}
