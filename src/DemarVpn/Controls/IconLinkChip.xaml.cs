using System.Diagnostics;
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

    /// <summary>If subscribed, replaces default URL open (used for Telegram OAuth).</summary>
    public event EventHandler? Click;

    public IconLinkChip() => InitializeComponent();

    private static void OnIconChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not IconLinkChip chip || e.NewValue is not SocialIcon icon) return;
        var threads = icon == SocialIcon.Threads;
        chip.ThreadsIcon.Visibility = threads ? Visibility.Visible : Visibility.Collapsed;
        chip.BrandGlyph.Visibility = threads ? Visibility.Collapsed : Visibility.Visible;
        if (threads) return;
        chip.BrandGlyph.Glyph = icon switch
        {
            SocialIcon.X => "\uE61B",
            SocialIcon.Bluesky => "\uE671",
            SocialIcon.Telegram => "\uF2C6",
            _ => string.Empty,
        };
    }

    private async void OnClick(object sender, RoutedEventArgs e)
    {
        if (Click is not null)
        {
            Click.Invoke(this, EventArgs.Empty);
            return;
        }
        await OpenUrlAsync(Url);
    }

    public static async Task OpenUrlAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Append($"link open failed: {url} — {ex.Message}");
            try { await Launcher.LaunchUriAsync(new Uri(url)); }
            catch (Exception ex2) { AppLog.Append($"link launch failed: {url} — {ex2.Message}"); }
        }
    }
}
