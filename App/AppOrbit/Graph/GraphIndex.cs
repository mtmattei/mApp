namespace AppOrbit.Graph;

/// <summary>graph.js indexGraph(): the raw graph plus byId, out/in edges and byType, read-only after load.</summary>
public sealed class GraphIndex
{
    public AppGraph Raw { get; }
    public IReadOnlyDictionary<string, Node> ById { get; }
    public IReadOnlyDictionary<string, GraphFile> Files { get; }

    private readonly Dictionary<string, List<Edge>> _out = new();
    private readonly Dictionary<string, List<Edge>> _in = new();
    private readonly Dictionary<string, List<Node>> _byType = new();
    private static readonly List<Edge> NoEdges = new();
    private static readonly List<Node> NoNodes = new();

    public GraphIndex(AppGraph raw)
    {
        Raw = raw;
        var byId = new Dictionary<string, Node>();
        foreach (var n in raw.Nodes) byId[n.Id] = n;
        ById = byId;
        foreach (var e in raw.Edges)
        {
            if (!_out.TryGetValue(e.From, out var o)) _out[e.From] = o = new();
            if (!_in.TryGetValue(e.To, out var i)) _in[e.To] = i = new();
            o.Add(e);
            i.Add(e);
        }
        foreach (var n in raw.Nodes)
        {
            if (!_byType.TryGetValue(n.Type, out var list)) _byType[n.Type] = list = new();
            list.Add(n);
        }
        var files = new Dictionary<string, GraphFile>();
        foreach (var f in raw.Files ?? new()) files[f.Id] = f;
        Files = files;
    }

    public Node? Node(string? id) => id != null && ById.TryGetValue(id, out var n) ? n : null;
    public IReadOnlyList<Node> NodesOf(string type) => _byType.TryGetValue(type, out var l) ? l : NoNodes;
    public IReadOnlyList<Edge> OutEdges(string id) => _out.TryGetValue(id, out var l) ? l : NoEdges;
    public IReadOnlyList<Edge> InEdges(string id) => _in.TryGetValue(id, out var l) ? l : NoEdges;
    public IEnumerable<Edge> OutEdges(string id, string relation) => OutEdges(id).Where(e => e.Relation == relation);
    public IEnumerable<Edge> InEdges(string id, string relation) => InEdges(id).Where(e => e.Relation == relation);
    public IEnumerable<Node> Targets(string id, string relation) => OutEdges(id, relation).Select(e => Node(e.To)).Where(n => n != null)!;
    public IEnumerable<Node> Sources(string id, string relation) => InEdges(id, relation).Select(e => Node(e.From)).Where(n => n != null)!;
}
