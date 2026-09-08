using DemarVpn.Services;
using DemarVpn.Services.Account;
using DemarVpn.Services.Auth;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DemarVpn.Controls;

public sealed partial class SubscriptionCheckoutSheet : UserControl
{
    private static readonly string[] Periods = ["week", "month", "quarter", "year"];
    private const double PeriodPillHeight = 36;
    private const double PeriodPillRadius = 18;
    private const double CurrencyCircleSize = 40;

    // Apple DemarTheme.swift — checkout currency + macOS period pills
    private static readonly SolidColorBrush AppleOnSurface = MkBrush(224, 226, 232);
    private static readonly SolidColorBrush AppleOnSurfaceVariant = MkBrush(255, 255, 255);
    private static readonly SolidColorBrush AppleAccent = MkBrush(164, 201, 255);
    private static readonly SolidColorBrush AppleSelectedText = MkBrush(0, 0, 0);

    private static SolidColorBrush MkBrush(byte r, byte g, byte b) => new(Windows.UI.Color.FromArgb(255, r, g, b));

    public event Action? CompareRequested;

    private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);

    private PayCatalog? _catalog;
    private string _tier = "plus";
    private string _period = "month";
    private string _currency = "RUB";
    private string? _appliedCode;
    private int _discountPercent;
    private bool _paying;
    private bool _loadingPlans;
    private bool _wide;
    private bool _built;

    public SubscriptionCheckoutSheet()
    {
        InitializeComponent();
        NarrowTitle.Text = Strings.AccountSubscription;
        CompareButton.Content = Strings.PayComparePlans;
        PayButton.Content = Strings.PayAction;
        PromoLabel.Text = Strings.SettingsPromo;
        Chrome.Dismissed += () => Visibility = Visibility.Collapsed;
        TierBar.SelectionChanged += (_, id) =>
        {
            _tier = id;
            RefreshPrices();
        };
    }

    public void SetWideLayout(bool wide)
    {
        if (_wide == wide) return;
        _wide = wide;
        NarrowPanel.Visibility = wide ? Visibility.Collapsed : Visibility.Visible;
        WidePanel.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
        if (_built) RefreshPrices();
    }

    public void Show(CheckoutIntent intent, string? currentPlan)
    {
        _tier = intent.DefaultTier(currentPlan);
        _period = "month";
        _appliedCode = null;
        _discountPercent = 0;
        _paying = false;
        EnsureBuilt();
        TierBar.SelectedId = _tier;
        UpdatePromoLabel();
        // Apple/Android: keep catalog for sheet lifetime; Windows sheet is long-lived → reuse cache.
        _catalog ??= PayApi.CachedPlans;
        UpdatePayEnabled();
        Visibility = Visibility.Visible;
        Chrome.Show();
        PaintSelection();
        DispatcherQueue.TryEnqueue(() =>
        {
            PaintSelection();
            InvalidateArrange();
        });
        if (_catalog is null) _ = LoadPlansAsync();
    }

    public void Dismiss()
    {
        Chrome.Dismiss();
        Visibility = Visibility.Collapsed;
    }

    private void EnsureBuilt()
    {
        if (_built) return;
        _built = true;
        BuildCurrency(NarrowCurrency);
        BuildCurrency(WideCurrency);
        TierBar.SetOptions([new("plus", "Plus"), new("vip", "VIP")], _tier);
        BuildPeriodRows();
        BuildPeriodPills();
        BuildPlanCards();
        NarrowPanel.Visibility = _wide ? Visibility.Collapsed : Visibility.Visible;
        WidePanel.Visibility = _wide ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PaintSelection()
    {
        RefreshPrices();
        ApplyCurrencySelection();
        TierBar.Repaint();
    }

    private async Task LoadPlansAsync()
    {
        if (_catalog is not null) return;
        _loadingPlans = true;
        PlansError.Visibility = Visibility.Collapsed;
        UpdatePayEnabled();
        PaintSelection();
        try
        {
            _catalog = await PayApi.FetchPlansAsync();
        }
        catch
        {
            PlansError.Text = Strings.PayPlansLoadFailed;
            PlansError.Visibility = Visibility.Visible;
            AppServices.Snackbar.ShowError(Strings.PayPlansLoadFailed);
        }
        finally
        {
            _loadingPlans = false;
            DispatcherQueue.TryEnqueue(() =>
            {
                PaintSelection();
                UpdatePayEnabled();
            });
        }
    }

    private void BuildCurrency(Panel host)
    {
        host.Children.Clear();
        foreach (var (id, label) in new[] { ("RUB", "₽"), ("EUR", "€") })
        {
            var circle = new Border
            {
                Width = CurrencyCircleSize,
                Height = CurrencyCircleSize,
                CornerRadius = new CornerRadius(CurrencyCircleSize / 2),
                Tag = id,
                Child = new TextBlock
                {
                    Text = label,
                    FontSize = 15,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            circle.PointerPressed += (_, _) =>
            {
                _currency = id;
                ApplyCurrencySelection();
                RefreshPrices();
            };
            host.Children.Add(circle);
        }
        ApplyCurrencySelection();
    }

    private void ApplyCurrencySelection()
    {
        foreach (var host in new[] { NarrowCurrency, WideCurrency })
        {
            foreach (var child in host.Children)
            {
                if (child is not Border b || b.Tag is not string id || b.Child is not TextBlock tb) continue;
                var on = id == _currency;
                b.Background = on ? AppleOnSurface : TransparentBrush;
                tb.Foreground = on ? AppleSelectedText : AppleOnSurfaceVariant;
            }
        }
    }

    private void BuildPeriodRows()
    {
        PeriodRows.Children.Clear();
        foreach (var p in Periods)
        {
            var row = new Border
            {
                Tag = p,
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12, 10, 12, 10),
                BorderThickness = new Thickness(1),
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new TextBlock
            {
                Text = PeriodLabel(p),
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(title, 0);
            grid.Children.Add(title);

            if (PeriodBadge(p) is { } badge)
            {
                var chip = PrimaryChip(badge, new Thickness(8, 2, 8, 2), 4);
                chip.Margin = new Thickness(8, 0, 0, 0);
                Grid.SetColumn(chip, 1);
                grid.Children.Add(chip);
            }

            var price = new TextBlock
            {
                Opacity = 0.7,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(price, 3);
            grid.Children.Add(price);

            row.Child = grid;
            row.PointerPressed += (_, _) => SelectPeriod(p);
            PeriodRows.Children.Add(row);
        }
    }

    private void BuildPeriodPills()
    {
        PeriodPillsHost.Children.Clear();
        foreach (var p in Periods)
        {
            var label = new TextBlock
            {
                Text = PeriodLabel(p),
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                LineHeight = 16,
            };
            var inner = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            inner.Children.Add(label);
            if (PeriodBadge(p) is { } badge)
            {
                inner.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(6, 2, 6, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = badge,
                        FontSize = 11,
                        FontWeight = FontWeights.Medium,
                        VerticalAlignment = VerticalAlignment.Center,
                        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                        LineHeight = 14,
                    },
                    Tag = "badge",
                });
            }
            var pill = new Border
            {
                Tag = p,
                Height = PeriodPillHeight,
                CornerRadius = new CornerRadius(PeriodPillRadius),
                Padding = new Thickness(12, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new Grid
                {
                    VerticalAlignment = VerticalAlignment.Stretch,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Children = { inner },
                },
            };
            pill.PointerPressed += (_, _) => SelectPeriod(p);
            PeriodPillsHost.Children.Add(pill);
        }
    }

    private void BuildPlanCards()
    {
        PlanCards.Children.Clear();
        AddPlanCard(0, "plus", "Plus", Strings.PayPlusTagline, true,
            [Strings.PayPerkPremium, Strings.PayPerk100Mbps, Strings.PayPerk500Gb]);
        AddPlanCard(1, "vip", "VIP", Strings.PayVipTagline, false,
            [Strings.PayPerkDedicated, Strings.PayPerk300Mbps, Strings.PayPerk3000Gb]);
    }

    private void AddPlanCard(int col, string tierId, string name, string tagline, bool featured, string[] perks)
    {
        var body = new StackPanel { Spacing = 10 };
        var head = new Grid { VerticalAlignment = VerticalAlignment.Center };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
        };
        titleRow.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (featured)
            titleRow.Children.Add(PrimaryChip(Strings.PayPopular, new Thickness(8, 3, 8, 3), 10));
        Grid.SetColumn(titleRow, 0);
        head.Children.Add(titleRow);

        var priceCol = new StackPanel
        {
            Tag = $"cardPrice_{tierId}",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 2,
        };
        Grid.SetColumn(priceCol, 1);
        head.Children.Add(priceCol);
        body.Children.Add(head);
        body.Children.Add(new TextBlock { Text = tagline, FontSize = 12, Opacity = 0.7 });
        foreach (var perk in perks)
            body.Children.Add(new TextBlock { Text = $"• {perk}", FontSize = 13 });

        var card = new Border
        {
            Tag = tierId,
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(16),
            BorderThickness = new Thickness(1),
            Child = body,
        };
        card.PointerPressed += (_, _) =>
        {
            _tier = tierId;
            TierBar.SelectedId = tierId;
            RefreshPrices();
        };
        Grid.SetColumn(card, col);
        PlanCards.Children.Add(card);
    }

    private void RefreshPrices()
    {
        if (_wide)
        {
            ApplyPeriodPillSelection();
            ApplyPlanCardSelection();
            UpdateCardPrices();
        }
        else
        {
            ApplyPeriodRowSelection();
            UpdateNarrowPriceBlock();
        }
    }

    private void SelectPeriod(string period)
    {
        _period = period;
        RefreshPrices();
    }

    private void ApplyPeriodRowSelection()
    {
        var outline = ResBrush("DemarOutlineVariantBrush");
        var surface = ResBrush("DemarSurfaceHighBrush");
        foreach (var child in PeriodRows.Children)
        {
            if (child is not Border row || row.Tag is not string p || row.Child is not Grid g) continue;
            ApplyOutlineSelection(row, p == _period, surface, outline);
            if (g.Children.OfType<TextBlock>().LastOrDefault() is { } priceTb)
                priceTb.Text = FormatPeriodPrice(p);
        }
    }

    private void ApplyPeriodPillSelection()
    {
        var accent = AppleAccent.Color;
        foreach (var child in PeriodPillsHost.Children)
        {
            if (child is not Border pill || pill.Tag is not string p) continue;
            if (pill.Child is not Grid grid || grid.Children.FirstOrDefault() is not StackPanel inner) continue;
            var on = p == _period;
            pill.Background = on ? AppleOnSurface : TransparentBrush;
            foreach (var node in inner.Children)
            {
                if (node is TextBlock tb)
                    tb.Foreground = on ? AppleSelectedText : AppleOnSurfaceVariant;
                else if (node is Border badge)
                {
                    badge.Background = new SolidColorBrush(on ? Colors.White : accent)
                    { Opacity = on ? 0.18 : 0.15 };
                    if (badge.Child is TextBlock bt)
                        bt.Foreground = on ? AppleSelectedText : AppleAccent;
                }
            }
        }
    }

    private void ApplyPlanCardSelection()
    {
        var outline = ResBrush("DemarOutlineBrush");
        var surface = ResBrush("DemarSurfaceHighBrush");
        foreach (var child in PlanCards.Children)
        {
            if (child is not Border card || card.Tag is not string id) continue;
            ApplyOutlineSelection(card, id == _tier, surface, outline, unselectedOutlineOpacity: 0.2);
        }
    }

    private void UpdateNarrowPriceBlock()
    {
        NarrowPriceBlock.Children.Clear();
        var major = _catalog?.Price(_tier, _period, _currency);
        if (major is null)
        {
            if (_loadingPlans)
                NarrowPriceBlock.Children.Add(new TextBlock { Text = Strings.Loading, Opacity = 0.7, FontSize = 13 });
            return;
        }
        var display = _discountPercent > 0
            ? PayApi.DiscountedDisplay(major, _discountPercent, _currency)
            : PayApi.FormatPrice(major, _currency);
        NarrowPriceBlock.Children.Add(new TextBlock
        {
            Text = display,
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (_discountPercent > 0)
        {
            NarrowPriceBlock.Children.Add(new TextBlock
            {
                Text = PayApi.FormatPrice(major, _currency),
                Opacity = 0.55,
                FontSize = 14,
                TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 0),
            });
        }
    }

    private void UpdateCardPrices()
    {
        foreach (var child in PlanCards.Children)
        {
            if (child is not Border card || card.Tag is not string tierId || card.Child is not StackPanel body) continue;
            if (body.Children.OfType<Grid>().FirstOrDefault() is not { } head) continue;
            var priceCol = head.Children.OfType<StackPanel>().FirstOrDefault(s => s.Tag as string == $"cardPrice_{tierId}");
            if (priceCol is null) continue;
            priceCol.Children.Clear();
            var major = _catalog?.Price(tierId, _period, _currency);
            if (major is null)
            {
                priceCol.Children.Add(new TextBlock
                {
                    Text = _loadingPlans ? Strings.Loading : "—",
                    Opacity = 0.7,
                    FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Right,
                });
                continue;
            }
            priceCol.Children.Add(BuildPriceRow(major, _discountPercent, _currency));
            priceCol.Children.Add(new TextBlock
            {
                Text = PeriodSub(_period),
                FontSize = 12,
                Opacity = 0.7,
                HorizontalAlignment = HorizontalAlignment.Right,
            });
        }
    }

    private static UIElement BuildPriceRow(string major, int discountPercent, string currency)
    {
        if (discountPercent <= 0)
        {
            return new TextBlock
            {
                Text = PayApi.FormatPrice(major, currency),
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
        }

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        row.Children.Add(new TextBlock
        {
            Text = PayApi.FormatPrice(major, currency),
            Opacity = 0.55,
            FontSize = 12,
            TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 5, 0, 0),
        });
        row.Children.Add(new TextBlock
        {
            Text = PayApi.DiscountedDisplay(major, discountPercent, currency),
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return row;
    }

    private void UpdatePromoLabel() =>
        PromoLabel.Text = _appliedCode is null
            ? Strings.SettingsPromo
            : string.Format(Strings.PayPromoAppliedFmt, _appliedCode, _discountPercent);

    private void UpdatePayEnabled() =>
        PayButton.IsEnabled = !_paying && !_loadingPlans && _catalog is not null;

    private async void OnPromoClick(object sender, RoutedEventArgs e)
    {
        if (XamlRoot is null) return;
        var code = await DemarDialog.PromptPromoCodeAsync(XamlRoot, Strings.PayPromoApply);
        if (code is null) return;

        var session = AuthSessionStore.Current;
        if (session is null) return;
        try
        {
            var result = await VpnAccountApi.ApplyPromoAsync(session.UserId, session.LoginAt, code);
            if (result.IsApplied)
            {
                _appliedCode = code.ToUpperInvariant();
                _discountPercent = result.DiscountPercent ?? 0;
                UpdatePromoLabel();
                RefreshPrices();
            }
            await DemarDialog.ShowMessageAsync(XamlRoot, Strings.SettingsPromo, result.LocalizedMessage);
        }
        catch (AuthException ex) when (ex.StatusCode == 429)
        {
            await DemarDialog.ShowMessageAsync(XamlRoot, Strings.SettingsPromo, Strings.PayRetryIn3s);
        }
        catch (Exception ex)
        {
            await DemarDialog.ShowMessageAsync(XamlRoot, Strings.SettingsPromo, ex.Message);
        }
    }

    private void OnCompare(object sender, RoutedEventArgs e)
    {
        Dismiss();
        CompareRequested?.Invoke();
    }

    private async void OnPay(object sender, RoutedEventArgs e)
    {
        if (_paying) return;
        var session = AuthSessionStore.Current;
        if (session is null) return;
        _paying = true;
        UpdatePayEnabled();
        try
        {
            var checkout = await PayApi.CreateAsync(
                session.UserId, session.LoginAt, _tier, _period, _currency, _appliedCode);
            Dismiss();
            await IconLinkChip.OpenUrlAsync(checkout.PayUrl);
        }
        catch (AuthException ex) when (ex.IsSessionInvalid)
        {
            _paying = false;
            UpdatePayEnabled();
            AuthRepository.ClearLocal();
            AppServices.Auth.NotifyChanged();
        }
        catch (AuthException ex) when (ex.StatusCode == 429)
        {
            _paying = false;
            UpdatePayEnabled();
            AppServices.Snackbar.ShowInfo(Strings.PayRetryIn3s);
        }
        catch (Exception ex)
        {
            _paying = false;
            UpdatePayEnabled();
            AppServices.Snackbar.ShowError(ex.Message);
        }
    }

    private string FormatPeriodPrice(string period) =>
        _catalog?.Price(_tier, period, _currency) is { } major
            ? PayApi.FormatPrice(major, _currency)
            : "";

    private static Brush ResBrush(string key) => (Brush)Application.Current.Resources[key];

    private static SolidColorBrush SeedTint(double opacity)
    {
        var color = ((SolidColorBrush)Application.Current.Resources["DemarSeedBrush"]).Color;
        return new SolidColorBrush(color) { Opacity = opacity };
    }

    private static Border PrimaryChip(string text, Thickness padding, double cornerRadius, int fontSize = 12) =>
        new()
        {
            CornerRadius = new CornerRadius(cornerRadius),
            Padding = padding,
            VerticalAlignment = VerticalAlignment.Center,
            Background = SeedTint(0.15),
            Child = new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = ResBrush("DemarPrimaryBrush"),
            },
        };

    private static void ApplyOutlineSelection(
        Border target,
        bool selected,
        Brush surface,
        Brush outline,
        double unselectedOutlineOpacity = 1)
    {
        target.Background = selected ? SeedTint(0.14) : surface;
        target.BorderBrush = selected
            ? SeedTint(0.45)
            : unselectedOutlineOpacity >= 1
                ? outline
                : new SolidColorBrush(((SolidColorBrush)outline).Color) { Opacity = unselectedOutlineOpacity };
    }

    private static string PeriodLabel(string p) => p switch
    {
        "week" => Strings.PayPeriodWeek,
        "month" => Strings.PayPeriodMonth,
        "quarter" => Strings.PayPeriodQuarter,
        "year" => Strings.PayPeriodYear,
        _ => p,
    };

    private static string PeriodSub(string p) => p switch
    {
        "week" => Strings.PayPeriodSubWeek,
        "month" => Strings.PayPeriodSubMonth,
        "quarter" => Strings.PayPeriodSubQuarter,
        "year" => Strings.PayPeriodSubYear,
        _ => "",
    };

    private static string? PeriodBadge(string p) => p switch
    {
        "quarter" => Strings.PayBadgeQuarter,
        "year" => Strings.PayBadgeYear,
        _ => null,
    };
}
