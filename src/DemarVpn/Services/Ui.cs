using Microsoft.UI.Dispatching;

namespace DemarVpn.Services;

/// <summary>Always marshal to DispatcherQueue — SyncContext is unreliable after DemarHttp Task.Run.</summary>
public static class Ui
{
    public static void Run(DispatcherQueue? dq, Action action)
    {
        if (dq is null)
        {
            try { action(); }
            catch (Exception ex) { AppLog.AppendCrash($"ui: no-dq — {ex}"); }
            return;
        }

        if (!dq.TryEnqueue(DispatcherQueuePriority.Normal, () =>
            {
                try { action(); }
                catch (Exception ex) { AppLog.AppendCrash($"ui: {ex}"); }
            }))
        {
            AppLog.AppendCrash("ui: enqueue failed");
        }
    }
}
