using SkiaSharp;

namespace AppOrbit.Scene;

/// <summary>The scene's two families, resolved once: the UI stack (Segoe UI on Windows) and the mono stack. Fonts are cached per (family, size, weight, italic).</summary>
public static class Fonts
{
    private static readonly string[] UiFamilies = { "Segoe UI", "Noto Sans", "DejaVu Sans", "Liberation Sans", "Helvetica", "Arial" };
    private static readonly string[] MonoFamilies = { "Cascadia Mono", "Consolas", "Menlo", "DejaVu Sans Mono", "Liberation Mono", "Noto Sans Mono", "Courier New" };

    private static readonly Dictionary<(bool Mono, int Weight, bool Italic), SKTypeface> Typefaces = new();
    private static readonly Dictionary<(bool Mono, float Size, int Weight, bool Italic), SKFont> Cache = new();
    private static readonly object Gate = new();

    public static SKTypeface Typeface(bool mono, int weight = 400, bool italic = false)
    {
        lock (Gate)
        {
            if (Typefaces.TryGetValue((mono, weight, italic), out var tf)) return tf;
            var style = new SKFontStyle(weight, (int)SKFontStyleWidth.Normal, italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
            var fm = SKFontManager.Default;
            SKTypeface? found = null;
            foreach (var family in mono ? MonoFamilies : UiFamilies)
            {
                var candidate = fm.MatchFamily(family, style);
                if (candidate != null && string.Equals(candidate.FamilyName, family, StringComparison.OrdinalIgnoreCase)) { found = candidate; break; }
                candidate?.Dispose();
            }
            found ??= fm.MatchFamily(null, style) ?? SKTypeface.Default;
            Typefaces[(mono, weight, italic)] = found;
            return found;
        }
    }

    public static SKFont Get(float size, bool mono = false, int weight = 400, bool italic = false)
    {
        lock (Gate)
        {
            var key = (mono, size, weight, italic);
            if (Cache.TryGetValue(key, out var f)) return f;
            f = new SKFont(Typeface(mono, weight, italic), size) { Subpixel = true, Edging = SKFontEdging.SubpixelAntialias, Hinting = SKFontHinting.Slight };
            // a family without the weight: let Skia embolden
            if (weight >= 600 && f.Typeface.FontWeight < 600) f.Embolden = true;
            if (italic && f.Typeface.FontSlant == SKFontStyleSlant.Upright) f.SkewX = -0.2f;
            Cache[key] = f;
            return f;
        }
    }

    public static float LineHeight(SKFont font) => font.Metrics.Descent - font.Metrics.Ascent;

    /// <summary>Baseline for text vertically centred in a box from top with height h.</summary>
    public static float Baseline(SKFont font, float top, float h)
    {
        var m = font.Metrics;
        return top + (h - (m.Descent - m.Ascent)) / 2 - m.Ascent;
    }

    /// <summary>Trims text with an ellipsis to fit maxWidth.</summary>
    public static string Fit(SKFont font, string text, float maxWidth)
    {
        if (string.IsNullOrEmpty(text) || font.MeasureText(text) <= maxWidth) return text;
        const string ell = "…";
        var ellW = font.MeasureText(ell);
        var lo = 0; var hi = text.Length;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (font.MeasureText(text[..mid]) + ellW <= maxWidth) lo = mid; else hi = mid - 1;
        }
        return lo == 0 ? "" : text[..lo].TrimEnd() + ell;
    }

    /// <summary>Draws text with its top-left at (x, top), centred in a line box of height h; left, centre or right aligned on x.</summary>
    public static float Draw(SKCanvas canvas, string text, float x, float top, float h, SKFont font, SKColor color, SKTextAlign align = SKTextAlign.Left, float maxWidth = float.PositiveInfinity, SKPaint? paint = null)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var own = paint == null;
        paint ??= new SKPaint { IsAntialias = true };
        paint.Style = SKPaintStyle.Fill;
        paint.Color = color;
        paint.PathEffect = null;
        var t = float.IsPositiveInfinity(maxWidth) ? text : Fit(font, text, maxWidth);
        canvas.DrawText(t, x, Baseline(font, top, h), align, font, paint);
        var w = font.MeasureText(t);
        if (own) paint.Dispose();
        return w;
    }

    /// <summary>Wraps text into lines that fit maxWidth (word boundaries, then characters).</summary>
    public static List<string> Wrap(SKFont font, string text, float maxWidth, int maxLines = int.MaxValue)
    {
        var lines = new List<string>();
        foreach (var para in text.Split('\n'))
        {
            var words = para.Split(' ');
            var line = "";
            foreach (var w in words)
            {
                var candidate = line.Length == 0 ? w : line + " " + w;
                if (font.MeasureText(candidate) <= maxWidth || line.Length == 0) line = candidate;
                else { lines.Add(line); line = w; }
                if (lines.Count >= maxLines) return lines;
            }
            lines.Add(line);
            if (lines.Count >= maxLines) return lines;
        }
        return lines;
    }
}
