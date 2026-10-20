using DemarVpn.Core;
using DemarVpn.Domain.Vpn;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
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

    public static readonly DependencyProperty ReachStateProperty =
        DependencyProperty.Register(nameof(ReachState), typeof(ServerReachState), typeof(ServerRow),
            new PropertyMetadata(ServerReachState.Checking, OnVisualChanged));

    public static readonly DependencyProperty IsFavoriteProperty =
        DependencyProperty.Register(nameof(IsFavorite), typeof(bool), typeof(ServerRow),
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

    public ServerReachState ReachState
    {
        get => (ServerReachState)GetValue(ReachStateProperty);
        set => SetValue(ReachStateProperty, value);
    }

    public bool IsFavorite
    {
        get => (bool)GetValue(IsFavoriteProperty);
        set => SetValue(IsFavoriteProperty, value);
    }

    public event EventHandler? Click;
    public event EventHandler? Details;
    public event EventHandler? NeedUpgrade;

    private string? _flagCode;

    public ServerRow()
    {
        InitializeComponent();
        PremiumLabel.Text = Strings.ServersPremium;
        FlagImage.ImageFailed += (_, _) => FlagImage.Source = new BitmapImage(CountryFlags.UnknownUri());
    }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ServerRow row) return;
        if (e.Property == ProfileProperty) row.ApplyProfile();
        else if (e.Property == IsSelectedProperty) row.ApplySelection();
        else if (e.Property == ReachStateProperty) row.ApplyReach();
        else if (e.Property == IsFavoriteProperty) row.ApplyFavorite();
    }

    private void ApplyProfile()
    {
        var p = Profile;
        if (p is null) return;

        TitleBlock.Text = p.TitleName;
        TierBadge.BindTier(p.Tier);
        ProtoBadge.BindProtocol(p.Protocol);

        var sub = p.ListSubtitle;
        SubtitleBlock.Text = sub;
        SubtitleBlock.Visibility = string.IsNullOrEmpty(sub) ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(Root, p.DisplayName);

        var locked = p.IsLocked;
        PremiumChip.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;
        LatencyPanel.Visibility = locked ? Visibility.Collapsed : Visibility.Visible;
        Root.Opacity = locked ? 0.72 : 1;

        if (_flagCode != p.CountryCode)
        {
            _flagCode = p.CountryCode;
            FlagImage.Source = new BitmapImage(CountryFlags.AssetUri(p.CountryCode));
        }
        ApplyFavorite();
        ApplySelection();
        ApplyReach();
    }

    private void ApplyFavorite()
    {
        FavIcon.Visibility = IsFavorite && Profile?.IsLocked != true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ApplySelection()
    {
        var res = Application.Current.Resources;
        var locked = Profile?.IsLocked == true;
        var selected = IsSelected && !locked;
        var onSel = (Brush)res["DemarOnPrimaryContainerBrush"];
        var onNorm = (Brush)res["DemarSecondaryBrush"];
        Root.Background = (Brush)res[selected ? "DemarPrimaryContainerBrush" : "DemarSurfaceHighBrush"];
        Root.BorderBrush = selected
            ? new SolidColorBrush(((SolidColorBrush)res["DemarSeedBrush"]).Color) { Opacity = 0.35 }
            : new SolidColorBrush(((SolidColorBrush)res["DemarOutlineBrush"]).Color) { Opacity = 0.2 };
        TitleBlock.Foreground = selected ? onSel : onNorm;
        SubtitleBlock.Foreground = selected ? onSel : onNorm;
        LatencyBlock.Foreground = selected ? onSel : onNorm;
    }

    private void ApplyReach()
    {
        if (Profile?.IsLocked == true) return;
        LatencyBlock.Text = ServerReachState.FormatMs(ReachState.TlsMs);
    }

    private void OnSelectClick(object sender, RoutedEventArgs e)
    {
        if (Profile?.IsLocked == true) NeedUpgrade?.Invoke(this, EventArgs.Empty);
        else Click?.Invoke(this, EventArgs.Empty);
    }

    private void OnTrailingClick(object sender, RoutedEventArgs e)
    {
        if (Profile?.IsLocked == true) NeedUpgrade?.Invoke(this, EventArgs.Empty);
        else Details?.Invoke(this, EventArgs.Empty);
    }
}
