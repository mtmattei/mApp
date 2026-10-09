using System.Diagnostics;
using AppOrbit.Graph;
using AppOrbit.Layout;
using AppOrbit.State;

namespace AppOrbit;

/// <summary>
/// The journey runner: with APP_ORBIT_JOURNEY=1 the app drives its own store through the smoke
/// test's steps (tests/smoke.mjs in the prototype), asserts the same facts, writes ok/FAIL lines to
/// APP_ORBIT_LOG (default shots/journey.log) and captures a screenshot per stop into APP_ORBIT_SHOTS
/// (default shots/) when a capture tool is available (ImageMagick's import under X11).
/// </summary>
internal static class Journey
{
    private static readonly List<string> Lines = new();
    private static int _failures;

    public static void Start(ShellPage page)
    {
        if (Environment.GetEnvironmentVariable("APP_ORBIT_JOURNEY") != "1") return;
        _ = page.DispatcherQueue.TryEnqueue(async () =>
        {
            try { await RunAsync(page); }
            catch (Exception ex) { Log($"FAIL journey threw {ex}"); }
            finally { Finish(); }
        });
    }

    private static string ShotsDir => Environment.GetEnvironmentVariable("APP_ORBIT_SHOTS") ?? Path.Combine(Directory.GetCurrentDirectory(), "shots");
    private static string LogPath => Environment.GetEnvironmentVariable("APP_ORBIT_LOG") ?? Path.Combine(ShotsDir, "journey.log");

    private static void Log(string line)
    {
        Lines.Add(line);
        Console.WriteLine(line);
        try { Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!); File.AppendAllText(LogPath, line + "\n"); } catch { }
    }

    private static void Check(bool cond, string msg)
    {
        if (!cond) _failures++;
        Log($"{(cond ? "ok  " : "FAIL")} {msg}");
    }

    private static void Finish()
    {
        Log(_failures == 0 ? "all checks passed" : $"{_failures} failure(s)");
        if (Environment.GetEnvironmentVariable("APP_ORBIT_EXIT") == "1") Environment.Exit(_failures == 0 ? 0 : 1);
    }

    private static async Task Settle(int ms = 500) => await Task.Delay(ms);

    private static async Task Shot(string name)
    {
        await Task.Delay(400);
        try
        {
            Directory.CreateDirectory(ShotsDir);
            if (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("DISPLAY") != null)
            {
                using var p = Process.Start(new ProcessStartInfo("import", $"-window root \"{Path.Combine(ShotsDir, name + ".png")}\"") { UseShellExecute = false });
                if (p != null) await p.WaitForExitAsync();
            }
        }
        catch (Exception ex) { Log($"note: no screenshot for {name} ({ex.Message})"); }
    }

    private static async Task RunAsync(ShellPage page)
    {
        await Task.Delay(1200);
        var store = page.StoreForJourney;
        var g = store.Graph;
        AppState S() => store.State;
        LayoutResult L() => LayoutEngine.Compute(g, S());
        int Cards(string kind, string? type = null) => L().Cards.Count(c => c.Kind == kind && (type == null || c.Type == type));
        bool HasCard(string key) => L().Cards.Any(c => c.Key == key);
        string Crumb() => page.BreadcrumbText;
        string InspectorText() => page.InspectorText;

        Log($"graph {g.Raw.GraphId}: {g.Raw.Nodes.Count} nodes, {g.Raw.Edges.Count} edges");

        // ---- 1. Orientation ----
        page.Focus(null); page.SetLens(Lens.Structure); await Settle();
        Check(Cards("feature") == 3, "application level shows 3 features");
        Check(L().Cards.Where(c => c.Kind == "feature").Sum(c => c.Screens!.Count) == 5, "application level shows 5 screens");
        await Shot("01-application");
        page.Focus("feature.purchase"); await Settle();
        Check(S().FocusId == "feature.purchase", "click a feature plate → feature level");
        Check(Crumb().Contains("Purchase"), "breadcrumb shows Purchase");
        await Shot("02-feature-purchase");
        page.Focus("screen.checkout"); await Settle();
        Check(S().FocusId == "screen.checkout", "click a screen → screen level");
        Check(Crumb().Contains("Orderly") && Crumb().IndexOf("Purchase") > Crumb().IndexOf("Orderly") && Crumb().IndexOf("Checkout") > Crumb().IndexOf("Purchase"), "breadcrumb reads Orderly › Purchase › Checkout");
        page.ZoomOut(); await Settle();
        Check(S().FocusId == "feature.purchase", "− zooms out to the feature");
        page.ZoomOut(); await Settle();
        Check(S().FocusId == null, "− again returns to the application");

        // semantic zoom: the wheel over a card past the threshold focuses it (driven through the same handler)
        page.Focus("feature.purchase"); await Settle();
        page.Hover("screen.checkout");
        page.StageForJourney.SimulateWheel(6, "screen.checkout"); await Settle();
        Check(S().FocusId == "screen.checkout", "wheel past the threshold over a card zooms into it");
        Check(Math.Abs(page.StageForJourney.Camera.TargetScale - 1) < 0.01, "the semantic jump resets the continuous scale to 100%");

        // ---- 2. Relationship tracing ----
        page.SetLens(Lens.Navigation); await Settle();
        Check(HasCard("route.cart-to-checkout#in"), "navigation lens: incoming route from Cart");
        Check(HasCard("route.checkout-to-orders#out"), "navigation lens: outgoing route to Orders");
        Check(L().Links.Count(l => l.Relation == "route") == 2, "two route connectors drawn");
        await Shot("03-checkout-navigation");
        page.Focus("screen.orders"); await Settle();
        Check(S().FocusId == "screen.orders", "following the outgoing route lands on Orders");
        page.ZoomOut(); await Settle();
        Check(S().FocusId == "feature.orders", "Backspace zooms out to the Orders feature (parent, not history)");
        page.Back(); await Settle(); page.Back(); await Settle();
        Check(S().FocusId == "screen.checkout", "the back button retraces the trail to Checkout");

        page.SetLens(Lens.Behavior); await Settle();
        Check(HasCard("vm.cart"), "behavior lens: CartViewModel plane is shown");
        Check(L().Cards.First(c => c.Key == "vm.cart").Tag == "shared", "CartViewModel is tagged shared");
        var bindLinks = L().Links.Count(l => l.Relation is Relation.BindsTo or Relation.Invokes);
        Check(bindLinks == 8, $"seven bindings and one command connector ({bindLinks})");
        await Shot("04-checkout-behavior");

        page.Focus("inst.checkout.place-order"); await Settle();
        Check(S().FocusId == "inst.checkout.place-order", "click Place order in the preview → component level");
        var vm = L().Cards.FirstOrDefault(c => c.Kind == "vm");
        Check(vm?.Hot.Count == 3, "three view model members light up for Place order");
        var hotNames = vm?.Members?.Where(m => vm.Hot.Contains(m.Id)).Select(m => m.Name).ToList() ?? new();
        Check(hotNames.Contains("CanPlaceOrder") && hotNames.Contains("PlaceOrder"), "they include CanPlaceOrder and PlaceOrder");
        await Shot("05-place-order-behavior");

        page.Focus("prop.cart.can-place-order"); await Settle();
        Check(S().FocusId == "prop.cart.can-place-order", "click a member → detail level");
        Check(HasCard("prop.cart.has-items#out"), "CanPlaceOrder reads HasItems");
        Check(HasCard("state.place-order.disabled#in"), "the Disabled state depends on it");
        await Shot("06-can-place-order");

        page.SearchAndPick("OrderLineRow"); await Settle();
        Check(S().FocusId == "component.order-line-row", "search + Enter focuses the OrderLineRow definition");
        var useScreens = L().Cards.Where(c => c.Kind == "screen").Select(c => g.Node(c.Id)!.Name).ToList();
        Check(new[] { "Cart", "Checkout", "Orders" }.All(useScreens.Contains), $"definition view shows uses on Cart, Checkout, Orders ({string.Join(", ", useScreens)})");
        await Shot("07-order-line-row-uses");

        // ---- 3. Inspection ----
        page.Focus("prop.cart.can-place-order"); await Settle();
        var inspector = InspectorText();
        Check(inspector.Contains("IFeed<bool>"), "inspector shows the declared type");
        Check(inspector.Contains("unavailable · no running app connected"), "inspector marks the live value unavailable");
        Check(inspector.Contains("CartModel.cs:20"), "inspector shows the source reference");
        Check(inspector.Contains("declared"), "inspector shows evidence kind");
        Check(!inspector.Contains("propertys") && !inspector.Contains("clrType"), "inspector labels are human (no raw keys, no bad plurals)");
        store.Dispatch(s => s with { Editor = new EditorState("f.app", null) }); await Settle();
        page.OpenSource(g.Node("prop.cart.can-place-order")!.Source!); await Settle();
        Check(page.EditorLineAt?.Contains("CanPlaceOrder") == true, "the source link moves the editor to the CanPlaceOrder line");
        page.ClickEditorLine(27); await Settle();
        Check(S().FocusId == "cmd.cart.place-order", "clicking a source line focuses the entity declared there");
        page.Focus("prop.catalog.cart-count"); await Settle();
        Check(InspectorText().Contains("inferred"), "inferred relationship is tagged in the inspector");
        Check(L().Links.Count(l => l.Inferred) == 1, "inferred relationship is drawn dashed");
        await Shot("08-inferred");

        // ---- fidelity toggle and card moves ----
        page.Focus("screen.checkout"); page.SetLens(Lens.Behavior); page.ToggleFidelity(); await Settle();
        Check(S().Fidelity == Fidelities.Ui, "W switches previews to UI fidelity");
        var checkout = g.Node("screen.checkout")!;
        Check(checkout.Preview!.Parts!.Any(p => p.Items != null && p.Items[0].Contains("Flat white")), "UI previews show the sample rows from the graph");
        await Shot("08b-ui-fidelity");
        page.ToggleFidelity(); await Settle();
        Check(S().Fidelity == Fidelities.Wire, "W again returns to wireframes");
        {
            page.StageForJourney.NudgeCard("vm.cart", 120, 60); await Settle();
            Check(page.StageForJourney.HasMovedCards, "dragging a card moves it (reset layout appears)");
            Check(S().FocusId == "screen.checkout", "dragging does not change the focus");
            page.StageForJourney.ResetOffsets(); await Settle();
            Check(!page.StageForJourney.HasMovedCards, "reset layout puts the card back");
        }

        // ---- flat view, docked mode, reduced motion, keyboard ----
        page.ToggleView(); await Settle();
        Check(S().View == Views.Flat, "F switches to the flat view");
        Check(L().Links.Count(l => l.Relation is Relation.BindsTo or Relation.Invokes) == 8, "flat view keeps the same connectors");
        Check(Math.Abs(page.StageForJourney.Camera.Yaw) < 0.01 && Math.Abs(page.StageForJourney.Camera.Pitch) < 0.01, "flat view has no rotation");
        await Shot("09-flat");
        page.ToggleView(); await Settle();
        page.ToggleMode(); await Settle(1200);
        Check(S().Mode == Modes.Docked, "D docks the viewer");
        Check(S().FocusId == "screen.checkout", "docking keeps the focus");
        Check(page.ViewerSize.Width is > 379 and < 381 && page.ViewerSize.Height is > 259 and < 261, $"it settles at the slot's exact size ({page.ViewerSize.Width:0}×{page.ViewerSize.Height:0})");
        Check(page.InspectorWidth > 600, "the inspector takes the width when docked");
        await Shot("10-docked");
        page.ToggleMode(); await Settle(1200);
        Check(S().Mode == Modes.Expanded, "and D expands it again");
        page.ToggleMotion(); page.Focus("screen.cart"); await Settle();
        Check(S().ReducedMotion, "reduced motion toggles on");
        page.ToggleMotion();

        page.Focus(null); await Settle();
        page.StageForJourney.StepCursor(1); await Settle(100);
        page.ZoomIn(); await Settle();
        Check(S().FocusId != null, "Tab + Enter focuses the first card from the keyboard");
        page.Focus(null); await Settle();
    }
}
