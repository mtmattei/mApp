namespace AppOrbit.Graph;

public sealed record RouteOrigin(Node Screen, Node Via, Edge Edge, Node? Instance);
public sealed record RouteHop(Node Route, RouteOrigin Origin, Node? Target);
public sealed record DefinitionUse(Node Screen, Node Instance);
public sealed record MemberConsumer(Edge Edge, Node Instance, Node? Screen);
public sealed record InstanceAt(Node Node, int Depth);
public sealed record FileLineRef(GraphFile File, string Text, string Path);

/// <summary>graph.js queries, as extension methods on the index. Order of results follows declaration order, as in the prototype.</summary>
public static class Queries
{
    // ---- structural helpers ----
    public static Node? FeatureOfScreen(this GraphIndex g, string screenId) => g.Targets(screenId, Relation.BelongsTo).FirstOrDefault();
    public static List<Node> ScreensOfFeature(this GraphIndex g, string featureId) => g.Sources(featureId, Relation.BelongsTo).ToList();
    public static Node? VmOfScreen(this GraphIndex g, string screenId) => g.Targets(screenId, Relation.UsesViewModel).FirstOrDefault();
    public static List<Node> ScreensUsingVm(this GraphIndex g, string? vmId) => vmId == null ? new() : g.Sources(vmId, Relation.UsesViewModel).ToList();
    public static List<Node> MembersOfVm(this GraphIndex g, string vmId) => g.Targets(vmId, Relation.Exposes).ToList();
    public static Node? VmOfMember(this GraphIndex g, string memberId) => g.Sources(memberId, Relation.Exposes).FirstOrDefault();
    public static Node? DefinitionOf(this GraphIndex g, string instId) => g.Targets(instId, Relation.InstanceOf).FirstOrDefault();
    public static List<Node> InstancesOf(this GraphIndex g, string defId) => g.Sources(defId, Relation.InstanceOf).ToList();
    public static List<Node> StatesOf(this GraphIndex g, string ownerId) => g.Targets(ownerId, Relation.HasState).ToList();
    public static Node? OwnerOfState(this GraphIndex g, string stateId) => g.Sources(stateId, Relation.HasState).FirstOrDefault();
    public static Node? ContainerOf(this GraphIndex g, string instId) => g.Sources(instId, Relation.Contains).FirstOrDefault();

    public static Node? HostScreenOf(this GraphIndex g, string id)
    {
        var cur = g.Node(id);
        var guard = 0;
        while (cur != null && cur.Type != NodeType.Screen && guard++ < 10) cur = g.ContainerOf(cur.Id);
        return cur is { Type: NodeType.Screen } ? cur : null;
    }

    /// <summary>All component instances in a screen, depth first, with their depth.</summary>
    public static List<InstanceAt> InstancesOfScreen(this GraphIndex g, string screenId)
    {
        var acc = new List<InstanceAt>();
        void Walk(string id, int depth)
        {
            foreach (var child in g.Targets(id, Relation.Contains))
            {
                acc.Add(new InstanceAt(child, depth));
                Walk(child.Id, depth + 1);
            }
        }
        Walk(screenId, 0);
        return acc;
    }

    /// <summary>Screens where a definition is used, through its instances (first instance per screen).</summary>
    public static List<DefinitionUse> ScreensUsingDefinition(this GraphIndex g, string defId)
    {
        var seen = new Dictionary<string, DefinitionUse>();
        var order = new List<string>();
        foreach (var inst in g.InstancesOf(defId))
        {
            var screen = g.HostScreenOf(inst.Id);
            if (screen != null && !seen.ContainsKey(screen.Id)) { seen[screen.Id] = new DefinitionUse(screen, inst); order.Add(screen.Id); }
        }
        return order.Select(id => seen[id]).ToList();
    }

    /// <summary>Instances that bind to or invoke a member, with their host screens.</summary>
    public static List<MemberConsumer> ConsumersOfMember(this GraphIndex g, string memberId) =>
        g.InEdges(memberId)
            .Where(e => e.Relation is Relation.BindsTo or Relation.Invokes)
            .Select(e => new MemberConsumer(e, g.Node(e.From)!, g.HostScreenOf(e.From)))
            .Where(c => c.Instance != null)
            .ToList();

    // ---- routes ----
    public static List<RouteOrigin> RouteOrigins(this GraphIndex g, string routeId)
    {
        var origins = new List<RouteOrigin?>();
        foreach (var e in g.InEdges(routeId, Relation.EnteredVia))
        {
            var via = g.Node(e.From);
            if (via == null) continue;
            if (via.Type == NodeType.Command)
            {
                var invokers = g.Sources(via.Id, Relation.Invokes).ToList();
                if (invokers.Count == 0)
                {
                    foreach (var s in g.ScreensUsingVm(g.VmOfMember(via.Id)?.Id)) origins.Add(new RouteOrigin(s, via, e, null));
                }
                foreach (var inst in invokers)
                {
                    var screen = g.HostScreenOf(inst.Id);
                    origins.Add(screen == null ? null : new RouteOrigin(screen, via, e, inst));
                }
            }
            else
            {
                var screen = g.HostScreenOf(via.Id) ?? (via.Type == NodeType.Screen ? via : null);
                origins.Add(screen == null ? null : new RouteOrigin(screen, via, e, via.Type == NodeType.Instance ? via : null));
            }
        }
        return origins.Where(o => o != null).ToList()!;
    }

    public static Node? RouteTarget(this GraphIndex g, string routeId) => g.Targets(routeId, Relation.NavigatesTo).FirstOrDefault();

    public static List<RouteHop> RoutesFrom(this GraphIndex g, string screenId)
    {
        var acc = new List<RouteHop>();
        foreach (var r in g.NodesOf(NodeType.Route))
            foreach (var o in g.RouteOrigins(r.Id))
                if (o.Screen.Id == screenId) acc.Add(new RouteHop(r, o, g.RouteTarget(r.Id)));
        return acc;
    }

    public static List<RouteHop> RoutesTo(this GraphIndex g, string screenId)
    {
        var acc = new List<RouteHop>();
        foreach (var r in g.NodesOf(NodeType.Route))
        {
            var t = g.RouteTarget(r.Id);
            if (t?.Id != screenId) continue;
            foreach (var o in g.RouteOrigins(r.Id)) acc.Add(new RouteHop(r, o, t));
        }
        return acc;
    }

    /// <summary>Routes an instance (or the command it invokes) starts.</summary>
    public static List<RouteHop> RoutesFromInstance(this GraphIndex g, string instId)
    {
        var acc = new List<RouteHop>();
        foreach (var r in g.NodesOf(NodeType.Route))
            foreach (var o in g.RouteOrigins(r.Id))
                if (o.Instance?.Id == instId) acc.Add(new RouteHop(r, o, g.RouteTarget(r.Id)));
        return acc;
    }

    // ---- levels and context ----
    public static string LevelOf(this GraphIndex g, string? id)
    {
        var n = g.Node(id);
        if (n == null) return Level.Application;
        return n.Type switch
        {
            NodeType.Feature => Level.Feature,
            NodeType.Screen => Level.Screen,
            NodeType.Component or NodeType.Instance => Level.Component,
            _ => Level.Detail,
        };
    }

    /// <summary>Parent for zoom-out. Prefers a parent present in the trail.</summary>
    public static Node? ParentOf(this GraphIndex g, string id, IReadOnlyList<string>? trail = null)
    {
        var n = g.Node(id);
        if (n == null) return null;
        trail ??= Array.Empty<string>();
        Node? Prefer(IEnumerable<Node?> candidates)
        {
            var list = candidates.Where(c => c != null).Cast<Node>().ToList();
            if (list.Count == 0) return null;
            for (var i = trail.Count - 1; i >= 0; i--)
            {
                var hit = list.FirstOrDefault(c => c.Id == trail[i]);
                if (hit != null) return hit;
            }
            return list[0];
        }
        switch (n.Type)
        {
            case NodeType.Feature: return null;
            case NodeType.Screen: return g.FeatureOfScreen(id);
            case NodeType.Instance: return g.ContainerOf(id);
            case NodeType.Component: return Prefer(g.ScreensUsingDefinition(id).Select(u => u.Instance));
            case NodeType.ViewModel: return Prefer(g.ScreensUsingVm(id));
            case NodeType.Property:
            case NodeType.Command:
            {
                var consumers = g.ConsumersOfMember(id).Select(c => (Node?)c.Instance).ToList();
                consumers.Add(g.VmOfMember(id));
                return Prefer(consumers);
            }
            case NodeType.State: return g.OwnerOfState(id);
            case NodeType.Route: return Prefer(g.RouteOrigins(id).Select(o => o.Instance ?? o.Screen));
            default: return null;
        }
    }

    /// <summary>Breadcrumb chain from the application down to id.</summary>
    public static List<Node> ContextChain(this GraphIndex g, string? id, IReadOnlyList<string>? trail = null)
    {
        var chain = new List<Node>();
        var cur = id;
        var guard = 0;
        while (cur != null && guard++ < 12)
        {
            var n = g.Node(cur);
            if (n == null) break;
            chain.Insert(0, n);
            cur = g.ParentOf(cur, trail)?.Id;
        }
        return chain;
    }

    /// <summary>Screen that gives spatial context to an entity (for previews at detail level).</summary>
    public static Node? ContextScreen(this GraphIndex g, string id, IReadOnlyList<string>? trail = null) =>
        g.ContextChain(id, trail).FirstOrDefault(n => n.Type == NodeType.Screen);

    // ---- search ----
    public static List<Node> Search(this GraphIndex g, string query, int limit = 12)
    {
        var q = query.Trim().ToLowerInvariant();
        if (q.Length == 0) return new();
        int? Score(Node n)
        {
            var name = n.Name.ToLowerInvariant();
            if (name == q) return 0;
            if (name.StartsWith(q, StringComparison.Ordinal)) return 1;
            if (name.Contains(q, StringComparison.Ordinal)) return 2;
            if (n.Id.Contains(q, StringComparison.Ordinal)) return 3;
            if ((n.Summary ?? "").ToLowerInvariant().Contains(q, StringComparison.Ordinal)) return 4;
            return null;
        }
        return g.Raw.Nodes
            .Select(n => (n, s: Score(n)))
            .Where(x => x.s != null)
            .OrderBy(x => x.s).ThenBy(x => x.n.Name, StringComparer.Ordinal)
            .Take(limit)
            .Select(x => x.n)
            .ToList();
    }

    public static FileLineRef? FileLine(this GraphIndex g, SourceRef? r)
    {
        if (r == null || !g.Files.TryGetValue(r.File, out var f)) return null;
        var i = r.Line - f.StartLine;
        var text = i >= 0 && i < f.Lines.Count ? f.Lines[i] : "";
        return new FileLineRef(f, text, f.Path);
    }
}

public static class Level
{
    public const string Application = "application";
    public const string Feature = "feature";
    public const string Screen = "screen";
    public const string Component = "component";
    public const string Detail = "detail";
}
