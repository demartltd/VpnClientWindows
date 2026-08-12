using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DemarVpn.Controls;

public sealed partial class DemarToggle : UserControl
{
    public event RoutedEventHandler? Toggled;

    public DemarToggle() => InitializeComponent();

    public bool IsOn
    {
        get => Inner.IsOn;
        set => Inner.IsOn = value;
    }

    private void OnInnerToggled(object sender, RoutedEventArgs e) => Toggled?.Invoke(this, e);
}
