using AppOrbit.State;

namespace AppOrbit;

/// <summary>What the journey runner reads back from the shell: the same facts the smoke test reads from the DOM.</summary>
public sealed partial class ShellPage
{
    internal Store StoreForJourney => Store;
    internal Scene.SceneCanvas StageForJourney => Stage;

    internal string BreadcrumbText => string.Join(" ", Breadcrumb.Children.Select(TextOf));

    internal string InspectorText => string.Join("\n", Inspector.Children.Select(TextOf));

    internal string? EditorLineAt
    {
        get
        {
            var line = _store?.State.Editor.Line;
            var row = _codeLines.FirstOrDefault(x => x.Line == line).Row;
            return row == null ? null : TextOf(row);
        }
    }

    internal void ClickEditorLine(int line)
    {
        var fileId = _store?.State.Editor.FileId;
        if (fileId == null || !_rangesByFile.TryGetValue(fileId, out var ranges)) return;
        var covering = ranges.Where(r => line >= r.Start && line <= r.End).OrderBy(r => r.End - r.Start).ToList();
        if (covering.Count > 0) Focus(covering[0].Id, line);
    }

    /// <summary>The entity under the projected centre of a card's sub-element or face, as a pointer would find it.</summary>
    internal string? HitAtCenterOf(string cardKey, string? anchorKey = null) => Stage.HitAtAnchor(anchorKey ?? $"{cardKey}:c");

    internal Windows.Foundation.Size ViewerSize => new(Viewer.ActualWidth, Viewer.ActualHeight);
    internal double InspectorWidth => InspectorScroll.ActualWidth;

    private static string TextOf(UIElement el)
    {
        switch (el)
        {
            case TextBlock tb: return tb.Text;
            case Button b: return b.Content is UIElement c ? TextOf(c) : b.Content?.ToString() ?? "";
            case Border bd: return bd.Child != null ? TextOf(bd.Child) : "";
            case ContentControl cc: return cc.Content is UIElement c2 ? TextOf(c2) : cc.Content?.ToString() ?? "";
            case Panel p: return string.Join(" ", p.Children.Select(TextOf).Where(t => t.Length > 0));
            default: return "";
        }
    }
}
