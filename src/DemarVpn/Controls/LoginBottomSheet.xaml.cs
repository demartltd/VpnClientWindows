using DemarVpn.Services.Auth;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DemarVpn.Controls;

public sealed partial class LoginBottomSheet : UserControl
{
    public event EventHandler? SignedIn;

    private string _email = string.Empty;
    private string? _challengeToken;
    private bool _registering;
    private bool _busy;

    public string TitleText => Strings.LoginSheetTitle;
    public string EmailHint => Strings.LoginEmailHint;
    public string ContinueText => Strings.LoginContinue;
    public string ResendText => Strings.LoginResend;
    public string MoreMethodsText => Strings.LoginMoreMethods;

    public LoginBottomSheet()
    {
        InitializeComponent();
        Chrome.Dismissed += () =>
        {
            Visibility = Visibility.Collapsed;
            ResetSteps();
        };
        TelegramChip.Icon = SocialIcon.Telegram;
        TelegramChip.Click += (_, _) => OpenTelegramOAuth();
    }

    private static void OpenTelegramOAuth()
    {
        ProtocolRegistration.EnsureRegistered();
        var url = AuthApi.TelegramOAuthStartUrl;
        AppLog.Append("auth: open telegram oauth in browser");
        _ = IconLinkChip.OpenUrlAsync(url);
    }

    public void Show()
    {
        ResetSteps();
        Visibility = Visibility.Visible;
        Chrome.Show();
        EmailBox.Focus(FocusState.Programmatic);
    }

    public void Dismiss()
    {
        Chrome.Dismiss();
        Visibility = Visibility.Collapsed;
        ResetSteps();
    }

    private void ResetSteps()
    {
        _email = string.Empty;
        _challengeToken = null;
        _registering = false;
        SetBusy(false);
        ClearError();
        EmailBox.Text = string.Empty;
        StepTitle.Text = Strings.LoginSheetTitle;
        StepSubtitle.Visibility = Visibility.Collapsed;
        EmailStep.Visibility = Visibility.Visible;
        VerifyStep.Visibility = Visibility.Collapsed;
        TotpStep.Visibility = Visibility.Collapsed;
        VerifyOtp.Reset();
        TotpOtp.Reset();
    }

    private void OnEmailTextChanged(object sender, TextChangedEventArgs e)
    {
        ClearError();
        _registering = false;
    }

    private async void OnEmailContinue(object sender, RoutedEventArgs e)
    {
        _email = EmailBox.Text.Trim();
        if (string.IsNullOrEmpty(_email) || !LooksLikeEmail(_email))
        {
            ShowError(Strings.LoginErrorInvalidEmail);
            return;
        }
        await SendOtpAsync(_registering ? "register" : "login");
    }

    private async void OnVerifyComplete(object sender, EventArgs e)
    {
        var code = VerifyOtp.Code;
        if (code.Length != 6 || _busy) return;
        await RunAuthAsync(async () =>
        {
            var result = await AuthRepository.ConfirmEmailAsync(_email, code);
            if (result is EmailConfirmResult.NeedsTotp totp)
            {
                _challengeToken = totp.ChallengeToken;
                OnUi(() =>
                {
                    StepTitle.Text = Strings.LoginTotp;
                    StepSubtitle.Visibility = Visibility.Collapsed;
                    VerifyStep.Visibility = Visibility.Collapsed;
                    TotpStep.Visibility = Visibility.Visible;
                    TotpOtp.Reset();
                });
                return;
            }
            FinishSignedIn();
        });
    }

    private async void OnTotpComplete(object sender, EventArgs e)
    {
        var code = TotpOtp.Code;
        if (_challengeToken is null || code.Length != 6 || _busy) return;
        await RunAuthAsync(async () =>
        {
            await AuthRepository.VerifyTotpAsync(_challengeToken, code);
            FinishSignedIn();
        });
    }

    private async void OnResend(object sender, RoutedEventArgs e) =>
        await SendOtpAsync(_registering ? "register" : "login");

    private async Task SendOtpAsync(string purpose)
    {
        if (_busy) return;
        ClearError();
        SetBusy(true);
        try
        {
            try
            {
                await AuthRepository.SendEmailAsync(_email, purpose);
            }
            catch (AuthException ex) when (purpose == "login" && ex.StatusCode == 404 && ex.Hint == "register")
            {
                SetBusy(false);
                if (!await ConfirmRegisterAsync()) return;
                _registering = true;
                await SendOtpAsync("register");
                return;
            }

            _registering = purpose == "register";
            OnUi(() =>
            {
                StepTitle.Text = Strings.LoginOtpTitle;
                StepSubtitle.Text = _email;
                StepSubtitle.Visibility = Visibility.Visible;
                EmailStep.Visibility = Visibility.Collapsed;
                VerifyStep.Visibility = Visibility.Visible;
                TotpStep.Visibility = Visibility.Collapsed;
                VerifyOtp.Reset();
            });
        }
        catch (AuthException ex)
        {
            ShowError(string.IsNullOrWhiteSpace(ex.Message) ? Strings.LoginErrorGeneric : ex.Message);
            OnUi(() => VerifyOtp.Reset());
        }
        catch (Exception ex)
        {
            AppLog.Append($"auth: send failed — {ex}");
            ShowError(Strings.LoginErrorGeneric);
            OnUi(() => VerifyOtp.Reset());
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task<bool> ConfirmRegisterAsync()
    {
        var root = XamlRoot;
        if (root is null)
        {
            ShowError(Strings.LoginErrorGeneric);
            return false;
        }
        var dialog = new ContentDialog
        {
            Title = Strings.LoginRegisterTitle,
            Content = string.Format(Strings.LoginRegisterBody, _email),
            PrimaryButtonText = Strings.LoginRegisterCreate,
            CloseButtonText = Strings.LoginRegisterFix,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = root,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task RunAuthAsync(Func<Task> block)
    {
        if (_busy) return;
        SetBusy(true);
        ClearError();
        try
        {
            await block();
        }
        catch (AuthException ex)
        {
            ShowError(string.IsNullOrWhiteSpace(ex.Message) ? Strings.LoginErrorGeneric : ex.Message);
            OnUi(() =>
            {
                VerifyOtp.Reset();
                TotpOtp.Reset();
            });
        }
        catch (Exception ex)
        {
            AppLog.Append($"auth: failed — {ex}");
            ShowError(Strings.LoginErrorGeneric);
            OnUi(() =>
            {
                VerifyOtp.Reset();
                TotpOtp.Reset();
            });
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        Chrome.BlockDismiss = busy;
        OnUi(() =>
        {
            BusyRing.IsActive = busy;
            BusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            ContinueBtn.IsEnabled = !busy;
            ResendBtn.IsEnabled = !busy;
            EmailBox.IsEnabled = !busy;
        });
    }

    private void ShowError(string message) => OnUi(() =>
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    });

    private void ClearError() => OnUi(() =>
    {
        ErrorText.Text = string.Empty;
        ErrorText.Visibility = Visibility.Collapsed;
    });

    private void FinishSignedIn() => OnUi(() =>
    {
        AppServices.Auth.NotifyChanged();
        Dismiss();
        SignedIn?.Invoke(this, EventArgs.Empty);
    });

    private void OnUi(Action action)
    {
        var dq = DispatcherQueue;
        if (dq is null || dq.HasThreadAccess) action();
        else dq.TryEnqueue(DispatcherQueuePriority.Normal, () => action());
    }

    private static bool LooksLikeEmail(string email) =>
        email.Contains('@') && email.Contains('.') && email.Length >= 5;
}
