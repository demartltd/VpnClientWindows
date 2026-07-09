using DemarVpn.Core;
using DemarVpn.Domain.Vpn;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DemarVpn.Controls;

public sealed partial class ServerRow : UserControl
{
    public static readonly DependencyProperty ProfileProperty =
        DependencyProperty.Register(nameof(Profile), typeof(ServerProfile), typeof(ServerRow),
            new PropertyMetadata(null, OnVisualChanged));

    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(ServerRow),
            new PropertyMetadata(false, OnVisualChanged));

    public ServerProfile? Profile
    {
        get => (ServerProfile?)GetValue(ProfileProperty);
        set => SetValue(ProfileProperty, value);
    }

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public event EventHandler? Click;

    public ServerRow()
    {
        InitializeComponent();
        FlagImage.ImageFailed += (_, _) => FlagImage.Source = new BitmapImage(CountryFlags.UnknownUri());
    }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ServerRow row) row.Apply();
    }

    private void Apply()
    {
        var p = Profile;
        if (p is null) return;

        TitleBlock.Text = p.DisplayName;
        CountryBlock.Text = p.CountryName ?? "";
        CountryBlock.Visibility = string.IsNullOrEmpty(p.CountryName) ? Visibility.Collapsed : Visibility.Visible;
        FlagImage.Source = new BitmapImage(CountryFlags.AssetUri(p.CountryCode));

        var res = Application.Current.Resources;
        var onSel = (Brush)res["DemarOnPrimaryContainerBrush"];
        var onNorm = (Brush)res["DemarSecondaryBrush"];
        Root.Background = (Brush)res[IsSelected ? "DemarPrimaryContainerBrush" : "DemarSurfaceHighBrush"];
        Root.BorderBrush = IsSelected
            ? new SolidColorBrush(((SolidColorBrush)res["DemarSeedBrush"]).Color) { Opacity = 0.35 }
            : new SolidColorBrush(((SolidColorBrush)res["DemarSurfaceHighBrush"]).Color) { Opacity = 0.2 };
        TitleBlock.Foreground = IsSelected ? onSel : onNorm;
        CountryBlock.Foreground = IsSelected ? onSel : onNorm;
    }

    private void OnClick(object sender, RoutedEventArgs e) => Click?.Invoke(this, EventArgs.Empty);
}
