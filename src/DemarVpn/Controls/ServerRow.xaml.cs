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
            new PropertyMetadata(new ServerReachState(ServerReachStatus.Checking), OnVisualChanged));

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

    public event EventHandler? Click;

    private string? _flagCode;

    public ServerRow()
    {
        InitializeComponent();
        FlagImage.ImageFailed += (_, _) => FlagImage.Source = new BitmapImage(CountryFlags.UnknownUri());
    }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ServerRow row) return;
        if (e.Property == ProfileProperty) row.ApplyProfile();
        else if (e.Property == IsSelectedProperty) row.ApplySelection();
        else if (e.Property == ReachStateProperty) row.ApplyReach();
    }

    private void ApplyProfile()
    {
        var p = Profile;
        if (p is null) return;

        TitleBlock.Text = p.ListTitle;
        var sub = p.ListSubtitle;
        SubtitleBlock.Text = sub;
        SubtitleBlock.Visibility = string.IsNullOrEmpty(sub) ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(Root, p.DisplayName);
        if (_flagCode != p.CountryCode)
        {
            _flagCode = p.CountryCode;
            FlagImage.Source = new BitmapImage(CountryFlags.AssetUri(p.CountryCode));
        }
        ApplySelection();
        ApplyReach();
    }

    private void ApplySelection()
    {
        var res = Application.Current.Resources;
        var onSel = (Brush)res["DemarOnPrimaryContainerBrush"];
        var onNorm = (Brush)res["DemarSecondaryBrush"];
        Root.Background = (Brush)res[IsSelected ? "DemarPrimaryContainerBrush" : "DemarSurfaceHighBrush"];
        Root.BorderBrush = IsSelected
            ? new SolidColorBrush(((SolidColorBrush)res["DemarSeedBrush"]).Color) { Opacity = 0.35 }
            : new SolidColorBrush(((SolidColorBrush)res["DemarOutlineBrush"]).Color) { Opacity = 0.2 };
        TitleBlock.Foreground = IsSelected ? onSel : onNorm;
        SubtitleBlock.Foreground = IsSelected ? onSel : onNorm;
    }

    private void ApplyReach()
    {
        var res = Application.Current.Resources;
        StatusDot.Fill = DotBrush(ReachState.Status, res);
        ToolTipService.SetToolTip(StatusDot, ReachState.Status switch
        {
            ServerReachStatus.Online => Strings.ServerStatusOnline,
            ServerReachStatus.Offline => Strings.ServerStatusOffline,
            ServerReachStatus.Degraded => Strings.ServerStatusDegraded,
            _ => Strings.ServerStatusChecking,
        });
    }

    private static Brush DotBrush(ServerReachStatus status, ResourceDictionary res) => status switch
    {
        ServerReachStatus.Online => (Brush)res["DemarSecondaryBrush"],
        ServerReachStatus.Offline => (Brush)res["DemarErrorBrush"],
        ServerReachStatus.Degraded => (Brush)res["DemarTertiaryBrush"],
        _ => new SolidColorBrush(((SolidColorBrush)res["DemarSecondaryBrush"]).Color) { Opacity = 0.45 },
    };

    private void OnClick(object sender, RoutedEventArgs e) => Click?.Invoke(this, EventArgs.Empty);
}
