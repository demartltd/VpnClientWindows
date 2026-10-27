using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DemarVpn.Controls;

public sealed partial class PlanCompareSheet : UserControl
{
    public event Action? BuyRequested;

    public PlanCompareSheet()
    {
        InitializeComponent();
        TitleText.Text = Strings.PayComparePlans;
        FootnoteText.Text = Strings.PayCompareFootnote;
        BuyButton.Content = Strings.AccountBuy;
        Chrome.Dismissed += () => Visibility = Visibility.Collapsed;
        BuildTable();
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

    private void OnBuy(object sender, RoutedEventArgs e)
    {
        Dismiss();
        BuyRequested?.Invoke();
    }

    private void BuildTable()
    {
        var rows = new (string Label, object Free, object Plus, object Vip)[]
        {
            (Strings.PayCompareSpeed, Strings.PayCompareSpeedFree, Strings.PayCompareSpeedPlus, Strings.PayCompareSpeedVip),
            (Strings.PayCompareTraffic, Strings.PayCompareTrafficFree, Strings.PayCompareTrafficPlus, Strings.PayCompareTrafficVip),
            (Strings.PayCompareFamily, false, "2", "4"),
            (Strings.PayCompareAds, false, true, true),
            (Strings.PayCompareTun, false, true, true),
        };

        var panel = new StackPanel();
        panel.Children.Add(HeaderRow());
        panel.Children.Add(Divider(0.6));
        for (var i = 0; i < rows.Length; i++)
        {
            panel.Children.Add(DataRow(rows[i].Label, rows[i].Free, rows[i].Plus, rows[i].Vip));
            if (i < rows.Length - 1) panel.Children.Add(Divider(0.45));
        }
        TableHost.Child = panel;
    }

    private static Grid HeaderRow()
    {
        var g = RowGrid();
        g.Children.Add(Cell("", 0, weight: true));
        g.Children.Add(Cell("Free", 1, weight: true));
        g.Children.Add(Cell("Plus", 2, weight: true));
        g.Children.Add(Cell("VIP", 3, weight: true));
        return g;
    }

    private static Grid DataRow(string label, object free, object plus, object vip)
    {
        var g = RowGrid();
        g.Children.Add(Cell(label, 0, alignStart: true));
        g.Children.Add(CellValue(free, 1));
        g.Children.Add(CellValue(plus, 2));
        g.Children.Add(CellValue(vip, 3));
        return g;
    }

    private static Grid RowGrid()
    {
        var g = new Grid { Padding = new Thickness(12, 10, 12, 10) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return g;
    }

    private static UIElement Cell(string text, int col, bool weight = false, bool alignStart = false)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontWeight = weight ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            FontSize = weight ? 13 : 13,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = alignStart ? HorizontalAlignment.Left : HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = alignStart ? TextAlignment.Left : TextAlignment.Center,
        };
        Grid.SetColumn(tb, col);
        return tb;
    }

    private static UIElement CellValue(object value, int col)
    {
        if (value is bool b)
        {
            if (b)
            {
                var icon = new FontIcon
                {
                    Glyph = "\uE73E",
                    FontSize = 16,
                    Foreground = (Brush)Application.Current.Resources["DemarPrimaryBrush"],
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(icon, col);
                return icon;
            }
            var dash = new TextBlock
            {
                Text = "—",
                Opacity = 0.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(dash, col);
            return dash;
        }
        return Cell(value.ToString() ?? "", col);
    }

    private static Border Divider(double opacity) => new()
    {
        Height = 1,
        Margin = new Thickness(12, 0, 12, 0),
        Background = (Brush)Application.Current.Resources["DemarOutlineVariantBrush"],
        Opacity = opacity,
    };
}
