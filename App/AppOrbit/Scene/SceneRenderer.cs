using AppOrbit.Graph;
using AppOrbit.Layout;
using Card = AppOrbit.Layout.Card;
using SkiaSharp;

namespace AppOrbit.Scene;

/// <summary>Everything the painter needs for one frame, snapshotted on the UI thread.</summary>
public sealed class SceneFrame
{
    public required List<CardGeom> Geoms { get; init; }
    public required List<Link> Links { get; init; }
    public string? FocusId, HoverId, CursorId;
    public float Opacity = 1;
    public float DrawT = 1;
    public bool Docked;
    public float Width, Height;
}

/// <summary>
/// scene.js, drawn: cards on their planes through one perspective matrix each, then the connectors
/// in screen space between projected anchors. Paints are built once; Paint runs on the render thread
/// and only reads the frame it is given.
/// </summary>
public sealed class SceneRenderer : IDisposable
{
    private readonly GraphIndex _g;
    private readonly Palette _p;
    private readonly PreviewPainter _previews;
    private readonly SKPaint _fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
    private readonly SKPaint _text = new() { IsAntialias = true };
    private readonly SKPaint _shadow = new() { IsAntialias = true, Style = SKPaintStyle.Fill, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 10) };
    private readonly SKPaint _halo = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKPathEffect _dash = SKPathEffect.CreateDash(new[] { 4f, 3f }, 0);
    private readonly SKPathEffect _dashBorder = SKPathEffect.CreateDash(new[] { 4f, 3f }, 0);
    private readonly SKPathEffect _dots = SKPathEffect.CreateDash(new[] { 1f, 3f }, 0);

    public PreviewPainter Previews => _previews;
    public Palette Palette => _p;

    public SceneRenderer(GraphIndex g, Palette palette)
    {
        _g = g;
        _p = palette;
        _previews = new PreviewPainter(g, palette);
    }

    public void Dispose()
    {
        _fill.Dispose(); _stroke.Dispose(); _text.Dispose(); _shadow.Dispose(); _halo.Dispose();
        _dash.Dispose(); _dashBorder.Dispose(); _dots.Dispose();
    }

    // ---------------------------------------------------------------- frame

    public void Paint(SKCanvas canvas, SceneFrame f)
    {
        DrawPaper(canvas, f);
        if (f.Opacity < 1) canvas.SaveLayer(new SKPaint { Color = SKColors.White.WithAlpha((byte)(255 * f.Opacity)) });
        foreach (var gm in SceneGeometry.PaintOrder(f.Geoms)) DrawCard(canvas, gm, f);
        DrawLinks(canvas, f);
        if (f.Opacity < 1) canvas.Restore();
    }

    /// <summary>The viewer's dotted paper: a rule-coloured dot every 24px (16px docked).</summary>
    private void DrawPaper(SKCanvas canvas, SceneFrame f)
    {
        var step = f.Docked ? 16 : 24;
        _fill.Color = _p.Rule;
        for (float y = 0; y < f.Height; y += step)
            for (float x = 0; x < f.Width; x += step)
                canvas.DrawCircle(x, y, 0.85f, _fill);
    }

    // ---------------------------------------------------------------- cards

    private void DrawCard(SKCanvas canvas, CardGeom gm, SceneFrame f)
    {
        var c = gm.Card;
        var s = gm.Shape;
        var n = _g.Node(c.Id);
        var isFocus = c.Id == f.FocusId && !c.Key.Contains('#');
        var isHover = f.HoverId != null && c.Id == f.HoverId && c.Id != f.FocusId;
        var isCursor = f.CursorId != null && c.Id == f.CursorId && f.CursorId != f.FocusId;
        var hue = _p.HueOf(c.Type);
        var face = new SKRoundRect(s.Face, 8, 8);

        canvas.Save();
        canvas.Concat(gm.H);

        // shadow on focal cards: screens, details and view model planes (not chips)
        var focal = c.Kind == "screen" || c.Kind == "detail" || (c.Kind == "vm");
        if (focal || isFocus)
        {
            _shadow.Color = _p.Shadow;
            var sr = s.Face; sr.Inflate(-12, -12); sr.Offset(0, 8);
            canvas.DrawRoundRect(new SKRoundRect(sr, 8, 8), _shadow);
        }

        // face
        if (c.Kind != "feature") { _fill.Color = _p.Paper; canvas.DrawRoundRect(face, _fill); }
        _stroke.StrokeWidth = 1;
        _stroke.Color = isFocus ? _p.Focus : isHover ? _p.Ink2 : _p.Rule;
        _stroke.PathEffect = c.Kind == "feature" ? _dashBorder : null;
        canvas.DrawRoundRect(face, _stroke);
        _stroke.PathEffect = null;
        if (isFocus)
        {
            _stroke.Color = _p.Focus; _stroke.StrokeWidth = 2;
            var ring = s.Face; ring.Inflate(1.5f, 1.5f);
            canvas.DrawRoundRect(new SKRoundRect(ring, 9.5f, 9.5f), _stroke);
            _stroke.StrokeWidth = 1;
        }
        if (isCursor)
        {
            _stroke.Color = _p.Focus; _stroke.StrokeWidth = 2; _stroke.PathEffect = _dots;
            var ring = s.Face; ring.Inflate(4, 4);
            canvas.DrawRoundRect(new SKRoundRect(ring, 12, 12), _stroke);
            _stroke.PathEffect = null; _stroke.StrokeWidth = 1;
        }

        canvas.Save();
        canvas.ClipRoundRect(face, antialias: true);
        switch (c.Kind)
        {
            case "screen": FillScreen(canvas, gm, n, f); break;
            case "feature": FillFeature(canvas, gm, n, f); break;
            case "vm": FillVm(canvas, gm, n, f); break;
            case "chip": FillChip(canvas, gm, n); break;
            case "state": FillState(canvas, gm, n, f); break;
            case "detail": FillDetail(canvas, gm, n); break;
        }
        canvas.Restore();
        canvas.Restore();
    }

    private readonly record struct Tag(string Text, SKColor Color, bool Dashed);

    /// <summary>The card head: glyph, name, tags, type on the right. Returns the head height.</summary>
    private void DrawHead(SKCanvas canvas, CardShape s, string? glyphType, string name, string type, IEnumerable<Tag> tags, bool chip = false, bool italic = false, bool mono = false, bool rule = true)
    {
        var h = s.Head;
        var font = Fonts.Get(chip ? 12.5f : 13, mono, chip ? 500 : 600, italic);
        var x = 10f;
        var lineTop = chip ? 6f : 7f;
        var lineH = chip ? 18f : 17f;
        if (glyphType != null)
        {
            Icons.Draw(canvas, glyphType, x + 1, lineTop + (lineH - 12) / 2, _p.HueOf(glyphType), 12, _stroke);
            x += 14 + 6;
        }
        var typeFont = Fonts.Get(11);
        var typeW = type.Length > 0 ? typeFont.MeasureText(type) : 0;
        var right = s.Head.Right - 10 - (typeW > 0 ? typeW + 6 : 0);
        if (s.RouteButton != null) right = s.RouteButton.Value.Left - 6;
        var tagFont = Fonts.Get(11);
        var tagList = tags.Where(t => t.Text.Length > 0).ToList();
        var tagsW = tagList.Sum(t => tagFont.MeasureText(t.Text) + 10 + 6);
        var nameW = Fonts.Draw(canvas, name, x, lineTop, lineH, font, _p.Ink, maxWidth: Math.Max(10, right - x - tagsW), paint: _text);
        x += nameW + 6;
        foreach (var t in tagList)
        {
            var tw = tagFont.MeasureText(t.Text) + 10;
            var tr = new SKRect(x, lineTop + (lineH - 16) / 2, x + tw, lineTop + (lineH - 16) / 2 + 16);
            _stroke.Color = t.Color; _stroke.PathEffect = t.Dashed ? _dash : null;
            canvas.DrawRoundRect(new SKRoundRect(tr, 3, 3), _stroke);
            _stroke.PathEffect = null;
            Fonts.Draw(canvas, t.Text, tr.Left + 5, tr.Top, 16, Fonts.Get(11, italic: t.Dashed), t.Color, paint: _text);
            x += tw + 6;
        }
        if (type.Length > 0)
            Fonts.Draw(canvas, type, s.Head.Right - 10, lineTop, lineH, typeFont, _p.Ink3, SKTextAlign.Right, maxWidth: italic ? s.Head.Width * 0.55f : float.PositiveInfinity, paint: _text);
        if (s.RouteButton is { } rb)
        {
            Icons.Draw(canvas, NodeType.Route, rb.MidX - 6, rb.MidY - 6, _p.HueRoute, 12, _stroke);
        }
        if (rule && !chip)
        {
            _stroke.Color = _p.Rule2;
            canvas.DrawLine(h.Left, h.Bottom - 0.5f, h.Right, h.Bottom - 0.5f, _stroke);
        }
    }

    private void FillScreen(SKCanvas canvas, CardGeom gm, Node? n, SceneFrame f)
    {
        var c = gm.Card; var s = gm.Shape;
        DrawHead(canvas, s, NodeType.Screen, n?.Name ?? "", "Screen", new[] { new Tag(c.IsEntry ? "entry" : "", _p.HueRoute, false) });
        if (s.PreviewRect is { } pr && s.Preview != null)
        {
            canvas.Save();
            canvas.Translate(pr.Left, pr.Top);
            _previews.Draw(canvas, s.Preview, f.HoverId);
            canvas.Restore();
        }
        if (s.Foot is { } foot && c.Caption != null)
        {
            _stroke.Color = _p.Rule2;
            canvas.DrawLine(foot.Left, foot.Top + 0.5f, foot.Right, foot.Top + 0.5f, _stroke);
            Fonts.Draw(canvas, c.Caption, 10, foot.Top + 6, 16, Fonts.Get(11), _p.Ink3, maxWidth: foot.Width - 20, paint: _text);
        }
    }

    private void FillFeature(SKCanvas canvas, CardGeom gm, Node? n, SceneFrame f)
    {
        var c = gm.Card; var s = gm.Shape;
        DrawHead(canvas, s, NodeType.Feature, n?.Name ?? "", "Feature", Array.Empty<Tag>());
        foreach (var (ps, cell, pr) in s.Plates)
        {
            var hovered = f.HoverId == ps.Screen.Id;
            _fill.Color = _p.Paper;
            canvas.DrawRoundRect(new SKRoundRect(cell, 6, 6), _fill);
            _stroke.Color = hovered ? _p.Ink3 : _p.Rule;
            canvas.DrawRoundRect(new SKRoundRect(cell, 6, 6), _stroke);
            var nameFont = Fonts.Get(11, weight: 600);
            var x = cell.Left + 6;
            var nw = Fonts.Draw(canvas, ps.Screen.Name, x, cell.Top + 6, 16, nameFont, _p.Ink, maxWidth: cell.Width - 12, paint: _text);
            x += nw + 4;
            var tags = new List<Tag>();
            if (ps.IsEntry) tags.Add(new Tag("entry", _p.HueRoute, false));
            if (c.ShowStateCounts) tags.Add(new Tag($"{ps.StateCount} states", _p.Ink3, false));
            foreach (var t in tags)
            {
                var tf = Fonts.Get(11);
                var tw = tf.MeasureText(t.Text) + 10;
                if (x + tw > cell.Right - 6) break;
                var tr = new SKRect(x, cell.Top + 6, x + tw, cell.Top + 22);
                _stroke.Color = t.Text == "entry" ? _p.HueRoute : _p.Rule;
                canvas.DrawRoundRect(new SKRoundRect(tr, 3, 3), _stroke);
                Fonts.Draw(canvas, t.Text, tr.Left + 5, tr.Top, 16, tf, t.Color, paint: _text);
                x += tw + 4;
            }
            canvas.Save();
            canvas.Translate(pr.Left, pr.Top);
            _previews.Draw(canvas, new PreviewSpec(ps.Screen.Id, PreviewFrame.Xs, null, false, null, null, false, true), null);
            canvas.Restore();
        }
        if (s.Foot is { } foot)
        {
            var screens = c.Screens ?? new();
            var vms = screens.Select(ps => _g.VmOfScreen(ps.Screen.Id)?.Name).Where(x => x != null).Distinct().ToList();
            var sub = $"{screens.Count} screen{(screens.Count == 1 ? "" : "s")} · {(vms.Count == 1 ? vms[0] : vms.Count + " view models")}";
            Fonts.Draw(canvas, sub, 10, foot.Top + 6, 16, Fonts.Get(11), _p.Ink3, maxWidth: foot.Width - 20, paint: _text);
        }
    }

    private void FillVm(SKCanvas canvas, CardGeom gm, Node? n, SceneFrame f)
    {
        var c = gm.Card; var s = gm.Shape;
        DrawHead(canvas, s, NodeType.ViewModel, n?.Name ?? "", "View model", new[] { new Tag(c.Tag ?? "", _p.HueVm, false) });
        if (!string.IsNullOrEmpty(c.Sub))
            Fonts.Draw(canvas, c.Sub, 10, s.MembersTop + 3, 15, Fonts.Get(11), _p.Ink3, maxWidth: s.Face.Width - 20, paint: _text);
        var first = true;
        foreach (var (m, row, hot, faded) in s.Members)
        {
            var isHover = f.HoverId == m.Id;
            var isFocus = f.FocusId == m.Id;
            if (hot) { _fill.Color = _p.FocusSoft; canvas.DrawRect(row, _fill); }
            if (isHover) { _fill.Color = _p.Paper2; canvas.DrawRect(row, _fill); }
            if (isFocus) { _fill.Color = _p.Focus; canvas.DrawRect(new SKRect(row.Left, row.Top, row.Left + 2, row.Bottom), _fill); }
            var alpha = faded ? (byte)90 : (byte)255;
            var glyphColor = _p.HueVm.WithAlpha(alpha);
            Icons.Draw(canvas, m.Type, 10, row.Top + 7, glyphColor, 12, _stroke);
            var mono = Fonts.Get(11, mono: true);
            var typeText = m.Prop("clrType") ?? "";
            var typeW = typeText.Length > 0 ? Math.Min(130, mono.MeasureText(typeText)) : 0;
            Fonts.Draw(canvas, m.Name, 10 + 14 + 6, row.Top + 4, 18, mono, _p.Ink.WithAlpha(alpha), maxWidth: row.Width - 30 - typeW - 16, paint: _text);
            if (typeText.Length > 0) Fonts.Draw(canvas, typeText, row.Right - 10, row.Top + 4, 18, mono, _p.Ink3.WithAlpha(alpha), SKTextAlign.Right, 130, _text);
            _stroke.Color = _p.Rule2;
            if (c.Rows != null) canvas.DrawLine(row.Left, row.Bottom - 0.5f, row.Right, row.Bottom - 0.5f, _stroke);
            else if (!first) canvas.DrawLine(row.Left, row.Top + 0.5f, row.Right, row.Top + 0.5f, _stroke);
            first = false;
        }
        if (c.More > 0)
        {
            var y = s.Members.Count > 0 ? s.Members[^1].Row.Bottom : s.MembersTop;
            Fonts.Draw(canvas, $"+ {c.More} more", 10, y + 4, 18, Fonts.Get(11), _p.Ink3, paint: _text);
        }
    }

    private void FillChip(SKCanvas canvas, CardGeom gm, Node? n)
    {
        var c = gm.Card; var s = gm.Shape;
        DrawHead(canvas, s, n?.Type, c.Title ?? n?.Name ?? "", "", new[] { new Tag(c.Tag ?? "", _p.HueVm, false) }, chip: true, mono: c.Mono);
        if (!string.IsNullOrEmpty(c.Sub))
            Fonts.Draw(canvas, c.Sub, 10, ChipBodyTop, 16, Fonts.Get(11), _p.Ink3, maxWidth: s.Face.Width - 20, paint: _text);
    }

    private const float ChipBodyTop = SceneGeometry.ChipHead;

    private void FillState(SKCanvas canvas, CardGeom gm, Node? n, SceneFrame f)
    {
        var c = gm.Card; var s = gm.Shape;
        DrawHead(canvas, s, NodeType.State, n?.Name ?? "", string.IsNullOrEmpty(c.OwnerName) ? "State" : c.OwnerName, Array.Empty<Tag>(), italic: true);
        if (s.PreviewRect is { } pr && s.Preview != null)
        {
            canvas.Save();
            canvas.Translate(pr.Left, pr.Top);
            _previews.Draw(canvas, s.Preview, null);
            canvas.Restore();
        }
    }

    private void FillDetail(SKCanvas canvas, CardGeom gm, Node? n)
    {
        var c = gm.Card; var s = gm.Shape;
        if (n == null) return;
        DrawHead(canvas, s, n.Type, n.Name, NodeType.Label(n.Type), Array.Empty<Tag>());
        var rows = new List<(string K, string V)>();
        void Add(string k, string? v) { if (!string.IsNullOrEmpty(v)) rows.Add((k, v)); }
        if (n.Type is NodeType.Property or NodeType.Command)
        {
            Add("type", n.Prop("clrType")); Add("expression", n.Prop("expression")); Add("default", n.Prop("default")); Add("parameter", n.Prop("parameter"));
            if (n.PropBool("writable")) Add("writable", "yes (TwoWay capable)");
        }
        else if (n.Type == NodeType.Route)
        {
            Add("request", n.Prop("request")); Add("mechanism", n.Prop("mechanism")); Add("qualifier", n.Prop("qualifier")); Add("data", n.Prop("data"));
        }
        else if (n.Type == NodeType.Component)
        {
            Add("class", n.Prop("uno.class")); Add("parts", string.Join(", ", n.PropList("parts"))); Add("props", string.Join(", ", n.PropList("dependencyProperties")));
            Add("uses", _g.InstancesOf(n.Id).Count.ToString());
        }
        var y = SceneGeometry.Head + 8;
        var keyFont = Fonts.Get(11);
        var valFont = Fonts.Get(11, mono: true);
        foreach (var (k, v) in rows)
        {
            Fonts.Draw(canvas, k, 10, y, 16, keyFont, _p.Ink3, paint: _text);
            var lines = Fonts.Wrap(valFont, v, s.Face.Width - 10 - 100 - 10, 3);
            for (var i = 0; i < lines.Count; i++) Fonts.Draw(canvas, lines[i], 10 + 100, y + i * 16, 16, valFont, _p.Ink, paint: _text);
            y += Math.Max(1, lines.Count) * 16 + 2;
        }
        if (n.Type is NodeType.Property or NodeType.Command)
        {
            y += 8;
            _stroke.Color = _p.Ink3;
            canvas.DrawCircle(10 + 3.5f, y + 8, 3.5f, _stroke);
            Fonts.Draw(canvas, "live value unavailable · no running app connected", 10 + 7 + 6, y, 16, keyFont, _p.Ink3, maxWidth: s.Face.Width - 40, paint: _text);
        }
    }

    // ---------------------------------------------------------------- links

    private static string EndId(string anchor) => anchor.Split(':')[0].Split('/')[^1].Split('#')[0];

    private void DrawLinks(SKCanvas canvas, SceneFrame f)
    {
        var anchors = new Dictionary<string, SKPoint>();
        foreach (var gm in f.Geoms)
            foreach (var (key, local) in gm.Anchors)
            {
                var (x, y, _) = gm.M.Project(local.X, local.Y);
                anchors[key] = new SKPoint((float)x, (float)y);
            }
        var hover = f.HoverId;
        using var path = new SKPath();
        foreach (var l in f.Links)
        {
            if (!anchors.TryGetValue(l.From, out var a) || !anchors.TryGetValue(l.To, out var b)) continue;
            var idA = EndId(l.From); var idB = EndId(l.To);
            var hot = hover != null && (idA == hover || idB == hover || l.Id == hover);
            var dim = hover != null && !hot;
            var horizontal = Math.Abs(b.X - a.X) >= Math.Abs(b.Y - a.Y) * 0.8;
            SKPoint c1, c2;
            if (horizontal) { c1 = new SKPoint((a.X + b.X) / 2, a.Y); c2 = new SKPoint((a.X + b.X) / 2, b.Y); }
            else { c1 = new SKPoint(a.X, (a.Y + b.Y) / 2); c2 = new SKPoint(b.X, (a.Y + b.Y) / 2); }
            path.Reset();
            path.MoveTo(a);
            path.CubicTo(c1, c2, b);
            var color = _p.LinkColor(l.Relation);
            _stroke.Color = dim ? color.WithAlpha(64) : color;
            _stroke.StrokeWidth = hot ? 1.75f : 1f;
            _stroke.PathEffect = l.Inferred ? _dash : null;
            if (f.DrawT < 1)
            {
                using var trim = SKPathEffect.CreateTrim(0, f.DrawT);
                _stroke.PathEffect = l.Inferred ? SKPathEffect.CreateCompose(_dash, trim) : trim;
            }
            canvas.DrawPath(path, _stroke);
            _stroke.PathEffect = null;
            _stroke.StrokeWidth = 1;
            if (l.Arrow && f.DrawT >= 1)
            {
                var ang = horizontal ? (b.X >= a.X ? 0 : Math.PI) : (b.Y >= a.Y ? Math.PI / 2 : -Math.PI / 2);
                var cs = (float)Math.Cos(ang); var sn = (float)Math.Sin(ang);
                const float sz = 5;
                var pts = new[] { (0f, 0f), (-sz * 1.8f, -sz * 0.9f), (-sz * 1.8f, sz * 0.9f) }
                    .Select(p => new SKPoint(b.X + p.Item1 * cs - p.Item2 * sn, b.Y + p.Item1 * sn + p.Item2 * cs)).ToArray();
                using var tri = new SKPath();
                tri.MoveTo(pts[0]); tri.LineTo(pts[1]); tri.LineTo(pts[2]); tri.Close();
                _fill.Color = dim ? color.WithAlpha(64) : color;
                canvas.DrawPath(tri, _fill);
            }
            if (l.Label.Length > 0)
            {
                var t = (float)(l.LabelT ?? 0.5);
                static float Bz(float p0, float p1, float p2, float p3, float t) => (1 - t) * (1 - t) * (1 - t) * p0 + 3 * (1 - t) * (1 - t) * t * p1 + 3 * (1 - t) * t * t * p2 + t * t * t * p3;
                var lx = Bz(a.X, c1.X, c2.X, b.X, t);
                var ly = Bz(a.Y, c1.Y, c2.Y, b.Y, t) + (float)(l.LabelDy ?? -5);
                var text = l.Inferred ? $"{l.Label} · inferred" : l.Label;
                var font = Fonts.Get(10.5f, italic: l.Inferred);
                var baseline = ly; // SVG text y is the baseline
                _halo.Color = _p.Paper;
                canvas.DrawText(text, lx, baseline, SKTextAlign.Center, font, _halo);
                _text.Color = dim ? _p.Ink2.WithAlpha(77) : _p.Ink2;
                _text.Style = SKPaintStyle.Fill;
                canvas.DrawText(text, lx, baseline, SKTextAlign.Center, font, _text);
            }
        }
    }
}
