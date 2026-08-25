using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DemarVpn.Services;

public static class DemarDialog
{
    public static async Task ShowMessageAsync(XamlRoot xamlRoot, string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = CompactContent(new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.WrapWholeWords,
            }),
            CloseButtonText = Strings.Ok,
            XamlRoot = xamlRoot,
        };
        await dialog.ShowAsync();
    }

    public static async Task<string?> PromptPromoCodeAsync(
        XamlRoot xamlRoot,
        string primaryButtonText,
        string placeholder = "DEMAR10")
    {
        var input = new TextBox
        {
            PlaceholderText = placeholder,
            CharacterCasing = CharacterCasing.Upper,
            MinWidth = 280,
        };
        input.TextChanged += (_, _) => FilterPromoInput(input);

        var dialog = new ContentDialog
        {
            Title = Strings.PromoEnterTitle,
            Content = CompactContent(input),
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = Strings.Cancel,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        var code = input.Text.Trim();
        return code.Length > 0 ? code : null;
    }

    private static StackPanel CompactContent(UIElement child) =>
        new()
        {
            VerticalAlignment = VerticalAlignment.Top,
            Spacing = 0,
            Children = { child },
        };

    private static void FilterPromoInput(TextBox input)
    {
        var filtered = string.Concat(input.Text.ToUpperInvariant()
            .Where(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9'));
        if (filtered == input.Text) return;
        var sel = input.SelectionStart;
        input.Text = filtered;
        input.SelectionStart = Math.Min(sel, filtered.Length);
    }
}
