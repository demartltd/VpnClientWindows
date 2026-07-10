using DemarVpn.Models;

namespace DemarVpn.Services;

public sealed class AuthSession : NotifyBase
{
    public bool SignedIn { get; private set; }
    public SignedInUser User => UiMocks.SignedInUser;

    public void SignIn()
    {
        SignedIn = true;
        Raise(nameof(SignedIn));
    }

    public void SignOut()
    {
        SignedIn = false;
        Raise(nameof(SignedIn));
    }
}
