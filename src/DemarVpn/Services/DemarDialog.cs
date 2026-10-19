using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DemarVpn.Services;

public static class DemarDialog
{
    public static async Task ShowMessageAsync(XamlRoot? xamlRoot, string title, string message)
    {
        if (xamlRoot is null) return;
        var dialog = New(xamlRoot, title, CompactContent(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.WrapWholeWords,
        }));
        dialog.CloseButtonText = Strings.Ok;
        await dialog.ShowAsync();
    }

    /// <returns>true if Primary was pressed; false if cancelled / null root.</returns>
    public static async Task<bool> ConfirmAsync(
        XamlRoot? xamlRoot,
        string title,
        string body,
        string primaryButtonText,
        string closeButtonText,
        ContentDialogButton defaultButton = ContentDialogButton.Primary)
    {
        if (xamlRoot is null) return false;
        var dialog = New(xamlRoot, title, CompactContent(new TextBlock
        {
            Text = body,
            TextWrapping = TextWrapping.WrapWholeWords,
        }));
        dialog.PrimaryButtonText = primaryButtonText;
        dialog.CloseButtonText = closeButtonText;
        dialog.DefaultButton = defaultButton;
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <returns>Primary / Secondary / None (close); null if no XamlRoot.</returns>
    public static async Task<ContentDialogResult?> ChooseAsync(
        XamlRoot? xamlRoot,
        string title,
        object? content,
        string primaryButtonText,
        string? secondaryButtonText = null,
        string? closeButtonText = null,
        ContentDialogButton defaultButton = ContentDialogButton.Primary,
        bool isPrimaryEnabled = true)
    {
        if (xamlRoot is null) return null;
        UIElement body = content switch
        {
            UIElement el => el,
            string s when s.Length > 0 => CompactContent(new TextBlock
            {
                Text = s,
                TextWrapping = TextWrapping.WrapWholeWords,
            }),
            _ => new Grid(),
        };
        var dialog = New(xamlRoot, title, body);
        dialog.PrimaryButtonText = primaryButtonText;
        if (!string.IsNullOrEmpty(secondaryButtonText))
            dialog.SecondaryButtonText = secondaryButtonText;
        if (!string.IsNullOrEmpty(closeButtonText))
            dialog.CloseButtonText = closeButtonText;
        dialog.DefaultButton = defaultButton;
        dialog.IsPrimaryButtonEnabled = isPrimaryEnabled;
        return await dialog.ShowAsync();
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
        input.GotFocus += (_, _) => KeyboardLayout.PreferUsEnglish();

        var dialog = New(xamlRoot, Strings.PromoEnterTitle, CompactContent(input));
        dialog.PrimaryButtonText = primaryButtonText;
        dialog.CloseButtonText = Strings.Cancel;
        dialog.DefaultButton = ContentDialogButton.Primary;

        var previousLayout = KeyboardLayout.Current();
        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
            var code = input.Text.Trim();
            return code.Length > 0 ? code : null;
        }
        finally
        {
            KeyboardLayout.Activate(previousLayout);
        }
    }

    /// <summary>Build a styled dialog; caller may tweak buttons before ShowAsync.</summary>
    public static ContentDialog Create(XamlRoot xamlRoot, string title, UIElement content) =>
        New(xamlRoot, title, content);

    private static ContentDialog New(XamlRoot xamlRoot, string title, UIElement content) =>
        new()
        {
            Title = title,
            Content = content,
            XamlRoot = xamlRoot,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
        };

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
