using System.Diagnostics;
using AppOrbit.Graph;
using AppOrbit.Layout;
using AppOrbit.State;

namespace AppOrbit;

/// <summary>
/// The stress runner: with APP_ORBIT_STRESS=1 the app drives whatever graph it loaded (APP_ORBIT_GRAPH)
/// as hard as it can and writes timings, exceptions and memory to APP_ORBIT_LOG. It measures graph
/// parsing, the pure layout for every entity and lens, rendered paint over a sample of focuses, two long
/// random walks over every shell action (memory before and after each, to show a leak), and bursts of
/// actions with no delay between them. Any exception, handled or not, counts as a failure.
/// </summary>
internal static class Stress
{
    private static int _errors;
    private static readonly bool On = Environment.GetEnvironmentVariable("APP_ORBIT_STRESS") == "1";
    private static readonly Dictionary<string, (double Ms, int N, double Max)> Sections = new();

    /// <summary>Section timing for the state handler; free unless the stress runner is on.</summary>
    public static long Mark() => On ? Stopwatch.GetTimestamp() : 0;

    public static long Took(string section, long since)
    {
        if (!On) return 0;
        var ms = Stopwatch.GetElapsedTime(since).TotalMilliseconds;
        var (sum, n, max) = Sections.GetValueOrDefault(section);
        Sections[section] = (sum + ms, n + 1, Math.Max(max, ms));
        return Stopwatch.GetTimestamp();
    }

    private static void LogSections(string phase)
    {
        Log($"  where the time went ({phase}): " + string.Join(", ", Sections.OrderByDescending(x => x.Value.Ms).Select(x => $"{x.Key} {x.Value.Ms / x.Value.N:0.0} ms avg, {x.Value.Max:0} max over {x.Value.N}")));
        Sections.Clear();
    }

    private static string Mem()
    {
        var managed = Memory();
        var info = GC.GetGCMemoryInfo();
        return $"managed {managed / 1048576.0:0.0} MB (GC committed {info.TotalCommittedBytes / 1048576.0:0} MB), working set {Process.GetCurrentProcess().WorkingSet64 / 1048576} MB";
    }
    private static readonly string LogPath = Environment.GetEnvironmentVariable("APP_ORBIT_LOG") ?? "stress.log";

    public static void Start(ShellPage page)
    {
        if (Environment.GetEnvironmentVariable("APP_ORBIT_STRESS") != "1") return;
        Application.Current.UnhandledException += (_, e) => { _errors++; Log($"FAIL unhandled {e.Exception}"); e.Handled = true; };
        TaskScheduler.UnobservedTaskException += (_, e) => { _errors++; Log($"FAIL unobserved {e.Exception}"); e.SetObserved(); };
        _ = page.DispatcherQueue.TryEnqueue(async () =>
        {
            try { await RunAsync(page); }
            catch (Exception ex) { _errors++; Log($"FAIL stress runner threw {ex}"); }
            Log(_errors == 0 ? "stress passed" : $"{_errors} failure(s)");
            if (Environment.GetEnvironmentVariable("APP_ORBIT_EXIT") == "1") Environment.Exit(_errors == 0 ? 0 : 1);
        });
    }

    private static void Log(string line)
    {
        Console.WriteLine(line);
        try { File.AppendAllText(LogPath, line + "\n"); } catch { }
    }

    private static string Stats(List<double> ms) =>
        ms.Count == 0 ? "n/a" : $"avg {ms.Average():0.00} ms, p99 {ms.OrderBy(x => x).ElementAt(Math.Min(ms.Count - 1, (int)(ms.Count * 0.99))):0.00} ms, max {ms.Max():0.00} ms over {ms.Count}";

    private static long Memory()
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        return GC.GetTotalMemory(true);
    }

    private static async Task RunAsync(ShellPage page)
    {
        await Task.Delay(1500);
        var store = page.StoreForJourney;
        var g = store.Graph;
        var rnd = new Random(7);
        Log($"graph {g.Raw.GraphId}: {g.Raw.Nodes.Count} nodes, {g.Raw.Edges.Count} edges, {g.Raw.Files?.Count ?? 0} files");

        // 1. parse and index, as at launch
        if (App.LaunchGraph is { } path)
        {
            var sw = Stopwatch.StartNew();
            var json = await File.ReadAllTextAsync(path);
            var read = sw.Elapsed.TotalMilliseconds;
            var index = new GraphIndex(AppGraph.Parse(json));
            Log($"read {read:0} ms, parse and index {sw.Elapsed.TotalMilliseconds - read:0} ms ({index.Raw.Nodes.Count} nodes)");
        }

        // 2. the pure layout for every entity in every lens
        var lenses = new[] { Lens.Structure, Lens.Navigation, Lens.Behavior, Lens.States };
        var layoutMs = new List<double>();
        var maxCards = (Count: 0, Id: "", Lens: "");
        foreach (var id in g.Raw.Nodes.Select(n => (string?)n.Id).Prepend(null))
            foreach (var lens in lenses)
            {
                var s = store.State with { FocusId = id, Lens = lens, Trail = ImmutableList<string>.Empty };
                var sw = Stopwatch.StartNew();
                LayoutResult l;
                try { l = LayoutEngine.Compute(g, s); }
                catch (Exception ex) { _errors++; Log($"FAIL layout {id ?? "(application)"} {lens}: {ex.Message}"); continue; }
                layoutMs.Add(sw.Elapsed.TotalMilliseconds);
                if (l.Cards.Count > maxCards.Count) maxCards = (l.Cards.Count, id ?? "(application)", lens);
            }
        Log($"layout: {Stats(layoutMs)}; most cards {maxCards.Count} at {maxCards.Id} ({maxCards.Lens})");

        // 3. rendered sweep: features, screens and a random sample of the rest, every lens
        var sample = g.Raw.Nodes.Where(n => n.Type is NodeType.Feature or NodeType.Screen).Select(n => n.Id)
            .Concat(g.Raw.Nodes.Where(n => n.Type is not (NodeType.Feature or NodeType.Screen)).OrderBy(_ => rnd.Next()).Take(60).Select(n => n.Id))
            .Prepend(null).ToList();
        var paintAvg = new List<double>(); var paintMax = new List<double>(); var stepMs = new List<double>();
        foreach (var id in sample)
            foreach (var lens in lenses)
            {
                page.StageForJourney.ResetPaintStats();
                var sw = Stopwatch.StartNew();
                page.Focus(id); page.SetLens(lens);
                await Task.Delay(1);
                stepMs.Add(sw.Elapsed.TotalMilliseconds);
                await Task.Delay(60);
                var (avg, max, frames) = page.StageForJourney.PaintStats;
                if (frames > 0) { paintAvg.Add(avg); paintMax.Add(max); }
            }
        Log($"rendered sweep: {sample.Count} focuses x 4 lenses; dispatch {Stats(stepMs)}");
        LogSections("sweep");
        Log($"paint per step: average {Stats(paintAvg)}; worst frame {Stats(paintMax)}");

        // 4. random walks over every shell action, memory before and after each
        var ids = g.Raw.Nodes.Select(n => n.Id).ToArray();
        var withSource = g.Raw.Nodes.Where(n => n.Source != null).ToArray();
        string[] queries = { "a", "e", "Job", "Order", "zz", "Member1", "Loading", "→", "", "screen.", "ViewModel", "x" };
        var actions = new (string Name, Action Do)[]
        {
            ("focus", () => page.Focus(ids[rnd.Next(ids.Length)])),
            ("focus", () => page.Focus(ids[rnd.Next(ids.Length)])),
            ("lens", () => page.SetLens(lenses[rnd.Next(lenses.Length)])),
            ("view", page.ToggleView),
            ("fidelity", page.ToggleFidelity),
            ("dock", page.ToggleMode),
            ("motion", page.ToggleMotion),
            ("back", page.Back),
            ("zoom out", page.ZoomOut),
            ("zoom in", page.ZoomIn),
            ("search", () => page.SearchAndPick(queries[rnd.Next(queries.Length)])),
            ("hover", () => page.Hover(rnd.Next(3) == 0 ? null : ids[rnd.Next(ids.Length)])),
            ("source", () => { var n = withSource[rnd.Next(withSource.Length)]; page.OpenSource(n.Source!); }),
            ("camera", () => page.StageForJourney.Nudge(rnd.Next(-30, 30), rnd.Next(-10, 10))),
            ("reset", () => page.StageForJourney.ResetCamera()),
            ("cursor", () => page.StageForJourney.StepCursor(rnd.Next(2) == 0 ? 1 : -1)),
            ("application", () => page.Focus(null)),
        };
        var counts = new Dictionary<string, int>();
        async Task Walk(int steps, int delayMs)
        {
            for (var i = 0; i < steps; i++)
            {
                var (name, run) = actions[rnd.Next(actions.Length)];
                counts[name] = counts.GetValueOrDefault(name) + 1;
                try { run(); }
                catch (Exception ex) { _errors++; Log($"FAIL {name} at step {i}: {ex.GetType().Name}: {ex.Message}"); }
                if (delayMs > 0) await Task.Delay(delayMs); else if (i % 10 == 0) await Task.Yield();
            }
        }

        // three walks: bounded caches fill during the first, a leak keeps growing through the second and third
        Sections.Clear();
        Log($"before the walks: {Mem()}");
        var managed = new List<long>();
        var walks = int.TryParse(Environment.GetEnvironmentVariable("APP_ORBIT_STRESS_WALKS"), out var nw) ? Math.Max(3, nw) : 3;
        for (var w = 1; w <= walks; w++)
        {
            await Walk(1500, 8);
            await Task.Delay(1500);
            managed.Add(Memory());
            Log($"after walk {w}: {Mem()}");
            // APP_ORBIT_STRESS_PAUSE=<seconds>: hold still after each walk so a heap snapshot can be taken
            if (int.TryParse(Environment.GetEnvironmentVariable("APP_ORBIT_STRESS_PAUSE"), out var pause) && pause > 0)
            {
                Log($"pause after walk {w}");
                await Task.Delay(pause * 1000);
            }
        }
        Log("  actions: " + string.Join(", ", counts.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}")));
        LogSections("walks");
        // a leak climbs with every walk; bounded caches and in-flight work level off after the first
        var late = managed.Skip(1).ToList();
        var slope = late.Count > 1 ? (late[^1] - late[0]) / 1048576.0 / (late.Count - 1) : 0;
        if (slope > 4) { _errors++; Log($"FAIL managed memory climbs {slope:0.0} MB per walk after the first (a leak)"); }
        else Log($"ok   managed memory levels off after the first walk ({slope:+0.0;-0.0;0.0} MB per walk across walks 2-{managed.Count})");

        // 5. bursts with no delay, as fast as key repeat or faster
        var burst = Stopwatch.StartNew();
        await Walk(600, 0);
        Log($"burst: 600 actions with no delay in {burst.ElapsedMilliseconds} ms");
        LogSections("burst");
        await Task.Delay(2500);

        // the app still answers: a final focus lands and renders
        page.Focus(g.Raw.Entry);
        await Task.Delay(800);
        var landed = store.State.FocusId == g.Raw.Entry && page.InspectorText.Length > 0;
        if (!landed) _errors++;
        Log($"{(landed ? "ok  " : "FAIL")} after the stress, focusing the entry screen still lands and renders");
        Log($"at the end: {Mem()}");
    }
}
