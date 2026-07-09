using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace DemarVpn.Controls;

public sealed partial class IconLinkChip : UserControl
{
    public static readonly DependencyProperty IconKeyProperty =
        DependencyProperty.Register(nameof(IconKey), typeof(string), typeof(IconLinkChip),
            new PropertyMetadata(string.Empty, OnIconKeyChanged));

    public static readonly DependencyProperty UrlProperty =
        DependencyProperty.Register(nameof(Url), typeof(string), typeof(IconLinkChip),
            new PropertyMetadata(string.Empty));

    public string IconKey
    {
        get => (string)GetValue(IconKeyProperty);
        set => SetValue(IconKeyProperty, value);
    }

    public string Url
    {
        get => (string)GetValue(UrlProperty);
        set => SetValue(UrlProperty, value);
    }

    public IconLinkChip() => InitializeComponent();

    private static void OnIconKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not IconLinkChip chip || e.NewValue is not string key || string.IsNullOrEmpty(key)) return;
        var res = Application.Current.Resources;
        if (res[$"{key}_Data"] is not string data) return;
        chip.Icon.Data = ParsePath(data);
    }

    private static Geometry? ParsePath(string figures)
    {
        try
        {
            return (Geometry)Microsoft.UI.Xaml.Markup.XamlReader.Load(
                $"<PathGeometry xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Figures='{figures}' />");
        }
        catch
        {
            return null;
        }
    }

    private async void OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Url)) return;
        try { await Launcher.LaunchUriAsync(new Uri(Url)); }
        catch (Exception ex) { AppLog.Append($"link open failed: {Url} — {ex.Message}"); }
    }
}
