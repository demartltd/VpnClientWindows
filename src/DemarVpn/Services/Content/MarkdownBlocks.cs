using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI.Text;
using MdBlock = Markdig.Syntax.Block;
using MdInline = Markdig.Syntax.Inlines.Inline;

namespace DemarVpn.Services.Content;

/// <summary>Markdig AST → native WinUI blocks (headings, paragraphs, lists, HR, tables, links).</summary>
public static class MarkdownBlocks
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .Build();

    public static void Fill(Panel host, string markdown)
    {
        host.Children.Clear();
        foreach (var block in Markdown.Parse(markdown, Pipeline))
        {
            if (Create(block) is { } el)
                host.Children.Add(el);
        }
    }

    private static UIElement? Create(MdBlock block) => block switch
    {
        HeadingBlock h => Rich(h.Inline, FontSize(h.Level), FontWeights.SemiBold, marginBottom: 8, marginTop: h.Level <= 2 ? 16 : 8),
        ParagraphBlock p => Rich(p.Inline, 14, FontWeights.Normal, marginBottom: 10),
        ListBlock list => BuildList(list),
        ThematicBreakBlock => new Border
        {
            Height = 1,
            Margin = new Thickness(0, 12, 0, 12),
            Background = (Brush)Application.Current.Resources["DemarOutlineVariantBrush"],
        },
        Table table => BuildTable(table),
        _ => null,
    };

    private static double FontSize(int level) => level switch
    {
        1 => 22,
        2 => 18,
        3 => 16,
        _ => 14,
    };

    private static StackPanel BuildList(ListBlock list)
    {
        var panel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 10) };
        var i = 0;
        foreach (var item in list.OfType<ListItemBlock>())
        {
            i++;
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var bullet = new TextBlock
            {
                Text = list.IsOrdered ? $"{i}." : "•",
                FontSize = 14,
                Opacity = 0.8,
                Margin = new Thickness(4, 0, 0, 0),
            };
            Grid.SetColumn(bullet, 0);
            row.Children.Add(bullet);

            var body = new StackPanel { Spacing = 4 };
            foreach (var child in item)
            {
                if (Create(child) is { } el)
                    body.Children.Add(el);
            }
            Grid.SetColumn(body, 1);
            row.Children.Add(body);
            panel.Children.Add(row);
        }
        return panel;
    }

    private static ScrollViewer BuildTable(Table table)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 12) };
        var colCount = table.ColumnDefinitions.Count;
        for (var c = 0; c < colCount; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 120 });

        var r = 0;
        foreach (var row in table.OfType<TableRow>())
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var c = 0;
            foreach (var cell in row.OfType<TableCell>())
            {
                var border = new Border
                {
                    BorderBrush = (Brush)Application.Current.Resources["DemarOutlineVariantBrush"],
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(8),
                    Background = r == 0
                        ? (Brush)Application.Current.Resources["DemarSurfaceHighBrush"]
                        : null,
                };
                var cellPanel = new StackPanel { Spacing = 4 };
                foreach (var child in cell)
                {
                    if (Create(child) is { } el)
                    {
                        if (el is RichTextBlock rtb) rtb.FontSize = 12;
                        cellPanel.Children.Add(el);
                    }
                }
                border.Child = cellPanel;
                Grid.SetRow(border, r);
                Grid.SetColumn(border, c);
                grid.Children.Add(border);
                c++;
            }
            r++;
        }

        return new ScrollViewer
        {
            Content = grid,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Auto,
            VerticalScrollMode = ScrollMode.Disabled,
        };
    }

    private static RichTextBlock Rich(ContainerInline? inline, double size, FontWeight weight,
        double marginBottom = 0, double marginTop = 0)
    {
        var rtb = new RichTextBlock
        {
            FontSize = size,
            FontWeight = weight,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            Margin = new Thickness(0, marginTop, 0, marginBottom),
        };
        var para = new Paragraph();
        AppendInlines(para.Inlines, inline);
        rtb.Blocks.Add(para);
        return rtb;
    }

    private static void AppendInlines(InlineCollection dest, MdInline? inline)
    {
        for (var cur = inline; cur is not null; cur = cur.NextSibling)
        {
            switch (cur)
            {
                case LiteralInline lit:
                    dest.Add(new Run { Text = lit.Content.ToString() });
                    break;
                case LineBreakInline:
                    dest.Add(new LineBreak());
                    break;
                case EmphasisInline em:
                {
                    var span = new Span
                    {
                        FontWeight = em.DelimiterCount >= 2 ? FontWeights.Bold : FontWeights.Normal,
                        FontStyle = em.DelimiterCount == 1 ? FontStyle.Italic : FontStyle.Normal,
                    };
                    AppendInlines(span.Inlines, em.FirstChild);
                    dest.Add(span);
                    break;
                }
                case LinkInline link when !link.IsImage && Uri.TryCreate(link.Url, UriKind.Absolute, out var uri):
                {
                    var hyper = new Hyperlink
                    {
                        NavigateUri = uri,
                        Foreground = (Brush)Application.Current.Resources["DemarPrimaryBrush"],
                    };
                    AppendInlines(hyper.Inlines, link.FirstChild);
                    dest.Add(hyper);
                    break;
                }
                case LinkInline link:
                    AppendInlines(dest, link.FirstChild);
                    break;
                case CodeInline code:
                    dest.Add(new Run
                    {
                        Text = code.Content,
                        FontFamily = new FontFamily("Consolas"),
                    });
                    break;
                case ContainerInline container:
                    AppendInlines(dest, container.FirstChild);
                    break;
            }
        }
    }
}
