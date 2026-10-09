using AppOrbit.Graph;
using AppOrbit.State;
using Microsoft.UI.Xaml.Media;

namespace AppOrbit;

/// <summary>inspector.js: what the focused entity is, its declared facts, what it connects to, and the evidence.</summary>
public sealed partial class ShellPage
{
    private static readonly Dictionary<string, (string Out, string In)> RelWords = new()
    {
        ["belongs-to"] = ("belongs to", "contains screen"), ["contains"] = ("contains", "contained by"), ["instance-of"] = ("instance of", "has instance"),
        ["uses-viewmodel"] = ("uses view model", "used by screen"), ["exposes"] = ("exposes", "exposed by"), ["binds-to"] = ("binds to", "bound by"),
        ["invokes"] = ("invokes", "invoked by"), ["navigates-to"] = ("navigates to", "reached by route"), ["entered-via"] = ("starts route", "started by"),
        ["has-state"] = ("has state", "state of"), ["transitions-to"] = ("transitions to", "transitions from"), ["depends-on"] = ("depends on", "read by"),
    };

    private static readonly Dictionary<string, string> FactLabels = new()
    {
        ["uno.type"] = "control", ["uno.class"] = "class", ["uno.xName"] = "x:Name", ["uno.styleKey"] = "style", ["uno.pattern"] = "pattern", ["uno.model"] = "model",
        ["uno.mechanism"] = "mechanism", ["uno.member"] = "member", ["uno.property"] = "property", ["uno.data"] = "data", ["clrType"] = "type", ["isEntry"] = "entry",
        ["dependencyProperties"] = "properties", ["expression"] = "expression", ["default"] = "default", ["writable"] = "writable", ["parameter"] = "parameter", ["async"] = "async",
        ["route"] = "route", ["request"] = "request", ["mechanism"] = "mechanism", ["qualifier"] = "qualifier", ["data"] = "data", ["trigger"] = "when", ["duration"] = "duration", ["origin"] = "origin", ["parts"] = "parts",
    };

    private Brush B(string key) => Res<Brush>(key);
    private Style S(string key) => Res<Style>(key);

    private TextBlock Text(string text, string style, string? brush = null, bool wrap = false, bool mono = false)
    {
        var tb = new TextBlock { Text = text, Style = S(style), TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap };
        if (brush != null) tb.Foreground = B(brush);
        if (mono) tb.FontFamily = Res<FontFamily>("Font.Mono");
        return tb;
    }

    private UIElement Section(string title, int? count = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 24, 0, 8), BorderBrush = B("Rule2"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 0, 4) };
        row.Children.Add(Text(title, "T12"));
        ((TextBlock)row.Children[0]).FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        if (count != null) row.Children.Add(Text(count.ToString()!, "T12", "Ink3"));
        return row;
    }

    private Button SrcLink(string text, Action onClick, string? tooltip = null)
    {
        var b = new Button { Content = new TextBlock { Text = text, FontFamily = Res<FontFamily>("Font.Mono"), FontSize = 11, Foreground = B("Focus"), TextDecorations = Windows.UI.Text.TextDecorations.Underline }, Style = S("LinkButton") };
        b.Click += (_, _) => onClick();
        if (tooltip != null) ToolTipService.SetToolTip(b, tooltip);
        AutomationProperties.SetName(b, text);
        return b;
    }

    private Border TagChip(string text, string brush, bool dashed = false)
    {
        var border = new Border { BorderBrush = B(brush), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 0, 5, 0), Height = 16, VerticalAlignment = VerticalAlignment.Center };
        var tb = Text(text, "T11", brush);
        if (dashed) tb.FontStyle = Windows.UI.Text.FontStyle.Italic;
        tb.VerticalAlignment = VerticalAlignment.Center;
        border.Child = tb;
        return border;
    }

    private void RenderInspector(AppState s)
    {
        var root = Inspector;
        root.Children.Clear();
        if (s.FocusId == null) { RenderApplicationInspector(s); return; }
        var n = G.Node(s.FocusId);
        if (n == null) return;

        // identity
        var eyebrow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        eyebrow.Children.Add(Glyph(n.Type, Res<Brush>(HueKey(n.Type))));
        eyebrow.Children.Add(Text(NodeType.Label(n.Type), "T11", "Ink3"));
        root.Children.Add(eyebrow);
        root.Children.Add(Text(n.Name, "T20", wrap: true));
        if (n.Summary != null) { var p = Text(n.Summary, "T12", "Ink2", wrap: true); p.Margin = new Thickness(0, 8, 0, 0); p.MaxWidth = 380; root.Children.Add(p); }
        root.Children.Add(SourceRow(n.Source, s.WorkspaceRoot));

        // declared
        var facts = Flatten.Facts(n.Properties);
        var hasValue = n.Type is NodeType.Property or NodeType.Command or NodeType.State;
        if (facts.Count > 0 || hasValue)
        {
            root.Children.Add(Section("Declared"));
            var kv = KvGrid();
            foreach (var (k, v) in facts) KvRow(kv, FactLabels.GetValueOrDefault(k, k), v, "Ink");
            if (hasValue) KvRow(kv, "live value", s.RuntimeConnected ? "connected" : "unavailable · no running app connected", s.RuntimeConnected ? "Ink" : "Ink3");
            root.Children.Add(kv);
        }

        // relationships
        var outs = G.OutEdges(n.Id); var ins = G.InEdges(n.Id);
        var contextScreen = G.ContextScreen(n.Id, s.Trail);
        root.Children.Add(Section("Relationships", outs.Count + ins.Count));
        if (outs.Count + ins.Count == 0) root.Children.Add(Text("None declared.", "T12", "Ink3"));
        var groups = new List<(string Key, List<(Edge E, Node? Other)> Items)>();
        void Push(string key, Edge e, Node? other)
        {
            var g = groups.FirstOrDefault(x => x.Key == key);
            if (g.Items == null) { g = (key, new()); groups.Add(g); }
            g.Items.Add((e, other));
        }
        foreach (var e in outs) Push($"{e.Relation}|out", e, G.Node(e.To));
        foreach (var e in ins) Push($"{e.Relation}|in", e, G.Node(e.From));
        foreach (var (key, items) in groups)
        {
            var parts = key.Split('|');
            var rel = parts[0]; var dir = parts[1];
            var grp = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 6, 0, 2) };
            var words = RelWords.TryGetValue(rel, out var w) ? (dir == "out" ? w.Out : w.In) : rel;
            nameRow.Children.Add(Text(words, "Mono11", "Ink3"));
            if (items.Count > 1) nameRow.Children.Add(Text(items.Count.ToString(), "Mono11", "Ink3"));
            grp.Children.Add(nameRow);
            foreach (var (e, other) in items)
            {
                if (other == null) continue;
                grp.Children.Add(RelRow(other, e, ContextOf(other, contextScreen)));
            }
            root.Children.Add(grp);
        }

        // evidence
        root.Children.Add(Section("Evidence"));
        root.Children.Add(EvidenceBox(n.Evidence, n.Source, null));
        foreach (var e in outs.Concat(ins).Where(x => x.IsInferred))
            root.Children.Add(EvidenceBox(e.Evidence, e.Evidence.Source, $"{G.Node(e.From)?.Name} → {e.Relation} → {G.Node(e.To)?.Name}"));
    }

    internal static string HueKey(string? type) => type switch
    {
        NodeType.Screen => "Hue.Screen", NodeType.Feature => "Hue.Feature",
        NodeType.ViewModel or NodeType.Property or NodeType.Command => "Hue.Vm",
        NodeType.State => "Hue.State", NodeType.Route => "Hue.Route",
        NodeType.Component or NodeType.Instance => "Hue.Comp", _ => "Ink2",
    };

    private Grid KvGrid()
    {
        var kv = new Grid { ColumnSpacing = 10, RowSpacing = 4 };
        kv.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        kv.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return kv;
    }

    private void KvRow(Grid kv, string k, string v, string valueBrush)
    {
        var row = kv.RowDefinitions.Count;
        kv.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var kt = Text(k, "T12", "Ink3"); Grid.SetRow(kt, row); kv.Children.Add(kt);
        var vt = Text(v, "Mono11", valueBrush, wrap: true); Grid.SetRow(vt, row); Grid.SetColumn(vt, 1); kv.Children.Add(vt);
    }

    private Button RelRow(Node other, Edge? e, string ctx)
    {
        var b = new Button { Style = S("RowButton") };
        var grid = new Grid { ColumnSpacing = 4 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var glyph = Glyph(other.Type, Res<Brush>(HueKey(other.Type)));
        glyph.VerticalAlignment = VerticalAlignment.Top; glyph.Margin = new Thickness(0, 2, 0, 0);
        grid.Children.Add(glyph);
        var main = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        main.Children.Add(Text(other.Name, "T12"));
        if (e?.Label is { Length: > 0 } lbl) main.Children.Add(Text(lbl, "Mono11", "Ink3"));
        if (e?.IsInferred == true) main.Children.Add(TagChip($"inferred {Math.Round(e.Evidence.Confidence * 100)}%", "Ink3", dashed: true));
        Grid.SetColumn(main, 1); grid.Children.Add(main);
        var ctxT = Text(ctx, "T11", "Ink3"); ctxT.Margin = new Thickness(0, 2, 0, 0); Grid.SetColumn(ctxT, 2); grid.Children.Add(ctxT);
        b.Content = grid;
        b.Click += (_, _) => Focus(other.Id);
        b.PointerEntered += (_, _) => Hover(other.Id);
        b.PointerExited += (_, _) => Hover(null);
        AutomationProperties.SetName(b, $"{NodeType.Label(other.Type)} {other.Name}");
        return b;
    }

    /// <summary>Context worth showing beside a related entity: only when the row does not already imply it.</summary>
    private string ContextOf(Node other, Node? contextScreen)
    {
        switch (other.Type)
        {
            case NodeType.Instance:
            {
                var host = G.HostScreenOf(other.Id);
                return host != null && host.Id != contextScreen?.Id ? host.Name : "";
            }
            case NodeType.Property:
            case NodeType.Command:
                return G.VmOfMember(other.Id)?.Name ?? "";
            case NodeType.Screen:
                return G.FeatureOfScreen(other.Id)?.Name ?? "";
            case NodeType.State:
            {
                var owner = G.OwnerOfState(other.Id);
                return owner != null && owner.Id != contextScreen?.Id ? owner.Name : "";
            }
            default: return "";
        }
    }

    private UIElement SourceRow(SourceRef? r, string workspaceRoot)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 12, 0, 0) };
        var fl = G.FileLine(r);
        if (fl == null || r == null) { row.Children.Add(Text("No source location", "T12", "Ink3")); return row; }
        row.Children.Add(SrcLink($"{fl.Path}:{r.Line}", () => OpenSource(r), "Show in the editor"));
        if (!string.IsNullOrEmpty(workspaceRoot))
        {
            var sep = workspaceRoot.Contains('\\') ? "\\" : "/";
            var full = workspaceRoot.TrimEnd('\\', '/') + sep + string.Join(sep, fl.Path.Split('/'));
            var uri = new Uri($"vscode://file/{full}:{r.Line}");
            row.Children.Add(SrcLink("open in VS Code", () => _ = Windows.System.Launcher.LaunchUriAsync(uri), full));
        }
        return row;
    }

    private UIElement EvidenceBox(Evidence? ev, SourceRef? source, string? edgeText)
    {
        var box = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        box.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        box.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var dot = new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 6, Height = 6, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(5, 5, 0, 0) };
        if (ev?.Kind == "inferred") { dot.Stroke = B("Ink3"); dot.StrokeThickness = 1; dot.StrokeDashArray = new DoubleCollection { 2, 2 }; }
        else dot.Fill = B("Ok");
        box.Children.Add(dot);
        var body = new StackPanel { Spacing = 2 };
        Grid.SetColumn(body, 1); box.Children.Add(body);
        if (ev == null) { body.Children.Add(Text("No evidence recorded.", "T12", "Ink3")); return box; }
        if (edgeText != null) body.Children.Add(Text(edgeText, "Mono11", "Ink2"));
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        head.Children.Add(TagChip(ev.Kind, ev.Kind == "observed" ? "Ok" : "Ink3", ev.Kind == "inferred"));
        head.Children.Add(Text($"{Math.Round(ev.Confidence * 100)}% confidence", "T12", "Ink2"));
        body.Children.Add(head);
        if (ev.Rationale != null) { var rt = Text(ev.Rationale, "T12", "Ink2", wrap: true); rt.MaxWidth = 380; body.Children.Add(rt); }
        var r = ev.Source ?? source;
        var fl = G.FileLine(r);
        if (fl != null && r != null)
        {
            body.Children.Add(SrcLink($"{fl.Path}:{r.Line}", () => OpenSource(r), "Show in the editor"));
            var first = r.Line - fl.File.StartLine;
            var last = Math.Min(first + 3, (r.EndLine ?? r.Line) - fl.File.StartLine);
            var lines = fl.File.Lines.Skip(Math.Max(0, first)).Take(Math.Max(0, last - first + 1)).ToList();
            var pre = new Border { Background = B("Paper2"), CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 4, 0, 0) };
            pre.Child = Text(string.Join("\n", Dedent(lines)), "Mono11", "Ink2", wrap: true);
            body.Children.Add(pre);
        }
        return box;
    }

    private static List<string> Dedent(List<string> lines)
    {
        var indents = lines.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart().Length).ToList();
        var min = indents.Count > 0 ? indents.Min() : 0;
        return lines.Select(l => l.Length >= min ? l[min..] : l).ToList();
    }

    private void RenderApplicationInspector(AppState s)
    {
        var root = Inspector;
        var r = G.Raw;
        var eyebrow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        eyebrow.Children.Add(Glyph("app", B("Ink2")));
        eyebrow.Children.Add(Text("Application", "T11", "Ink3"));
        root.Children.Add(eyebrow);
        root.Children.Add(Text(r.Name, "T20"));
        root.Children.Add(FigureHost());
        if (r.Description != null) { var p = Text(r.Description, "T12", "Ink2", wrap: true); p.Margin = new Thickness(0, 8, 0, 0); p.MaxWidth = 380; root.Children.Add(p); }
        var entry = G.Node(r.Entry);
        if (entry != null)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            row.Children.Add(SrcLink($"Entry: {entry.Name}", () => Focus(entry.Id)));
            root.Children.Add(row);
        }
        root.Children.Add(Section("Graph"));
        var stats = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var wrap = new Microsoft.UI.Xaml.Controls.ItemsControl();
        var statsText = new List<string>();
        foreach (var type in NodeType.All)
        {
            var c = G.NodesOf(type).Count;
            if (c == 0) continue;
            var (one, many) = NodeType.Plural(type);
            statsText.Add($"{c} {(c == 1 ? one : many)}");
        }
        statsText.Add($"{r.Edges.Count} relationships");
        var statsBlock = Text(string.Join("  ·  ", statsText), "T12", "Ink2", wrap: true);
        root.Children.Add(statsBlock);
        var inferred = r.Edges.Count(e => e.IsInferred);
        var q = Text($"{inferred} relationship{(inferred == 1 ? " is" : "s are")} inferred and marked as such. Everything else is declared in source.", "T12", "Ink3", wrap: true);
        q.Margin = new Thickness(0, 8, 0, 0); q.MaxWidth = 380;
        root.Children.Add(q);
        root.Children.Add(Section("Features", G.NodesOf(NodeType.Feature).Count));
        foreach (var f in G.NodesOf(NodeType.Feature))
        {
            var n = G.ScreensOfFeature(f.Id).Count;
            root.Children.Add(RelRow(f, null, $"{n} screen{(n == 1 ? "" : "s")}"));
        }
        if (r.Unresolved is { Count: > 0 })
        {
            root.Children.Add(Section("Unresolved", r.Unresolved.Count));
            foreach (var u in r.Unresolved) { var t = Text("• " + u, "T12", "Ink2", wrap: true); t.Margin = new Thickness(20, 0, 0, 4); root.Children.Add(t); }
        }
    }
}
