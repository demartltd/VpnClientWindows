using DemarVpn.Controls;
using DemarVpn.Domain.Vpn.Tun;
using DemarVpn.Services;
using DemarVpn.Services.Account;
using DemarVpn.Services.Vpn;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DemarVpn.Views;

public sealed partial class TunSettingsPage : OverlayPage
{
    private bool _uiReady;
    private bool _picking;
    private string? _catalogKey;
    private DemarToggle? _adBlockToggle;
    private UIElement? _adBlockCard;
    private DemarToggle? _rulesToggle;
    private readonly HashSet<string> _expandedAppGroups = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _expandedSiteGroups = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _expandedLabels = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GroupUi> _appGroups = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GroupUi> _siteGroups = new(StringComparer.OrdinalIgnoreCase);
    private ItemsControl? _customChips;

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
        if (Frame?.CanGoBack == true) { Frame.GoBack(); return true; }
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
        void Apply() { EnsureBuilt(); ApplySettings(); }
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
        _adBlockCard = BuildAdBlockCard(isFree);
        RootPanel.Children.Add(_adBlockCard);
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
        if (isFree && settings.AdBlockEnabled is not null)
        {
            AppServices.Vpn.SetAdBlockEnabled(null);
            return;
        }
        var catalog = TunSettingsStore.Catalog;
        var rulesOn = settings.IsRulesEnabled(isFree);

        foreach (var child in RootPanel.Children)
        {
            if (child is FrameworkElement fe)
                fe.Opacity = child == _adBlockCard || rulesOn ? 1 : 0.45;
        }
        SetToggle(_adBlockToggle, settings.IsAdBlockEnabled(isFree), !isFree);
        SetToggle(_rulesToggle, rulesOn, !isFree);
        RefreshCustomChips(settings, isFree);

        ApplyGroupStates(catalog.AppGroups, _appGroups, _expandedAppGroups, g => settings.IsAppGroupEnabled(g, isFree), !isFree && rulesOn);
        ApplyGroupStates(catalog.SiteGroups, _siteGroups, _expandedSiteGroups, g => settings.IsSiteGroupEnabled(g, isFree), !isFree && rulesOn);
    }

    private void ApplyGroupStates<T>(
        IReadOnlyList<T> groups,
        Dictionary<string, GroupUi> map,
        HashSet<string> expanded,
        Func<T, bool> isOn,
        bool toggleEnabled) where T : class
    {
        for (var i = 0; i < groups.Count; i++)
        {
            var id = groups[i] switch
            {
                TunAppGroup a => a.Id,
                TunSiteGroup s => s.Id,
                _ => null,
            };
            if (id is null || !map.TryGetValue(GroupMapKey(id, i), out var ui)) continue;
            SetToggle(ui.Toggle, isOn(groups[i]), toggleEnabled);
            var open = expanded.Contains(ui.MapKey);
            ui.Body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            ui.Chevron.Glyph = open ? "\uE70E" : "\uE70D";
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

    private void SetToggle(DemarToggle? toggle, bool on, bool enabled)
    {
        if (toggle is null) return;
        var was = _uiReady;
        _uiReady = false;
        toggle.IsEnabled = enabled;
        toggle.IsOn = on;
        _uiReady = was;
    }

    private static string GroupMapKey(string id, int index) => $"{id}#{index}";

    private static string CatalogKey(TunCatalog catalog) =>
        string.Join('|', catalog.AppGroups.Select(g =>
            $"{g.Id}#{string.Join(',', g.Apps.Select(a => $"{a.Id}:{a.Name}"))}"))
        + ";"
        + string.Join('|', catalog.SiteGroups.Select(g =>
            $"{g.Id}#{string.Join(',', g.Sites.Select(s => $"{s.Host}:{s.Name}"))}"));
}
