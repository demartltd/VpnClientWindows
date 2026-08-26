using DemarVpn.Controls;
using DemarVpn.Domain.Vpn.Tun;
using DemarVpn.Services;
using DemarVpn.Services.Account;
using DemarVpn.Services.Vpn;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;
using Windows.Storage.Pickers;

namespace DemarVpn.Views;

public sealed partial class TunSettingsPage : OverlayPage
{
    private bool _uiReady;
    private bool _picking;
    private bool _customExpanded;
    private string? _catalogKey;
    private DemarToggle? _rulesToggle;
    private readonly HashSet<string> _expandedAppGroups = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _expandedSiteGroups = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _expandedLabels = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GroupUi> _appGroups = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GroupUi> _siteGroups = new(StringComparer.OrdinalIgnoreCase);
    private ItemsControl? _customChips;
    private Button? _customAddBtn;
    private Border? _customBody;

    private sealed class GroupUi
    {
        public required string MapKey { get; init; }
        public required DemarToggle Toggle { get; init; }
        public required UIElement Body { get; init; }
        public required FontIcon Chevron { get; init; }
        public required IReadOnlyList<LabelChipUi> Labels { get; init; }
    }

    private sealed class LabelChipUi
    {
        public required string Key { get; init; }
        public required FontIcon Chevron { get; init; }
        public required IReadOnlyList<UIElement> Details { get; init; }
        public required TunChipWrapPanel Host { get; init; }
    }

    public TunSettingsPage()
    {
        InitializeComponent();
        Chrome.Title = Strings.SettingsTunTitle;
    }

    protected override bool OnOverlayBack()
    {
        if (Frame?.CanGoBack == true)
        {
            Frame.GoBack();
            return true;
        }
        return false;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        TunSettingsStore.Changed += OnStoreChanged;
        AccountStore.Changed += OnAccountChanged;
        EnsureBuilt();
        ApplySettings();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        TunSettingsStore.Changed -= OnStoreChanged;
        AccountStore.Changed -= OnAccountChanged;
    }

    private void OnStoreChanged()
    {
        void Apply()
        {
            EnsureBuilt();
            ApplySettings();
        }

        if (DispatcherQueue.HasThreadAccess) Apply();
        else DispatcherQueue.TryEnqueue(Apply);
    }

    private void OnAccountChanged() => OnStoreChanged();

    private void EnsureBuilt()
    {
        var catalog = TunSettingsStore.Catalog;
        var key = CatalogKey(catalog);
        if (key == _catalogKey && RootPanel.Children.Count > 0) return;

        _catalogKey = key;
        _appGroups.Clear();
        _siteGroups.Clear();
        _uiReady = false;
        RootPanel.Children.Clear();

        var isFree = VpnController.IsFreeSubscription();
        RootPanel.Children.Add(BuildRulesCard(isFree));
        if (isFree) RootPanel.Children.Add(BuildBuyButton());
        RootPanel.Children.Add(BuildCustomCard(isFree));
        if (catalog.AppGroups.Count > 0)
            RootPanel.Children.Add(BuildGroupsSection(Strings.SettingsTunGroupsTitle, isFree, catalog.AppGroups, _appGroups, _expandedAppGroups, true));
        if (catalog.SiteGroups.Count > 0)
            RootPanel.Children.Add(BuildGroupsSection(Strings.SettingsTunSiteGroupsTitle, isFree, catalog.SiteGroups, _siteGroups, _expandedSiteGroups, false));

        DispatcherQueue.TryEnqueue(() => _uiReady = true);
    }

    private void ApplySettings()
    {
        var isFree = VpnController.IsFreeSubscription();
        var settings = TunSettingsStore.Settings;
        var catalog = TunSettingsStore.Catalog;
        var rulesOn = settings.IsRulesEnabled(isFree);

        RootPanel.Opacity = rulesOn ? 1 : 0.45;
        SetToggle(_rulesToggle, rulesOn, !isFree);

        if (_customBody is not null)
            _customBody.Visibility = _customExpanded && rulesOn ? Visibility.Visible : Visibility.Collapsed;
        if (_customAddBtn is not null)
            _customAddBtn.Visibility = !isFree && rulesOn && _customExpanded ? Visibility.Visible : Visibility.Collapsed;
        RefreshCustomChips(settings, isFree);

        foreach (var (group, index) in catalog.AppGroups.Select((g, i) => (g, i)))
        {
            if (!_appGroups.TryGetValue(GroupMapKey(group.Id, index), out var ui)) continue;
            SetToggle(ui.Toggle, settings.IsAppGroupEnabled(group, isFree), !isFree && rulesOn);
            var expanded = _expandedAppGroups.Contains(ui.MapKey);
            ui.Body.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            ui.Chevron.Glyph = expanded ? "\uE70E" : "\uE70D";
            ApplyLabelExpansion(ui.Labels);
        }

        foreach (var (group, index) in catalog.SiteGroups.Select((g, i) => (g, i)))
        {
            if (!_siteGroups.TryGetValue(GroupMapKey(group.Id, index), out var ui)) continue;
            SetToggle(ui.Toggle, settings.IsSiteGroupEnabled(group, isFree), !isFree && rulesOn);
            var expanded = _expandedSiteGroups.Contains(ui.MapKey);
            ui.Body.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            ui.Chevron.Glyph = expanded ? "\uE70E" : "\uE70D";
            ApplyLabelExpansion(ui.Labels);
        }
    }

    private void ApplyLabelExpansion(IReadOnlyList<LabelChipUi> labels)
    {
        foreach (var chip in labels)
        {
            var open = _expandedLabels.Contains(chip.Key);
            foreach (var detail in chip.Details)
                detail.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            chip.Chevron.Glyph = open ? "\uE70E" : "\uE76C";
            chip.Host.InvalidateMeasure();
        }
    }

    private static string GroupMapKey(string id, int index) => $"{id}#{index}";

    private Border BuildRulesCard(bool isFree)
    {
        _rulesToggle = new DemarToggle { IsEnabled = !isFree };
        _rulesToggle.Toggled += (_, _) =>
        {
            if (!_uiReady) return;
            AppServices.Vpn.SetTunRulesEnabled(_rulesToggle.IsOn);
        };
        return Card(WrapToggle(Strings.SettingsTunRulesTitle, Strings.SettingsTunRulesSub, _rulesToggle));
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
        header.Click += (_, _) =>
        {
            _customExpanded = !_customExpanded;
            ApplySettings();
        };
        panel.Children.Add(header);

        _customBody = new Border { Padding = new Thickness(4, 0, 4, 4) };
        var body = new StackPanel { Spacing = 8 };
        _customChips = new ItemsControl { ItemsPanel = (ItemsPanelTemplate)Resources["TunChipWrapPanel"] };
        _customAddBtn = new Button
        {
            Content = Strings.SettingsTunAppsAdd,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        _customAddBtn.Click += async (_, _) => await PickExeAsync();
        body.Children.Add(_customChips);
        body.Children.Add(_customAddBtn);
        _customBody.Child = body;
        panel.Children.Add(_customBody);
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
            var group = groups[i];
            if (group is TunAppGroup appGroup)
                panel.Children.Add(BuildGroupBlock(GroupMapKey(appGroup.Id, i), appGroup.Id, appGroup.Name, appGroup.Apps.Count, map, expanded, isApp, appGroup));
            else if (group is TunSiteGroup siteGroup)
                panel.Children.Add(BuildGroupBlock(GroupMapKey(siteGroup.Id, i), siteGroup.Id, siteGroup.Name, siteGroup.Sites.Count, map, expanded, isApp, siteGroup));
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

        var chevron = new FontIcon
        {
            FontSize = 14,
            Opacity = 0.7,
            VerticalAlignment = VerticalAlignment.Center,
        };
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
            VerticalAlignment = VerticalAlignment.Center,
        };
        expandHit.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        expandHit.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(title, 0);
        Grid.SetColumn(chevron, 1);
        expandHit.Children.Add(title);
        expandHit.Children.Add(chevron);
        expandHit.PointerPressed += (_, e) =>
        {
            if (expanded.Contains(mapKey)) expanded.Remove(mapKey);
            else expanded.Add(mapKey);
            ApplySettings();
            e.Handled = true;
        };

        var header = new Grid
        {
            ColumnSpacing = 12,
            MinHeight = 40,
            VerticalAlignment = VerticalAlignment.Center,
        };
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
        var labelSection = isApp ? "a" : "s";
        if (group is TunAppGroup ag)
        {
            var labelIndex = 0;
            foreach (var merged in MergeApps(ag.Apps))
            {
                var key = LabelKey(labelSection, mapKey, labelIndex);
                var chip = BuildMergedLabelChip(key, merged.Name, merged.Items.Select(x => x.Id), labelsWrap);
                labelUis.Add(chip);
                labelIndex++;
            }
        }
        else if (group is TunSiteGroup sg)
        {
            var labelIndex = 0;
            foreach (var merged in MergeSites(sg.Sites))
            {
                var key = LabelKey(labelSection, mapKey, labelIndex);
                var chip = BuildMergedLabelChip(key, merged.Name, merged.Items.Select(x => x.Host), labelsWrap);
                labelUis.Add(chip);
                labelIndex++;
            }
        }

        var body = new Border
        {
            Child = labelsWrap,
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        map[mapKey] = new GroupUi { MapKey = mapKey, Toggle = toggle, Body = body, Chevron = chevron, Labels = labelUis };

        var block = new StackPanel { Spacing = 0, Margin = new Thickness(0, 4, 0, 4), HorizontalAlignment = HorizontalAlignment.Stretch };
        block.Children.Add(header);
        block.Children.Add(body);
        return block;
    }

    private LabelChipUi BuildMergedLabelChip(
        string key,
        string name,
        IEnumerable<string> details,
        TunChipWrapPanel host)
    {
        var detailList = details
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var chevron = new FontIcon
        {
            Glyph = "\uE76C",
            FontSize = 10,
            Opacity = 0.75,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var headerRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
        };
        headerRow.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 12,
            Foreground = Res("DemarPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
        });
        headerRow.Children.Add(chevron);

        var nameChip = NameChipBorder(headerRow);
        nameChip.PointerPressed += (_, e) =>
        {
            if (_expandedLabels.Contains(key)) _expandedLabels.Remove(key);
            else _expandedLabels.Add(key);
            ApplySettings();
            e.Handled = true;
        };
        host.Children.Add(nameChip);

        var detailChips = new List<UIElement>();
        foreach (var item in detailList)
        {
            var chip = DetailChip(item);
            chip.Visibility = Visibility.Collapsed;
            detailChips.Add(chip);
            host.Children.Add(chip);
        }

        return new LabelChipUi
        {
            Key = key,
            Chevron = chevron,
            Details = detailChips,
            Host = host,
        };
    }

    private static Border NameChipBorder(UIElement child) =>
        new()
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 6, 10, 6),
            Background = SeedTint(0.15),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Child = child,
        };

    private static Border DetailChip(string text) =>
        new()
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 6, 10, 6),
            Background = SeedTint(0.1),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text,
                FontSize = 12,
                Opacity = 0.85,
                TextWrapping = TextWrapping.NoWrap,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

    private static string LabelKey(string section, string groupMapKey, int labelIndex) =>
        $"{section}|{groupMapKey}|{labelIndex}";

    private sealed record MergedApp(string Name, IReadOnlyList<TunAppRef> Items);

    private sealed record MergedSite(string Name, IReadOnlyList<TunSiteRef> Items);

    private static IEnumerable<MergedApp> MergeApps(IReadOnlyList<TunAppRef> apps) =>
        apps.GroupBy(a => DisplayName(a.Name, a.Id), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new MergedApp(g.Key, g.ToList()));

    private static IEnumerable<MergedSite> MergeSites(IReadOnlyList<TunSiteRef> sites) =>
        sites.GroupBy(s => DisplayName(s.Name, s.Host), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new MergedSite(g.Key, g.ToList()));

    private static string DisplayName(string name, string sub) =>
        string.IsNullOrWhiteSpace(name) || name.Equals(sub, StringComparison.OrdinalIgnoreCase) ? sub : name;

    private UIElement BuildRemovableChip(string exeName, bool isFree)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
        };
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

        return ChipBorder(row);
    }

    private static Border ChipBorder(UIElement child) =>
        new()
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 6, 10, 6),
            Background = SeedTint(0.15),
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

    private void SetToggle(DemarToggle? toggle, bool on, bool enabled)
    {
        if (toggle is null) return;
        var was = _uiReady;
        _uiReady = false;
        toggle.IsEnabled = enabled;
        toggle.IsOn = on;
        _uiReady = was;
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

    private async Task PickExeAsync()
    {
        if (_picking || XamlRoot is null) return;
        var dialog = new ContentDialog
        {
            Title = Strings.SettingsTunAppsAdd,
            PrimaryButtonText = Strings.SettingsTunAppsPickExplorer,
            SecondaryButtonText = Strings.SettingsTunAppsTypeName,
            CloseButtonText = Strings.Cancel,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
            await PickExeFromDiskAsync();
        else if (result == ContentDialogResult.Secondary)
            await PromptExeNameAsync();
    }

    private async Task PromptExeNameAsync()
    {
        if (XamlRoot is null) return;
        var input = new TextBox { PlaceholderText = Strings.SettingsTunAppsTypeHint, MinWidth = 280 };
        var dialog = new ContentDialog
        {
            Title = Strings.SettingsTunAppsTypeName,
            Content = input,
            PrimaryButtonText = Strings.SettingsTunAppsAdd,
            CloseButtonText = Strings.Cancel,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var raw = input.Text.Trim();
        if (raw.Length == 0) return;
        if (!TunExeNames.IsExeName(raw))
        {
            AppServices.Snackbar.ShowError(Strings.SettingsTunAppsMustExe);
            return;
        }
        AppServices.Vpn.AddBypassExe(raw);
    }

    private async Task PickExeFromDiskAsync()
    {
        if (_picking || XamlRoot is null) return;
        _picking = true;
        try
        {
            var path = await PickFileExeAsync();
            if (path is not null)
                AppServices.Vpn.AddBypassExe(path);
        }
        catch (Exception ex)
        {
            AppLog.Append($"tun_apps: pick failed — {ex.Message}");
            AppServices.Snackbar.ShowError(ex.Message);
        }
        finally
        {
            _picking = false;
        }
    }

    private static async Task<string?> PickFileExeAsync()
    {
        var hwnd = App.MainHwnd;
        if (hwnd == 0)
            throw new InvalidOperationException(Strings.SettingsTunAppsPickUnavailable);

        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, hwnd);
        picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
        picker.FileTypeFilter.Add(".exe");
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    private static string CatalogKey(TunCatalog catalog)
    {
        var apps = string.Join('|', catalog.AppGroups.Select(g =>
            $"{g.Id}#{string.Join(',', g.Apps.Select(a => $"{a.Id}:{a.Name}"))}"));
        var sites = string.Join('|', catalog.SiteGroups.Select(g =>
            $"{g.Id}#{string.Join(',', g.Sites.Select(s => $"{s.Host}:{s.Name}"))}"));
        return apps + ";" + sites;
    }
}
