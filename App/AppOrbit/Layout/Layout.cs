using AppOrbit.Graph;
using AppOrbit.State;

namespace AppOrbit.Layout;

// layout.js: (focus, lens, view, mode) → placed cards and links. Pure; no UI.
// Coordinates are scene units (px at scale 1), origin at the scene centre, y down, z toward the viewer.

public sealed record PlateScreen(Node Screen, int StateCount, bool IsEntry);
public sealed record MemberRow(string Id, double Y, bool Hot);

public sealed class Card
{
    public required string Kind { get; init; }
    public required string Key { get; init; }
    public required string Type { get; init; }
    public required string Id { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Z { get; init; }
    public double W { get; init; }
    public double H { get; init; }
    public double RotY { get; init; }

    // feature plates
    public List<PlateScreen>? Screens { get; init; }
    public bool ShowStateCounts { get; init; }

    // previews (screen and state cards)
    public string? Size { get; init; }
    public bool Interactive { get; init; }
    public bool IsEntry { get; init; }
    public string? HighlightId { get; init; }
    public List<string>? HighlightIds { get; init; }
    public bool Recede { get; init; }
    public string? Caption { get; init; }
    public string? ScreenId { get; init; }
    public string? OwnerName { get; init; }
    public bool Big { get; init; }

    // chips
    public string? Title { get; init; }
    public string? Sub { get; init; }
    public string? Tag { get; init; }
    public bool Mono { get; init; }
    public string? RouteId { get; init; }

    // view model cards
    public List<Node>? Members { get; init; }
    public List<MemberRow>? Rows { get; init; }
    public HashSet<string> Hot { get; init; } = new();
    public HashSet<string> Faded { get; init; } = new();
    public int More { get; init; }
    public bool Expanded { get; init; }
}

public sealed record Link(string From, string To, string Relation, string Label, bool Arrow, string? Id, double? LabelT, double? LabelDy, bool Inferred);

public readonly record struct Bounds(double X, double Y, double W, double H);

public sealed class LayoutResult
{
    public List<Card> Cards { get; } = new();
    public List<Link> Links { get; } = new();
    public string Note { get; set; } = "";
    public string Level { get; set; } = "";
    public Bounds Bounds { get; set; }
}

public static class PreviewFrame
{
    public const string Lg = "lg", Md = "md", Sm = "sm", Xs = "xs";

    public static (double W, double H) Of(string size) => size switch
    {
        Lg => (220, 392), Md => (150, 267), Sm => (100, 178), Xs => (62, 110), _ => (220, 392),
    };

    /// <summary>The same frame in floats, for the Skia side.</summary>
    public static (float W, float H) OfF(string size) { var (w, h) = Of(size); return ((float)w, (float)h); }
}

public static class LayoutEngine
{
    public const double Head = 30;
    public const double Pad = 8;
    public const double VmW = 280;
    public const double ChipW = 170;
    public const double ChipH = 52;
    public const double MemberH = 26;

    public static (double W, double H) PreviewCardSize(string size)
    {
        var (w, h) = PreviewFrame.Of(size);
        return (w + Pad * 2, h + Head + Pad * 2);
    }

    private static double VmHeight(int count, bool sub = false) => Head + 8 + count * MemberH + (sub ? 18 : 0) + 6;
    private static double Spread(int i, int n, double step) => (i - (n - 1) / 2.0) * step;

    /// <summary>A screen's feature as a subtitle, unless it would only repeat the screen's name.</summary>
    private static string FeatureSub(GraphIndex g, Node screen)
    {
        var f = g.FeatureOfScreen(screen.Id)?.Name ?? "";
        return f == screen.Name ? "" : f;
    }

    private static Link L(string from, string to, string relation, string label = "", bool arrow = false, string? id = null, double? labelT = null, double? labelDy = null, bool inferred = false) =>
        new(from, to, relation, label, arrow, id, labelT, labelDy, inferred);

    /// <summary>Member rows for a view model card whose bound members align with the UI rows they drive.</summary>
    private static (List<MemberRow> Rows, double H) AlignedRows(List<Node> members, HashSet<string> hot, Dictionary<string, double> frac, double bodyH, bool sub = false)
    {
        var top = sub ? 18.0 : 0.0;
        var hotList = members.Where(m => hot.Contains(m.Id)).Select(m => (m, f: frac.TryGetValue(m.Id, out var v) ? v : 0.5)).OrderBy(x => x.f).ToList();
        var rows = new List<MemberRow>();
        var y = top;
        foreach (var (m, f) in hotList)
        {
            y = Math.Max(top + f * bodyH - MemberH / 2, y);
            rows.Add(new MemberRow(m.Id, y, true));
            y += MemberH;
        }
        var yy = Math.Max(y, top + bodyH * 0.55);
        foreach (var m in members.Where(mm => !hot.Contains(mm.Id))) { rows.Add(new MemberRow(m.Id, yy, false)); yy += MemberH; }
        return (rows, Head + 8 + yy + 8);
    }

    public static LayoutResult Compute(GraphIndex g, AppState state)
    {
        var flat = state.View == Views.Flat;
        // a lifted viewer takes its compact form the moment it is picked up, whatever home it is heading for
        var docked = state.Mode == Modes.Docked || state.Carrying;
        var level = g.LevelOf(state.FocusId);
        var result = level switch
        {
            Level.Application => LayoutApplication(g, state.Lens, flat, docked),
            Level.Feature => LayoutFeature(g, state.FocusId!, state.Lens, flat, docked),
            Level.Screen => LayoutScreen(g, state.FocusId!, state.Lens, flat, docked),
            Level.Component => LayoutComponent(g, state.FocusId!, state.Lens, flat, docked),
            _ => LayoutDetail(g, state.FocusId!, flat, docked),
        };
        result.Level = level;
        result.Bounds = BoundsOf(result.Cards);
        return result;
    }

    private static Bounds BoundsOf(List<Card> cards)
    {
        if (cards.Count == 0) return new Bounds(0, 0, 1, 1);
        double x0 = double.PositiveInfinity, y0 = double.PositiveInfinity, x1 = double.NegativeInfinity, y1 = double.NegativeInfinity;
        foreach (var c in cards)
        {
            x0 = Math.Min(x0, c.X - c.W / 2); x1 = Math.Max(x1, c.X + c.W / 2);
            y0 = Math.Min(y0, c.Y - c.H / 2); y1 = Math.Max(y1, c.Y + c.H / 2);
        }
        return new Bounds((x0 + x1) / 2, (y0 + y1) / 2, x1 - x0, y1 - y0);
    }

    private static Card FindCard(List<Card> cards, string key) => cards.First(c => c.Key == key);

    // ------------------------------------------------------------------ application
    private static LayoutResult LayoutApplication(GraphIndex g, string lens, bool flat, bool docked)
    {
        var r = new LayoutResult();
        var cards = r.Cards; var links = r.Links;
        var features = g.NodesOf(NodeType.Feature);
        const double plateW = 250;
        var rowH = PreviewFrame.Of(PreviewFrame.Xs).H + 44;
        var plateOf = new Dictionary<string, string>();
        for (var i = 0; i < features.Count; i++)
        {
            var f = features[i];
            var screens = g.ScreensOfFeature(f.Id);
            var rows = Math.Max(1, (int)Math.Ceiling(screens.Count / 2.0));
            var h = Head + 10 + rows * rowH + 30;
            cards.Add(new Card
            {
                Kind = "feature", Key = f.Id, Type = NodeType.Feature, Id = f.Id,
                X = Spread(i, features.Count, 300), Y = 0, Z = 0, W = plateW, H = h,
                Screens = screens.Select(s => new PlateScreen(s, g.StatesOf(s.Id).Count, s.Id == g.Raw.Entry)).ToList(),
                ShowStateCounts = lens == Lens.States,
            });
            foreach (var s in screens) plateOf[s.Id] = f.Id;
        }

        if (docked) return r;

        if (lens == Lens.Navigation)
        {
            var k = 0;
            foreach (var route in g.NodesOf(NodeType.Route))
            {
                var target = g.RouteTarget(route.Id);
                foreach (var o in g.RouteOrigins(route.Id))
                {
                    if (target == null || !plateOf.TryGetValue(o.Screen.Id, out var fromKey) || !plateOf.TryGetValue(target.Id, out var toKey)) continue;
                    var cross = fromKey != toKey;
                    links.Add(L($"{fromKey}/{o.Screen.Id}:{(cross ? "r" : "c")}", $"{toKey}/{target.Id}:{(cross ? "l" : "c")}", "route",
                        cross ? o.Via.Name : "", arrow: true, id: route.Id, labelT: cross ? 0.25 + 0.25 * (k % 3) : 0.5, labelDy: k++ % 2 == 1 ? 14 : -5));
                }
            }
        }
        if (lens == Lens.Behavior)
        {
            var vms = g.NodesOf(NodeType.ViewModel);
            foreach (var vm in vms)
            {
                var screens = g.ScreensUsingVm(vm.Id);
                var plates = screens.Select(s => plateOf.GetValueOrDefault(s.Id)).Where(p => p != null).Distinct().Cast<string>().ToList();
                if (plates.Count == 0) continue;
                var xs = plates.Select(p => FindCard(cards, p).X).ToList();
                var x = xs.Sum() / xs.Count;
                string? FirstPlate(Node v) => g.ScreensUsingVm(v.Id).Select(s => plateOf.GetValueOrDefault(s.Id)).Distinct().FirstOrDefault();
                var idx = vms.Where(v => FirstPlate(v) == plates[0]).ToList().IndexOf(vm);
                cards.Add(new Card
                {
                    Kind = "chip", Key = vm.Id, Type = NodeType.ViewModel, Id = vm.Id,
                    X = x, Y = 190 + idx * 56, Z = flat ? 0 : -160, W = ChipW + 20, H = ChipH,
                    Title = vm.Name, Sub = screens.Count > 1 ? $"serves {screens.Count} screens" : $"serves {screens.FirstOrDefault()?.Name}",
                    Tag = screens.Count > 1 ? "shared" : "",
                });
                foreach (var s in screens) links.Add(L($"{plateOf[s.Id]}/{s.Id}:b", $"{vm.Id}:t", Relation.UsesViewModel));
            }
        }
        if (lens == Lens.Structure)
        {
            var defs = g.NodesOf(NodeType.Component);
            for (var i = 0; i < defs.Count; i++)
            {
                var def = defs[i];
                var uses = g.ScreensUsingDefinition(def.Id);
                cards.Add(new Card
                {
                    Kind = "chip", Key = def.Id, Type = NodeType.Component, Id = def.Id,
                    X = Spread(i, defs.Count, 220), Y = 200, Z = flat ? 0 : 60, W = ChipW + 20, H = ChipH,
                    Title = def.Name, Sub = $"{uses.Count} uses · {string.Join(", ", uses.Select(u => u.Screen.Name))}",
                });
                foreach (var u in uses) links.Add(L($"{plateOf[u.Screen.Id]}/{u.Screen.Id}:b", $"{def.Id}:t", Relation.InstanceOf));
            }
        }
        r.Note = lens == Lens.States ? "Declared state counts per screen" : "";
        return r;
    }

    // ------------------------------------------------------------------ feature
    private static LayoutResult LayoutFeature(GraphIndex g, string featureId, string lens, bool flat, bool docked)
    {
        var r = new LayoutResult();
        var cards = r.Cards; var links = r.Links;
        var screens = g.ScreensOfFeature(featureId);
        var size = docked ? PreviewFrame.Sm : PreviewFrame.Md;
        var sz = PreviewCardSize(size);
        var step = sz.W + (docked ? 40 : 90);
        var keyOf = new HashSet<string>();
        for (var i = 0; i < screens.Count; i++)
        {
            var s = screens[i];
            keyOf.Add(s.Id);
            cards.Add(new Card
            {
                Kind = "screen", Key = s.Id, Type = NodeType.Screen, Id = s.Id,
                X = Spread(i, screens.Count, step), Y = 0, Z = 0, W = sz.W, H = sz.H,
                Size = size, Interactive = false, IsEntry = s.Id == g.Raw.Entry,
            });
        }
        var leftX = Spread(0, screens.Count, step) - sz.W / 2;
        var rightX = -leftX;

        // routes inside the feature are always shown (navigation is what a flow is about)
        if (lens == Lens.Navigation || docked)
        {
            var inChips = new List<(RouteHop Hop, string To)>();
            var outChips = new List<(RouteHop Hop, string FromAnchor)>();
            foreach (var s in screens)
            {
                foreach (var hop in g.RoutesFrom(s.Id))
                {
                    if (hop.Target == null) continue;
                    var fromAnchor = hop.Origin.Instance != null ? $"{s.Id}/{hop.Origin.Instance.Id}:r" : $"{s.Id}:r";
                    if (keyOf.Contains(hop.Target.Id))
                        links.Add(L(fromAnchor, $"{hop.Target.Id}:l", "route", docked ? "" : hop.Origin.Via.Name, arrow: true, id: hop.Route.Id));
                    else if (!docked)
                        outChips.Add((hop, fromAnchor));
                }
                if (docked) continue;
                foreach (var hop in g.RoutesTo(s.Id))
                    if (!keyOf.Contains(hop.Origin.Screen.Id)) inChips.Add((hop, s.Id));
            }
            for (var i = 0; i < inChips.Count; i++)
            {
                var (c, to) = inChips[i];
                var key = $"{c.Route.Id}#in";
                cards.Add(new Card
                {
                    Kind = "chip", Key = key, Type = NodeType.Screen, Id = c.Origin.Screen.Id,
                    X = leftX - 150, Y = Spread(i, inChips.Count, ChipH + 12), Z = 0, W = ChipW, H = ChipH,
                    Title = c.Origin.Screen.Name, Sub = $"via {c.Origin.Via.Name}", RouteId = c.Route.Id,
                });
                links.Add(L($"{key}:r", $"{to}:l", "route", "", arrow: true, id: c.Route.Id));
            }
            for (var i = 0; i < outChips.Count; i++)
            {
                var (c, fromAnchor) = outChips[i];
                var key = $"{c.Route.Id}#out";
                cards.Add(new Card
                {
                    Kind = "chip", Key = key, Type = NodeType.Screen, Id = c.Target!.Id,
                    X = rightX + 150, Y = Spread(i, outChips.Count, ChipH + 12), Z = 0, W = ChipW, H = ChipH,
                    Title = c.Target.Name, Sub = FeatureSub(g, c.Target), RouteId = c.Route.Id,
                });
                links.Add(L(fromAnchor, $"{key}:l", "route", c.Origin.Edge.Label ?? "", arrow: true, id: c.Route.Id));
            }
        }
        if (docked) return r;

        if (lens == Lens.Behavior)
        {
            var vms = screens.Select(s => g.VmOfScreen(s.Id)?.Id).Where(id => id != null).Distinct().Select(id => g.Node(id)!).ToList();
            foreach (var vm in vms)
            {
                var users = g.ScreensUsingVm(vm.Id).Where(s => keyOf.Contains(s.Id)).ToList();
                var x = users.Sum(s => FindCard(cards, s.Id).X) / users.Count;
                var members = g.MembersOfVm(vm.Id);
                var h = VmHeight(Math.Min(members.Count, 6)) + (members.Count > 6 ? 26 : 0);
                cards.Add(new Card
                {
                    Kind = "vm", Key = vm.Id, Type = NodeType.ViewModel, Id = vm.Id,
                    X = x + 20, Y = sz.H / 2 + 70 + h / 2, Z = flat ? 0 : -200, W = VmW, H = h,
                    Members = members.Take(6).ToList(), More = Math.Max(0, members.Count - 6),
                    Tag = g.ScreensUsingVm(vm.Id).Count > 1 ? "shared" : "",
                });
                foreach (var s in users) links.Add(L($"{s.Id}:b", $"{vm.Id}:t", Relation.UsesViewModel));
            }
        }
        if (lens == Lens.Structure)
        {
            var defs = new Dictionary<string, List<(Node Screen, Node Inst)>>();
            var defOrder = new List<string>();
            foreach (var s in screens)
            {
                foreach (var at in g.InstancesOfScreen(s.Id))
                {
                    var def = g.DefinitionOf(at.Node.Id);
                    if (def == null) continue;
                    if (!defs.TryGetValue(def.Id, out var list)) { defs[def.Id] = list = new(); defOrder.Add(def.Id); }
                    list.Add((s, at.Node));
                }
            }
            for (var i = 0; i < defOrder.Count; i++)
            {
                var defId = defOrder[i];
                var uses = defs[defId];
                var def = g.Node(defId)!;
                var all = g.ScreensUsingDefinition(defId);
                cards.Add(new Card
                {
                    Kind = "chip", Key = defId, Type = NodeType.Component, Id = defId,
                    X = Spread(i, defOrder.Count, 230), Y = sz.H / 2 + 70, Z = flat ? 0 : 60, W = ChipW + 30, H = ChipH,
                    Title = def.Name, Sub = $"{all.Count} uses · {string.Join(", ", all.Select(u => u.Screen.Name))}",
                });
                foreach (var u in uses) links.Add(L($"{u.Screen.Id}/{u.Inst.Id}:c", $"{defId}:t", Relation.InstanceOf));
            }
            if (defOrder.Count == 0) { r.Note = "No shared component definitions in this feature"; return r; }
        }
        if (lens == Lens.States)
        {
            foreach (var s in screens)
            {
                var sx = FindCard(cards, s.Id).X;
                var states = g.StatesOf(s.Id).Take(4).ToList();
                var tsz = PreviewCardSize(PreviewFrame.Xs);
                for (var j = 0; j < states.Count; j++)
                {
                    var st = states[j];
                    cards.Add(new Card
                    {
                        Kind = "state", Key = $"{st.Id}#thumb", Type = NodeType.State, Id = st.Id,
                        X = sx + Spread(j, states.Count, tsz.W + 6), Y = sz.H / 2 + 40 + tsz.H / 2, Z = flat ? 0 : 40, W = tsz.W, H = tsz.H,
                        ScreenId = s.Id, Size = PreviewFrame.Xs,
                    });
                }
            }
        }
        return r;
    }

    // ------------------------------------------------------------------ screen
    private static LayoutResult LayoutScreen(GraphIndex g, string screenId, string lens, bool flat, bool docked)
    {
        var r = new LayoutResult();
        var cards = r.Cards; var links = r.Links;
        var size = docked ? PreviewFrame.Md : PreviewFrame.Lg;
        var sz = PreviewCardSize(size);
        var front = screenId;
        cards.Add(new Card
        {
            Kind = "screen", Key = front, Type = NodeType.Screen, Id = screenId,
            X = 0, Y = 0, Z = 0, W = sz.W, H = sz.H, Size = size, Interactive = !docked, IsEntry = screenId == g.Raw.Entry,
        });

        if (lens == Lens.Navigation || docked)
        {
            var ins = g.RoutesTo(screenId);
            var outs = g.RoutesFrom(screenId);
            var chipW = docked ? 120 : ChipW;
            var dx = sz.W / 2 + (docked ? 90 : 170);
            for (var i = 0; i < ins.Count; i++)
            {
                var hop = ins[i];
                var key = $"{hop.Route.Id}#in";
                cards.Add(new Card
                {
                    Kind = "chip", Key = key, Type = NodeType.Screen, Id = hop.Origin.Screen.Id,
                    X = -dx, Y = Spread(i, ins.Count, ChipH + 12), Z = 0, W = chipW, H = ChipH,
                    Title = hop.Origin.Screen.Name, Sub = docked ? "" : $"via {hop.Origin.Via.Name}", RouteId = hop.Route.Id,
                });
                links.Add(L($"{key}:r", $"{front}:l", "route", "", arrow: true, id: hop.Route.Id));
            }
            for (var i = 0; i < outs.Count; i++)
            {
                var hop = outs[i];
                var key = $"{hop.Route.Id}#out";
                var target = hop.Target!;
                cards.Add(new Card
                {
                    Kind = "chip", Key = key, Type = NodeType.Screen, Id = target.Id,
                    X = dx, Y = Spread(i, outs.Count, ChipH + 12), Z = 0, W = chipW, H = ChipH,
                    Title = target.Name,
                    Sub = docked ? "" : (hop.Route.Prop("qualifier") != null ? "clears back stack" : FeatureSub(g, target)),
                    RouteId = hop.Route.Id,
                });
                var fromAnchor = hop.Origin.Instance != null && !docked ? $"{front}/{hop.Origin.Instance.Id}:r" : $"{front}:r";
                links.Add(L(fromAnchor, $"{key}:l", "route", docked ? "" : (hop.Origin.Edge.Label ?? ""), arrow: true, id: hop.Route.Id));
            }
            if (docked) return r;
            if (ins.Count + outs.Count == 0) { r.Note = "No declared routes touch this screen"; return r; }
        }

        if (lens == Lens.Behavior)
        {
            var vm = g.VmOfScreen(screenId);
            if (vm == null) { r.Note = "No view model declared for this screen"; return r; }
            var members = g.MembersOfVm(vm.Id);
            var hot = new HashSet<string>();
            var instIds = g.InstancesOfScreen(screenId).Select(x => x.Node.Id).ToHashSet();
            var bindingEdges = new List<Edge>();
            foreach (var m in members)
                foreach (var e in g.InEdges(m.Id))
                    if ((e.Relation is Relation.BindsTo or Relation.Invokes) && instIds.Contains(e.From)) { hot.Add(m.Id); bindingEdges.Add(e); }
            // bound members sit at the height of the UI element they drive
            var frac = new Dictionary<string, double>();
            foreach (var e in bindingEdges)
            {
                var b = g.Node(e.From)?.Preview?.Bounds;
                if (b != null && !frac.ContainsKey(e.To)) frac[e.To] = b.Y + b.H / 2;
            }
            var others = g.ScreensUsingVm(vm.Id).Where(s => s.Id != screenId).ToList();
            var (rows, h) = AlignedRows(members, hot, frac, PreviewFrame.Of(size).H, others.Count > 0);
            var key = vm.Id;
            cards.Add(new Card
            {
                Kind = "vm", Key = key, Type = NodeType.ViewModel, Id = vm.Id,
                X = sz.W / 2 + 210, Y = -sz.H / 2 + h / 2, Z = flat ? 0 : -200, W = VmW, H = h,
                Members = members, Rows = rows, Hot = hot, Faded = members.Where(m => !hot.Contains(m.Id)).Select(m => m.Id).ToHashSet(),
                Tag = others.Count > 0 ? "shared" : "", Sub = others.Count > 0 ? $"also serves {string.Join(", ", others.Select(s => s.Name))}" : "",
            });
            var perSource = new Dictionary<string, int>();
            foreach (var e in bindingEdges)
            {
                var k = perSource.GetValueOrDefault(e.From);
                perSource[e.From] = k + 1;
                var total = bindingEdges.Count(b => b.From == e.From);
                links.Add(L($"{front}/{e.From}:r", $"{key}/{e.To}:l", e.Relation, e.Label ?? "", arrow: e.Relation == Relation.Invokes, inferred: e.IsInferred,
                    labelT: total == 1 ? 0.45 : 0.2 + 0.6 * ((double)k / (total - 1))));
            }
            for (var i = 0; i < others.Count; i++)
            {
                var s = others[i];
                var ck = $"{s.Id}#shared";
                cards.Add(new Card
                {
                    Kind = "chip", Key = ck, Type = NodeType.Screen, Id = s.Id,
                    X = sz.W / 2 + 210 + Spread(i, others.Count, ChipW + 10), Y = -sz.H / 2 + h + 60, Z = flat ? 0 : -200, W = ChipW, H = ChipH,
                    Title = s.Name, Sub = "uses the same view model",
                });
                links.Add(L($"{key}:b", $"{ck}:t", Relation.UsesViewModel));
            }
        }

        if (lens == Lens.States)
        {
            var owned = g.StatesOf(screenId).Select(st => (St: st, Owner: (Node?)null)).ToList();
            foreach (var at in g.InstancesOfScreen(screenId))
                foreach (var st in g.StatesOf(at.Node.Id)) owned.Add((st, at.Node));
            if (owned.Count == 0) { r.Note = "No declared states for this screen"; return r; }
            var tsz = PreviewCardSize(PreviewFrame.Sm);
            const int cols = 2;
            var x0 = sz.W / 2 + 60 + tsz.W / 2;
            var rowsN = (int)Math.Ceiling(owned.Count / (double)cols);
            for (var j = 0; j < owned.Count; j++)
            {
                var (st, owner) = owned[j];
                var col = j % cols; var row = j / cols;
                cards.Add(new Card
                {
                    Kind = "state", Key = $"{st.Id}#thumb", Type = NodeType.State, Id = st.Id,
                    X = x0 + col * (tsz.W + 44), Y = Spread(row, rowsN, tsz.H + 16), Z = flat ? 0 : 20 + col * 10, W = tsz.W, H = tsz.H,
                    ScreenId = screenId, Size = PreviewFrame.Sm, OwnerName = owner?.Name ?? "", HighlightId = owner?.Id, RotY = flat ? 0 : -6,
                });
            }
        }

        if (lens == Lens.Structure)
        {
            var insts = g.InstancesOfScreen(screenId);
            var defs = new Dictionary<string, List<Node>>();
            var defOrder = new List<string>();
            foreach (var at in insts)
            {
                var def = g.DefinitionOf(at.Node.Id);
                if (def == null) continue;
                if (!defs.TryGetValue(def.Id, out var list)) { defs[def.Id] = list = new(); defOrder.Add(def.Id); }
                list.Add(at.Node);
            }
            for (var i = 0; i < defOrder.Count; i++)
            {
                var defId = defOrder[i];
                var def = g.Node(defId)!;
                var uses = g.ScreensUsingDefinition(defId);
                cards.Add(new Card
                {
                    Kind = "chip", Key = defId, Type = NodeType.Component, Id = defId,
                    X = Spread(i, defOrder.Count, 230), Y = sz.H / 2 + 60, Z = flat ? 0 : 80, W = ChipW + 40, H = ChipH,
                    Title = def.Name, Sub = $"{uses.Count} uses · {string.Join(", ", uses.Select(u => u.Screen.Name))}",
                });
                foreach (var inst in defs[defId]) links.Add(L($"{front}/{inst.Id}:c", $"{defId}:t", Relation.InstanceOf, "instance of"));
            }
            r.Note = defOrder.Count == 0 ? $"{insts.Count} elements, all declared inline" : $"{insts.Count} elements · hover the preview to name them";
            return r;
        }
        return r;
    }

    // ------------------------------------------------------------------ component (instance or definition)
    private static LayoutResult LayoutComponent(GraphIndex g, string id, string lens, bool flat, bool docked)
    {
        var n = g.Node(id)!;
        if (n.Type == NodeType.Component) return LayoutDefinition(g, n, flat, docked);
        var r = new LayoutResult();
        var cards = r.Cards; var links = r.Links;
        var screen = g.HostScreenOf(id)!;
        var size = docked ? PreviewFrame.Md : PreviewFrame.Lg;
        var sz = PreviewCardSize(size);
        var front = screen.Id;
        cards.Add(new Card
        {
            Kind = "screen", Key = front, Type = NodeType.Screen, Id = screen.Id,
            X = docked ? 0 : -60, Y = 0, Z = 0, W = sz.W, H = sz.H, Size = size, Interactive = !docked, HighlightId = id, Recede = true,
        });
        if (docked) return r;
        var rx = sz.W / 2 + 160;
        var region = $"{front}/{id}";

        if (lens == Lens.Behavior)
        {
            var vm = g.VmOfScreen(screen.Id);
            var edges = g.OutEdges(id).Where(e => e.Relation is Relation.BindsTo or Relation.Invokes).ToList();
            if (vm == null || edges.Count == 0) { r.Note = "No bindings or commands declared on this element"; return r; }
            var members = g.MembersOfVm(vm.Id);
            var hot = edges.Select(e => e.To).ToHashSet();
            var b = n.Preview?.Bounds;
            var hotMembers = members.Where(m => hot.Contains(m.Id)).ToList();
            var frac = new Dictionary<string, double>();
            for (var i = 0; i < hotMembers.Count; i++)
                frac[hotMembers[i].Id] = b != null ? b.Y + b.H / 2 + (i - (hot.Count - 1) / 2.0) * 0.08 : 0.5;
            var (rows, h) = AlignedRows(members, hot, frac, PreviewFrame.Of(size).H);
            cards.Add(new Card
            {
                Kind = "vm", Key = vm.Id, Type = NodeType.ViewModel, Id = vm.Id,
                X = rx + 40, Y = -sz.H / 2 + h / 2, Z = flat ? 0 : -200, W = VmW, H = h,
                Members = members, Rows = rows, Hot = hot, Faded = members.Where(m => !hot.Contains(m.Id)).Select(m => m.Id).ToHashSet(),
                Tag = g.ScreensUsingVm(vm.Id).Count > 1 ? "shared" : "",
            });
            for (var i = 0; i < edges.Count; i++)
            {
                var e = edges[i];
                links.Add(L($"{region}:r", $"{vm.Id}/{e.To}:l", e.Relation, e.Label ?? "", arrow: e.Relation == Relation.Invokes, inferred: e.IsInferred,
                    labelT: edges.Count == 1 ? 0.45 : 0.2 + 0.6 * ((double)i / (edges.Count - 1))));
            }
            // what the hot members depend on, as a second column
            var deps = new List<Edge>();
            foreach (var m in members.Where(m => hot.Contains(m.Id)))
                foreach (var e in g.OutEdges(m.Id, Relation.DependsOn)) deps.Add(e);
            var depY0 = -sz.H / 2 + (rows.FirstOrDefault(rw => rw.Hot)?.Y ?? 0) + Head + 8;
            for (var i = 0; i < deps.Count; i++)
            {
                var e = deps[i];
                var dep = g.Node(e.To)!;
                var key = $"{e.To}#dep";
                if (cards.Any(c => c.Key == key)) { links.Add(L($"{vm.Id}/{e.From}:r", $"{key}:l", Relation.DependsOn, e.Label ?? "", arrow: true, inferred: e.IsInferred)); continue; }
                cards.Add(new Card
                {
                    Kind = "chip", Key = key, Type = dep.Type, Id = dep.Id,
                    X = rx + 40 + VmW / 2 + 70 + (ChipW + 20) / 2, Y = depY0 + i * (ChipH + 10), Z = flat ? 0 : -200, W = ChipW + 20, H = ChipH,
                    Title = dep.Name, Sub = dep.Prop("clrType") ?? "", Mono = true,
                });
                links.Add(L($"{vm.Id}/{e.From}:r", $"{key}:l", Relation.DependsOn, e.Label ?? "", arrow: true, inferred: e.IsInferred));
            }
            r.Note = $"{edges.Count} binding{(edges.Count == 1 ? "" : "s")} on {n.Name}";
            return r;
        }

        if (lens == Lens.Navigation)
        {
            var routes = g.RoutesFromInstance(id);
            if (routes.Count == 0) { r.Note = "No navigation starts from this element"; return r; }
            for (var i = 0; i < routes.Count; i++)
            {
                var hop = routes[i];
                var key = $"{hop.Route.Id}#out";
                cards.Add(new Card
                {
                    Kind = "chip", Key = key, Type = NodeType.Screen, Id = hop.Target!.Id,
                    X = rx, Y = Spread(i, routes.Count, ChipH + 12), Z = 0, W = ChipW, H = ChipH,
                    Title = hop.Target.Name, Sub = hop.Route.Prop("mechanism")?.Split('.')[0] ?? "", RouteId = hop.Route.Id,
                });
                links.Add(L($"{region}:r", $"{key}:l", "route", hop.Origin.Edge.Label is { Length: > 0 } lbl ? lbl : hop.Origin.Via.Name, arrow: true, id: hop.Route.Id));
            }
            return r;
        }

        if (lens == Lens.Structure)
        {
            var def = g.DefinitionOf(id);
            var container = g.ContainerOf(id);
            if (def == null)
            {
                if (container != null)
                {
                    cards.Add(new Card
                    {
                        Kind = "chip", Key = $"{container.Id}#host", Type = container.Type, Id = container.Id,
                        X = rx, Y = -40, Z = 0, W = ChipW + 20, H = ChipH, Title = container.Name, Sub = "contains this element",
                    });
                    links.Add(L($"{container.Id}#host:l", $"{region}:r", Relation.Contains, "contains", arrow: true));
                }
                var file = g.FileLine(n.Source)?.Path.Split('/')[^1];
                r.Note = $"Declared inline in {(string.IsNullOrEmpty(file) ? "the page" : file)} · no shared definition";
                return r;
            }
            var siblings = g.InstancesOf(def.Id).Where(i => i.Id != id).ToList();
            cards.Add(new Card
            {
                Kind = "chip", Key = def.Id, Type = NodeType.Component, Id = def.Id,
                X = rx, Y = -120, Z = flat ? 0 : 60, W = ChipW + 40, H = ChipH, Title = def.Name, Sub = $"definition · {siblings.Count + 1} uses",
            });
            links.Add(L($"{region}:r", $"{def.Id}:l", Relation.InstanceOf, "instance of", arrow: true));
            for (var i = 0; i < siblings.Count; i++)
            {
                var s = siblings[i];
                var host = g.HostScreenOf(s.Id);
                var key = $"{s.Id}#sib";
                cards.Add(new Card
                {
                    Kind = "chip", Key = key, Type = NodeType.Instance, Id = s.Id,
                    X = rx + 20, Y = -20 + i * (ChipH + 10), Z = flat ? 0 : 60, W = ChipW + 40, H = ChipH, Title = s.Name, Sub = $"on {host?.Name}",
                });
                links.Add(L($"{def.Id}:b", $"{key}:t", Relation.InstanceOf));
            }
            r.Note = $"{def.Name} is also used on {string.Join(", ", siblings.Select(s => g.HostScreenOf(s.Id)?.Name))}";
            return r;
        }

        if (lens == Lens.States)
        {
            var all = g.StatesOf(id).Select(st => (St: st, Label: $"on {n.Name}")).ToList();
            foreach (var st in g.StatesOf(screen.Id))
                if ((st.Preview?.Dim ?? new()).Contains(id) || (st.Preview?.Hide ?? new()).Contains(id)) all.Add((st, $"screen state affects {n.Name}"));
            if (all.Count == 0) { r.Note = $"No declared states involve {n.Name}"; return r; }
            var tsz = PreviewCardSize(PreviewFrame.Sm);
            for (var j = 0; j < all.Count; j++)
            {
                var (st, label) = all[j];
                cards.Add(new Card
                {
                    Kind = "state", Key = $"{st.Id}#thumb", Type = NodeType.State, Id = st.Id,
                    X = rx - 40 + j * (tsz.W + 44), Y = 0, Z = flat ? 0 : 20 + j * 10, W = tsz.W, H = tsz.H,
                    ScreenId = screen.Id, Size = PreviewFrame.Sm, OwnerName = label, HighlightId = id, RotY = flat ? 0 : -6,
                });
                links.Add(L($"{region}:r", $"{st.Id}#thumb:l", Relation.HasState));
            }
            return r;
        }
        return r;
    }

    private static LayoutResult LayoutDefinition(GraphIndex g, Node def, bool flat, bool docked)
    {
        var r = new LayoutResult();
        var cards = r.Cards; var links = r.Links;
        var uses = g.ScreensUsingDefinition(def.Id);
        cards.Add(new Card { Kind = "detail", Key = def.Id, Type = NodeType.Component, Id = def.Id, X = 0, Y = docked ? 0 : -180, Z = flat ? 0 : 40, W = 320, H = 132 });
        if (docked) return r;
        var tsz = PreviewCardSize(PreviewFrame.Sm);
        for (var i = 0; i < uses.Count; i++)
        {
            var u = uses[i];
            var key = $"{u.Screen.Id}#use";
            cards.Add(new Card
            {
                Kind = "screen", Key = key, Type = NodeType.Screen, Id = u.Screen.Id,
                X = Spread(i, uses.Count, tsz.W + 40), Y = 60 + tsz.H / 2 - 60, Z = 0, W = tsz.W, H = tsz.H,
                Size = PreviewFrame.Sm, Interactive = true, HighlightId = u.Instance.Id, Recede = true, Caption = u.Instance.Name,
            });
            links.Add(L($"{def.Id}:b", $"{key}/{u.Instance.Id}:c", Relation.InstanceOf, "", arrow: true));
        }
        r.Note = $"{uses.Count} uses across {uses.Count} screen{(uses.Count == 1 ? "" : "s")}";
        return r;
    }

    // ------------------------------------------------------------------ detail (member, state, route, view model)
    private static LayoutResult LayoutDetail(GraphIndex g, string id, bool flat, bool docked)
    {
        var n = g.Node(id)!;
        return n.Type switch
        {
            NodeType.ViewModel => LayoutViewModel(g, n, flat, docked),
            NodeType.State => LayoutState(g, n, flat, docked),
            NodeType.Route => LayoutRoute(g, n, flat, docked),
            _ => LayoutMember(g, n, flat, docked),
        };
    }

    private static LayoutResult LayoutViewModel(GraphIndex g, Node vm, bool flat, bool docked)
    {
        var r = new LayoutResult();
        var cards = r.Cards; var links = r.Links;
        var members = g.MembersOfVm(vm.Id);
        var h = VmHeight(members.Count);
        cards.Add(new Card { Kind = "vm", Key = vm.Id, Type = NodeType.ViewModel, Id = vm.Id, X = 0, Y = 0, Z = 0, W = VmW + 30, H = h, Members = members, Expanded = true });
        if (docked) return r;
        var users = g.ScreensUsingVm(vm.Id);
        var tsz = PreviewCardSize(PreviewFrame.Sm);
        for (var i = 0; i < users.Count; i++)
        {
            var s = users[i];
            var key = $"{s.Id}#user";
            cards.Add(new Card
            {
                Kind = "screen", Key = key, Type = NodeType.Screen, Id = s.Id,
                X = -(VmW / 2 + 200), Y = Spread(i, users.Count, tsz.H + 16), Z = flat ? 0 : 60, W = tsz.W, H = tsz.H, Size = PreviewFrame.Sm, Interactive = false,
            });
            links.Add(L($"{key}:r", $"{vm.Id}:l", Relation.UsesViewModel, "uses", arrow: true));
        }
        r.Note = users.Count > 1 ? $"Shared: serves {string.Join(" and ", users.Select(s => s.Name))}" : "";
        return r;
    }

    private static LayoutResult LayoutMember(GraphIndex g, Node m, bool flat, bool docked)
    {
        var r = new LayoutResult();
        var cards = r.Cards; var links = r.Links;
        const double W = 320, H = 190;
        cards.Add(new Card { Kind = "detail", Key = m.Id, Type = m.Type, Id = m.Id, X = 0, Y = 0, Z = 0, W = W, H = H });
        if (docked) return r;
        var vm = g.VmOfMember(m.Id);
        if (vm != null)
        {
            cards.Add(new Card { Kind = "chip", Key = $"{vm.Id}#owner", Type = NodeType.ViewModel, Id = vm.Id, X = 40, Y = -(H / 2 + 80), Z = flat ? 0 : -160, W = ChipW + 30, H = ChipH, Title = vm.Name, Sub = "exposes this member" });
            links.Add(L($"{vm.Id}#owner:b", $"{m.Id}:t", Relation.Exposes, "", arrow: true));
        }
        // consumers: the UI that binds to or invokes the member, shown on their screens
        var consumers = g.ConsumersOfMember(m.Id);
        var byScreen = new Dictionary<string, List<MemberConsumer>>();
        var screenOrder = new List<string>();
        foreach (var c in consumers)
        {
            if (c.Screen == null) continue;
            if (!byScreen.TryGetValue(c.Screen.Id, out var list)) { byScreen[c.Screen.Id] = list = new(); screenOrder.Add(c.Screen.Id); }
            list.Add(c);
        }
        var tsz = PreviewCardSize(PreviewFrame.Sm);
        for (var i = 0; i < screenOrder.Count; i++)
        {
            var screenId = screenOrder[i];
            var cs = byScreen[screenId];
            var key = $"{screenId}#ctx";
            cards.Add(new Card
            {
                Kind = "screen", Key = key, Type = NodeType.Screen, Id = screenId,
                X = -(W / 2 + 200), Y = Spread(i, screenOrder.Count, tsz.H + 16), Z = 0, W = tsz.W, H = tsz.H,
                Size = PreviewFrame.Sm, Interactive = true, HighlightId = cs[0].Instance.Id, HighlightIds = cs.Select(c => c.Instance.Id).ToList(), Recede = true,
            });
            foreach (var c in cs) links.Add(L($"{key}/{c.Instance.Id}:r", $"{m.Id}:l", c.Edge.Relation, c.Edge.Label ?? "", arrow: c.Edge.Relation == Relation.Invokes, inferred: c.Edge.IsInferred));
        }
        // dependencies (what it reads) above right, dependents (what reads it) below right
        var deps = g.OutEdges(m.Id, Relation.DependsOn).ToList();
        var dependents = g.InEdges(m.Id, Relation.DependsOn).ToList();
        var routes = g.OutEdges(m.Id, Relation.EnteredVia).ToList();
        var transitions = g.OutEdges(m.Id, Relation.TransitionsTo).ToList();
        var rightCol = new List<(Edge E, string Dir, Node Node, string Head)>();
        rightCol.AddRange(deps.Select(e => (e, "out", g.Node(e.To)!, "reads")));
        rightCol.AddRange(transitions.Select(e => (e, "out", g.Node(e.To)!, "transitions to")));
        rightCol.AddRange(routes.Select(e => (e, "out", g.Node(e.To)!, "navigates")));
        rightCol.AddRange(dependents.Select(e => (e, "in", g.Node(e.From)!, "read by")));
        for (var i = 0; i < rightCol.Count; i++)
        {
            var item = rightCol[i];
            var key = $"{item.Node.Id}#{item.Dir}";
            if (cards.Any(c => c.Key == key)) continue;
            var route = item.Node.Type == NodeType.Route ? g.RouteTarget(item.Node.Id) : null;
            cards.Add(new Card
            {
                Kind = "chip", Key = key, Type = item.Node.Type, Id = item.Node.Id,
                X = W / 2 + 180, Y = Spread(i, rightCol.Count, ChipH + 10), Z = 0, W = ChipW + 30, H = ChipH,
                Title = route != null ? $"→ {route.Name}" : item.Node.Name, Sub = item.Head + (item.Node.Type == NodeType.State ? " · state" : ""),
                Mono = item.Node.Type is NodeType.Property or NodeType.Command,
            });
            if (item.Dir == "out") links.Add(L($"{m.Id}:r", $"{key}:l", item.E.Relation, item.E.Label ?? "", arrow: true, inferred: item.E.IsInferred, labelT: 0.35));
            else links.Add(L($"{key}:l", $"{m.Id}:r", item.E.Relation, item.E.Label ?? "", arrow: true, inferred: item.E.IsInferred, labelT: 0.65));
        }
        r.Note = consumers.Count > 0
            ? $"{consumers.Count} binding{(consumers.Count == 1 ? "" : "s")} · {deps.Count} dependenc{(deps.Count == 1 ? "y" : "ies")} · {dependents.Count} dependent{(dependents.Count == 1 ? "" : "s")}"
            : "Not bound by any UI element";
        return r;
    }

    private static LayoutResult LayoutState(GraphIndex g, Node st, bool flat, bool docked)
    {
        var r = new LayoutResult();
        var cards = r.Cards; var links = r.Links;
        var owner = g.OwnerOfState(st.Id);
        var screen = owner == null ? null : owner.Type == NodeType.Screen ? owner : g.HostScreenOf(owner.Id);
        var size = docked ? PreviewFrame.Sm : PreviewFrame.Md;
        var sz = PreviewCardSize(size);
        var key = $"{st.Id}#big";
        cards.Add(new Card
        {
            Kind = "state", Key = key, Type = NodeType.State, Id = st.Id, X = 0, Y = 0, Z = 0, W = sz.W, H = sz.H,
            ScreenId = screen?.Id, Size = size, OwnerName = owner?.Type == NodeType.Instance ? $"on {owner.Name}" : "",
            HighlightId = owner?.Type == NodeType.Instance ? owner.Id : null, Big = true,
        });
        if (docked) return r;
        var drivers = g.OutEdges(st.Id, Relation.DependsOn).ToList();
        for (var i = 0; i < drivers.Count; i++)
        {
            var e = drivers[i];
            var d = g.Node(e.To)!;
            var ck = $"{d.Id}#driver";
            cards.Add(new Card
            {
                Kind = "chip", Key = ck, Type = d.Type, Id = d.Id,
                X = sz.W / 2 + 170, Y = Spread(i, drivers.Count, ChipH + 10) - 60, Z = flat ? 0 : -160, W = ChipW + 30, H = ChipH,
                Title = d.Name, Sub = $"{d.Prop("clrType") ?? ""}{(string.IsNullOrEmpty(e.Label) ? "" : " · " + e.Label)}", Mono = true,
            });
            links.Add(L($"{key}:r", $"{ck}:l", Relation.DependsOn, "driven by", arrow: true, inferred: e.IsInferred));
        }
        var ins = g.InEdges(st.Id, Relation.TransitionsTo).ToList();
        var outs = g.OutEdges(st.Id, Relation.TransitionsTo).ToList();
        for (var i = 0; i < ins.Count; i++)
        {
            var e = ins[i];
            var s = g.Node(e.From)!;
            var ck = $"{s.Id}#from";
            cards.Add(new Card
            {
                Kind = "chip", Key = ck, Type = s.Type, Id = s.Id,
                X = -(sz.W / 2 + 170), Y = Spread(i, ins.Count, ChipH + 10), Z = 0, W = ChipW + 20, H = ChipH,
                Title = s.Name, Sub = string.IsNullOrEmpty(e.Label) ? "transitions here" : e.Label,
            });
            links.Add(L($"{ck}:r", $"{key}:l", Relation.TransitionsTo, e.Label ?? "", arrow: true));
        }
        for (var i = 0; i < outs.Count; i++)
        {
            var e = outs[i];
            var s = g.Node(e.To)!;
            var ck = $"{s.Id}#to";
            cards.Add(new Card
            {
                Kind = "chip", Key = ck, Type = s.Type, Id = s.Id,
                X = sz.W / 2 + 170, Y = 60 + i * (ChipH + 10) + (drivers.Count > 0 ? 40 : 0), Z = 0, W = ChipW + 20, H = ChipH,
                Title = s.Name, Sub = string.IsNullOrEmpty(e.Label) ? "next" : e.Label,
            });
            links.Add(L($"{key}:r", $"{ck}:l", Relation.TransitionsTo, e.Label ?? "", arrow: true));
        }
        var trigger = st.Prop("trigger");
        r.Note = trigger != null ? $"when {trigger}" : "";
        return r;
    }

    private static LayoutResult LayoutRoute(GraphIndex g, Node route, bool flat, bool docked)
    {
        var r = new LayoutResult();
        var cards = r.Cards; var links = r.Links;
        cards.Add(new Card { Kind = "detail", Key = route.Id, Type = NodeType.Route, Id = route.Id, X = 0, Y = 0, Z = 0, W = 280, H = 130 });
        if (docked) return r;
        var tsz = PreviewCardSize(PreviewFrame.Sm);
        var origins = g.RouteOrigins(route.Id);
        for (var i = 0; i < origins.Count; i++)
        {
            var o = origins[i];
            var key = $"{o.Screen.Id}#origin";
            cards.Add(new Card
            {
                Kind = "screen", Key = key, Type = NodeType.Screen, Id = o.Screen.Id,
                X = -(140 + 160), Y = Spread(i, origins.Count, tsz.H + 16), Z = 0, W = tsz.W, H = tsz.H,
                Size = PreviewFrame.Sm, Interactive = true, HighlightId = o.Instance?.Id, Recede = o.Instance != null,
            });
            links.Add(L(o.Instance != null ? $"{key}/{o.Instance.Id}:r" : $"{key}:r", $"{route.Id}:l", "route", o.Edge.Label is { Length: > 0 } lbl ? lbl : o.Via.Name, arrow: true));
        }
        var target = g.RouteTarget(route.Id);
        if (target != null)
        {
            var key = $"{target.Id}#target";
            cards.Add(new Card { Kind = "screen", Key = key, Type = NodeType.Screen, Id = target.Id, X = 140 + 160, Y = 0, Z = 0, W = tsz.W, H = tsz.H, Size = PreviewFrame.Sm, Interactive = false });
            links.Add(L($"{route.Id}:r", $"{key}:l", "route", "navigates to", arrow: true));
        }
        r.Note = route.Prop("mechanism") ?? "";
        return r;
    }
}
