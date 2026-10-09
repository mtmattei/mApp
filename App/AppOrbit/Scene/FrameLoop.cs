using Microsoft.UI.Dispatching;

namespace AppOrbit.Scene;

/// <summary>
/// One 60 Hz clock for everything that animates (camera easing, the dock springs, the figure's
/// gap). It runs only while a client is attached and stops with the last one, so an idle app
/// costs no frames. A dispatcher timer rather than CompositionTarget.Rendering: the compositor
/// stops raising frames when nothing is dirty on some hosts, and a held Rendering subscription
/// pumps the display forever on others.
/// </summary>
public static class FrameLoop
{
    private sealed class Client : IDisposable
    {
        public Action<double> Tick = _ => { };
        public long Last;
        public bool Gone;
        public void Dispose() { Gone = true; Detach(this); }
    }

    private static readonly List<Client> Clients = new();
    private static DispatcherQueueTimer? _timer;

    /// <summary>Attaches a tick callback (dt in seconds, capped at 250 ms). Dispose the handle to stop.</summary>
    public static IDisposable Start(DispatcherQueue queue, Action<double> tick)
    {
        var c = new Client { Tick = tick, Last = Environment.TickCount64 };
        lock (Clients) Clients.Add(c);
        if (_timer == null)
        {
            _timer = queue.CreateTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(16);
            _timer.IsRepeating = true;
            _timer.Tick += OnTick;
        }
        if (!_timer.IsRunning) _timer.Start();
        return c;
    }

    private static void Detach(Client c)
    {
        lock (Clients) Clients.Remove(c);
        if (Clients.Count == 0) _timer?.Stop();
    }

    private static void OnTick(DispatcherQueueTimer sender, object args)
    {
        Client[] snapshot;
        lock (Clients) snapshot = Clients.ToArray();
        var now = Environment.TickCount64;
        foreach (var c in snapshot)
        {
            if (c.Gone) continue;
            var dt = Math.Min(0.25, (now - c.Last) / 1000.0);
            c.Last = now;
            c.Tick(dt);
        }
        if (Clients.Count == 0) sender.Stop();
    }
}
