namespace AppOrbit;

/// <summary>The shortcut sheet (?), with the workspace root for editor links.</summary>
public sealed partial class ShellPage
{
    private ContentDialog? _help;

    private async Task ShowHelpAsync()
    {
        if (_help != null) return;
        var rows = new (string Key, string What)[]
        {
            ("Tab / Shift+Tab", "move between cards"),
            ("Enter or +", "zoom in to the card under the cursor"),
            ("− or Backspace", "zoom out to the parent context"),
            ("Alt+←", "back along your trail (after following a relationship)"),
            ("Shift+arrows", "move the focused card 8px; double-click a moved card to snap it back"),
            ("← → ↑ ↓", "orbit (when the viewer has focus)"),
            ("0", "reset camera"),
            ("1–4", "Structure · Navigation · Behavior · States"),
            ("W", "wireframe ↔ UI previews"),
            ("F", "flat ↔ orbit"),
            ("D", "docked ↔ expanded"),
            ("/", "search"),
            ("Esc", "close search or this sheet"),
        };
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foreach (var (key, what) in rows)
        {
            var r = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var kbd = new Border { BorderBrush = B("Rule"), BorderThickness = new Thickness(1, 1, 1, 2), CornerRadius = new CornerRadius(4), Padding = new Thickness(4, 1, 4, 1), HorizontalAlignment = HorizontalAlignment.Left, Child = Text(key, "Mono11", "Ink2") };
            Grid.SetRow(kbd, r); grid.Children.Add(kbd);
            var t = Text(what, "T12", "Ink2", wrap: true); Grid.SetRow(t, r); Grid.SetColumn(t, 1); grid.Children.Add(t);
        }
        var root = new StackPanel { Spacing = 16, MaxWidth = 420 };
        root.Children.Add(grid);
        var setting = new StackPanel { Spacing = 4 };
        setting.Children.Add(Text("WORKSPACE ROOT FOR EDITOR LINKS", "T11", "Ink3"));
        var box = new TextBox { Text = Store.State.WorkspaceRoot, PlaceholderText = @"C:\src\Orderly  or  /Users/me/src/Orderly", Style = S("SearchBox") };
        setting.Children.Add(box);
        setting.Children.Add(Text("When set, the inspector offers an \"Open in VS Code\" link next to Open source. Kept on this machine only.", "T11", "Ink3", wrap: true));
        root.Children.Add(setting);
        _help = new ContentDialog { Title = "Keyboard", Content = root, CloseButtonText = "Close", XamlRoot = XamlRoot, Background = B("Paper"), Foreground = B("Ink") };
        try { await _help.ShowAsync(); }
        finally
        {
            var text = box.Text.Trim();
            Store.Dispatch(s => s.WorkspaceRoot == text ? s : s with { WorkspaceRoot = text, SceneVersion = s.SceneVersion + 1 });
            _help = null;
            Stage.Focus(FocusState.Programmatic);
        }
    }
}
