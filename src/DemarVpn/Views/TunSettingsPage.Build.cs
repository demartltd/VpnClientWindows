using DemarVpn.Controls;
using DemarVpn.Domain.Vpn.Tun;
using DemarVpn.Services;
using DemarVpn.Services.Vpn;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DemarVpn.Views;

public sealed partial class TunSettingsPage
{
    private Border BuildAdBlockCard(bool isFree) =>
        BuildToggleCard(Strings.SettingsTunAdBlockTitle, Strings.SettingsTunAdBlockSub, !isFree,
            t => _adBlockToggle = t, v => AppServices.Vpn.SetAdBlockEnabled(v));

    private Border BuildRulesCard(bool isFree) =>
        BuildToggleCard(Strings.SettingsTunRulesTitle, Strings.SettingsTunRulesSub, !isFree,
            t => _rulesToggle = t, AppServices.Vpn.SetTunRulesEnabled);

    private Border BuildToggleCard(string title, string sub, bool enabled, Action<DemarToggle> bind, Action<bool> onToggle)
    {
        var toggle = new DemarToggle { IsEnabled = enabled };
        toggle.Toggled += (_, _) => { if (_uiReady) onToggle(toggle.IsOn); };
        bind(toggle);
        return Card(WrapToggle(title, sub, toggle));
    }

    private static Button BuildBuyButton()
    {
        var buy = new Button
        {
            Content = Strings.AccountBuy,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Height = 48,
        };
        buy.Click += (_, _) => AppServices.Snackbar.ShowInfo(Strings.Soon);
        return buy;
    }

    private Border BuildCustomCard(bool isFree)
    {
        var panel = new StackPanel { Spacing = 8 };
        var header = new SettingsActionButton
        {
            Glyph = "\uE71D",
            Title = Strings.SettingsTunCustomTitle,
            Subtitle = isFree ? Strings.SettingsTunCustomFreeSub : Strings.SettingsTunAppsAddSub,
        };
        header.Click += async (_, _) =>
        {
            if (isFree || !TunSettingsStore.Settings.IsRulesEnabled(isFree)) return;
            await PickExeAsync();
        };
        _customChips = new ItemsControl { ItemsPanel = (ItemsPanelTemplate)Resources["TunChipWrapPanel"] };
        panel.Children.Add(header);
        panel.Children.Add(_customChips);
        return Card(panel);
    }

    private void RefreshCustomChips(TunSettings settings, bool isFree)
    {
        if (_customChips is null) return;
        _customChips.Items.Clear();
        foreach (var exe in settings.BypassExes)
            _customChips.Items.Add(BuildRemovableChip(TunExeNames.Normalize(exe), isFree));
    }

    private Border BuildGroupsSection<T>(
        string title,
        bool isFree,
        IReadOnlyList<T> groups,
        Dictionary<string, GroupUi> map,
        HashSet<string> expanded,
        bool isApp)
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        if (isFree)
            panel.Children.Add(new TextBlock { Text = Strings.SettingsTunGroupsFreeHint, Opacity = 0.7, FontSize = 12 });

        for (var i = 0; i < groups.Count; i++)
        {
            if (groups[i] is TunAppGroup app)
                panel.Children.Add(BuildGroupBlock(GroupMapKey(app.Id, i), app.Id, app.Name, app.Apps.Count, map, expanded, isApp, app));
            else if (groups[i] is TunSiteGroup site)
                panel.Children.Add(BuildGroupBlock(GroupMapKey(site.Id, i), site.Id, site.Name, site.Sites.Count, map, expanded, isApp, site));
        }
        return Card(panel);
    }

    private UIElement BuildGroupBlock(
        string mapKey,
        string settingsId,
        string name,
        int itemCount,
        Dictionary<string, GroupUi> map,
        HashSet<string> expanded,
        bool isApp,
        object group)
    {
        var toggle = new DemarToggle();
        toggle.Toggled += (_, _) =>
        {
            if (!_uiReady) return;
            if (isApp) AppServices.Vpn.SetAppGroupEnabled(settingsId, toggle.IsOn);
            else AppServices.Vpn.SetSiteGroupEnabled(settingsId, toggle.IsOn);
        };

        var chevron = new FontIcon { FontSize = 14, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock
        {
            Text = $"{name} ({itemCount})",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var expandHit = new Grid
        {
            ColumnSpacing = 8,
            MinHeight = 40,
            Background = new SolidColorBrush(Colors.Transparent),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        expandHit.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        expandHit.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(title, 0);
        Grid.SetColumn(chevron, 1);
        expandHit.Children.Add(title);
        expandHit.Children.Add(chevron);
        expandHit.PointerPressed += (_, e) =>
        {
            if (!expanded.Add(mapKey)) expanded.Remove(mapKey);
            ApplySettings();
            e.Handled = true;
        };

        var header = new Grid { ColumnSpacing = 12, MinHeight = 40 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toggle.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(expandHit, 0);
        Grid.SetColumn(toggle, 1);
        header.Children.Add(expandHit);
        header.Children.Add(toggle);

        var labelsWrap = new TunChipWrapPanel
        {
            ItemSpacing = 6,
            LineSpacing = 6,
            Margin = new Thickness(0, 4, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var labelUis = new List<LabelChipUi>();
        var section = isApp ? "a" : "s";
        if (group is TunAppGroup ag)
        {
            var i = 0;
            foreach (var (label, items) in MergeBy(ag.Apps, a => a.Name, a => a.Id))
                labelUis.Add(BuildMergedLabelChip($"{section}|{mapKey}|{i++}", label, items.Select(x => x.Id), labelsWrap));
        }
        else if (group is TunSiteGroup sg)
        {
            var i = 0;
            foreach (var (label, items) in MergeBy(sg.Sites, s => s.Name, s => s.Host))
                labelUis.Add(BuildMergedLabelChip($"{section}|{mapKey}|{i++}", label, items.Select(x => x.Host), labelsWrap));
        }

        var body = new Border { Child = labelsWrap, Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Stretch };
        map[mapKey] = new GroupUi { MapKey = mapKey, Toggle = toggle, Body = body, Chevron = chevron, Labels = labelUis };

        return new StackPanel
        {
            Spacing = 0,
            Margin = new Thickness(0, 4, 0, 4),
            Children = { header, body },
        };
    }

    private LabelChipUi BuildMergedLabelChip(
        string key, string name, IEnumerable<string> details, TunChipWrapPanel host)
    {
        var detailList = details
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var chevron = new FontIcon { Glyph = "\uE76C", FontSize = 10, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center };
        var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        headerRow.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 12,
            Foreground = Res("DemarPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
        });
        headerRow.Children.Add(chevron);

        var nameChip = TintChip(headerRow, 0.15);
        nameChip.PointerPressed += (_, e) =>
        {
            if (!_expandedLabels.Add(key)) _expandedLabels.Remove(key);
            ApplySettings();
            e.Handled = true;
        };
        host.Children.Add(nameChip);

        var detailChips = new List<UIElement>();
        foreach (var item in detailList)
        {
            var chip = TintChip(new TextBlock
            {
                Text = item,
                FontSize = 12,
                Opacity = 0.85,
                TextWrapping = TextWrapping.NoWrap,
                VerticalAlignment = VerticalAlignment.Center,
            }, 0.1);
            chip.Visibility = Visibility.Collapsed;
            detailChips.Add(chip);
            host.Children.Add(chip);
        }

        return new LabelChipUi { Key = key, Chevron = chevron, Details = detailChips, Host = host };
    }

    private static IEnumerable<(string Name, IReadOnlyList<T> Items)> MergeBy<T>(
        IEnumerable<T> items, Func<T, string> name, Func<T, string> id) =>
        items.GroupBy(x => DisplayName(name(x), id(x)), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.Key, (IReadOnlyList<T>)g.ToList()));

    private static string DisplayName(string name, string sub) =>
        string.IsNullOrWhiteSpace(name) || name.Equals(sub, StringComparison.OrdinalIgnoreCase) ? sub : name;

    private UIElement BuildRemovableChip(string exeName, bool isFree)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new TextBlock
        {
            Text = exeName,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Res("DemarPrimaryBrush"),
        });
        if (!isFree)
        {
            var remove = new Border
            {
                Width = 20,
                Height = 20,
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Colors.Transparent),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new FontIcon
                {
                    Glyph = "\uE711",
                    FontSize = 10,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            remove.PointerEntered += (_, _) => remove.Background = SeedTint(0.35);
            remove.PointerExited += (_, _) => remove.Background = new SolidColorBrush(Colors.Transparent);
            remove.PointerPressed += (_, e) =>
            {
                AppServices.Vpn.RemoveBypassExe(exeName);
                e.Handled = true;
            };
            row.Children.Add(remove);
        }
        return TintChip(row, 0.15);
    }

    private static Border TintChip(UIElement child, double opacity) => new()
    {
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(10, 6, 10, 6),
        Background = SeedTint(opacity),
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center,
        Child = child,
    };

    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    private static Brush SeedTint(double opacity)
    {
        var color = ((SolidColorBrush)Application.Current.Resources["DemarSeedBrush"]).Color;
        return new SolidColorBrush(color) { Opacity = opacity };
    }

    private static Border Card(UIElement content) =>
        new() { Style = (Style)Application.Current.Resources["DemarCard"], Child = content };

    private static Grid WrapToggle(string title, string subtitle, DemarToggle toggle)
    {
        var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        col.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        col.Children.Add(new TextBlock { Text = subtitle, Opacity = 0.7, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(col, 0);
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(col);
        grid.Children.Add(toggle);
        return grid;
    }
}
