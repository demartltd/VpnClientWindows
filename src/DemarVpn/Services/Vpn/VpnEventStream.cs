namespace DemarVpn.Services.Vpn;

public static class VpnEventStream
{
    private static readonly object Gate = new();
    private static readonly List<Action<VpnStateEvent>> Listeners = [];
    private static VpnStateEvent? _last;

    public static IDisposable AddListener(Action<VpnStateEvent> listener)
    {
        lock (Gate)
        {
            Listeners.Add(listener);
            if (_last is { } last) listener(last);
        }
        return new Subscription(listener);
    }

    public static void Emit(VpnStateEvent evt)
    {
        lock (Gate)
        {
            _last = evt;
            foreach (var l in Listeners) l(evt);
        }
    }

    private sealed class Subscription(Action<VpnStateEvent> listener) : IDisposable
    {
        public void Dispose()
        {
            lock (Gate) Listeners.Remove(listener);
        }
    }
}

public sealed record VpnStateEvent(string State, string Message = "");
