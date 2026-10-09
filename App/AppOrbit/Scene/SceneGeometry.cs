using AppOrbit.Graph;
using AppOrbit.Layout;
using Card = AppOrbit.Layout.Card;
using SkiaSharp;

namespace AppOrbit.Scene;

/// <summary>Something the pointer can land on, in card-local pixels. Kinds: card, region, member, plate, route.</summary>
public sealed record HitRegion(SKRect Rect, string Id, string Kind);

/// <summary>Sub-rectangles of a card, computed once per layout from the card's data (the DOM layout the prototype's CSS produced).</summary>
public sealed class CardShape
{
    public SKRect Face;
    public SKRect Head;
    public SKRect? Foot;
    public SKRect? PreviewRect;
    public PreviewSpec? Preview;
    public List<PreviewRegion> Regions = new();
    public List<(PlateScreen Screen, SKRect Cell, SKRect PreviewRect)> Plates = new();
    public List<(Node Member, SKRect Row, bool Hot, bool Faded)> Members = new();
    public SKRect? RouteButton;
    public float MembersTop;
    public bool Chip;
}

/// <summary>A card with its shape, its plane matrix for the current camera, its hit regions and its anchors.</summary>
public sealed class CardGeom
{
    public required Card Card { get; init; }
    public required CardShape Shape { get; init; }
    public required List<HitRegion> Hits { get; init; }
    public required Dictionary<string, SKPoint> Anchors { get; init; }
    public Mat4 M;
    public SKMatrix H;
    public SKMatrix Inv;
    public bool Invertible;
    public double Depth;
    public double OffsetX, OffsetY;
}

/// <summary>Builds card shapes, hit regions and anchors from a layout; projects them through the camera.</summary>
public static class SceneGeometry
{
    public const float Head = 30, ChipHead = 26, MemberH = 26, PlateCell = 142, PlatePitch = 152;

    public static List<CardGeom> Build(GraphIndex g, LayoutResult layout, PreviewPainter previews)
    {
        var geoms = new List<CardGeom>(layout.Cards.Count);
        foreach (var c in layout.Cards)
        {
            var w = (float)c.W; var h = (float)c.H;
            var shape = new CardShape { Face = new SKRect(0, 0, w, h), Chip = c.Kind == "chip" };
            shape.Head = new SKRect(0, 0, w, shape.Chip ? ChipHead : Head);
            var hits = new List<HitRegion>();
            var anchors = new Dictionary<string, SKPoint>();
            void AnchorsFor(string key, SKRect r)
            {
                anchors[$"{key}:l"] = new SKPoint(r.Left, r.MidY);
                anchors[$"{key}:r"] = new SKPoint(r.Right, r.MidY);
                anchors[$"{key}:t"] = new SKPoint(r.MidX, r.Top);
                anchors[$"{key}:b"] = new SKPoint(r.MidX, r.Bottom);
                anchors[$"{key}:c"] = new SKPoint(r.MidX, r.MidY);
            }
            AnchorsFor(c.Key, shape.Face);

            switch (c.Kind)
            {
                case "screen":
                {
                    var (fw, fh) = PreviewFrame.OfF(c.Size ?? PreviewFrame.Lg);
                    shape.PreviewRect = new SKRect(8, Head + 8, 8 + fw, Head + 8 + fh);
                    shape.Preview = new PreviewSpec(c.Id, c.Size ?? PreviewFrame.Lg, null, c.Interactive, c.HighlightId, c.HighlightIds, c.Recede, false);
                    if (c.Caption != null) shape.Foot = new SKRect(0, h - 28, w, h);
                    shape.Regions = previews.Regions(shape.Preview);
                    foreach (var region in shape.Regions)
                    {
                        var r = region.Rect;
                        r.Offset(shape.PreviewRect.Value.Left, shape.PreviewRect.Value.Top);
                        AnchorsFor($"{c.Key}/{region.InstanceId}", r);
                        if (!region.Faded) hits.Add(new HitRegion(r, region.InstanceId, "region"));
                    }
                    break;
                }
                case "state":
                {
                    var size = c.Size ?? PreviewFrame.Sm;
                    var (fw, fh) = PreviewFrame.OfF(size);
                    shape.PreviewRect = new SKRect(8, Head + 8, 8 + fw, Head + 8 + fh);
                    if (c.ScreenId != null)
                        shape.Preview = new PreviewSpec(c.ScreenId, size, c.Id, false, c.HighlightId, null, false, size is not (PreviewFrame.Lg or PreviewFrame.Md));
                    break;
                }
                case "feature":
                {
                    var screens = c.Screens ?? new();
                    var cellW = (w - 20 - 10) / 2;
                    for (var i = 0; i < screens.Count; i++)
                    {
                        var col = i % 2; var row = i / 2;
                        var cell = new SKRect(10 + col * (cellW + 10), Head + 10 + row * PlatePitch, 10 + col * (cellW + 10) + cellW, Head + 10 + row * PlatePitch + PlateCell);
                        var (pw, ph) = PreviewFrame.OfF(PreviewFrame.Xs);
                        var pr = new SKRect(cell.MidX - pw / 2, cell.Top + 6 + 16 + 4, cell.MidX + pw / 2, cell.Top + 6 + 16 + 4 + ph);
                        shape.Plates.Add((screens[i], cell, pr));
                        AnchorsFor($"{c.Key}/{screens[i].Screen.Id}", cell);
                        hits.Add(new HitRegion(cell, screens[i].Screen.Id, "plate"));
                    }
                    shape.Foot = new SKRect(0, h - 30, w, h);
                    break;
                }
                case "vm":
                {
                    var members = c.Members ?? new();
                    shape.MembersTop = Head;
                    var subTop = !string.IsNullOrEmpty(c.Sub) ? 18f : 0f;
                    if (c.Rows != null)
                    {
                        foreach (var m in members)
                        {
                            var row = c.Rows.FirstOrDefault(r => r.Id == m.Id);
                            var y = Head + (float)(row?.Y ?? 0);
                            var rect = new SKRect(0, y, w, y + MemberH);
                            shape.Members.Add((m, rect, c.Hot.Contains(m.Id), c.Faded.Contains(m.Id)));
                        }
                    }
                    else
                    {
                        var y = Head + subTop;
                        foreach (var m in members)
                        {
                            var rect = new SKRect(0, y, w, y + MemberH);
                            shape.Members.Add((m, rect, c.Hot.Contains(m.Id), c.Faded.Contains(m.Id)));
                            y += MemberH;
                        }
                    }
                    foreach (var (m, rect, _, _) in shape.Members)
                    {
                        AnchorsFor($"{c.Key}/{m.Id}", rect);
                        hits.Add(new HitRegion(rect, m.Id, "member"));
                    }
                    break;
                }
                case "chip":
                {
                    if (c.RouteId != null)
                    {
                        shape.RouteButton = new SKRect(w - 30, 4, w - 10, 24);
                        hits.Add(new HitRegion(shape.RouteButton.Value, c.RouteId, "route"));
                    }
                    break;
                }
            }
            hits.Add(new HitRegion(shape.Face, c.Id, "card"));
            geoms.Add(new CardGeom { Card = c, Shape = shape, Hits = hits, Anchors = anchors });
        }
        return geoms;
    }

    /// <summary>Places every card for the camera: plane matrix, inverse for hit testing, depth for paint order.</summary>
    public static void Project(List<CardGeom> geoms, Camera cam, double width, double height, Func<string, (double X, double Y)> offsetOf)
    {
        var world = cam.World(width, height);
        foreach (var gm in geoms)
        {
            var c = gm.Card;
            var (ox, oy) = offsetOf(c.Key);
            gm.OffsetX = ox; gm.OffsetY = oy;
            gm.M = world * Mat4.Translate(c.X + ox - c.W / 2, c.Y + oy - c.H / 2, c.Z) * Mat4.RotateY(c.RotY);
            gm.H = gm.M.ToPlaneMatrix();
            gm.Invertible = gm.H.TryInvert(out gm.Inv);
            gm.Depth = gm.M.Project(c.W / 2, c.H / 2).Depth;
        }
    }

    /// <summary>Cards back to front, the painter's order; sorted once per projection.</summary>
    public static CardGeom[] PaintOrder(List<CardGeom> geoms)
    {
        var order = geoms.ToArray();
        Array.Sort(order, (a, b) => a.Depth.CompareTo(b.Depth));
        return order;
    }

    /// <summary>The topmost hit under a stage point, sub-elements before their card. Walks the paint order front to back.</summary>
    public static (CardGeom Geom, HitRegion Hit)? HitTest(CardGeom[] paintOrder, SKPoint p)
    {
        for (var i = paintOrder.Length - 1; i >= 0; i--)
        {
            var gm = paintOrder[i];
            if (!gm.Invertible) continue;
            var local = gm.Inv.MapPoint(p);
            if (!gm.Shape.Face.Contains(local)) continue;
            foreach (var hit in gm.Hits)
                if (hit.Rect.Contains(local)) return (gm, hit);
        }
        return null;
    }
}
