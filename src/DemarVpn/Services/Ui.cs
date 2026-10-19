using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;

namespace DemarVpn.Services;

/// <summary>
/// UI-thread helpers. Network code uses ConfigureAwait(false); after any API await,
/// hop back via <see cref="ApiAsync{T}"/> / <see cref="SwitchTo"/> before touching XAML.
/// From background events use <see cref="Run"/>.
/// </summary>
public static class Ui
{
    /// <summary>Run API off the UI thread, then always resume on <paramref name="dq"/> (success or fault).</summary>
    public static async Task<T> ApiAsync<T>(DispatcherQueue? dq, Func<Task<T>> api)
    {
        try
        {
            var result = await api().ConfigureAwait(false);
            await SwitchTo(dq);
            return result;
        }
        catch
        {
            await SwitchTo(dq);
            throw;
        }
    }

    /// <summary>Run API off the UI thread, then always resume on <paramref name="dq"/> (success or fault).</summary>
    public static async Task ApiAsync(DispatcherQueue? dq, Func<Task> api)
    {
        try
        {
            await api().ConfigureAwait(false);
            await SwitchTo(dq);
        }
        catch
        {
            await SwitchTo(dq);
            throw;
        }
    }

    /// <summary>
    /// Continue the async state machine on <paramref name="dq"/>.
    /// Posts the continuation itself (TCS.SetResult does not hop after ConfigureAwait(false)).
    /// </summary>
    public static DispatcherSwitchAwaitable SwitchTo(DispatcherQueue? dq) => new(dq);

    /// <summary>Fire-and-forget on the dispatcher (inline if already on UI).</summary>
    public static void Run(
        DispatcherQueue? dq,
        Action action,
        DispatcherQueuePriority priority = DispatcherQueuePriority.Normal)
    {
        if (dq is null)
        {
            AppLog.AppendCrash("ui: no-dq");
            return;
        }

        if (dq.HasThreadAccess)
        {
            EnsureSyncContext(dq);
            try { action(); }
            catch (Exception ex) { AppLog.AppendCrash($"ui: {ex}"); }
            return;
        }

        if (!dq.TryEnqueue(priority, () =>
            {
                EnsureSyncContext(dq);
                try { action(); }
                catch (Exception ex) { AppLog.AppendCrash($"ui: {ex}"); }
            }))
            AppLog.AppendCrash("ui: enqueue failed");
    }

    /// <summary>WinUI .NET has no default sync context — install one so await resumes on the UI thread.</summary>
    public static void EnsureSyncContext(DispatcherQueue dq)
    {
        if (SynchronizationContext.Current is DispatcherQueueSynchronizationContext) return;
        SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(dq));
    }

    public readonly struct DispatcherSwitchAwaitable : INotifyCompletion
    {
        readonly DispatcherQueue? _dq;

        public DispatcherSwitchAwaitable(DispatcherQueue? dq) => _dq = dq;

        public DispatcherSwitchAwaitable GetAwaiter() => this;

        public bool IsCompleted => _dq is null || _dq.HasThreadAccess;

        public void GetResult()
        {
            if (_dq is not null)
                EnsureSyncContext(_dq);
        }

        public void OnCompleted(Action continuation)
        {
            var dq = _dq;
            if (dq is null)
            {
                continuation();
                return;
            }

            if (dq.HasThreadAccess)
            {
                EnsureSyncContext(dq);
                continuation();
                return;
            }

            if (!dq.TryEnqueue(DispatcherQueuePriority.Normal, () =>
                {
                    EnsureSyncContext(dq);
                    continuation();
                }))
            {
                AppLog.AppendCrash("ui: switch enqueue failed");
                continuation();
            }
        }
    }
}
