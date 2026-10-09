using System.Diagnostics;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace AppOrbit.Dock;

/// <summary>Where the viewer can rest, in shell coordinates, with the reach of its magnet.</summary>
public readonly record struct Home(double X, double Y, double W, double H, double Reach);

/// <summary>What the shell gives the controller: the homes, the viewer element, the ghost and the state hooks.</summary>
public sealed class DockHost
{
    public required FrameworkElement Viewer { get; init; }
    public required Rectangle Ghost { get; init; }
    public required UIElement Head { get; init; }
    public required FrameworkElement Shell { get; init; }
    public required Func<string, Home> HomeOf { get; init; }
    public required Func<string> GetMode { get; init; }
    public required Action<string> SetMode { get; init; }
    public required Func<bool> ReducedMotion { get; init; }
    public required Action<bool> SetCarrying { get; init; }
    public required Action<bool, bool> SetFloating { get; init; } // (floating, lifted)
    public required Func<Brush> GhostDashed { get; init; }
    public required Func<(Brush Stroke, Brush Fill)> GhostHolder { get; init; }
    public required Func<(Brush Stroke, Brush Fill)> GhostHard { get; init; }
}

/// <summary>
/// dock.js: pick the viewer up by its head bar, carry it, feel the pull of a home, let it settle.
/// Two homes, the main column (expanded) and the dock slot (docked); the panel never floats free.
/// All motion is springs on one frame loop, subscribed only while something moves; reduced motion
/// resolves each change in a step.
/// </summary>
public sealed class DockController
{
    private sealed class Spring
    {
        public double X, V, T, K, C;
        public Spring(double k, double c) { K = k; C = c; }
        public bool Step(double dt)
        {
            var n = Math.Max(1, (int)Math.Ceiling(dt * 240)); var h = dt / n;
            for (var i = 0; i < n; i++) { var a = -K * (X - T) - C * V; V += a * h; X += V * h; }
            if (Math.Abs(X - T) < 0.3 && Math.Abs(V) < 2) { X = T; V = 0; return false; }
            return true;
        }
        public void Snap(double v) { X = v; T = v; V = 0; }
    }

    private sealed class Drag { public double Px, Py, Gx, Gy; public string Origin = ""; public bool Pending; public Point Start; public uint PointerId; }

    private readonly DockHost _h;
    private readonly Spring _x = new(190, 26), _y = new(190, 26), _w = new(240, 30), _hgt = new(240, 30);
    private string _phase = "resting"; // resting | lifted | flying
    private Drag? _drag;
    private string? _preview;
    private IDisposable? _loop;
    private readonly Stopwatch _clock = new();
    private double _settleT = -1;
    private readonly ScaleTransform _settle = new();

    public DockController(DockHost host)
    {
        _h = host;
        _h.Viewer.RenderTransform = _settle;
        _h.Viewer.RenderTransformOrigin = new Point(0.5, 0.5);
        _h.Head.PointerPressed += OnHeadPressed;
        _h.Head.PointerMoved += OnHeadMoved;
        _h.Head.PointerReleased += (_, e) => End(e);
        _h.Head.PointerCanceled += (_, e) => End(e);
        _h.Head.PointerCaptureLost += (_, e) => End(e);
    }

    public bool IsMoving => _phase != "resting";
    public string Phase => _phase;
    public string GhostState { get; private set; } = "none";
    public double Width => _w.X;
    public int Ticks { get; private set; }
    public double Elapsed => _clock.Elapsed.TotalSeconds;

    private static double Clamp(double v, double a, double b) => Math.Max(a, Math.Min(b, v));
    private static double Smooth(double t) { t = Clamp(t, 0, 1); return t * t * (3 - 2 * t); }

    // ---------- placing ----------
    private void Apply()
    {
        Canvas.SetLeft(_h.Viewer, _x.X);
        Canvas.SetTop(_h.Viewer, _y.X);
        _h.Viewer.Width = Math.Max(1, _w.X);
        _h.Viewer.Height = Math.Max(1, _hgt.X);
    }

    private void SnapTo(Home home) { _x.Snap(home.X); _y.Snap(home.Y); _w.Snap(home.W); _hgt.Snap(home.H); Apply(); }
    private void Retarget(double x, double y, double w, double h) { _x.T = x; _y.T = y; _w.T = w; _hgt.T = h; }

    /// <summary>Puts the viewer on its current home without motion (boot, resize).</summary>
    public void Place()
    {
        if (_phase != "resting") return;
        SnapTo(_h.HomeOf(_h.GetMode()));
    }

    private void ShowGhost(Home? home, string kind)
    {
        if (home == null) { _h.Ghost.Visibility = Visibility.Collapsed; GhostState = "none"; _preview = null; return; }
        var g = _h.Ghost;
        Canvas.SetLeft(g, home.Value.X); Canvas.SetTop(g, home.Value.Y);
        g.Width = Math.Max(1, home.Value.W); g.Height = Math.Max(1, home.Value.H);
        switch (kind)
        {
            case "holder": { var (s, f) = _h.GhostHolder(); g.Stroke = s; g.Fill = f; g.StrokeDashArray = null; break; }
            case "hard": { var (s, f) = _h.GhostHard(); g.Stroke = s; g.Fill = f; g.StrokeDashArray = null; break; }
            default: g.Stroke = _h.GhostDashed(); g.Fill = null; g.StrokeDashArray = new DoubleCollection { 4, 3 }; break;
        }
        g.Visibility = Visibility.Visible;
        GhostState = kind;
    }

    // ---------- loop ----------
    private void Loop()
    {
        if (_loop != null) return;
        _clock.Restart();
        _loop = Scene.FrameLoop.Start(_h.Shell.DispatcherQueue, Tick);
    }

    private void StopLoop()
    {
        _loop?.Dispose();
        _loop = null;
    }

    private void Tick(double dt)
    {
        Ticks++;
        var moving = false;
        if (_x.Step(dt)) moving = true;
        if (_y.Step(dt)) moving = true;
        if (_w.Step(dt)) moving = true;
        if (_hgt.Step(dt)) moving = true;
        Apply();
        if (_settleT >= 0)
        {
            _settleT += dt * 1000;
            var t = Math.Min(1, _settleT / 180);
            var s = t < 0.55 ? 1.012 + (0.992 - 1.012) * (t / 0.55) : 0.992 + (1 - 0.992) * ((t - 0.55) / 0.45);
            _settle.ScaleX = _settle.ScaleY = s;
            if (t >= 1) { _settleT = -1; _settle.ScaleX = _settle.ScaleY = 1; } else moving = true;
        }
        if (_phase == "flying" && !moving) { Settle(); return; }
        if (!moving && _phase != "lifted") StopLoop();
    }

    // ---------- phases ----------
    /// <summary>Picks the viewer up at a shell point.</summary>
    public void Lift(Point p, uint pointerId = 0)
    {
        var origin = _h.GetMode();
        var from = _h.HomeOf(origin);
        _phase = "lifted";
        _h.SetFloating(true, true);
        var px = p.X; var py = p.Y;
        // the grab point, as a share of the panel, stays under the pointer while the size changes
        _drag = new Drag { Px = px, Py = py, Gx = Clamp((px - from.X) / Math.Max(1, from.W), 0.05, 0.95), Gy = Clamp((py - from.Y) / Math.Max(1, from.H), 0, 1), Origin = origin, PointerId = pointerId };
        SnapTo(from);
        var dock = _h.HomeOf(State.Modes.Docked);
        var cw = dock.W * 1.03; var ch = dock.H * 1.03;
        Retarget(px - _drag.Gx * cw, py - _drag.Gy * ch, cw, ch);
        if (_h.ReducedMotion()) SnapTo(new Home(_x.T, _y.T, cw, ch, 0));
        ShowGhost(from, "holder");
        _preview = null;
        _h.SetCarrying(true);
        Loop();
    }

    /// <summary>Carries the lifted viewer to a shell point.</summary>
    public void Carry(Point p)
    {
        if (_drag == null || _phase != "lifted") return;
        var px = p.X; var py = p.Y;
        _drag.Px = px; _drag.Py = py;
        var dock = _h.HomeOf(State.Modes.Docked);
        var expanded = _h.HomeOf(State.Modes.Expanded);
        var boundsW = _h.Shell.ActualWidth; var boundsH = _h.Shell.ActualHeight;
        var w = _w.T; var h = _hgt.T;
        // the panel stays inside the shell: a tool cannot be carried through the bench
        var x = Clamp(px - _drag.Gx * w, 0, Math.Max(0, boundsW - w));
        var y = Clamp(py - _drag.Gy * h, 0, Math.Max(0, boundsH - h));
        static bool Inside(Home r, double qx, double qy) => qx >= r.X && qx <= r.X + r.W && qy >= r.Y && qy <= r.Y + r.H;
        var cx = x + w / 2; var cy = y + h / 2;
        var toDock = Math.Sqrt(Math.Pow(cx - (dock.X + dock.W / 2), 2) + Math.Pow(cy - (dock.Y + dock.H / 2), 2));
        string? home = null;
        if (toDock < dock.Reach || Inside(dock, px, py)) home = State.Modes.Docked;
        else if (Inside(expanded, px, py) && _drag.Origin == State.Modes.Docked) home = State.Modes.Expanded;
        if (home == State.Modes.Docked)
        {
            // the magnet: alignment blends in as the slot comes near, but the hand stays in charge
            var pull = Smooth((dock.Reach - toDock) / dock.Reach) * 0.55;
            x += (dock.X - x) * pull; y += (dock.Y - y) * pull;
        }
        Retarget(x, y, w, h);
        if (_h.ReducedMotion()) SnapTo(new Home(x, y, w, h, 0));
        if (home != _preview)
        {
            _preview = home;
            var origin = _drag.Origin;
            // no home in reach: the holder you lifted it from stays drawn, so there is always a place it belongs
            ShowGhost(home != null ? _h.HomeOf(home) : _h.HomeOf(origin), home != null ? "show" : "holder");
            _preview = home;
            // the layout answers before the drop: the columns take the destination's shape (the inspector widens for the dock)
            _h.SetMode(home ?? origin);
            // homes moved with the grid: refresh the ghost after the layout settles
            _h.Shell.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (_drag == null) return;
                var kind = _preview != null ? "show" : "holder";
                ShowGhost(_h.HomeOf(_preview ?? _drag.Origin), kind);
                _preview = home;
            });
        }
        Loop();
    }

    /// <summary>Lets go: flies to the previewed home, or back where it came from.</summary>
    public void Release()
    {
        if (_drag == null) return;
        var home = _preview ?? _drag.Origin;
        _drag = null;
        FlyTo(home);
    }

    /// <summary>Flies the viewer to a home along the springs, then settles there. Also the D key's path.</summary>
    public void FlyTo(string home)
    {
        _phase = "flying";
        _h.SetFloating(true, false);
        _h.SetMode(home);
        if (_h.ReducedMotion())
        {
            // one step: the columns take their shape now, the viewer lands on the home they give
            _h.Shell.UpdateLayout();
            var hm = _h.HomeOf(home);
            SnapTo(hm);
            Settle();
            return;
        }
        _h.Shell.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (_phase != "flying") return;
            var hm = _h.HomeOf(home);
            Retarget(hm.X, hm.Y, hm.W, hm.H);
            ShowGhost(hm, "hard");
            Loop();
        });
    }

    private void Settle()
    {
        _phase = "resting";
        _h.SetCarrying(false);
        _h.SetFloating(false, false);
        ShowGhost(null, "none");
        Place();
        if (!_h.ReducedMotion()) { _settleT = 0; Loop(); }
        else StopLoop();
    }

    public void Toggle()
    {
        if (_phase != "resting") return;
        FlyTo(_h.GetMode() == State.Modes.Docked ? State.Modes.Expanded : State.Modes.Docked);
    }

    // ---------- input ----------
    private static bool OnControl(object? source)
    {
        var el = source as DependencyObject;
        while (el != null) { if (el is Control and not ContentControl) return true; if (el is Button) return true; el = VisualTreeHelper.GetParent(el); }
        return false;
    }

    private void OnHeadPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(_h.Shell);
        if (!pt.Properties.IsLeftButtonPressed || OnControl(e.OriginalSource)) return;
        if (_phase == "flying") return;
        _drag = new Drag { Pending = true, Start = pt.Position, PointerId = e.Pointer.PointerId };
        _h.Head.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnHeadMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_drag == null) return;
        var p = e.GetCurrentPoint(_h.Shell).Position;
        if (_drag.Pending)
        {
            if (Math.Sqrt(Math.Pow(p.X - _drag.Start.X, 2) + Math.Pow(p.Y - _drag.Start.Y, 2)) < 4) return;
            Lift(_drag.Start, e.Pointer.PointerId);
        }
        if (_phase == "lifted") Carry(p);
        e.Handled = true;
    }

    private void End(Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_drag == null) return;
        if (_drag.Pending) { _drag = null; return; }
        Release();
    }
}
