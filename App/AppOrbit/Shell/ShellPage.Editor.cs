using AppOrbit.Graph;
using AppOrbit.State;
using Microsoft.UI.Xaml.Media;

namespace AppOrbit;

/// <summary>
/// editor.js: a file list and a code pane built from graph.files. Lines inside an entity's source
/// range are linked; clicking one focuses the most specific entity. Focus moves the cursor here.
/// </summary>
public sealed partial class ShellPage
{
    private readonly Dictionary<string, List<(string Id, int Start, int End)>> _rangesByFile = new();
    private readonly Dictionary<string, Button> _fileButtons = new();
    private readonly List<(int Line, Border Row, TextBlock Number)> _codeLines = new();
    private string? _renderedFile = "\0";

    private void BuildEditorFiles()
    {
        _rangesByFile.Clear();
        foreach (var n in G.Raw.Nodes)
        {
            if (n.Source == null) continue;
            if (!_rangesByFile.TryGetValue(n.Source.File, out var list)) _rangesByFile[n.Source.File] = list = new();
            list.Add((n.Id, n.Source.Line, n.Source.EndLine ?? n.Source.Line));
        }
        EditorFiles.Children.Clear();
        _fileButtons.Clear();
        var groups = new List<(string Dir, List<GraphFile> Files)>();
        foreach (var f in G.Raw.Files ?? new())
        {
            var dir = string.Join("/", f.Path.Split('/')[..^1]);
            var g = groups.FirstOrDefault(x => x.Dir == dir);
            if (g.Files == null) { g = (dir, new()); groups.Add(g); }
            g.Files.Add(f);
        }
        foreach (var (dir, files) in groups)
        {
            var label = Text(dir, "Mono11", "Ink3"); label.Margin = new Thickness(8, 8, 8, 2);
            EditorFiles.Children.Add(label);
            foreach (var f in files)
            {
                var name = f.FileName;
                var ext = name.EndsWith(".xaml.cs") ? ".xaml.cs" : name.EndsWith(".xaml") ? ".xaml" : name.EndsWith(".cs") ? ".cs" : "";
                var b = new Button { Style = S("RowButton"), Padding = new Thickness(8, 3, 8, 3) };
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(Text(ext.Length > 0 ? name[..^ext.Length] : name, "Mono11", "Ink2"));
                var e = Text(ext, "Mono11", "Ink3"); Grid.SetColumn(e, 1); row.Children.Add(e);
                b.Content = row;
                var fileId = f.Id;
                b.Click += (_, _) => Store.Dispatch(s => s with { Editor = new EditorState(fileId, s.Editor.FileId == fileId ? s.Editor.Line : null) });
                AutomationProperties.SetName(b, name);
                _fileButtons[f.Id] = b;
                EditorFiles.Children.Add(b);
            }
        }
    }

    private void RenderEditor(AppState s)
    {
        var (fileId, line) = s.Editor;
        foreach (var (id, b) in _fileButtons) b.Background = id == fileId ? B("Paper") : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        if (fileId == null)
        {
            if (_renderedFile != null)
            {
                EditorCode.Children.Clear();
                _codeLines.Clear();
                var empty = Text("Select an entity, or a file, to see its source.", "T12", "Ink3", wrap: true); empty.Margin = new Thickness(12);
                EditorCode.Children.Add(empty);
                _renderedFile = null;
            }
            return;
        }
        if (!G.Files.TryGetValue(fileId, out var f)) return;
        if (_renderedFile != fileId)
        {
            EditorCode.Children.Clear();
            _codeLines.Clear();
            var path = Text(f.Path, "Mono11", "Ink3"); path.Margin = new Thickness(12, 2, 12, 6);
            EditorCode.Children.Add(new Border { Child = path, BorderBrush = B("Rule2"), BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 0, 0, 4) });
            var ranges = _rangesByFile.GetValueOrDefault(fileId) ?? new();
            for (var i = 0; i < f.Lines.Count; i++)
            {
                var ln = f.StartLine + i;
                var covering = ranges.Where(r => ln >= r.Start && ln <= r.End).OrderBy(r => r.End - r.Start).ToList();
                var grid = new Grid { Height = 17.6, Padding = new Thickness(0, 0, 16, 0) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var num = Text(ln.ToString(), "Mono11", "Ink3"); num.HorizontalAlignment = HorizontalAlignment.Right; num.Margin = new Thickness(0, 0, 8, 0); num.VerticalAlignment = VerticalAlignment.Center;
                var mark = Text(covering.Count > 0 ? "·" : "", "Mono11", covering.Count > 0 ? "Ink3" : "Rule"); Grid.SetColumn(mark, 1); mark.VerticalAlignment = VerticalAlignment.Center;
                var code = Text(f.Lines[i].Length > 0 ? f.Lines[i] : " ", "Mono11", "Ink2"); Grid.SetColumn(code, 2); code.VerticalAlignment = VerticalAlignment.Center;
                grid.Children.Add(num); grid.Children.Add(mark); grid.Children.Add(code);
                var row = new Border { Child = grid, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
                if (covering.Count > 0)
                {
                    var target = covering[0].Id; var at = ln;
                    ToolTipService.SetToolTip(row, string.Join(" › ", covering.Select(c => G.Node(c.Id)?.Name)));
                    row.Tapped += (_, _) => Focus(target, at);
                    row.PointerEntered += (_, _) => { if (_store?.State.Editor.Line != at) row.Background = B("Paper2"); };
                    row.PointerExited += (_, _) => StyleLine(row, num, at, _store?.State ?? s);
                }
                _codeLines.Add((ln, row, num));
                EditorCode.Children.Add(row);
            }
            _renderedFile = fileId;
        }
        foreach (var (ln, row, num) in _codeLines) StyleLine(row, num, ln, s);
        var atRow = _codeLines.FirstOrDefault(x => x.Line == line).Row;
        if (atRow != null)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    var p = atRow.TransformToVisual(EditorCode).TransformPoint(new Windows.Foundation.Point(0, 0));
                    var target = Math.Max(0, p.Y - EditorScroll.ViewportHeight / 2 + 9);
                    EditorScroll.ChangeView(null, target, null, s.ReducedMotion);
                }
                catch { }
            });
        }
    }

    private void StyleLine(Border row, TextBlock num, int ln, AppState s)
    {
        var focusNode = s.FocusId != null ? G.Node(s.FocusId) : null;
        var range = focusNode?.Source?.File == s.Editor.FileId ? focusNode!.Source : null;
        var at = ln == s.Editor.Line;
        var inRange = range != null && ln >= range.Line && ln <= (range.EndLine ?? range.Line) && !at;
        row.Background = at || inRange ? B("FocusSoft") : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        row.BorderBrush = at ? B("Focus") : null;
        row.BorderThickness = new Thickness(at ? 2 : 0, 0, 0, 0);
        num.Foreground = at ? B("Focus") : B("Ink3");
        if (row.Child is Grid g && g.Children.Count > 2 && g.Children[2] is TextBlock code) code.Foreground = at ? B("Ink") : B("Ink2");
    }
}
