using AppOrbit.Graph;

namespace AppOrbit.State;

/// <summary>store.js: one state object, one writer. Subscribers get (next, prev) after every change.</summary>
public sealed class Store
{
    public GraphIndex Graph { get; }
    public AppState State { get; private set; }

    public event Action<AppState, AppState>? Changed;

    public Store(GraphIndex graph, AppState initial)
    {
        Graph = graph;
        State = initial;
    }

    public void Dispatch(Func<AppState, AppState> patch)
    {
        var next = patch(State);
        if (ReferenceEquals(next, State) || next == State) return;
        var prev = State;
        State = next;
        Changed?.Invoke(next, prev);
    }
}
