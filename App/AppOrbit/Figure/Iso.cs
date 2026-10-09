// The drawing maths of Hairline (https://github.com/lucasmarkes/hairline, MIT License,
// Copyright (c) 2026 Lucas Marques), ported arithmetic-for-arithmetic from
// packages/hairline/src/core/iso.ts so a result here can be checked against the
// original by eye. Path strings are left out: Skia wants points, so every function
// that wrote an SVG `d` there returns points here, and IsoPaths turns them into SKPath.

using System;
using System.Collections.Generic;

namespace AppOrbit.Figure;

/// <summary>A point on the screen, in stage units.</summary>
internal readonly record struct Vec2(double X, double Y);

/// <summary>A point in the world: x/y on the ground, z up.</summary>
internal readonly record struct Vec3(double X, double Y, double Z);

/// <summary>A sample on a rounded outline, with its outward normal on the ground plane.</summary>
internal readonly record struct Sample(double U, double V, double Nu, double Nv);

/// <summary>
/// An orthographic camera. <see cref="Az"/> is in radians, <see cref="K"/> is
/// sin(elevation) (0.5 is the 2:1 view every figure rests at), <see cref="S"/> is
/// the scale, and <see cref="Ox"/>/<see cref="Oy"/> the screen offset
/// <see cref="Iso.Fit"/> computes. Mutable on purpose: a figure that turns the
/// camera writes it.
/// </summary>
internal sealed class Camera
{
    public double Az;
    public double K;
    public double S;
    public double Ox;
    public double Oy;

    public Camera(double az, double k, double s)
    {
        Az = az;
        K = k;
        S = s;
    }

    /// <summary>How much one world unit of height moves a point up the screen.</summary>
    public double Zf => Math.Sqrt(1d - K * K);
}

/// <summary>Projects a world point to the screen.</summary>
internal delegate Vec2 Projector(double x, double y, double z);

/// <summary>Two outlines of a solid: its silhouette and the one crease inside it.</summary>
internal readonly record struct PrismOutline(Vec2[] Silhouette, Vec2[] Crease);

/// <summary>
/// World space is x/y on the ground and z up; the camera turns the ground by an
/// azimuth, then squashes y by sin(el) and lifts z by cos(el). There is no
/// perspective and no hidden-line removal: plates are filled with the ground
/// colour and painted back to front, so a nearer one simply covers. At the
/// default camera, <c>Cam(45, 0.5, S)</c>, +x runs down to the right and +y down
/// to the left, so the corner with the largest x and y is nearest the viewer:
/// paint by ascending x + y.
/// </summary>
internal static class Iso
{
    public static double Clamp(double v, double a, double b) => Math.Max(a, Math.Min(b, v));
    public static double Lerp(double a, double b, double t) => a + (b - a) * t;
    public static double Rad(double degrees) => degrees * Math.PI / 180d;

    // ---------- projection ----------

    public static Camera Cam(double azDegrees, double k, double s) => new(Rad(azDegrees), k, s);

    /// <summary>
    /// The projector for the camera as it is now. Figures that move the camera
    /// call this again each frame; the rest call it once.
    /// </summary>
    public static Projector Proj(Camera c)
    {
        var cos = Math.Cos(c.Az);
        var sin = Math.Sin(c.Az);
        var zf = c.Zf;
        return (x, y, z) =>
        {
            var tx = x * cos - y * sin;
            var ty = x * sin + y * cos;
            return new Vec2(c.Ox + c.S * tx, c.Oy + c.S * (ty * c.K - z * zf));
        };
    }

    /// <summary>
    /// The ground-plane inverse: the world x/y under a screen point, on the plane
    /// at height z. This is how the pointer reaches the drawing: every hit test is
    /// made in world units, never in pixels.
    /// </summary>
    public static Vec2 Unproj(Camera c, double sx, double sy, double z)
    {
        var cos = Math.Cos(c.Az);
        var sin = Math.Sin(c.Az);
        var zf = c.Zf;
        var tx = (sx - c.Ox) / c.S;
        var ty = ((sy - c.Oy) / c.S + z * zf) / c.K;
        return new Vec2(tx * cos + ty * sin, -tx * sin + ty * cos);
    }

    /// <summary>Sets the camera's offset so the bounding box of <paramref name="points"/> is centred on (cx, cy). It only centres, it never scales.</summary>
    public static void Fit(Camera c, IReadOnlyList<Vec3> points, double cx, double cy)
    {
        c.Ox = 0;
        c.Oy = 0;
        var p = Proj(c);
        double a = 1e9, b = -1e9, lo = 1e9, hi = -1e9;
        foreach (var pt in points)
        {
            var q = p(pt.X, pt.Y, pt.Z);
            a = Math.Min(a, q.X);
            b = Math.Max(b, q.X);
            lo = Math.Min(lo, q.Y);
            hi = Math.Max(hi, q.Y);
        }

        c.Ox = cx - (a + b) / 2d;
        c.Oy = cy - (lo + hi) / 2d;
    }

    // ---------- rounded solids ----------

    /// <summary>
    /// A rounded rectangle, sampled. Each sample carries its outward normal, so a
    /// caller can keep just the run that faces the camera. <paramref name="n"/>
    /// samples per corner: four gives a sixteen-sided round, enough until the
    /// radius on screen passes about 20 units.
    /// </summary>
    public static Sample[] Rrect(double u0, double v0, double u1, double v1, double r, int n = 4)
    {
        r = Math.Max(0d, Math.Min(r, Math.Min((u1 - u0) / 2d, (v1 - v0) / 2d)));
        var corners = new (double Cu, double Cv, double A0)[]
        {
            (u1 - r, v1 - r, 0d),
            (u0 + r, v1 - r, 90d),
            (u0 + r, v0 + r, 180d),
            (u1 - r, v0 + r, 270d),
        };
        var ring = new Sample[4 * (n + 1)];
        var i = 0;
        foreach (var (cu, cv, a0) in corners)
        {
            for (var k = 0; k <= n; k++)
            {
                var a = Rad(a0 + 90d * k / n);
                var ca = Math.Cos(a);
                var sa = Math.Sin(a);
                ring[i++] = new Sample(cu + r * ca, cv + r * sa, ca, sa);
            }
        }

        return ring;
    }

    /// <summary>A circle of radius <paramref name="radius"/> about the origin, sampled the same way.</summary>
    public static Sample[] Circ(double radius, int n = 96)
    {
        var ring = new Sample[n];
        for (var k = 0; k < n; k++)
        {
            var a = k / (double)n * Math.PI * 2d;
            var ca = Math.Cos(a);
            var sa = Math.Sin(a);
            ring[k] = new Sample(radius * ca, radius * sa, ca, sa);
        }

        return ring;
    }

    /// <summary>Convex hull (monotone chain), counter-clockwise in screen space.</summary>
    public static Vec2[] Hull(IReadOnlyList<Vec2> input)
    {
        var pts = new List<Vec2>(input);
        pts.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
        static double Cross(Vec2 o, Vec2 a, Vec2 b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

        var lo = new List<Vec2>();
        foreach (var p in pts)
        {
            while (lo.Count > 1 && Cross(lo[^2], lo[^1], p) <= 0) lo.RemoveAt(lo.Count - 1);
            lo.Add(p);
        }

        var up = new List<Vec2>();
        for (var i = pts.Count - 1; i >= 0; i--)
        {
            var p = pts[i];
            while (up.Count > 1 && Cross(up[^2], up[^1], p) <= 0) up.RemoveAt(up.Count - 1);
            up.Add(p);
        }

        lo.RemoveAt(lo.Count - 1);
        up.RemoveAt(up.Count - 1);
        lo.AddRange(up);
        return lo.ToArray();
    }

    /// <summary>A ring laid flat at height z, projected.</summary>
    public static Vec2[] RingAt(Projector p, IReadOnlyList<Sample> ring, double z)
    {
        var pts = new Vec2[ring.Count];
        for (var i = 0; i < ring.Count; i++) pts[i] = p(ring[i].U, ring[i].V, z);
        return pts;
    }

    /// <summary>
    /// Whether a ring sample faces the camera. The world direction of
    /// screen-down is (sin az, cos az), so facing is a dot product with it.
    /// </summary>
    public static Func<Sample, bool> Facing(Camera c)
    {
        var s = Math.Sin(c.Az);
        var cos = Math.Cos(c.Az);
        return q => q.Nu * s + q.Nv * cos >= -1e-6;
    }

    /// <summary>The one cyclic run of samples that pass <paramref name="keep"/>, in ring order.</summary>
    public static Sample[] Run(IReadOnlyList<Sample> ring, Func<Sample, bool> keep)
    {
        var n = ring.Count;
        var start = -1;
        for (var i = 0; i < n; i++)
        {
            if (keep(ring[i]) && !keep(ring[(i + n - 1) % n]))
            {
                start = i;
                break;
            }
        }

        if (start < 0)
        {
            return keep(ring[0]) ? new List<Sample>(ring).ToArray() : Array.Empty<Sample>();
        }

        var run = new List<Sample>();
        for (var k = 0; k < n && keep(ring[(start + k) % n]); k++) run.Add(ring[(start + k) % n]);
        return run.ToArray();
    }

    /// <summary>
    /// A prism standing from z0 to z1. Its silhouette is the hull of the two rings,
    /// so the vertical corners are never drawn; its only inner line is the crease,
    /// the front run of an inset ring on the lid, which reads as a bevel. An empty
    /// crease means no inner ring was given.
    /// </summary>
    public static PrismOutline Prism(Projector p, Func<Sample, bool> front, IReadOnlyList<Sample> ring, IReadOnlyList<Sample>? inner, double z0, double z1)
    {
        var top = RingAt(p, ring, z1);
        var bottom = RingAt(p, ring, z0);
        var both = new Vec2[top.Length + bottom.Length];
        top.CopyTo(both, 0);
        bottom.CopyTo(both, top.Length);
        var crease = inner is null ? Array.Empty<Vec2>() : RingAt(p, Run(inner, front), z1);
        return new PrismOutline(Hull(both), crease);
    }

    /// <summary>
    /// A solid whose top is smaller than its foot (a keycap, a tower that tapers):
    /// the hull of the foot at z0 and the top at z1, with the crease on the top.
    /// That is all <see cref="Prism"/> does with one ring, so a tapered solid is
    /// the same two lines from two rings.
    /// </summary>
    public static PrismOutline Taper(Projector p, Func<Sample, bool> front, IReadOnlyList<Sample> foot, IReadOnlyList<Sample> top, IReadOnlyList<Sample>? inner, double z0, double z1)
    {
        var a = RingAt(p, foot, z0);
        var b = RingAt(p, top, z1);
        var both = new Vec2[a.Length + b.Length];
        a.CopyTo(both, 0);
        b.CopyTo(both, a.Length);
        var crease = inner is null ? Array.Empty<Vec2>() : RingAt(p, Run(inner, front), z1);
        return new PrismOutline(Hull(both), crease);
    }

    /// <summary>A rounded footprint and its crease ring, inset by <paramref name="b"/>.</summary>
    public static (Sample[] Ring, Sample[] Inner) Rings(double x0, double y0, double x1, double y1, double r, double b)
        => (Rrect(x0, y0, x1, y1, r), Rrect(x0 + b, y0 + b, x1 - b, y1 - b, Math.Max(0.3d, r - b)));

    /// <summary>The leftmost, rightmost and nearest samples of a ring: where construction lines drop from.</summary>
    public static (Sample Left, Sample Right, Sample Nearest) Extremes(Projector p, IReadOnlyList<Sample> ring)
    {
        var pr = RingAt(p, ring, 0);
        int a = 0, b = 0, c = 0;
        for (var k = 0; k < pr.Length; k++)
        {
            if (pr[k].X < pr[a].X) a = k;
            if (pr[k].X > pr[b].X) b = k;
            if (pr[k].Y > pr[c].Y) c = k;
        }

        return (ring[a], ring[b], ring[c]);
    }

    /// <summary>Rounds every vertex of a closed polygon with a quadratic through the vertex; one radius per vertex, each cut to half its shorter edge.</summary>
    public static Vec2[] Fillet(IReadOnlyList<Vec2> pts, IReadOnlyList<double> radii, int n = 4)
    {
        var m = pts.Count;
        var output = new List<Vec2>(m * (n + 1));
        for (var i = 0; i < m; i++)
        {
            var a = pts[(i + m - 1) % m];
            var p = pts[i];
            var b = pts[(i + 1) % m];
            var la = Math.Sqrt((a.X - p.X) * (a.X - p.X) + (a.Y - p.Y) * (a.Y - p.Y));
            var lb = Math.Sqrt((b.X - p.X) * (b.X - p.X) + (b.Y - p.Y) * (b.Y - p.Y));
            var t = Math.Min(radii[i], Math.Min(la / 2d, lb / 2d));
            var p1 = new Vec2(p.X + (a.X - p.X) / la * t, p.Y + (a.Y - p.Y) / la * t);
            var p2 = new Vec2(p.X + (b.X - p.X) / lb * t, p.Y + (b.Y - p.Y) / lb * t);
            for (var k = 0; k <= n; k++)
            {
                var s = k / (double)n;
                var w = 1d - s;
                output.Add(new Vec2(
                    w * w * p1.X + 2d * w * s * p.X + s * s * p2.X,
                    w * w * p1.Y + 2d * w * s * p.Y + s * s * p2.Y));
            }
        }

        return output.ToArray();
    }
}
