using DemarVpn.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace DemarVpn.Controls;

public sealed partial class IconLinkChip : UserControl
{
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(SocialIcon), typeof(IconLinkChip),
            new PropertyMetadata(SocialIcon.Telegram, OnIconChanged));

    public static readonly DependencyProperty UrlProperty =
        DependencyProperty.Register(nameof(Url), typeof(string), typeof(IconLinkChip),
            new PropertyMetadata(string.Empty));

    public SocialIcon Icon
    {
        get => (SocialIcon)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string Url
    {
        get => (string)GetValue(UrlProperty);
        set => SetValue(UrlProperty, value);
    }

    public IconLinkChip() => InitializeComponent();

    private static void OnIconChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not IconLinkChip chip || e.NewValue is not SocialIcon icon) return;
        chip.BrandGlyph.Glyph = icon switch
        {
            SocialIcon.X => "\uE61B",
            SocialIcon.Bluesky => "\uE671",
            SocialIcon.Instagram => "\uF16D",
            SocialIcon.Facebook => "\uF09A",
            SocialIcon.Reddit => "\uF1A1",
            SocialIcon.Telegram => "\uF2C6",
            _ => string.Empty,
        };
    }

    private async void OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Url)) return;
        try { await Launcher.LaunchUriAsync(new Uri(Url)); }
        catch (Exception ex) { AppLog.Append($"link open failed: {Url} — {ex.Message}"); }
    }
}
