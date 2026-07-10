using DemarVpn.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;

namespace DemarVpn.Controls;

public sealed partial class LoginBottomSheet : UserControl
{
    public event EventHandler? SignedIn;

    private string _email = string.Empty;

    public string TitleText => Strings.LoginSheetTitle;
    public string EmailHint => Strings.LoginEmailHint;
    public string ContinueText => Strings.LoginContinue;
    public string ResendText => Strings.LoginResend;
    public string MoreMethodsText => Strings.LoginMoreMethods;

    public LoginBottomSheet()
    {
        InitializeComponent();
        TelegramChip.Icon = SocialIcon.Telegram;
        TelegramChip.Url = "https://t.me/demargames";
    }

    public void Show()
    {
        ResetSteps();
        Visibility = Visibility.Visible;
        SheetSlide.Y = 400;
        var anim = new DoubleAnimation
        {
            From = 400,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(280),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(anim, SheetSlide);
        Storyboard.SetTargetProperty(anim, "Y");
        var sb = new Storyboard();
        sb.Children.Add(anim);
        sb.Begin();
        EmailBox.Focus(FocusState.Programmatic);
    }

    public void Dismiss()
    {
        Visibility = Visibility.Collapsed;
        ResetSteps();
    }

    private void ResetSteps()
    {
        _email = string.Empty;
        EmailBox.Text = string.Empty;
        StepTitle.Text = Strings.LoginSheetTitle;
        EmailStep.Visibility = Visibility.Visible;
        VerifyStep.Visibility = Visibility.Collapsed;
        TotpStep.Visibility = Visibility.Collapsed;
        VerifyOtp.Reset();
        TotpOtp.Reset();
    }

    private void OnScrimPressed(object sender, PointerRoutedEventArgs e) => Dismiss();

    private void OnEmailContinue(object sender, RoutedEventArgs e)
    {
        _email = EmailBox.Text.Trim();
        if (string.IsNullOrEmpty(_email)) return;
        StepTitle.Text = string.Format(Strings.LoginOtpSent, _email);
        EmailStep.Visibility = Visibility.Collapsed;
        VerifyStep.Visibility = Visibility.Visible;
        VerifyOtp.Reset();
    }

    private void OnVerifyComplete(object sender, EventArgs e)
    {
        StepTitle.Text = Strings.LoginTotp;
        VerifyStep.Visibility = Visibility.Collapsed;
        TotpStep.Visibility = Visibility.Visible;
        TotpOtp.Reset();
    }

    private void OnTotpComplete(object sender, EventArgs e)
    {
        Dismiss();
        SignedIn?.Invoke(this, EventArgs.Empty);
    }

    private void OnResend(object sender, RoutedEventArgs e) => VerifyOtp.Reset();
}
