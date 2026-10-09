using System.Text.Json;
using System.Text.Json.Serialization;

namespace AppOrbit.Graph;

/// <summary>The App Graph file (graph/app-graph.schema.json v0.1), as records. Strings for enums so the ids match the prototype one for one.</summary>
public sealed record AppGraph(
    string SchemaVersion,
    string GraphId,
    string Name,
    string? Description,
    string Entry,
    List<GraphFile>? Files,
    List<Node> Nodes,
    List<Edge> Edges,
    List<string>? Unresolved)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static AppGraph Parse(string json) =>
        JsonSerializer.Deserialize<AppGraph>(json, Json) ?? throw new InvalidDataException("The graph file is empty.");
}

public sealed record GraphFile(string Id, string Path, string Language, int StartLine, List<string> Lines)
{
    public string FileName => Path.Split('/')[^1];
}

public sealed record SourceRef(string File, int Line, int? EndLine);

public sealed record Evidence(string Kind, double Confidence, SourceRef? Source, string? Rationale);

public sealed record Rect(double X, double Y, double W, double H);

public sealed record PreviewPart(string Kind, Rect Rect, string? Label, int? Rows, List<string>? Items, string? Text, string? Emphasis);

public sealed record Preview(List<PreviewPart>? Parts, Rect? Bounds, List<PreviewPart>? Overrides, List<string>? Dim, List<string>? Hide);

public sealed record Node(
    string Id,
    string Type,
    string Name,
    string? Summary,
    Evidence Evidence,
    SourceRef? Source,
    JsonElement? Properties,
    Preview? Preview)
{
    /// <summary>A declared fact by dotted path ("uno.type"), as text; arrays join with ", ".</summary>
    public string? Prop(string path)
    {
        if (Properties is not { ValueKind: JsonValueKind.Object } el) return null;
        foreach (var part in path.Split('.'))
        {
            if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(part, out el)) return null;
        }
        return Flatten.Text(el);
    }

    public bool PropBool(string path)
    {
        if (Properties is not { ValueKind: JsonValueKind.Object } el) return false;
        foreach (var part in path.Split('.'))
        {
            if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(part, out el)) return false;
        }
        return el.ValueKind == JsonValueKind.True;
    }

    public List<string> PropList(string path)
    {
        if (Properties is not { ValueKind: JsonValueKind.Object } el) return new();
        foreach (var part in path.Split('.'))
        {
            if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(part, out el)) return new();
        }
        return el.ValueKind == JsonValueKind.Array ? el.EnumerateArray().Select(Flatten.Text).ToList() : new();
    }
}

public sealed record Edge(string From, string Relation, string To, string? Label, Evidence Evidence)
{
    public bool IsInferred => Evidence.Kind == "inferred";
}

public static class NodeType
{
    public const string Feature = "feature";
    public const string Screen = "screen";
    public const string Component = "component";
    public const string Instance = "component-instance";
    public const string ViewModel = "viewmodel";
    public const string Property = "property";
    public const string Command = "command";
    public const string State = "state";
    public const string Route = "route";

    public static readonly string[] All = { Feature, Screen, Component, Instance, ViewModel, Property, Command, State, Route };

    public static string Label(string? type) => type switch
    {
        Feature => "Feature", Screen => "Screen", Component => "Component", Instance => "Instance",
        ViewModel => "View model", Property => "Property", Command => "Command", State => "State", Route => "Route",
        _ => "",
    };

    public static (string One, string Many) Plural(string type) => type switch
    {
        Feature => ("feature", "features"), Screen => ("screen", "screens"), Component => ("component", "components"),
        Instance => ("instance", "instances"), ViewModel => ("view model", "view models"), Property => ("property", "properties"),
        Command => ("command", "commands"), State => ("state", "states"), Route => ("route", "routes"), _ => (type, type + "s"),
    };
}

public static class Relation
{
    public const string BelongsTo = "belongs-to";
    public const string Contains = "contains";
    public const string InstanceOf = "instance-of";
    public const string UsesViewModel = "uses-viewmodel";
    public const string Exposes = "exposes";
    public const string BindsTo = "binds-to";
    public const string Invokes = "invokes";
    public const string NavigatesTo = "navigates-to";
    public const string EnteredVia = "entered-via";
    public const string HasState = "has-state";
    public const string TransitionsTo = "transitions-to";
    public const string DependsOn = "depends-on";
}

/// <summary>inspector.js flatten(): nested objects become dotted keys, arrays join.</summary>
public static class Flatten
{
    public static string Text(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString() ?? "",
        JsonValueKind.Number => el.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null or JsonValueKind.Undefined => "null",
        JsonValueKind.Array => string.Join(", ", el.EnumerateArray().Select(Text)),
        _ => el.GetRawText(),
    };

    public static List<(string Key, string Value)> Facts(JsonElement? props)
    {
        var acc = new List<(string, string)>();
        if (props is { ValueKind: JsonValueKind.Object } el) Walk(el, "", acc);
        return acc;
    }

    private static void Walk(JsonElement el, string prefix, List<(string, string)> acc)
    {
        foreach (var p in el.EnumerateObject())
        {
            if (p.Value.ValueKind == JsonValueKind.Object) Walk(p.Value, $"{prefix}{p.Name}.", acc);
            else acc.Add(($"{prefix}{p.Name}", Text(p.Value)));
        }
    }
}
