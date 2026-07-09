using DemarVpn.Models;

namespace DemarVpn.Services;

public sealed class SnackbarService : NotifyBase
{
    private const int MaxChars = 256;

    public SnackMessage? Current { get; private set; }

    public void Show(SnackKind kind, string text)
    {
        var msg = text.Length > MaxChars ? text[..(MaxChars - 3)] + "..." : text;
        Current = new(kind, msg, DateTimeOffset.UtcNow);
        AppLog.Append($"[snack] {kind}: {msg}");
        Raise(nameof(Current));
    }

    public void ShowInfo(string text) => Show(SnackKind.Info, text);
    public void ShowSuccess(string text) => Show(SnackKind.Success, text);
    public void ShowError(string text) => Show(SnackKind.Error, text);
    public void ShowWarning(string text) => Show(SnackKind.Warning, text);
    public void ShowSoon() => ShowInfo(Strings.Soon);
    public void Dismiss() { Current = null; Raise(nameof(Current)); }
}
