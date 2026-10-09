using AppOrbit.Dock;
using AppOrbit.Graph;
using AppOrbit.Layout;
using AppOrbit.Scene;
using AppOrbit.State;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;
using Windows.System;

namespace AppOrbit;

/// <summary>
/// The one page: top bar, mock editor, viewer and inspector (main.js). Levels and lenses are
/// state, not routes. Every panel re-renders from store subscriptions; the store is the one writer.
/// </summary>
public sealed partial class ShellPage : Page
{
    private Store? _store;
    private DockController? _dock;
    private GraphIndex? _graph;
    private int _lastScene = -1;
    private string? _lastMode, _lastView, _lastLens;

    public ShellPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        PreviewKeyDown += OnKeyDown;
        ViewerSlot.SizeChanged += (_, _) => PlaceViewer();
        Shell.SizeChanged += (_, _) => { ApplyBreakpoints(); PlaceViewer(); };
        ActualThemeChanged += (_, _) => ApplyPalette();
    }

    private Store Store => _store ?? throw new InvalidOperationException("The graph is not loaded.");
    private GraphIndex G => _graph ?? throw new InvalidOperationException("The graph is not loaded.");

    // ---------------------------------------------------------------- boot

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_store != null) return;
        string json;
        try
        {
            json = await ReadGraphAsync();
            _graph = new GraphIndex(AppGraph.Parse(json));
        }
        catch (Exception ex)
        {
            BootError.Visibility = Visibility.Visible;
            BootErrorText.Text = $"Expected Graph/orderly.graph.json next to the app (packaged from ../../graph). {ex.Message}";
            return;
        }
        var prefs = Prefs.Load();
        _store = new Store(_graph, AppState.Initial(prefs.Lens, prefs.Mode, prefs.View, prefs.ReducedMotion ?? false, prefs.Fidelity, prefs.WorkspaceRoot));

        ApplyPalette();
        WireScene();
        WireSearch();
        WireDock();
        BuildEditorFiles();
        _store.Changed += OnStateChanged;

        _ = EnsureWindowSizeAsync();

        // first paint, honouring a deep link
        var deep = App.LaunchFocus;
        if (deep != null && _graph.Node(deep) is { } target)
            _store.Dispatch(s => s with { FocusId = deep, Trail = ImmutableList.Create(deep), Editor = target.Source != null ? new EditorState(target.Source.File, target.Source.Line) : s.Editor, SceneVersion = 1 });
        else
            _store.Dispatch(s => s with { SceneVersion = 1 });
        _dock?.Place();
        Stage.ResetCamera(_store.State.View);
        FocusViewer();
        Journey.Start(this);
    }

    /// <summary>Re-asks for the launch size until the root reports it, a few times at most.</summary>
    internal async Task EnsureWindowSizeAsync()
    {
        var want = App.DesiredSize;
        for (var i = 0; i < 12 && App.Window != null; i++)
        {
            var size = XamlRoot?.Size ?? new Windows.Foundation.Size(0, 0);
            if (Math.Abs(size.Width - want.Width) < 2 && Math.Abs(size.Height - want.Height) < 2) return;
            try { App.Window.AppWindow.Resize(want); } catch { }
            await Task.Delay(250);
        }
    }

    private static async Task<string> ReadGraphAsync()
    {
        try
        {
            var file = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Graph/orderly.graph.json"));
            return await FileIO.ReadTextAsync(file);
        }
        catch
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Graph", "orderly.graph.json");
            return await File.ReadAllTextAsync(path);
        }
    }

    private void ApplyPalette()
    {
        if (_graph == null) return;
        var palette = Palette.FromResources(ActualTheme == ElementTheme.Dark);
        Stage.Attach(_graph, palette);
        _figure?.SetPalette(palette);
    }

    // ---------------------------------------------------------------- actions (main.js)

    internal void Focus(string? id, int? line = null, string? lens = null)
    {
        Store.Dispatch(s =>
        {
            if (id == s.FocusId && line == null) return s;
            var n = id != null ? G.Node(id) : null;
            var trail = id != null ? s.Trail.Where(t => t != id).Append(id).TakeLast(24).ToImmutableList() : s.Trail;
            var editor = n?.Source != null ? new EditorState(n.Source.File, line ?? n.Source.Line) : s.Editor;
            return s with { FocusId = id, Trail = trail, Editor = editor, Lens = lens ?? s.Lens, CursorId = null, SearchOpen = false, SceneVersion = s.SceneVersion + 1 };
        });
    }

    internal void ZoomOut()
    {
        var s = Store.State;
        if (s.FocusId == null) return;
        var p = G.ParentOf(s.FocusId, s.Trail);
        Focus(p?.Id);
    }

    internal void Back()
    {
        var t = Store.State.Trail;
        if (t.Count < 2) { Focus(null); return; }
        var prev = t[^2];
        Store.Dispatch(s => s with { Trail = s.Trail.RemoveAt(s.Trail.Count - 1) });
        Focus(prev);
    }

    internal void ZoomIn()
    {
        var s = Store.State;
        var id = s.CursorId ?? s.HoverId;
        if (id != null && id != s.FocusId) Focus(id);
    }

    internal void SetLens(string lens) => Store.Dispatch(s => s.Lens == lens ? s : s with { Lens = lens, SceneVersion = s.SceneVersion + 1 });
    internal void ToggleView() => Store.Dispatch(s => s with { View = s.View == Views.Orbit ? Views.Flat : Views.Orbit, SceneVersion = s.SceneVersion + 1 });
    internal void SetMode(string mode) => Store.Dispatch(s => s.Mode == mode ? s : s with { Mode = mode, SceneVersion = s.SceneVersion + 1 });
    internal void ToggleMode() { if (_dock != null) _dock.Toggle(); else SetMode(Store.State.Mode == Modes.Docked ? Modes.Expanded : Modes.Docked); }
    internal DockController? DockForJourney => _dock;
    internal void ToggleMotion() => Store.Dispatch(s => s with { ReducedMotion = !s.ReducedMotion });
    internal void ToggleFidelity() => Store.Dispatch(s => s with { Fidelity = s.Fidelity == Fidelities.Ui ? Fidelities.Wire : Fidelities.Ui, SceneVersion = s.SceneVersion + 1 });
    internal void Hover(string? id) => Store.Dispatch(s => s.HoverId == id ? s : s with { HoverId = id });
    internal void OpenSource(SourceRef r) => Store.Dispatch(s => s with { Editor = new EditorState(r.File, r.Line) });

    // ---------------------------------------------------------------- scene wiring

    internal void FocusViewer() => StageHost.Focus(FocusState.Programmatic);

    private void WireDock()
    {
        _dock = new DockController(new DockHost
        {
            Viewer = Viewer, Ghost = DockGhost, Head = ViewerHead, Shell = Shell,
            HomeOf = mode => { var r = HomeRect(mode); return new Home(r.X, r.Y, r.Width, r.Height, mode == Modes.Docked ? 210 : 0); },
            GetMode = () => Store.State.Mode,
            SetMode = SetMode,
            ReducedMotion = () => Store.State.ReducedMotion,
            SetCarrying = on => Store.Dispatch(s => s.Carrying == on ? s : s with { Carrying = on, SceneVersion = s.SceneVersion + 1 }),
            SetFloating = (floating, lifted) =>
            {
                var docked = Store.State.Mode == Modes.Docked;
                Viewer.BorderThickness = new Thickness(floating || docked ? 1 : 0);
                Viewer.CornerRadius = new CornerRadius(floating || docked ? 8 : 0);
                Canvas.SetZIndex(Viewer, lifted ? 30 : docked ? 10 : 4);
                ViewerFoot.Visibility = floating || docked ? Visibility.Collapsed : Visibility.Visible;
                ViewerNote.Visibility = floating || docked ? Visibility.Collapsed : Visibility.Visible;
                ViewerTitle.Visibility = floating || docked ? Visibility.Visible : Visibility.Collapsed;
            },
            GhostDashed = () => Res<Brush>("Ink3"),
            GhostHolder = () => (Res<Brush>("Rule"), Res<Brush>("Paper2")),
            GhostHard = () => (Res<Brush>("Focus"), Res<Brush>("FocusSoft")),
        });
        Canvas.SetZIndex(DockGhost, 3);
        Canvas.SetZIndex(Viewer, 4);
    }

    private void WireScene()
    {
        Stage.AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => StageHost.Focus(FocusState.Pointer)), true);
        Stage.FocusRequested += id => Focus(id);
        Stage.HoverChanged += Hover;
        Stage.CursorChanged += id => Store.Dispatch(s => s.CursorId == id ? s : s with { CursorId = id });
        Stage.ZoomInRequested += id => Focus(id);
        Stage.ZoomOutRequested += ZoomOut;
        Stage.LayoutMoved += moved => LayoutReset.Visibility = moved ? Visibility.Visible : Visibility.Collapsed;
        Stage.CameraChanged += (yaw, pitch, s) => CameraReadout.Text = $"yaw {yaw:0}° · pitch {pitch:0}° · {Math.Round(s * 100)}%";
    }

    // ---------------------------------------------------------------- render (store subscription)

    private void OnStateChanged(AppState s, AppState prev)
    {
        FidelityButton.Content = s.Fidelity == Fidelities.Ui ? "UI" : "Wire";
        Pressed(FidelityButton, s.Fidelity == Fidelities.Ui);
        Pressed(ViewButton, s.View == Views.Flat);
        Pressed(ModeButton, s.Mode == Modes.Docked);
        Pressed(MotionButton, s.ReducedMotion);
        Tab(LensStructure, s.Lens == Lens.Structure);
        Tab(LensNavigation, s.Lens == Lens.Navigation);
        Tab(LensBehavior, s.Lens == Lens.Behavior);
        Tab(LensStates, s.Lens == Lens.States);

        if (s.SceneVersion != _lastScene || s.Mode != _lastMode || s.View != _lastView || s.Lens != _lastLens)
        {
            if (s.Mode != _lastMode) ApplyMode(s.Mode);
            var l = LayoutEngine.Compute(G, s);
            Stage.Render(s, l);
            ViewerNote.Text = l.Note;
            var focusNode = s.FocusId != null ? G.Node(s.FocusId) : null;
            ViewerTitle.Text = focusNode?.Name ?? G.Raw.Name;
            _lastScene = s.SceneVersion; _lastMode = s.Mode; _lastView = s.View; _lastLens = s.Lens;
            ViewerBack.IsEnabled = s.Trail.Count > 0;
            RenderBreadcrumb(s);
            RenderInspector(s);
        }
        else if (s.HoverId != prev.HoverId || s.CursorId != prev.CursorId)
        {
            Stage.UpdateHighlights(s);
        }
        if (s.Editor != prev.Editor || s.FocusId != prev.FocusId || s.SceneVersion != prev.SceneVersion) RenderEditor(s);
        if (_figure != null) _figure.ReducedMotion = s.ReducedMotion;
        if (s.SearchOpen != prev.SearchOpen && !s.SearchOpen) CloseSearch();
        if (s.Lens != prev.Lens || s.Mode != prev.Mode || s.View != prev.View || s.ReducedMotion != prev.ReducedMotion || s.Fidelity != prev.Fidelity || s.WorkspaceRoot != prev.WorkspaceRoot) Prefs.Save(s);
    }

    /// <summary>A shell toggle's pressed look: paper-2, ink, an ink-3 border (the prototype's aria-pressed style).</summary>
    private void Pressed(Button b, bool on)
    {
        b.Background = Res<Brush>(on ? "Paper2" : "Paper");
        b.Foreground = Res<Brush>(on ? "Ink" : "Ink2");
        b.BorderBrush = Res<Brush>(on ? "Ink3" : "Rule");
        AutomationProperties.SetItemStatus(b, on ? "on" : "off");
    }

    /// <summary>A lens tab's selected look: paper on the paper-2 track, ink text.</summary>
    private void Tab(Button b, bool on)
    {
        b.Background = on ? Res<Brush>("Paper") : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        b.Foreground = Res<Brush>(on ? "Ink" : "Ink2");
        AutomationProperties.SetItemStatus(b, on ? "selected" : "");
    }

    /// <summary>The prototype's breakpoints: the inspector narrows to 280 under 1400; under 1100 the editor collapses while expanded.</summary>
    private void ApplyBreakpoints()
    {
        if (_store == null) return;
        var w = ActualWidth;
        var docked = _store.State.Mode == Modes.Docked;
        var inspectorW = w < 1400 ? 280 : 320;
        if (!docked) InspectorColumn.Width = new GridLength(inspectorW);
        EditorColumn.Width = w < 1100 && !docked ? new GridLength(0) : Res<GridLength>("EditorWidth");
        Editor.Visibility = w < 1100 && !docked ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Docked: the slot column collapses, the inspector takes the width, the viewer's chrome changes.</summary>
    private void ApplyMode(string mode)
    {
        var docked = mode == Modes.Docked;
        SlotColumn.Width = docked ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        InspectorColumn.Width = docked ? new GridLength(1, GridUnitType.Star) : Res<GridLength>("InspectorWidth");
        InspectorScroll.Padding = docked ? new Thickness(16, 16, 24, 16) : new Thickness(16);
        LensTabs.Visibility = docked ? Visibility.Collapsed : Visibility.Visible;
        ViewerFoot.Visibility = docked ? Visibility.Collapsed : Visibility.Visible;
        ViewerNote.Visibility = docked ? Visibility.Collapsed : Visibility.Visible;
        ViewerTitle.Visibility = docked ? Visibility.Visible : Visibility.Collapsed;
        ViewerDock.Visibility = docked ? Visibility.Collapsed : Visibility.Visible;
        ViewerExpand.Visibility = docked ? Visibility.Visible : Visibility.Collapsed;
        ViewerHead.Padding = docked ? new Thickness(8, 6, 8, 6) : new Thickness(12, 8, 12, 8);
        Viewer.BorderBrush = Res<Brush>("Rule");
        Viewer.BorderThickness = new Thickness(docked ? 1 : 0);
        Viewer.CornerRadius = new CornerRadius(docked ? 8 : 0);
        PlaceViewer();
    }

    /// <summary>Puts the viewer on its home without motion (boot, resize, mode change). Dock will animate between homes.</summary>
    private void PlaceViewer()
    {
        if (_store == null) return;
        if (_dock != null) { _dock.Place(); return; }
        var home = HomeRect(_store.State.Mode);
        Canvas.SetLeft(Viewer, home.X);
        Canvas.SetTop(Viewer, home.Y);
        Viewer.Width = Math.Max(1, home.Width);
        Viewer.Height = Math.Max(1, home.Height);
    }

    internal Windows.Foundation.Rect HomeRect(string mode)
    {
        var w = Res<double>("Dock.W"); var h = Res<double>("Dock.H"); var gap = Res<double>("Dock.Gap");
        if (mode == Modes.Docked)
            return new Windows.Foundation.Rect(Shell.ActualWidth - gap - w, Shell.ActualHeight - gap - h, w, h);
        var p = ViewerSlot.TransformToVisual(Shell).TransformPoint(new Windows.Foundation.Point(0, 0));
        return new Windows.Foundation.Rect(p.X, p.Y, ViewerSlot.ActualWidth, ViewerSlot.ActualHeight);
    }

    // ---------------------------------------------------------------- breadcrumb

    private void RenderBreadcrumb(AppState s)
    {
        Breadcrumb.Children.Clear();
        var chain = s.FocusId != null ? G.ContextChain(s.FocusId, s.Trail) : new List<Node>();
        var crumbs = new List<(string? Id, string Name, string? Type, List<Node>? Hidden)> { (null, G.Raw.Name, null, null) };
        crumbs.AddRange(chain.Select(n => ((string?)n.Id, n.Name, (string?)n.Type, (List<Node>?)null)));
        if (crumbs.Count > 4)
        {
            var hidden = chain.Take(chain.Count - 2).ToList();
            crumbs = new() { crumbs[0], (hidden[^1].Id, "…", null, hidden), crumbs[^2], crumbs[^1] };
        }
        for (var i = 0; i < crumbs.Count; i++)
        {
            if (i > 0) Breadcrumb.Children.Add(new TextBlock { Text = "›", Style = Res<Style>("T12"), Foreground = Res<Brush>("Ink3"), VerticalAlignment = VerticalAlignment.Center });
            var (id, name, type, hidden) = crumbs[i];
            var b = new Button { Style = Res<Style>("CrumbButton") };
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
            if (type != null) content.Children.Add(Glyph(type, Res<Brush>("Ink3")));
            content.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, FontWeight = i == crumbs.Count - 1 ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal, Foreground = (Brush)Res<Brush>(i == crumbs.Count - 1 ? "Ink" : "Ink2") });
            b.Content = content;
            if (hidden != null) ToolTipService.SetToolTip(b, string.Join(" › ", hidden.Select(x => x.Name)));
            var target = id;
            b.Click += (_, _) => Focus(target);
            AutomationProperties.SetName(b, name);
            Breadcrumb.Children.Add(b);
        }
    }

    /// <summary>The 12px entity icon as a XAML element, drawn by the same path data the scene uses.</summary>
    internal static FrameworkElement Glyph(string type, Brush color, double size = 12)
    {
        var (stroke, fill, dashed) = Icons.For(type);
        var canvas = new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = GeometryFrom(stroke), Stroke = color, StrokeThickness = 1.25, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Width = 12, Height = 12, Stretch = Stretch.None, VerticalAlignment = VerticalAlignment.Center,
        };
        if (dashed) canvas.StrokeDashArray = new DoubleCollection { 2, 1.4 };
        var box = new Grid { Width = size + 2, Height = size + 2, VerticalAlignment = VerticalAlignment.Center };
        box.Children.Add(canvas);
        if (fill != null) box.Children.Add(new Microsoft.UI.Xaml.Shapes.Path { Data = GeometryFrom(fill), Fill = color, Width = 12, Height = 12, Stretch = Stretch.None, VerticalAlignment = VerticalAlignment.Center });
        return box;
    }

    private static Geometry GeometryFrom(SkiaSharp.SKPath path)
    {
        // circles come out of Skia as conics; rebuild as cubics through a flattened copy so the XAML parser accepts every segment
        var d = path.ToSvgPathData();
        return (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), d);
    }

    // ---------------------------------------------------------------- controls

    private void OnLensTab(object sender, RoutedEventArgs e) { if (sender is Button t && t.Tag is string lens) SetLens(lens); }
    private void OnToggleFidelity(object sender, RoutedEventArgs e) => ToggleFidelity();
    private void OnToggleView(object sender, RoutedEventArgs e) => ToggleView();
    private void OnToggleMotion(object sender, RoutedEventArgs e) => ToggleMotion();
    private void OnToggleMode(object sender, RoutedEventArgs e) => ToggleMode();
    private void OnToggleHelp(object sender, RoutedEventArgs e) => _ = ShowHelpAsync();
    private void OnBack(object sender, RoutedEventArgs e) => Back();
    private void OnCameraReset(object sender, RoutedEventArgs e) => Stage.ResetCamera();
    private void OnLayoutReset(object sender, RoutedEventArgs e) => Stage.ResetOffsets();

    // ---------------------------------------------------------------- keyboard (main.js keydown)

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_store == null || XamlRoot == null) return;
        var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        var typing = focused is TextBox;
        if (typing && e.Key != VirtualKey.Escape) return;
        var inViewer = focused == null || IsInside(focused, Viewer);
        var shift = IsDown(VirtualKey.Shift);
        var alt = IsDown(VirtualKey.Menu);
        switch (e.Key)
        {
            case VirtualKey.Divide: case (VirtualKey)191: SearchBox.Focus(FocusState.Keyboard); SearchBox.SelectAll(); break;
            case VirtualKey.Add: case (VirtualKey)187: ZoomIn(); break;
            case VirtualKey.Subtract: case (VirtualKey)189: case VirtualKey.Back: ZoomOut(); break;
            case VirtualKey.Escape: if (typing) { CloseSearch(); FocusViewer(); } else Focus(_store.State.FocusId); break;
            case VirtualKey.Number0: case VirtualKey.NumberPad0: Stage.ResetCamera(); break;
            case VirtualKey.Number1: SetLens(Lens.Structure); break;
            case VirtualKey.Number2: SetLens(Lens.Navigation); break;
            case VirtualKey.Number3: SetLens(Lens.Behavior); break;
            case VirtualKey.Number4: SetLens(Lens.States); break;
            case VirtualKey.F: ToggleView(); break;
            case VirtualKey.D: ToggleMode(); break;
            case VirtualKey.W: ToggleFidelity(); break;
            case VirtualKey.Enter: if (inViewer) { ZoomIn(); } break;
            case VirtualKey.Tab:
                if (!inViewer) return;
                Stage.StepCursor(shift ? -1 : 1);
                break;
            case VirtualKey.Left: case VirtualKey.Right: case VirtualKey.Up: case VirtualKey.Down:
            {
                var dx = e.Key == VirtualKey.Left ? -1 : e.Key == VirtualKey.Right ? 1 : 0;
                var dy = e.Key == VirtualKey.Up ? -1 : e.Key == VirtualKey.Down ? 1 : 0;
                if (alt && dx < 0) { Back(); break; }
                if (shift)
                {
                    var id = _store.State.CursorId ?? _store.State.FocusId;
                    if (id != null) Stage.NudgeCard(id, dx * 8, dy * 8);
                    break;
                }
                if (inViewer && _store.State.View == Views.Orbit) Stage.Nudge(dx * 6, -dy * 4);
                break;
            }
            default:
                if (shift && e.Key == (VirtualKey)191) { _ = ShowHelpAsync(); break; }
                return;
        }
        e.Handled = true;
    }

    private static bool IsDown(VirtualKey key) =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private static bool IsInside(DependencyObject? el, DependencyObject root)
    {
        while (el != null) { if (el == root) return true; el = VisualTreeHelper.GetParent(el); }
        return false;
    }
}
