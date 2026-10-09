using AppOrbit.Scene;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;

namespace AppOrbit.Figure;

/// <summary>
/// figures/orbit.js, drawn with the Hairline geometry in <see cref="Iso"/>: one app screen taken
/// apart into four plates on one axis (routes, view model, states, UI). The pointer's x scrubs the
/// gap through a spring; its y picks a plate, which takes the bright edge from the button. One
/// dashed thread drops from the button to the member it is bound to, and the plates between hide
/// it. A click while a plate is named picks its lens. Strokes follow Hairline's ten rules: one
/// stroke in four weights, no words inside the drawing.
/// </summary>
public sealed partial class OrbitFigure : SKCanvasElement
{
    private const double W = 132, H = 96, TK = 2.4, Rest = 0.32;
    private const int Button = 3, Bind = 1;
    private const double VbW = 400, VbH = 320;

    private sealed record Box(double X0, double Y0, double X1, double Y1, double R, string Cls);
    private sealed record Layer(string Name, double[] R, double Rad, double[][][] Segs, Box[] Boxes, double[][] Pads);

    private static readonly Layer[] Lay =
    {
        new("routes", new[] { 0d, 0, W, H }, 7, new[] { new[] { new[] { 22d, 48 }, new[] { 50d, 48 } }, new[] { new[] { 82d, 48 }, new[] { 110d, 48 } } },
            new[] { new Box(52, 30, 80, 66, 4, "nf lo") }, new[] { new[] { 14d, 48 }, new[] { 118d, 48 } }),
        new("behavior", new[] { 10d, 10, 122, 86 }, 5, new[] { new[] { new[] { 18d, 22 }, new[] { 60d, 22 } }, new[] { new[] { 18d, 32 }, new[] { 84d, 32 } }, new[] { new[] { 18d, 42 }, new[] { 52d, 42 } }, new[] { new[] { 18d, 62 }, new[] { 72d, 62 } }, new[] { new[] { 18d, 72 }, new[] { 96d, 72 } } },
            new[] { new Box(15, 47, 117, 57, 2, "nf sil") }, Array.Empty<double[]>()),
        new("states", new[] { 4d, 4, 128, 92 }, 6, new[] { new[] { new[] { 16d, 24 }, new[] { 30d, 24 } }, new[] { new[] { 55d, 24 }, new[] { 69d, 24 } }, new[] { new[] { 94d, 24 }, new[] { 108d, 24 } } },
            new[] { new Box(12, 18, 42, 78, 3, "nf"), new Box(51, 18, 81, 78, 3, "nf"), new Box(90, 18, 120, 78, 3, "nf"), new Box(16, 66, 38, 73, 2, "nf"), new Box(55, 66, 77, 73, 2, "nf lo"), new Box(94, 66, 116, 73, 2, "nf") }, Array.Empty<double[]>()),
        new("ui", new[] { 0d, 0, W, H }, 7, new[] { new[] { new[] { 10d, 12 }, new[] { 58d, 12 } }, new[] { new[] { 25d, 25 }, new[] { 92d, 25 } }, new[] { new[] { 25d, 39 }, new[] { 78d, 39 } } },
            new[] { new Box(10, 20, 20, 30, 2, "nf"), new Box(10, 34, 20, 44, 2, "nf"), new Box(10, 52, 122, 63, 3, "nf"), new Box(10, 73, 122, 86, 4, "nf sil") }, Array.Empty<double[]>()),
    };

    private static readonly Dictionary<string, string> LensOf = new() { ["ui"] = "structure", ["states"] = "states", ["behavior"] = "behavior", ["routes"] = "navigation" };

    private sealed class Plate
    {
        public required Sample[] Ring; public required Sample[] Inner; public required Sample[] Ext; public required Sample[][] BoxRings;
    }

    private readonly Camera _cam = Iso.Cam(45, 0.5, 1.7);
    private readonly Projector _p;
    private readonly Func<Sample, bool> _front;
    private readonly Plate[] _plates;
    private readonly Sample[] _pad = Iso.Circ(5, 24);
    private double _gap = 20;
    private int _act = -1;
    // the gap spring (kernel defaults k 100, c 18), as a share of GAP
    private double _x = Rest, _v, _t = Rest;
    private IDisposable? _loop;
    private Palette _pal = Palette.Light;
    private readonly SKPaint _fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeJoin = SKStrokeJoin.Round, StrokeCap = SKStrokeCap.Round };
    private volatile Snapshot? _snap;

    private sealed record Snapshot(double E, int Act, float Scale);

    public event Action<string>? ReadChanged;
    public event Action<string>? Picked;
    public bool ReducedMotion { get; set; }
    public string Read { get; private set; } = "rest";

    public OrbitFigure()
    {
        if (!IsSupportedOnCurrentPlatform()) throw new PlatformNotSupportedException("The figure needs Skia rendering.");
        var pts = new List<Vec3> { new(0, 0, 0), new(W, H, 0), new(W, 0, 0), new(0, H, 0), new(0, 0, 3 * 34 + TK), new(W, 0, 3 * 34 + TK) };
        Iso.Fit(_cam, pts, 190, 166);
        _p = Iso.Proj(_cam);
        _front = Iso.Facing(_cam);
        _plates = Lay.Select(l =>
        {
            var (ring, inner) = Iso.Rings(l.R[0], l.R[1], l.R[2], l.R[3], l.Rad, 1.3);
            var (left, right, nearest) = Iso.Extremes(_p, ring);
            return new Plate { Ring = ring, Inner = inner, Ext = new[] { left, right, nearest }, BoxRings = l.Boxes.Select(b => Iso.Rrect(b.X0, b.Y0, b.X1, b.Y1, b.R)).ToArray() };
        }).ToArray();
        ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Cross);
        PointerMoved += OnMoved;
        PointerExited += (_, _) => { _t = Rest; SetAct(-1); Loop(); };
        Tapped += (_, _) => { if (_act >= 0 && LensOf.TryGetValue(Lay[_act].Name, out var lens)) Picked?.Invoke(lens); };
        SizeChanged += (_, _) => Project();
        Unloaded += (_, _) => StopLoop();
        Project();
    }

    public void SetPalette(Palette p) { _pal = p; Project(); }

    /// <summary>The journey runner's pointer, in element pixels.</summary>
    public void SimulatePointer(double x, double y) => Move(new Point(x, y));
    public void SimulateClick() { if (_act >= 0 && LensOf.TryGetValue(Lay[_act].Name, out var lens)) Picked?.Invoke(lens); }

    private Point ToViewBox(Point p) => new(p.X / Math.Max(1, ActualWidth) * VbW, p.Y / Math.Max(1, ActualHeight) * VbH);

    private void OnMoved(object sender, PointerRoutedEventArgs e) => Move(e.GetCurrentPoint(this).Position);

    private void Move(Point p)
    {
        var vb = ToViewBox(p);
        _t = Rest + (1 - Rest) * Math.Clamp((vb.X - 60) / 280, 0, 1);
        SetAct(Pick(vb));
        Loop();
    }

    private static bool Inside(Point pt, Vec2[] pg)
    {
        var c = false;
        for (int i = 0, j = pg.Length - 1; i < pg.Length; j = i++)
        {
            if ((pg[i].Y > pt.Y) != (pg[j].Y > pt.Y) && pt.X < (pg[j].X - pg[i].X) * (pt.Y - pg[i].Y) / (pg[j].Y - pg[i].Y) + pg[i].X) c = !c;
        }
        return c;
    }

    private Vec2[] Corners(double[] r, double z) => new[] { _p(r[0], r[1], z), _p(r[2], r[1], z), _p(r[2], r[3], z), _p(r[0], r[3], z) };

    /// <summary>The topmost plate under the pointer, tested where the plates are going, not where they are.</summary>
    private int Pick(Point vb)
    {
        for (var i = Lay.Length - 1; i >= 0; i--)
            if (Inside(vb, Corners(Lay[i].R, i * _gap * _t + TK))) return i;
        return -1;
    }

    private void SetAct(int a)
    {
        if (a == _act) return;
        _act = a;
        UpdateRead();
    }

    private void UpdateRead()
    {
        var z = _act * _gap * _x;
        var read = _act >= 0 ? $"0{Lay.Length - _act} · {Lay[_act].Name} · z {z:0.0}" : "rest";
        if (read == Read) return;
        Read = read;
        ReadChanged?.Invoke(read);
    }

    // ---------- loop ----------
    private void Loop()
    {
        _loop ??= FrameLoop.Start(DispatcherQueue, Tick);
    }

    private void StopLoop()
    {
        _loop?.Dispose();
        _loop = null;
    }

    private void Tick(double dt)
    {
        bool moving;
        if (ReducedMotion) { _x = _t; _v = 0; moving = false; }
        else
        {
            var n = Math.Max(1, (int)Math.Ceiling(dt * 240)); var h = dt / n;
            for (var i = 0; i < n; i++) { var a = -100 * (_x - _t) - 18 * _v; _v += a * h; _x += _v * h; }
            moving = Math.Abs(_x - _t) > 0.002 || Math.Abs(_v) > 0.01;
            if (!moving) { _x = _t; _v = 0; }
        }
        UpdateRead();
        Project();
        if (!moving) StopLoop();
    }

    private void Project()
    {
        _snap = new Snapshot(_x, _act, (float)(Math.Max(1, ActualWidth) / VbW));
        Invalidate();
    }

    // ---------- drawing ----------
    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        var s = _snap;
        if (s == null) return;
        var scale = (float)(area.Width / VbW);
        canvas.Save();
        canvas.Scale(scale);
        var sw = 0.9f / scale; // hairline: one stroke, non-scaling
        _stroke.StrokeWidth = sw;
        using var dash = SKPathEffect.CreateDash(new[] { 1f / scale, 3f / scale }, 0);
        double Z(int i) => i * _gap * s.E;

        for (var i = 0; i < Lay.Length; i++)
        {
            var l = Lay[i]; var pl = _plates[i];
            var zi = Z(i); var zt = zi + TK;
            // guides go in before the plate they belong to: paint order hides them
            if (i > 0)
            {
                var zp = Z(i - 1) + TK;
                _stroke.Color = _pal.Rule; _stroke.PathEffect = dash;
                foreach (var q in pl.Ext) DrawSeg(canvas, _p(q.U, q.V, zi), _p(q.U, q.V, zp));
                _stroke.PathEffect = null;
            }
            if (i == Bind + 1)
            {
                // from the button's centre on the ui plate's underside down to the ringed row on the behavior plate
                var b = Lay[3].Boxes[Button]; var r = Lay[Bind].Boxes[0];
                _stroke.Color = _pal.Rule; _stroke.PathEffect = dash;
                DrawSeg(canvas, _p((b.X0 + b.X1) / 2, (b.Y0 + b.Y1) / 2, Z(3)), _p((r.X0 + r.X1) / 2, (r.Y0 + r.Y1) / 2, Z(Bind) + TK));
                _stroke.PathEffect = null;
            }
            var outline = Iso.Prism(_p, _front, pl.Ring, pl.Inner, zi, zt);
            using (var sil = IsoPaths.Poly(outline.Silhouette))
            {
                _fill.Color = _pal.Paper; canvas.DrawPath(sil, _fill);
                _stroke.Color = i == s.Act ? _pal.Ink : _pal.Ink3; canvas.DrawPath(sil, _stroke);
            }
            using (var cr = IsoPaths.Open(outline.Crease)) { _stroke.Color = _pal.Rule2; canvas.DrawPath(cr, _stroke); }
            _stroke.Color = _pal.Rule;
            foreach (var seg in l.Segs) DrawSeg(canvas, _p(seg[0][0], seg[0][1], zt), _p(seg[1][0], seg[1][1], zt));
            for (var k = 0; k < l.Boxes.Length; k++)
            {
                var cls = l.Boxes[k].Cls;
                var hi = i == 3 && k == Button && s.Act < 0;
                _stroke.Color = hi ? _pal.Ink : cls.Contains("sil") ? _pal.Ink3 : cls.Contains("lo") ? _pal.Rule2 : _pal.Rule;
                using var bp = IsoPaths.Poly(Iso.RingAt(_p, pl.BoxRings[k], zt));
                canvas.DrawPath(bp, _stroke);
            }
            _stroke.Color = _pal.Rule;
            foreach (var pad in l.Pads)
            {
                var pts = _pad.Select(q => _p(pad[0] + q.U, pad[1] + q.V, zt)).ToArray();
                using var pp = IsoPaths.Poly(pts);
                canvas.DrawPath(pp, _stroke);
            }
        }
        canvas.Restore();
    }

    private void DrawSeg(SKCanvas canvas, Vec2 a, Vec2 b) => canvas.DrawLine((float)a.X, (float)a.Y, (float)b.X, (float)b.Y, _stroke);
}
