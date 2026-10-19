using DemarVpn.Services.Account;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DemarVpn.Controls;

public sealed partial class SubscriptionInfoSheet : UserControl
{
    public event Action<SubscriptionAlertKind>? ActionRequested;

    SubscriptionAlertKind _kind;

    public SubscriptionInfoSheet()
    {
        InitializeComponent();
        Chrome.Dismissed += () => Visibility = Visibility.Collapsed;
    }

    public bool IsOpen => Visibility == Visibility.Visible;

    public void Show(SubscriptionAlertKind kind, string highlight)
    {
        _kind = kind;
        var free = kind == SubscriptionAlertKind.FreeTraffic;
        TitleText.Text = free ? Strings.SubAlertFreeTitle : Strings.SubAlertExpiryTitle;
        ActionButton.Content = free ? Strings.AccountBuy : Strings.AccountExtend;
        BuildTable(free ? Strings.SubAlertFreeBody : Strings.SubAlertExpiryBody, highlight, afterPositive: free);
        Visibility = Visibility.Visible;
        Chrome.Show();
    }

    public void Dismiss()
    {
        Chrome.Dismiss();
        Visibility = Visibility.Collapsed;
    }

    void OnAction(object sender, RoutedEventArgs e)
    {
        var kind = _kind;
        Dismiss();
        ActionRequested?.Invoke(kind);
    }

    void BuildTable(string body, string highlight, bool afterPositive)
    {
        var panel = new StackPanel();
        panel.Children.Add(HeaderRow(body, highlight));
        panel.Children.Add(Divider(0.6));
        panel.Children.Add(ColHeaders());
        panel.Children.Add(Divider(0.45));
        var features = new[] { Strings.SubAlertSecure, Strings.PayCompareAds, Strings.PayCompareTun };
        for (var i = 0; i < features.Length; i++)
        {
            panel.Children.Add(FeatureRow(features[i], afterPositive));
            if (i < features.Length - 1) panel.Children.Add(Divider(0.45));
        }
        TableHost.Children.Clear();
        TableHost.Children.Add(panel);
    }

    static Grid HeaderRow(string body, string highlight)
    {
        var g = new Grid { Padding = new Thickness(16) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        var bodyTb = new TextBlock
        {
            Text = body,
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var hi = new TextBlock
        {
            Text = highlight,
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources["DemarErrorBrush"],
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetColumn(hi, 1);
        g.Children.Add(bodyTb);
        g.Children.Add(hi);
        return g;
    }

    static Grid ColHeaders()
    {
        var g = new Grid { Padding = new Thickness(16, 10, 16, 10) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(new Border()); // spacer
        var now = ColHeader(Strings.SubAlertColNow);
        var after = ColHeader(Strings.SubAlertColAfter);
        Grid.SetColumn(now, 1);
        Grid.SetColumn(after, 2);
        g.Children.Add(now);
        g.Children.Add(after);
        return g;
    }

    static TextBlock ColHeader(string text) => new()
    {
        Text = text,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        FontSize = 13,
        TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    static Grid FeatureRow(string label, bool afterPositive)
    {
        var g = new Grid { Padding = new Thickness(16, 11, 16, 11) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var tb = new TextBlock
        {
            Text = label,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var now = StatusIcon(!afterPositive);
        var after = StatusIcon(afterPositive);
        Grid.SetColumn(now, 1);
        Grid.SetColumn(after, 2);
        g.Children.Add(tb);
        g.Children.Add(now);
        g.Children.Add(after);
        return g;
    }

    static FontIcon StatusIcon(bool positive) => new()
    {
        Glyph = positive ? "\uE73E" : "\uE711",
        FontSize = 16,
        Foreground = (Brush)Application.Current.Resources[
            positive ? "DemarPrimaryBrush" : "DemarErrorBrush"],
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    static Border Divider(double opacity) => new()
    {
        Height = 1,
        Margin = new Thickness(12, 0, 12, 0),
        Background = (Brush)Application.Current.Resources["DemarOutlineVariantBrush"],
        Opacity = opacity,
    };
}
