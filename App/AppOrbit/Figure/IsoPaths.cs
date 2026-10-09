using SkiaSharp;

namespace AppOrbit.Figure;

/// <summary>
/// Turns <see cref="Iso"/>'s point runs into Skia paths. The SVG `d` strings
/// Hairline writes stop here: <see cref="Poly"/> is its closed path and
/// <see cref="Open"/> its polyline.
/// </summary>
internal static class IsoPaths
{
    /// <summary>A closed path through the points.</summary>
    public static SKPath Poly(Vec2[] pts)
    {
        var path = new SKPath();
        Append(path, pts, close: true);
        return path;
    }

    /// <summary>An open polyline; empty for fewer than two points.</summary>
    public static SKPath Open(Vec2[] pts)
    {
        var path = new SKPath();
        if (pts.Length >= 2)
        {
            Append(path, pts, close: false);
        }

        return path;
    }

    /// <summary>Adds a closed contour to an existing path, so one path can hold many solids.</summary>
    public static void AddPoly(SKPath path, Vec2[] pts) => Append(path, pts, close: true);

    /// <summary>Adds an open contour to an existing path.</summary>
    public static void AddOpen(SKPath path, Vec2[] pts)
    {
        if (pts.Length >= 2)
        {
            Append(path, pts, close: false);
        }
    }

    /// <summary>One straight segment between two screen points.</summary>
    public static void AddSeg(SKPath path, Vec2 a, Vec2 b)
    {
        path.MoveTo((float)a.X, (float)a.Y);
        path.LineTo((float)b.X, (float)b.Y);
    }

    private static void Append(SKPath path, Vec2[] pts, bool close)
    {
        if (pts.Length == 0)
        {
            return;
        }

        path.MoveTo((float)pts[0].X, (float)pts[0].Y);
        for (var i = 1; i < pts.Length; i++)
        {
            path.LineTo((float)pts[i].X, (float)pts[i].Y);
        }

        if (close)
        {
            path.Close();
        }
    }
}
