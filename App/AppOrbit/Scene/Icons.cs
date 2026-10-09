using AppOrbit.Graph;
using SkiaSharp;

namespace AppOrbit.Scene;

/// <summary>icons.js: one icon set for entity types on a 12px grid, one stroke weight, drawn in the current colour.</summary>
public static class Icons
{
    /// <summary>SVG path data per type, the same strings the prototype uses (as paths; rect/circle become path ops).</summary>
    private static readonly Dictionary<string, SKPath> Paths = new();
    private static readonly Dictionary<string, SKPath> Fills = new();
    private static readonly object Gate = new();

    public static (SKPath Stroke, SKPath? Fill, bool Dashed) For(string? type)
    {
        type ??= NodeType.Property;
        lock (Gate)
        {
            if (!Paths.TryGetValue(type, out var p))
            {
                (p, var f) = Build(type);
                Paths[type] = p;
                if (f != null) Fills[type] = f;
            }
            return (p, Fills.GetValueOrDefault(type), type == NodeType.Instance);
        }
    }

    private static (SKPath, SKPath?) Build(string type)
    {
        var p = new SKPath();
        SKPath? fill = null;
        switch (type)
        {
            case NodeType.Feature:
                p.AddRoundRect(new SKRect(1.5f, 1.5f, 10.5f, 10.5f), 1.5f, 1.5f);
                p.MoveTo(1.5f, 4.5f); p.LineTo(10.5f, 4.5f);
                break;
            case NodeType.Screen:
                p.AddRoundRect(new SKRect(2.5f, 1, 9.5f, 11), 1.5f, 1.5f);
                p.MoveTo(5, 9.5f); p.LineTo(7, 9.5f);
                break;
            case NodeType.Component:
            case NodeType.Instance:
                p.AddRoundRect(new SKRect(1.5f, 2.5f, 10.5f, 9.5f), 1, 1);
                p.MoveTo(4.5f, 2.5f); p.LineTo(4.5f, 9.5f);
                break;
            case NodeType.ViewModel:
                p.MoveTo(6, 1.3f); p.LineTo(10.7f, 6); p.LineTo(6, 10.7f); p.LineTo(1.3f, 6); p.Close();
                break;
            case NodeType.Property:
                p.AddCircle(6, 6, 3.3f);
                break;
            case NodeType.Command:
                p.MoveTo(3.6f, 1.6f); p.LineTo(3.6f, 10.4f); p.LineTo(9.8f, 6); p.Close();
                break;
            case NodeType.State:
                p.AddCircle(6, 6, 4.4f);
                fill = new SKPath();
                fill.MoveTo(6, 1.6f); fill.LineTo(6, 10.4f); fill.ArcTo(new SKPoint(4.4f, 4.4f), 0, SKPathArcSize.Small, SKPathDirection.CounterClockwise, new SKPoint(6, 1.6f)); fill.Close();
                break;
            case NodeType.Route:
                p.MoveTo(1.5f, 6); p.LineTo(9.7f, 6); p.MoveTo(7, 3.3f); p.LineTo(9.8f, 6); p.LineTo(7, 8.7f);
                break;
            case "app":
                p.AddCircle(6, 6, 2.2f);
                var ellipse = new SKPath();
                ellipse.AddOval(new SKRect(1, 4, 11, 8));
                ellipse.Transform(SKMatrix.CreateRotationDegrees(-24, 6, 6));
                p.AddPath(ellipse);
                break;
            default:
                p.AddCircle(6, 6, 3.3f);
                break;
        }
        return (p, fill);
    }

    /// <summary>Draws the 12px icon with its top-left at (x, y), scaled to size px.</summary>
    public static void Draw(SKCanvas canvas, string? type, float x, float y, SKColor color, float size = 12, SKPaint? paint = null)
    {
        var (stroke, fill, dashed) = For(type);
        var own = paint == null;
        paint ??= new SKPaint { IsAntialias = true };
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = 1.25f;
        paint.StrokeCap = SKStrokeCap.Round;
        paint.StrokeJoin = SKStrokeJoin.Round;
        paint.Color = color;
        paint.PathEffect = dashed ? SKPathEffect.CreateDash(new[] { 2f, 1.4f }, 0) : null;
        canvas.Save();
        canvas.Translate(x, y);
        canvas.Scale(size / 12f);
        canvas.DrawPath(stroke, paint);
        if (fill != null)
        {
            paint.PathEffect = null;
            paint.Style = SKPaintStyle.Fill;
            canvas.DrawPath(fill, paint);
        }
        canvas.Restore();
        paint.PathEffect = null;
        if (own) paint.Dispose();
    }
}
