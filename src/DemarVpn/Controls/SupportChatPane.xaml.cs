using DemarVpn.Services;
using DemarVpn.Services.Auth;
using DemarVpn.Services.Support;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI.Core;
using WinRT.Interop;

namespace DemarVpn.Controls;

public sealed partial class SupportChatPane : UserControl
{
    private string? _ticketId;
    private string? _themeId;
    private SupportTicket? _ticket;
    private SupportMessage? _editTarget;
    private readonly List<SupportUploadMedia> _pending = [];
    private CancellationTokenSource? _pollCts;
    private CancellationTokenSource? _loadCts;
    private int _sessionEpoch;
    private bool _sending;
    private bool _attaching;

    public event Action<Exception>? Error;
    public event Action? TicketGone;
    public event Action<SupportTicket>? TicketCreated;

    public SupportChatPane()
    {
        InitializeComponent();
        InputBox.PlaceholderText = Strings.SupportMessageHint;
        ToolTipService.SetToolTip(AttachBtn, Strings.SupportAttach);
        Loaded += (_, _) =>
        {
            if (!EditShadow.Receivers.Contains(FloatShadowCast))
                EditShadow.Receivers.Add(FloatShadowCast);
            if (!PendingShadow.Receivers.Contains(FloatShadowCast))
                PendingShadow.Receivers.Add(FloatShadowCast);
        };
    }

    private void OnInputPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        if ((shift & CoreVirtualKeyStates.Down) != 0)
        {
            // Explicit newline — WinUI TextBox often uses \r; normalize to \n for API + bubble.
            e.Handled = true;
            var box = InputBox;
            var pos = box.SelectionStart;
            var text = box.Text ?? "";
            var sel = box.SelectionLength;
            if (sel > 0)
                text = text.Remove(pos, sel);
            box.Text = text.Insert(pos, "\n");
            box.SelectionStart = pos + 1;
            return;
        }
        e.Handled = true;
        OnSend(sender, new RoutedEventArgs());
    }

    public void Start(string? ticketId, string? themeId = null)
    {
        Stop();
        var epoch = ++_sessionEpoch;
        _ticketId = ticketId;
        _themeId = themeId;
        _ticket = null;
        _editTarget = null;
        _pending.Clear();
        InputBox.Text = "";
        UpdatePendingUi();
        UpdateEditBanner();
        MsgHost.Children.Clear();

        var isDraft = string.IsNullOrWhiteSpace(ticketId);
        if (isDraft)
        {
            LoadingRing.IsActive = false;
            UpdateComposerState();
            return;
        }

        var session = AuthSessionStore.Current;
        if (session is null) return;

        // Seed from cache / list summary so composer (placeholder/enabled) matches immediately.
        var cached = SupportStore.LoadTicket(session.UserId, ticketId!);
        if (cached is not null && cached.Id == ticketId)
            _ticket = cached;
        else if (SupportStore.LoadList(session.UserId) is { } list
                 && list.FirstOrDefault(t => t.Id == ticketId) is { } sum)
        {
            _ticket = new SupportTicket(
                sum.Id, sum.Status, sum.Channel, sum.SourceId, sum.ThemeId,
                sum.OpenedAt, sum.ClosedAt, []);
        }

        UpdateComposerState();

        if (_ticket is { Messages.Count: > 0 })
        {
            RenderMessages();
            LoadingRing.IsActive = false;
            ScrollToEnd(force: true);
        }
        else LoadingRing.IsActive = true;

        _loadCts = new CancellationTokenSource();
        _ = ReloadAsync(ticketId!, quiet: _ticket is { Messages.Count: > 0 }, epoch, _loadCts.Token);
        _pollCts = new CancellationTokenSource();
        _ = PollLoopAsync(ticketId!, epoch, _pollCts.Token);
    }

    public void Stop()
    {
        _pollCts?.Cancel();
        _pollCts = null;
        _loadCts?.Cancel();
        _loadCts = null;
    }

    private async Task PollLoopAsync(string id, int epoch, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(8_000, ct);
                if (epoch != _sessionEpoch || _ticketId != id) return;
                if (_ticket?.IsOpen != true) continue;
                await ReloadAsync(id, quiet: true, epoch, ct);
            }
        }
        catch (OperationCanceledException) { /* stop */ }
    }

    private async Task ReloadAsync(string id, bool quiet, int epoch, CancellationToken ct)
    {
        var session = AuthSessionStore.Current;
        if (session is null) return;
        var delayMs = 250;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (ct.IsCancellationRequested || epoch != _sessionEpoch || _ticketId != id) return;
            try
            {
                var fresh = await SupportApi.GetTicketAsync(session.UserId, session.LoginAt, id, ct);
                if (ct.IsCancellationRequested || epoch != _sessionEpoch || _ticketId != id) return;
                if (fresh.Id != id) return;
                var changed = _ticket is null || !TicketEquals(_ticket, fresh);
                _ticket = fresh;
                LoadingRing.IsActive = false;
                SupportStore.SaveTicket(session.UserId, fresh);
                if (changed)
                {
                    RenderMessages();
                    ScrollToEnd(force: true);
                }
                UpdateComposerState();
                _ = MarkReadAsync(session, id);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (AuthException ae) when (ae.StatusCode == 404)
            {
                if (epoch != _sessionEpoch || _ticketId != id) return;
                SupportStore.RemoveTicket(session.UserId, id);
                TicketGone?.Invoke();
                return;
            }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested || epoch != _sessionEpoch) return;
                if (attempt < 2 && SupportApi.IsTransient(ex))
                {
                    await Task.Delay(delayMs, ct);
                    delayMs *= 2;
                    continue;
                }
                LoadingRing.IsActive = false;
                if (_ticket is null || !quiet) Error?.Invoke(ex);
                return;
            }
        }
    }

    private async Task MarkReadAsync(AuthSessionData session, string id)
    {
        try
        {
            await SupportApi.MarkTicketReadAsync(session.UserId, session.LoginAt, id);
            await SupportStore.RefreshUnreadAsync();
        }
        catch { /* quiet */ }
    }

    private void RenderMessages()
    {
        MsgHost.Children.Clear();
        var session = AuthSessionStore.Current;
        if (_ticket is null || session is null) return;
        if (!string.IsNullOrWhiteSpace(_ticketId) && _ticket.Id != _ticketId) return;
        var canWrite = _ticket.IsOpen && _ticket.Channel == SupportApi.Channel;
        foreach (var msg in _ticket.Messages)
        {
            var bubble = new SupportBubble();
            bubble.Bind(msg, _ticket.Id, session.UserId, session.LoginAt, canWrite);
            bubble.EditRequested += OnEdit;
            bubble.DeleteRequested += m => _ = OnDeleteAsync(m);
            MsgHost.Children.Add(bubble);
        }
        ScrollToEnd(force: true);
    }

    private static bool TicketEquals(SupportTicket a, SupportTicket b)
    {
        if (a.Id != b.Id || a.Status != b.Status || a.Channel != b.Channel
            || a.Messages.Count != b.Messages.Count)
            return false;
        for (var i = 0; i < a.Messages.Count; i++)
        {
            var x = a.Messages[i];
            var y = b.Messages[i];
            if (x.Id != y.Id || x.Role != y.Role || x.Edited != y.Edited || x.Deleted != y.Deleted
                || x.Text != y.Text || x.Media.Count != y.Media.Count)
                return false;
            for (var m = 0; m < x.Media.Count; m++)
            {
                if (x.Media[m].Url != y.Media[m].Url || x.Media[m].FileName != y.Media[m].FileName)
                    return false;
            }
        }
        return true;
    }

    private void ScrollToEnd(bool force = false)
    {
        void Go()
        {
            MsgHost.UpdateLayout();
            MsgScroll.UpdateLayout();
            MsgScroll.ChangeView(null, MsgScroll.ScrollableHeight, null, disableAnimation: !force);
        }
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, Go);
        _ = Task.Delay(50).ContinueWith(_ => DispatcherQueue.TryEnqueue(Go));
        _ = Task.Delay(200).ContinueWith(_ => DispatcherQueue.TryEnqueue(Go));
    }

    private void OnEdit(SupportMessage msg)
    {
        _editTarget = msg;
        InputBox.Text = msg.Text ?? "";
        UpdateEditBanner();
        UpdateComposerState();
    }

    private void OnCancelEdit(object sender, RoutedEventArgs e)
    {
        _editTarget = null;
        InputBox.Text = "";
        UpdateEditBanner();
        UpdateComposerState();
    }

    private async Task OnDeleteAsync(SupportMessage msg)
    {
        var session = AuthSessionStore.Current;
        var ticket = _ticket;
        if (session is null || ticket is null) return;
        if (!(ticket.IsOpen && ticket.Channel == SupportApi.Channel)) return;
        try
        {
            _ticket = await SupportApi.PatchMessageAsync(
                session.UserId, session.LoginAt, ticket.Id, msg.Id, deleted: true);
            SupportStore.SaveTicket(session.UserId, _ticket);
            RenderMessages();
        }
        catch (Exception ex) { Error?.Invoke(ex); }
    }

    private void UpdateEditBanner()
    {
        if (_editTarget is null)
        {
            EditBanner.Visibility = Visibility.Collapsed;
            SendBtn.Content = "\uE724";
            return;
        }
        EditTitle.Text = Strings.SupportEditing;
        EditPreview.Text = _editTarget.Text ?? "";
        EditBanner.Visibility = Visibility.Visible;
        SendBtn.Content = "\uE74E";
    }

    private void UpdatePendingUi()
    {
        PendingHost.Children.Clear();
        PendingScroll.Visibility = _pending.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var chipBg = (Brush)Application.Current.Resources["DemarSurfaceHighBrush"];
        for (var i = 0; i < _pending.Count; i++)
        {
            var idx = i;
            var name = _pending[i].FileName ?? "photo.jpg";
            var chip = new Border
            {
                Background = chipBg,
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(10, 6, 6, 6),
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            row.Children.Add(new TextBlock
            {
                Text = name,
                MaxWidth = 140,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            });
            var close = new Button
            {
                Content = "\uE711",
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 12,
                Padding = new Thickness(4),
                MinWidth = 28,
                MinHeight = 28,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
            };
            close.Click += (_, _) =>
            {
                if (idx < _pending.Count)
                {
                    _pending.RemoveAt(idx);
                    UpdatePendingUi();
                }
            };
            row.Children.Add(close);
            chip.Child = row;
            PendingHost.Children.Add(chip);
        }
    }

    private void UpdateComposerState()
    {
        var isDraft = string.IsNullOrWhiteSpace(_ticketId) && _ticket is null;
        var canWrite = isDraft
            || (_ticket is { IsOpen: true } t && t.Channel == SupportApi.Channel);
        InputBox.IsEnabled = canWrite && !_sending;
        AttachBtn.IsEnabled = canWrite && !_sending && !_attaching && _editTarget is null;
        SendBtn.IsEnabled = canWrite && !_sending;

        if (_ticket is { IsOpen: true } open && open.Channel != SupportApi.Channel)
            InputBox.PlaceholderText = Strings.SupportChannelLocked;
        else if (_ticket is { IsOpen: false })
            InputBox.PlaceholderText = Strings.SupportClosedHint;
        else
            InputBox.PlaceholderText = Strings.SupportMessageHint;
    }

    private async void OnAttach(object sender, RoutedEventArgs e)
    {
        if (_attaching || _pending.Count >= SupportApi.MaxMediaFiles)
        {
            if (_pending.Count >= SupportApi.MaxMediaFiles)
                Error?.Invoke(new AuthException(Strings.SupportMediaLimit, 400));
            return;
        }
        var hwnd = App.MainHwnd;
        if (hwnd == 0)
        {
            Error?.Invoke(new AuthException(Strings.SupportAttachUnavailable, 400));
            return;
        }

        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, hwnd);
        picker.ViewMode = PickerViewMode.Thumbnail;
        picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".webp");

        var files = await picker.PickMultipleFilesAsync();
        if (files is null || files.Count == 0) return;

        _attaching = true;
        AttachBtn.IsEnabled = false;
        try
        {
            var room = SupportApi.MaxMediaFiles - _pending.Count;
            foreach (var f in files.Take(room))
            {
                try { _pending.Add(await SupportApi.ReadFileAsUploadAsync(f.Path)); }
                catch (Exception ex) { Error?.Invoke(ex); }
            }
            if (files.Count > room)
                Error?.Invoke(new AuthException(Strings.SupportMediaLimit, 400));
            UpdatePendingUi();
        }
        finally
        {
            _attaching = false;
            UpdateComposerState();
        }
    }

    private async void OnSend(object sender, RoutedEventArgs e)
    {
        if (_sending) return;
        var session = AuthSessionStore.Current;
        if (session is null) return;

        var body = NormalizeBody(InputBox.Text);
        if (_editTarget is not null)
        {
            if (body.Length == 0 || _ticket is null) return;
            _sending = true;
            UpdateComposerState();
            try
            {
                _ticket = await SupportApi.PatchMessageAsync(
                    session.UserId, session.LoginAt, _ticket.Id, _editTarget.Id, text: body);
                SupportStore.SaveTicket(session.UserId, _ticket);
                _editTarget = null;
                InputBox.Text = "";
                UpdateEditBanner();
                RenderMessages();
            }
            catch (Exception ex) { Error?.Invoke(ex); }
            finally
            {
                _sending = false;
                UpdateComposerState();
            }
            return;
        }

        if (body.Length == 0 && _pending.Count == 0) return;
        var creating = string.IsNullOrWhiteSpace(_ticketId) && _ticket is null;
        if (creating && string.IsNullOrWhiteSpace(_themeId))
        {
            Error?.Invoke(new AuthException(Strings.SupportPickTheme, 400));
            return;
        }

        _sending = true;
        UpdateComposerState();
        var uploads = _pending.ToList();
        try
        {
            SupportTicket fresh;
            if (creating)
            {
                var diagnostics = await SupportApi.BuildSilentDiagnosticsAsync();
                fresh = await SupportApi.PostTicketCreateAsync(
                    session.UserId, session.LoginAt,
                    text: body.Length > 0 ? body : null,
                    log: diagnostics.LogText,
                    logMedia: diagnostics.LogMedia,
                    themeId: _themeId,
                    forceNew: true);
                _ticket = fresh;
                _ticketId = fresh.Id;
                SupportStore.SaveTicket(session.UserId, fresh);
                if (uploads.Count > 0)
                {
                    fresh = await SupportApi.PostMediaAlbumAsync(
                        session.UserId, session.LoginAt, uploads, fresh.Id);
                    _ticket = fresh;
                    SupportStore.SaveTicket(session.UserId, fresh);
                }
                TicketCreated?.Invoke(fresh);
            }
            else
            {
                var id = _ticket?.Id ?? _ticketId!;
                fresh = _ticket!;
                if (body.Length > 0)
                {
                    fresh = await SupportApi.PostTicketAsync(
                        session.UserId, session.LoginAt, text: body, ticketId: id);
                }
                if (uploads.Count > 0)
                {
                    fresh = await SupportApi.PostMediaAlbumAsync(
                        session.UserId, session.LoginAt, uploads, id);
                }
                _ticket = fresh;
                SupportStore.SaveTicket(session.UserId, fresh);
            }

            InputBox.Text = "";
            _pending.Clear();
            UpdatePendingUi();
            RenderMessages();
            UpdateComposerState();
        }
        catch (Exception ex) { Error?.Invoke(ex); }
        finally
        {
            _sending = false;
            UpdateComposerState();
        }
    }

    /// <summary>Trim edges but keep internal newlines; normalize CR/LF → \n (parity mobile).</summary>
    private static string NormalizeBody(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        return raw.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
    }
}
