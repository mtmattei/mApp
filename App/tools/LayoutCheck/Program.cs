using System.Text.Json;
using AppOrbit.Graph;
using AppOrbit.Layout;
using AppOrbit.State;

// Recompute every fixture case with the C# layout and report differences.
//   dotnet run --project tools/LayoutCheck -- [path/to/orderly.graph.json] [path/to/layouts.json]

var root = AppContext.BaseDirectory;
while (root != null && !File.Exists(Path.Combine(root, "AppOrbit.sln"))) root = Path.GetDirectoryName(root);
root ??= Directory.GetCurrentDirectory();
var graphPath = args.Length > 0 ? args[0] : Path.Combine(root, "..", "graph", "orderly.graph.json");
var fixturePath = args.Length > 1 ? args[1] : Path.Combine(root, "tools", "fixtures", "layouts.json");

var g = new GraphIndex(AppGraph.Parse(File.ReadAllText(graphPath)));
using var doc = JsonDocument.Parse(File.ReadAllText(fixturePath));
var fx = doc.RootElement;
Console.WriteLine($"graph {g.Raw.GraphId}: {g.Raw.Nodes.Count} nodes, {g.Raw.Edges.Count} edges (fixture: {fx.GetProperty("nodes").GetInt32()}, {fx.GetProperty("edges").GetInt32()})");

var diffs = new List<string>();
int cases = 0, cards = 0, links = 0;
static double R3(double v) => Math.Round(v, 3);
static string? S(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind != JsonValueKind.Null ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText()) : null;
static double N(JsonElement e, string k) => e.GetProperty(k).GetDouble();
static bool B(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.True;
static double? NN(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
static List<string>? SL(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : null;
static bool Eq(double a, double b) => Math.Abs(a - b) < 0.0015;
static string Str(string? s) => s ?? "";

foreach (var c in fx.GetProperty("cases").EnumerateArray())
{
    cases++;
    var focusId = S(c, "focusId");
    var state = AppState.Initial(lens: S(c, "lens"), mode: S(c, "mode"), view: S(c, "view")) with { FocusId = focusId };
    var tag = $"[{focusId ?? "app"} · {state.Lens} · {state.View} · {state.Mode}]";
    LayoutResult l;
    try { l = LayoutEngine.Compute(g, state); }
    catch (Exception ex) { diffs.Add($"{tag} threw {ex.GetType().Name}: {ex.Message}"); continue; }

    void D(string what, object? want, object? got) { if (!Equals(Str(want?.ToString()), Str(got?.ToString()))) diffs.Add($"{tag} {what}: expected '{want}', got '{got}'"); }
    void DN(string what, double want, double got) { if (!Eq(want, got)) diffs.Add($"{tag} {what}: expected {want}, got {R3(got)}"); }

    D("level", S(c, "level"), l.Level);
    D("note", S(c, "note"), l.Note);
    var b = c.GetProperty("bounds");
    DN("bounds.x", N(b, "x"), l.Bounds.X); DN("bounds.y", N(b, "y"), l.Bounds.Y); DN("bounds.w", N(b, "w"), l.Bounds.W); DN("bounds.h", N(b, "h"), l.Bounds.H);

    var fcards = c.GetProperty("cards").EnumerateArray().ToList();
    if (fcards.Count != l.Cards.Count) diffs.Add($"{tag} cards: expected {fcards.Count}, got {l.Cards.Count} ({string.Join(", ", l.Cards.Select(x => x.Key))})");
    for (var i = 0; i < Math.Min(fcards.Count, l.Cards.Count); i++)
    {
        var e = fcards[i]; var k = l.Cards[i]; cards++;
        var ct = $"card {i} {S(e, "key")}";
        D($"{ct} kind", S(e, "kind"), k.Kind); D($"{ct} key", S(e, "key"), k.Key); D($"{ct} type", S(e, "type"), k.Type); D($"{ct} id", S(e, "id"), k.Id);
        DN($"{ct} x", N(e, "x"), k.X); DN($"{ct} y", N(e, "y"), k.Y); DN($"{ct} z", N(e, "z"), k.Z); DN($"{ct} w", N(e, "w"), k.W); DN($"{ct} h", N(e, "h"), k.H); DN($"{ct} rotY", N(e, "rotY"), k.RotY);
        D($"{ct} size", S(e, "size"), k.Size); D($"{ct} interactive", B(e, "interactive"), k.Interactive); D($"{ct} isEntry", B(e, "isEntry"), k.IsEntry);
        D($"{ct} highlightId", S(e, "highlightId"), k.HighlightId); D($"{ct} highlightIds", SL(e, "highlightIds") is { } hl ? string.Join(",", hl) : null, k.HighlightIds is { } kh ? string.Join(",", kh) : null);
        D($"{ct} recede", B(e, "recede"), k.Recede); D($"{ct} caption", S(e, "caption"), k.Caption); D($"{ct} screenId", S(e, "screenId"), k.ScreenId);
        D($"{ct} ownerName", S(e, "ownerName"), k.OwnerName); D($"{ct} big", B(e, "big"), k.Big); D($"{ct} title", S(e, "title"), k.Title); D($"{ct} sub", S(e, "sub"), k.Sub);
        D($"{ct} tag", S(e, "tag"), k.Tag); D($"{ct} mono", B(e, "mono"), k.Mono); D($"{ct} routeId", S(e, "routeId"), k.RouteId);
        D($"{ct} members", SL(e, "members") is { } ml ? string.Join(",", ml) : null, k.Members is { } km ? string.Join(",", km.Select(m => m.Id)) : null);
        D($"{ct} hot", string.Join(",", SL(e, "hot") ?? new()), string.Join(",", k.Hot.OrderBy(x => x, StringComparer.Ordinal)));
        D($"{ct} faded", string.Join(",", SL(e, "faded") ?? new()), string.Join(",", k.Faded.OrderBy(x => x, StringComparer.Ordinal)));
        D($"{ct} more", N(e, "more"), k.More); D($"{ct} expanded", B(e, "expanded"), k.Expanded); D($"{ct} showStateCounts", B(e, "showStateCounts"), k.ShowStateCounts);
        if (e.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
        {
            var want = rows.EnumerateArray().Select(rw => $"{S(rw, "id")}@{N(rw, "y")}{(B(rw, "hot") ? "*" : "")}").ToList();
            var got = (k.Rows ?? new()).Select(rw => $"{rw.Id}@{R3(rw.Y)}{(rw.Hot ? "*" : "")}").ToList();
            D($"{ct} rows", string.Join(" ", want), string.Join(" ", got));
        }
        else if (k.Rows != null) diffs.Add($"{tag} {ct} rows: expected none");
        if (e.TryGetProperty("screens", out var scr) && scr.ValueKind == JsonValueKind.Array)
        {
            var want = scr.EnumerateArray().Select(s => $"{S(s, "id")}:{N(s, "stateCount")}{(B(s, "isEntry") ? "*" : "")}").ToList();
            var got = (k.Screens ?? new()).Select(s => $"{s.Screen.Id}:{s.StateCount}{(s.IsEntry ? "*" : "")}").ToList();
            D($"{ct} screens", string.Join(" ", want), string.Join(" ", got));
        }
    }
    var flinks = c.GetProperty("links").EnumerateArray().ToList();
    if (flinks.Count != l.Links.Count) diffs.Add($"{tag} links: expected {flinks.Count}, got {l.Links.Count}");
    for (var i = 0; i < Math.Min(flinks.Count, l.Links.Count); i++)
    {
        var e = flinks[i]; var k = l.Links[i]; links++;
        var lt = $"link {i} {S(e, "from")}→{S(e, "to")}";
        D($"{lt} from", S(e, "from"), k.From); D($"{lt} to", S(e, "to"), k.To); D($"{lt} relation", S(e, "relation"), k.Relation); D($"{lt} label", S(e, "label"), k.Label);
        D($"{lt} arrow", B(e, "arrow"), k.Arrow); D($"{lt} id", S(e, "id"), k.Id); D($"{lt} inferred", B(e, "inferred"), k.Inferred);
        var wt = NN(e, "labelT"); if ((wt == null) != (k.LabelT == null) || (wt != null && !Eq(wt.Value, k.LabelT!.Value))) diffs.Add($"{tag} {lt} labelT: expected {wt}, got {k.LabelT}");
        var wd = NN(e, "labelDy"); if ((wd == null) != (k.LabelDy == null) || (wd != null && !Eq(wd.Value, k.LabelDy!.Value))) diffs.Add($"{tag} {lt} labelDy: expected {wd}, got {k.LabelDy}");
    }
}

foreach (var d in diffs.Take(60)) Console.WriteLine("DIFF " + d);
Console.WriteLine(diffs.Count == 0
    ? $"OK layout parity: {cases} cases, {cards} cards, {links} links match the prototype"
    : $"{diffs.Count} difference(s) across {cases} cases");
return diffs.Count == 0 ? 0 : 1;
