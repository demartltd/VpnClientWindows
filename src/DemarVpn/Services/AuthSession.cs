using DemarVpn.Services.Auth;

namespace DemarVpn.Services;

public sealed class AuthSession : NotifyBase
{
    public bool SignedIn => AuthRepository.IsSignedIn;

    public SignedInUser? User
    {
        get
        {
            var s = AuthRepository.Session;
            return s is null
                ? null
                : new SignedInUser(
                    s.Username,
                    s.UserId,
                    s.VpnSubscription ?? "—",
                    s.VpnExpiresAt);
        }
    }

    public void NotifyChanged()
    {
        Raise(nameof(SignedIn));
        Raise(nameof(User));
    }

    public async Task SignOutAsync()
    {
        await AuthRepository.SignOutAsync();
        NotifyChanged();
    }
}
