namespace AppOrbit.State;

public static class Lens
{
    public const string Structure = "structure";
    public const string Navigation = "navigation";
    public const string Behavior = "behavior";
    public const string States = "states";
    public static readonly string[] All = { Structure, Navigation, Behavior, States };
}

public readonly record struct EditorState(string? FileId, int? Line);

/// <summary>store.js initialState(): everything the shell derives from. The graph lives on the Store; the camera on the scene.</summary>
public sealed record AppState(
    string? FocusId,
    string? HoverId,
    string? CursorId,
    string Lens,
    string Mode,
    string View,
    bool ReducedMotion,
    string Fidelity,
    ImmutableList<string> Trail,
    bool RuntimeConnected,
    EditorState Editor,
    string WorkspaceRoot,
    bool SearchOpen,
    bool Carrying,
    int SceneVersion)
{
    public static AppState Initial(string? lens = null, string? mode = null, string? view = null, bool reducedMotion = false, string? fidelity = null, string? workspaceRoot = null) => new(
        FocusId: null,
        HoverId: null,
        CursorId: null,
        Lens: State.Lens.All.Contains(lens) ? lens! : State.Lens.Structure,
        Mode: mode == Modes.Docked ? Modes.Docked : Modes.Expanded,
        View: view == Views.Flat ? Views.Flat : Views.Orbit,
        ReducedMotion: reducedMotion,
        Fidelity: fidelity == Fidelities.Ui ? Fidelities.Ui : Fidelities.Wire,
        Trail: ImmutableList<string>.Empty,
        RuntimeConnected: false,
        Editor: new EditorState(null, null),
        WorkspaceRoot: workspaceRoot ?? "",
        SearchOpen: false,
        Carrying: false,
        SceneVersion: 0);

    public bool IsDocked => Mode == Modes.Docked;
    public bool IsFlat => View == Views.Flat;
}

public static class Modes { public const string Docked = "docked"; public const string Expanded = "expanded"; }
public static class Views { public const string Orbit = "orbit"; public const string Flat = "flat"; }
public static class Fidelities { public const string Wire = "wire"; public const string Ui = "ui"; }
