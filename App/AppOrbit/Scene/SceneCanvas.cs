using AppOrbit.Graph;
using AppOrbit.Layout;
using Card = AppOrbit.Layout.Card;
using AppOrbit.State;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;

namespace AppOrbit.Scene;

/// <summary>
/// The viewer's stage: one SKCanvasElement that draws every card and connector, owns the camera,
/// the card offsets and the pointer gestures (scene.js createScene). The store owns focus, hover
/// and cursor; the canvas asks for changes through its events and is told the result through Render
/// and UpdateHighlights.
/// </summary>
public sealed partial class SceneCanvas : SKCanvasElement
{
    private const string OffsetsKey = "app-orbit.offsets";

    private GraphIndex? _g;
    private SceneRenderer? _renderer;
    private readonly Camera _cam = new();
    private List<CardGeom> _geoms = new();
    private LayoutResult? _layout;
    private AppState? _state;
    private SceneFrame? _frame;
    private Dictionary<string, Dictionary<string, double[]>> _offsets = new();
    private IDisposable? _loop;
    private double _fade = 1, _draw = 1;
    private long _fadeStart, _drawStart;
    private long _zoomLock;
    private Drag? _drag;
    private string? _lastView;
    private readonly List<string> _tabOrder = new();

    private sealed class Drag
    {
        public Point Start; public double Yaw, Pitch; public bool Moved; public uint PointerId; public string? Key; public double BaseX, BaseY;
    }

    public event Action<string>? FocusRequested;
    public event Action<string?>? HoverChanged;
    public event Action<string?>? CursorChanged;
    public event Action<string>? ZoomInRequested;
    public event Action? ZoomOutRequested;
    public event Action<bool>? LayoutMoved;
    public event Action<double, double, double>? CameraChanged;

    public SceneCanvas()
    {
        if (!IsSupportedOnCurrentPlatform())
            throw new PlatformNotSupportedException("The viewer is drawn with SKCanvasElement, which needs Skia rendering on this platform.");
        _offsets = Prefs.LoadJson(OffsetsKey, new Dictionary<string, Dictionary<string, double[]>>());
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCanceled += OnPointerEnded;
        PointerCaptureLost += OnPointerEnded;
        PointerExited += (_, _) => { if (_drag == null) HoverChanged?.Invoke(null); };
        PointerWheelChanged += OnWheel;
        DoubleTapped += OnDoubleTapped;
        SizeChanged += (_, _) => { if (_layout != null && _state != null) { ComputeFit(); Project(); } };
        Unloaded += (_, _) => StopLoop();
    }

    public void Attach(GraphIndex g, Palette palette)
    {
        _g = g;
        _renderer?.Dispose();
        _renderer = new SceneRenderer(g, palette);
        if (_layout != null && _state != null) Render(_state, _layout);
    }

    public Camera Camera => _cam;
    public bool HasMovedCards => _layout != null && _state != null && _offsets.ContainsKey(LayoutId(_state, _layout));

    private static string LayoutId(AppState s, LayoutResult l) => $"{l.Level}:{s.FocusId ?? ""}:{s.Lens}:{s.View}:{s.Mode}";

    private (double X, double Y) OffsetOf(string key)
    {
        if (_state == null || _layout == null) return (0, 0);
        return _offsets.TryGetValue(LayoutId(_state, _layout), out var m) && m.TryGetValue(key, out var o) ? (o[0], o[1]) : (0, 0);
    }

    // ---------------------------------------------------------------- render

    /// <summary>A new layout (focus, lens, view or mode changed): rebuild shapes, refit, crossfade.</summary>
    public void Render(AppState state, LayoutResult layout)
    {
        if (_g == null || _renderer == null) return;
        var prevView = _lastView;
        _state = state;
        _layout = layout;
        _lastView = state.View;
        _renderer.Previews.UiFidelity = state.Fidelity == Fidelities.Ui;
        _geoms = SceneGeometry.Build(_g, layout, _renderer.Previews);
        _tabOrder.Clear();
        foreach (var gm in _geoms) foreach (var h in gm.Hits) if (h.Kind != "route" && !_tabOrder.Contains(h.Id)) _tabOrder.Add(h.Id);
        if (prevView != state.View) { var d = Camera.Default(state.View); _cam.SetTarget(d.Yaw, d.Pitch); _cam.Snap(); }
        ComputeFit();
        LayoutMoved?.Invoke(HasMovedCards);
        if (!state.ReducedMotion)
        {
            _fade = 0; _fadeStart = Environment.TickCount64;
            _draw = 0; _drawStart = Environment.TickCount64;
            StartLoop();
        }
        else { _fade = 1; _draw = 1; }
        Project();
    }

    /// <summary>Hover, focus or cursor changed; the layout did not.</summary>
    public void UpdateHighlights(AppState state)
    {
        _state = state;
        Project();
    }

    private void ComputeFit()
    {
        if (_layout == null || _state == null) return;
        _cam.Fit(ActualWidth, ActualHeight, _layout.Bounds, _state.Mode == Modes.Docked);
    }

    private void Project()
    {
        if (_layout == null || _state == null) return;
        SceneGeometry.Project(_geoms, _cam, ActualWidth, ActualHeight, OffsetOf);
        _frame = new SceneFrame
        {
            Geoms = _geoms, Links = _layout.Links,
            FocusId = _state.FocusId, HoverId = _state.HoverId, CursorId = _state.CursorId,
            Opacity = (float)_fade, DrawT = (float)_draw, Docked = _state.Mode == Modes.Docked,
            Width = (float)ActualWidth, Height = (float)ActualHeight,
        };
        CameraChanged?.Invoke(_cam.Yaw, _cam.Pitch, _cam.S);
        Invalidate();
    }

    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        var frame = _frame;
        if (frame == null || _renderer == null) return;
        _renderer.Paint(canvas, frame);
    }

    // ---------------------------------------------------------------- loop (subscribed only while something moves)

    private void StartLoop()
    {
        _loop ??= FrameLoop.Start(DispatcherQueue, OnTick);
    }

    private void StopLoop()
    {
        _loop?.Dispose();
        _loop = null;
    }

    private void OnTick(double dt)
    {
        var now = Environment.TickCount64;
        var moving = _cam.Step(dt);
        if (_fade < 1) { _fade = Math.Min(1, (now - _fadeStart) / 180.0); moving = true; }
        if (_draw < 1) { _draw = Math.Min(1, (now - _drawStart) / 240.0); moving = true; }
        Project();
        if (!moving) StopLoop();
    }

    // ---------------------------------------------------------------- camera

    public void SetCamera(double? yaw = null, double? pitch = null, double? scale = null, bool immediate = false)
    {
        _cam.SetTarget(yaw, pitch, scale);
        if (immediate || _state?.ReducedMotion == true) { _cam.Snap(); Project(); }
        else StartLoop();
    }

    public void ResetCamera(string? view = null)
    {
        var d = Camera.Default(view ?? _state?.View ?? Views.Orbit);
        SetCamera(d.Yaw, d.Pitch, 1);
    }

    public void Nudge(double dYaw, double dPitch) => SetCamera(_cam.TargetYaw + dYaw, _cam.TargetPitch + dPitch);

    // ---------------------------------------------------------------- offsets (moved cards)

    private void SaveOffsets() => Prefs.SaveJson(OffsetsKey, _offsets);

    public void ResetOffsets()
    {
        if (_state == null || _layout == null) return;
        _offsets.Remove(LayoutId(_state, _layout));
        SaveOffsets();
        Project();
        LayoutMoved?.Invoke(false);
    }

    public bool NudgeCard(string id, double dx, double dy)
    {
        if (_state == null || _layout == null) return false;
        var gm = _geoms.FirstOrDefault(x => x.Card.Id == id && !x.Card.Key.Contains('#')) ?? _geoms.FirstOrDefault(x => x.Card.Id == id);
        if (gm == null) return false;
        var lid = LayoutId(_state, _layout);
        if (!_offsets.TryGetValue(lid, out var m)) _offsets[lid] = m = new();
        var o = m.TryGetValue(gm.Card.Key, out var cur) ? cur : new double[] { 0, 0 };
        m[gm.Card.Key] = new[] { o[0] + dx, o[1] + dy };
        SaveOffsets();
        Project();
        LayoutMoved?.Invoke(true);
        return true;
    }

    public void ResetCard(string key)
    {
        if (_state == null || _layout == null) return;
        var lid = LayoutId(_state, _layout);
        if (!_offsets.TryGetValue(lid, out var m) || !m.Remove(key)) return;
        if (m.Count == 0) _offsets.Remove(lid);
        SaveOffsets();
        Project();
        LayoutMoved?.Invoke(_offsets.ContainsKey(lid));
    }

    // ---------------------------------------------------------------- keyboard cursor

    /// <summary>Tab / Shift+Tab: the next selectable thing in layout order.</summary>
    public void StepCursor(int dir)
    {
        if (_tabOrder.Count == 0 || _state == null) return;
        var i = _state.CursorId != null ? _tabOrder.IndexOf(_state.CursorId) : -1;
        i = i < 0 ? (dir > 0 ? 0 : _tabOrder.Count - 1) : (i + dir + _tabOrder.Count) % _tabOrder.Count;
        CursorChanged?.Invoke(_tabOrder[i]);
    }

    /// <summary>For the journey runner: the id a press at a projected anchor would hit.</summary>
    public string? HitAtAnchor(string anchorKey)
    {
        foreach (var gm in _geoms)
        {
            if (!gm.Anchors.TryGetValue(anchorKey, out var local)) continue;
            var (x, y, _) = gm.M.Project(local.X, local.Y);
            return SceneGeometry.HitTest(_geoms, new SKPoint((float)x, (float)y))?.Hit.Id;
        }
        return null;
    }

    // ---------------------------------------------------------------- pointer

    private (CardGeom Geom, HitRegion Hit)? HitAt(Point p) => SceneGeometry.HitTest(_geoms, new SKPoint((float)p.X, (float)p.Y));

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(this);
        if (!pt.Properties.IsLeftButtonPressed) return;
        var hit = HitAt(pt.Position);
        var key = hit?.Geom.Card.Key;
        var (bx, by) = key != null ? OffsetOf(key) : (0, 0);
        _drag = new Drag { Start = pt.Position, Yaw = _cam.TargetYaw, Pitch = _cam.TargetPitch, PointerId = e.Pointer.PointerId, Key = key, BaseX = bx, BaseY = by };
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(this).Position;
        if (_drag == null)
        {
            var hit = HitAt(p);
            HoverChanged?.Invoke(hit?.Hit.Id);
            return;
        }
        var dx = p.X - _drag.Start.X; var dy = p.Y - _drag.Start.Y;
        if (!_drag.Moved && Math.Sqrt(dx * dx + dy * dy) < 5) return;
        if (!_drag.Moved) { _drag.Moved = true; CapturePointer(e.Pointer); }
        if (_drag.Key != null && _state != null && _layout != null)
        {
            // move the card in scene units: undo the camera scale and the foreshortening of the orbit
            var s = _cam.S;
            var kx = Math.Max(0.5, Math.Cos(_cam.Yaw * Math.PI / 180));
            var ky = Math.Max(0.5, Math.Cos(_cam.Pitch * Math.PI / 180));
            var lid = LayoutId(_state, _layout);
            if (!_offsets.TryGetValue(lid, out var m)) _offsets[lid] = m = new();
            m[_drag.Key] = new[] { Math.Round(_drag.BaseX + dx / (s * kx)), Math.Round(_drag.BaseY + dy / (s * ky)) };
            Project();
            return;
        }
        if (_state?.View == Views.Flat) return;
        SetCamera(_drag.Yaw + dx * 0.25, _drag.Pitch - dy * 0.18, immediate: true);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var drag = _drag;
        _drag = null;
        ReleasePointerCapture(e.Pointer);
        if (drag == null) return;
        if (drag.Moved)
        {
            if (drag.Key != null) { SaveOffsets(); LayoutMoved?.Invoke(true); }
            return;
        }
        var hit = HitAt(e.GetCurrentPoint(this).Position);
        if (hit != null) { FocusRequested?.Invoke(hit.Value.Hit.Id); e.Handled = true; }
    }

    private void OnPointerEnded(object sender, PointerRoutedEventArgs e)
    {
        if (_drag?.Moved == true && _drag.Key != null) { SaveOffsets(); LayoutMoved?.Invoke(true); }
        _drag = null;
    }

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var hit = HitAt(e.GetPosition(this));
        if (hit != null) ResetCard(hit.Value.Geom.Card.Key);
    }

    /// <summary>Semantic zoom: continuous scale until a threshold, then a level change; momentum after a jump is swallowed.</summary>
    private void OnWheel(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;
        var pt = e.GetCurrentPoint(this);
        Wheel(pt.Properties.MouseWheelDelta, HitAt(pt.Position)?.Hit.Id); // +120 per notch up
    }

    /// <summary>The journey runner's wheel: n notches up over an entity.</summary>
    public void SimulateWheel(int notches, string? overId)
    {
        for (var i = 0; i < notches; i++) Wheel(120, overId);
    }

    private void Wheel(int delta, string? hitId)
    {
        var now = Environment.TickCount64;
        if (now < _zoomLock) { _zoomLock = now + 250; return; }
        var factor = Math.Exp(delta * 0.0016);
        var next = Math.Clamp(_cam.TargetScale * factor, Camera.ScaleMin, Camera.ScaleMax);
        var over = hitId ?? _state?.HoverId;
        if (next >= 1.6 && over != null && over != _state?.FocusId)
        {
            _zoomLock = now + 450;
            SetCamera(scale: 1, immediate: true);
            ZoomInRequested?.Invoke(over);
            return;
        }
        if (next <= 0.62)
        {
            _zoomLock = now + 450;
            SetCamera(scale: 1, immediate: true);
            ZoomOutRequested?.Invoke();
            return;
        }
        SetCamera(scale: next, immediate: true);
    }
}
