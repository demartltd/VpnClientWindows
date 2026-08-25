using System.Globalization;
using DemarVpn.Services;
using DemarVpn.Services.Account;
using DemarVpn.Services.Auth;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel.DataTransfer;

namespace DemarVpn.Views;

public sealed partial class AccountPage : Page
{
    public event Action? SignOutRequested;
    public event Action? NotifySheetRequested;
    public event Action? TrafficSheetRequested;
    public event Action<CheckoutIntent>? CheckoutRequested;

    private bool _signingOut;
    private string? _userId;

    public AccountPage()
    {
        InitializeComponent();
        FamilyTitle.Text = Strings.AccountFamilyTitle;
        FamilySub.Text = Strings.AccountFamilySub;
        FamilyFree.Text = Strings.AccountFamilyFree;
        FamilyChange.Text = Strings.AccountFamilyChange;
        PlanLabel.Text = Strings.AccountPlan;
        ExpiresLabel.Text = Strings.AccountExpires;
        NotifyRow.Title = Strings.AccountNotifyTitle;
        NotifyRow.Subtitle = Strings.AccountNotifySub;
        TrafficRow.Title = Strings.AccountTrafficTitle;
        SignOutRow.Title = Strings.SignOut;
        SignOutRow.Subtitle = Strings.SignOutSub;
        BuildFamilySlots();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AccountStore.Changed += OnStoreChanged;
        Refresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) =>
        AccountStore.Changed -= OnStoreChanged;

    private void OnStoreChanged() =>
        DispatcherQueue.TryEnqueue(Refresh);

    public void Refresh()
    {
        var session = AuthSessionStore.Current;
        if (session is null)
        {
            _signingOut = false;
            UserName.Text = Strings.AccountSignInHint;
            UserIdText.Text = string.Empty;
            _userId = null;
            return;
        }

        _userId = session.UserId;
        UserName.Text = session.Username;
        UserIdText.Text = string.Format(Strings.AccountId, session.UserId);

        var account = AccountStore.Account;
        var traffic = AccountStore.Traffic;
        var loading = AccountStore.Loading;

        var rawPlan = account?.VpnSubscription ?? session.VpnSubscription;
        var isFree = string.Equals(rawPlan, "free", StringComparison.OrdinalIgnoreCase);
        var plan = string.IsNullOrWhiteSpace(rawPlan)
            ? Strings.AccountVpnUnknown
            : char.ToUpper(rawPlan[0], CultureInfo.CurrentCulture) + rawPlan[1..];
        PlanValue.Text = plan;

        var showLoading = loading && account is null;
        SubLoading.IsActive = showLoading;
        SubLoading.Visibility = showLoading ? Visibility.Visible : Visibility.Collapsed;
        SubBody.Visibility = showLoading ? Visibility.Collapsed : Visibility.Visible;

        var expiresIso = account?.VpnExpiresAt ?? session.VpnExpiresAt;
        var expires = isFree ? null : FormatExpires(expiresIso);
        ExpiresRow.Visibility = expires is null ? Visibility.Collapsed : Visibility.Visible;
        ExpiresValue.Text = expires ?? string.Empty;
        PurchaseButton.Content = isFree ? Strings.AccountBuy : Strings.AccountExtend;

        double progress = double.NaN;
        var trafficFull = false;
        string trafficSub;
        if (traffic is null && loading)
            trafficSub = Strings.Loading;
        else if (traffic is null)
            trafficSub = Strings.AccountTrafficUnavailable;
        else
        {
            progress = traffic.LimitBytes <= 0
                ? 0
                : Math.Clamp(traffic.Total / (double)traffic.LimitBytes, 0, 1);
            trafficFull = progress >= 1;
            trafficSub = string.Format(
                Strings.AccountTrafficPreview,
                VpnAccountApi.FormatBytes(traffic.Total),
                VpnAccountApi.FormatBytes(traffic.LimitBytes),
                (int)(progress * 100));
        }
        TrafficRow.Subtitle = trafficSub;
        TrafficRow.Progress = progress;
        TrafficRow.IsDanger = trafficFull;

        SignOutRow.Title = _signingOut ? Strings.Loading : Strings.SignOut;
        SignOutRow.IsEnabled = !_signingOut;
    }

    private static string? FormatExpires(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return null;
        try
        {
            var date = DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                .UtcDateTime.Date;
            var formatted = date.ToString("D", CultureInfo.CurrentCulture);
            var days = (date - DateTime.UtcNow.Date).Days;
            var relative = days > 0 ? InDays(days) : Strings.AccountExpiresToday;
            return string.Format(Strings.AccountExpiresWithDays, formatted, relative);
        }
        catch { return null; }
    }

    private static string InDays(int days)
    {
        var n = Math.Abs(days) % 100;
        var n1 = n % 10;
        var unit = n is >= 11 and <= 14 ? "дней" : n1 == 1 ? "день" : n1 is >= 2 and <= 4 ? "дня" : "дней";
        return $"{days} {unit}";
    }

    private void BuildFamilySlots()
    {
        FamilySlots.Children.Clear();
        var outline = (SolidColorBrush)Application.Current.Resources["DemarOutlineBrush"];
        var wellFill = new SolidColorBrush(outline.Color) { Opacity = 0.28 };
        for (var i = 0; i < 3; i++)
        {
            var well = new Grid { Width = 36, Height = 36 };
            well.Children.Add(new Ellipse { Fill = wellFill });
            well.Children.Add(new FontIcon
            {
                Glyph = "\uE77B",
                FontSize = 20,
                Opacity = 0.55,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
            FamilySlots.Children.Add(well);
        }
        FamilySlots.Children.Add(new Ellipse
        {
            Width = 36,
            Height = 36,
            Stroke = outline,
            StrokeThickness = 1.5,
            Opacity = 0.55,
            StrokeDashArray = new DoubleCollection { 3, 2.5 },
            Fill = null,
        });
    }

    private void OnCopyId(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_userId)) return;
        var data = new DataPackage();
        data.SetText(_userId);
        Clipboard.SetContent(data);
        AppServices.Snackbar.ShowInfo(Strings.AccountIdCopied);
    }

    private void OnPurchase(object sender, RoutedEventArgs e)
    {
        var rawPlan = AccountStore.Account?.VpnSubscription ?? AuthSessionStore.Current?.VpnSubscription;
        var isFree = string.Equals(rawPlan, "free", StringComparison.OrdinalIgnoreCase)
                     || string.IsNullOrWhiteSpace(rawPlan);
        CheckoutRequested?.Invoke(isFree ? CheckoutIntent.Buy : CheckoutIntent.Renew);
    }

    private void OnFamily(object sender, TappedRoutedEventArgs e) => AppServices.Snackbar.ShowSoon();
    private void OnTraffic(object? sender, EventArgs e) => TrafficSheetRequested?.Invoke();
    private void OnNotify(object? sender, EventArgs e) => NotifySheetRequested?.Invoke();

    private async void OnSignOutClick(object? sender, EventArgs e)
    {
        if (_signingOut) return;
        var dialog = new ContentDialog
        {
            Title = Strings.SignOutConfirmTitle,
            Content = Strings.SignOutConfirmBody,
            PrimaryButtonText = Strings.Cancel,
            CloseButtonText = Strings.SignOut,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.None) return;
        _signingOut = true;
        Refresh();
        SignOutRequested?.Invoke();
    }
}
