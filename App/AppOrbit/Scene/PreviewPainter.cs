using AppOrbit.Graph;
using AppOrbit.Layout;
using SkiaSharp;

namespace AppOrbit.Scene;

/// <summary>What a preview shows: preview.js renderPreview's options.</summary>
public sealed record PreviewSpec(string ScreenId, string Size, string? StateId, bool Interactive, string? HighlightId, List<string>? HighlightIds, bool Recede, bool Quiet);

/// <summary>A selectable component instance inside a preview, in preview-local pixels.</summary>
public sealed record PreviewRegion(string InstanceId, string Name, SKRect Rect, bool Highlight, bool Faded);

/// <summary>preview.js: wireframe (or UI-fidelity) screen previews from node.preview specs, drawn in Skia.</summary>
public sealed class PreviewPainter
{
    private static readonly float[] Hues = { 24, 150, 205, 38, 280, 190 };
    private readonly GraphIndex _g;
    private readonly Palette _p;
    private readonly SKPaint _fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
    private readonly SKPaint _text = new() { IsAntialias = true };
    private readonly SKPaint _hatch = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2 };

    public bool UiFidelity { get; set; }

    public PreviewPainter(GraphIndex g, Palette palette)
    {
        _g = g;
        _p = palette;
    }

    public static bool Near(Rect a, Rect b) =>
        Math.Abs(a.X - b.X) < 0.015 && Math.Abs(a.Y - b.Y) < 0.015 && Math.Abs(a.W - b.W) < 0.015 && Math.Abs(a.H - b.H) < 0.015;

    /// <summary>The instance regions of a preview, for hit testing and anchors (the same rects the painter draws).</summary>
    public List<PreviewRegion> Regions(PreviewSpec o)
    {
        var (w, h) = PreviewFrame.OfF(o.Size);
        var screen = _g.Node(o.ScreenId);
        var acc = new List<PreviewRegion>();
        if (screen?.Preview?.Parts == null) return acc;
        var highlight = new HashSet<string>(o.HighlightIds ?? new());
        if (o.HighlightId != null) highlight.Add(o.HighlightId);
        if (!o.Interactive && highlight.Count == 0) return acc;
        foreach (var at in _g.InstancesOfScreen(screen.Id))
        {
            var b = at.Node.Preview?.Bounds;
            if (b == null) continue;
            var hi = highlight.Contains(at.Node.Id);
            acc.Add(new PreviewRegion(at.Node.Id, at.Node.Name, R(b, w, h), hi, !hi && o.Recede));
        }
        return acc;
    }

    private static SKRect R(Rect r, float w, float h) => new((float)(r.X * w), (float)(r.Y * h), (float)((r.X + r.W) * w), (float)((r.Y + r.H) * h));

    /// <summary>Draws the preview with its top-left at (0, 0) in the current canvas space. hoverId lights its region.</summary>
    public void Draw(SKCanvas canvas, PreviewSpec o, string? hoverId)
    {
        var (w, h) = PreviewFrame.OfF(o.Size);
        var u = w / 220f;
        var frame = new SKRect(0, 0, w, h);
        canvas.Save();
        canvas.ClipRoundRect(new SKRoundRect(frame, 10, 10), antialias: true);
        _fill.Color = _p.Paper;
        canvas.DrawRect(frame, _fill);

        var screen = _g.Node(o.ScreenId);
        if (screen?.Preview?.Parts != null)
        {
            var instances = _g.InstancesOfScreen(screen.Id).Select(x => x.Node).Where(n => n.Preview?.Bounds != null).ToList();
            var state = o.StateId != null ? _g.Node(o.StateId) : null;
            var alpha = o.Recede ? (byte)64 : (byte)255;
            if (o.Recede) canvas.SaveLayer(new SKPaint { Color = SKColors.White.WithAlpha(alpha) });
            foreach (var part in screen.Preview.Parts) DrawPart(canvas, part, u, w, h);
            if (state?.Preview != null)
            {
                foreach (var part in state.Preview.Overrides ?? new()) DrawPart(canvas, part, u, w, h);
                foreach (var id in state.Preview.Dim ?? new())
                {
                    var inst = instances.FirstOrDefault(i => i.Id == id);
                    if (inst != null) { _fill.Color = _p.Paper.WithAlpha(166); canvas.DrawRect(R(inst.Preview!.Bounds!, w, h), _fill); }
                }
                foreach (var id in state.Preview.Hide ?? new())
                {
                    var inst = instances.FirstOrDefault(i => i.Id == id);
                    if (inst != null) { _fill.Color = _p.Paper; canvas.DrawRect(R(inst.Preview!.Bounds!, w, h), _fill); }
                }
            }
            if (o.Recede) canvas.Restore();

            // regions: a hairline on hover, a focus ring on the highlighted instance, a name chip on either
            foreach (var region in Regions(o))
            {
                var rr = new SKRoundRect(region.Rect, 4, 4);
                var hovered = !region.Faded && hoverId == region.InstanceId;
                if (hovered && !region.Highlight)
                {
                    _fill.Color = _p.HueComp.WithAlpha(20); canvas.DrawRoundRect(rr, _fill);
                    _stroke.Color = _p.HueComp; _stroke.StrokeWidth = 1; canvas.DrawRoundRect(rr, _stroke);
                }
                if (region.Highlight)
                {
                    _stroke.Color = _p.FocusSoft; _stroke.StrokeWidth = 4; canvas.DrawRoundRect(rr, _stroke);
                    _stroke.Color = _p.Focus; _stroke.StrokeWidth = 1; canvas.DrawRoundRect(rr, _stroke);
                }
                if ((hovered || region.Highlight) && !o.Quiet)
                {
                    var font = Fonts.Get(11, weight: 400);
                    var tw = font.MeasureText(region.Name) + 8;
                    var chip = new SKRect(region.Rect.Left - 1, region.Rect.Top - 16, region.Rect.Left - 1 + tw, region.Rect.Top - 1);
                    _fill.Color = _p.HueComp;
                    var path = new SKPath();
                    path.AddRoundRect(chip, 3, 3);
                    canvas.DrawPath(path, _fill);
                    Fonts.Draw(canvas, region.Name, chip.Left + 4, chip.Top, 15, font, _p.Paper, paint: _text);
                }
            }
        }
        canvas.Restore();
        _stroke.Color = _p.Rule; _stroke.StrokeWidth = 1;
        canvas.DrawRoundRect(new SKRoundRect(frame, 10, 10), _stroke);
    }

    private static (string Name, string Value) SplitValue(string item)
    {
        var i = item.LastIndexOf(" · ", StringComparison.Ordinal);
        return i > 0 ? (item[..i], item[(i + 3)..]) : (item, "");
    }

    private void Bars(SKCanvas canvas, SKRect r, float u, int rows)
    {
        var barH = 5 * u; var gap = 4 * u;
        var total = rows * barH + (rows - 1) * gap;
        var y = r.MidY - total / 2;
        _fill.Color = _p.Paper3;
        for (var i = 0; i < rows; i++)
        {
            var w = i == rows - 1 && rows > 1 ? r.Width * 0.62f : r.Width;
            canvas.DrawRoundRect(new SKRect(r.Left, y, r.Left + w, y + barH), 2, 2, _fill);
            y += barH + gap;
        }
    }

    private void HatchFill(SKCanvas canvas, SKRoundRect rr, float period, SKColor color)
    {
        canvas.Save();
        canvas.ClipRoundRect(rr, antialias: true);
        _hatch.Color = color;
        _hatch.StrokeWidth = 2;
        var b = rr.Rect;
        var span = b.Width + b.Height;
        for (float d = -b.Height; d < span; d += period)
            canvas.DrawLine(b.Left + d, b.Top, b.Left + d - b.Height, b.Bottom, _hatch);
        canvas.Restore();
    }

    private void DrawPart(SKCanvas canvas, PreviewPart part, float u, float w, float h)
    {
        var r = R(part.Rect, w, h);
        var ui = UiFidelity;
        switch (part.Kind)
        {
            case "heading":
            {
                if (part.Rows is > 0 && !(ui && part.Text != null)) { Bars(canvas, r, u, part.Rows.Value); break; }
                var font = Fonts.Get(13 * u, weight: ui ? 600 : 700);
                Fonts.Draw(canvas, (ui && part.Rows is > 0 ? part.Text : part.Label) ?? "", r.Left, r.Top, r.Height, font, _p.Ink, maxWidth: r.Width, paint: _text);
                break;
            }
            case "text":
            {
                if (part.Rows is > 0 && ui && part.Text != null)
                {
                    var font = Fonts.Get(9.5f * u);
                    var lh = 9.5f * u * 1.35f;
                    var lines = Fonts.Wrap(font, part.Text, r.Width, Math.Max(1, (int)(r.Height / lh)));
                    for (var i = 0; i < lines.Count; i++) Fonts.Draw(canvas, lines[i], r.Left, r.Top + i * lh, lh, font, _p.Ink2, paint: _text);
                }
                else if (part.Rows is > 0) Bars(canvas, r, u, part.Rows.Value);
                else Fonts.Draw(canvas, part.Label ?? "", r.Left, r.Top, r.Height, Fonts.Get((ui ? 9.5f : 10) * u), _p.Ink2, maxWidth: r.Width, paint: _text);
                break;
            }
            case "button":
            {
                var rr = new SKRoundRect(r, 6 * u, 6 * u);
                var emphasis = part.Emphasis ?? "primary";
                SKColor fg;
                if (emphasis == "primary") { _fill.Color = ui ? _p.UiAccent : _p.Ink; canvas.DrawRoundRect(rr, _fill); fg = ui ? SKColors.White : _p.Paper; }
                else if (emphasis == "secondary") { _stroke.Color = ui ? _p.UiAccent : _p.Ink2; _stroke.StrokeWidth = 1; canvas.DrawRoundRect(rr, _stroke); fg = ui ? _p.UiAccent : _p.Ink; }
                else { _fill.Color = _p.Paper3; canvas.DrawRoundRect(rr, _fill); fg = _p.Ink3; }
                Fonts.Draw(canvas, part.Label ?? "", r.MidX, r.Top, r.Height, Fonts.Get(10.5f * u, weight: 600), fg, SKTextAlign.Center, r.Width - 8 * u, _text);
                break;
            }
            case "list":
            {
                canvas.Save();
                canvas.ClipRect(r);
                var rows = part.Rows ?? 3;
                var thumb = (ui ? 28 : 24) * u;
                var pitch = thumb + 5 * u + 1 + 6 * u;
                var y = r.Top;
                for (var i = 0; i < rows; i++)
                {
                    var trr = new SKRoundRect(new SKRect(r.Left, y, r.Left + thumb, y + thumb), ui ? 5 * u : 4, ui ? 5 * u : 4);
                    if (ui)
                    {
                        _fill.Color = SKColor.FromHsl(Hues[i % Hues.Length], 28, 72);
                        canvas.DrawRoundRect(trr, _fill);
                    }
                    else
                    {
                        HatchFill(canvas, trr, 5, _p.Paper3);
                        _stroke.Color = _p.Rule2; _stroke.StrokeWidth = 1; canvas.DrawRoundRect(trr, _stroke);
                    }
                    var tx = r.Left + thumb + 6 * u;
                    var textRect = new SKRect(tx, y, r.Right, y + thumb);
                    var item = ui && part.Items != null && i < part.Items.Count ? part.Items[i] : null;
                    if (item != null)
                    {
                        var (name, value) = SplitValue(item);
                        var font = Fonts.Get(9.5f * u);
                        var vw = value.Length > 0 ? font.MeasureText(value) : 0;
                        Fonts.Draw(canvas, name, tx, y, thumb, font, _p.Ink, maxWidth: Math.Max(0, textRect.Width - vw - 6 * u), paint: _text);
                        if (value.Length > 0) Fonts.Draw(canvas, value, r.Right, y, thumb, font, _p.Ink2, SKTextAlign.Right, paint: _text);
                    }
                    else Bars(canvas, textRect, u, 2);
                    _stroke.Color = _p.Rule2; _stroke.StrokeWidth = 1;
                    canvas.DrawLine(r.Left, y + thumb + 5 * u + 0.5f, r.Right, y + thumb + 5 * u + 0.5f, _stroke);
                    y += pitch;
                }
                canvas.Restore();
                break;
            }
            case "image":
            {
                var rr = new SKRoundRect(r, 8 * u, 8 * u);
                if (ui)
                {
                    _fill.Shader = SKShader.CreateLinearGradient(new SKPoint(r.Left, r.Top), new SKPoint(r.Right, r.Bottom), new[] { SKColor.FromHsl(24, 30, 78), SKColor.FromHsl(24, 35, 60) }, SKShaderTileMode.Clamp);
                    canvas.DrawRoundRect(rr, _fill);
                    _fill.Shader = null;
                }
                else
                {
                    HatchFill(canvas, rr, 6, _p.Paper3);
                    _stroke.Color = _p.Rule2; _stroke.StrokeWidth = 1; canvas.DrawRoundRect(rr, _stroke);
                }
                break;
            }
            case "input":
            {
                var rr = new SKRoundRect(r, 5 * u, 5 * u);
                if (ui) { _fill.Color = _p.Paper2; canvas.DrawRoundRect(rr, _fill); }
                else { _stroke.Color = _p.Rule; _stroke.StrokeWidth = 1; canvas.DrawRoundRect(rr, _stroke); }
                Fonts.Draw(canvas, part.Label ?? "", r.Left + 8 * u, r.Top, r.Height, Fonts.Get(9.5f * u), _p.Ink3, maxWidth: r.Width - 10 * u, paint: _text);
                break;
            }
            case "stepper":
            {
                var rr = new SKRoundRect(r, 5 * u, 5 * u);
                if (ui) { _fill.Color = _p.Paper2; canvas.DrawRoundRect(rr, _fill); }
                else { _stroke.Color = _p.Rule; _stroke.StrokeWidth = 1; canvas.DrawRoundRect(rr, _stroke); }
                var font = Fonts.Get(10 * u);
                var cw = r.Width / 3;
                Fonts.Draw(canvas, "−", r.Left + cw * 0.5f, r.Top, r.Height, font, ui ? _p.Ink : _p.Ink2, SKTextAlign.Center, paint: _text);
                Fonts.Draw(canvas, part.Label ?? "1", r.Left + cw * 1.5f, r.Top, r.Height, font, ui ? _p.Ink : _p.Ink2, SKTextAlign.Center, paint: _text);
                Fonts.Draw(canvas, "+", r.Left + cw * 2.5f, r.Top, r.Height, font, ui ? _p.Ink : _p.Ink2, SKTextAlign.Center, paint: _text);
                break;
            }
            case "badge":
            {
                var rr = new SKRoundRect(r, r.Height / 2, r.Height / 2);
                if (ui) { _fill.Color = _p.UiAccentSoft; canvas.DrawRoundRect(rr, _fill); }
                else { _stroke.Color = _p.Rule; _stroke.StrokeWidth = 1; canvas.DrawRoundRect(rr, _stroke); }
                Fonts.Draw(canvas, part.Label ?? "", r.MidX, r.Top, r.Height, Fonts.Get(9 * u, weight: ui ? 600 : 400), ui ? _p.UiAccent : _p.Ink2, SKTextAlign.Center, r.Width, _text);
                break;
            }
            case "bar":
                _fill.Color = _p.Paper3; canvas.DrawRoundRect(new SKRoundRect(r, 2, 2), _fill);
                break;
            case "empty":
            {
                var rr = new SKRoundRect(r, 6 * u, 6 * u);
                _fill.Color = ui ? _p.Paper2 : _p.Paper; canvas.DrawRoundRect(rr, _fill);
                if (!ui) { _stroke.Color = _p.Rule; _stroke.StrokeWidth = 1; _stroke.PathEffect = SKPathEffect.CreateDash(new[] { 3f, 3f }, 0); canvas.DrawRoundRect(rr, _stroke); _stroke.PathEffect = null; }
                Fonts.Draw(canvas, part.Label ?? "Nothing here", r.MidX, r.Top, r.Height, Fonts.Get(10 * u, italic: !ui), _p.Ink3, SKTextAlign.Center, r.Width - 8 * u, _text);
                break;
            }
            case "spinner":
            {
                _fill.Color = _p.Paper; canvas.DrawRect(r, _fill);
                var rad = 11 * u;
                var c = new SKPoint(r.MidX, r.MidY);
                _stroke.Color = _p.Rule; _stroke.StrokeWidth = 2.5f * u;
                canvas.DrawCircle(c, rad, _stroke);
                _stroke.Color = ui ? _p.UiAccent : _p.Ink2;
                canvas.DrawArc(new SKRect(c.X - rad, c.Y - rad, c.X + rad, c.Y + rad), -135, 90, false, _stroke);
                _stroke.StrokeWidth = 1;
                break;
            }
            case "alert":
            {
                var rr = new SKRoundRect(r, 4 * u, 4 * u);
                _fill.Color = Mix(_p.Danger, _p.Paper, 0.10f); canvas.DrawRoundRect(rr, _fill);
                _fill.Color = _p.Danger; canvas.DrawRect(new SKRect(r.Left, r.Top, r.Left + 3 * u, r.Bottom), _fill);
                Fonts.Draw(canvas, part.Label ?? "Something went wrong", r.Left + 8 * u, r.Top, r.Height, Fonts.Get(9.5f * u), _p.Ink, maxWidth: r.Width - 10 * u, paint: _text);
                break;
            }
        }
    }

    public static SKColor Mix(SKColor a, SKColor b, float t) => new(
        (byte)(a.Red * t + b.Red * (1 - t)), (byte)(a.Green * t + b.Green * (1 - t)), (byte)(a.Blue * t + b.Blue * (1 - t)));
}
