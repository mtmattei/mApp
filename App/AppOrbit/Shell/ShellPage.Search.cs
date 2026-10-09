using AppOrbit.Graph;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace AppOrbit;

/// <summary>main.js search: type to filter entities, ↑ ↓ to move, Enter to focus, Esc to close.</summary>
public sealed partial class ShellPage
{
    private List<Node> _hits = new();
    private int _selected;

    private void WireSearch()
    {
        SearchBox.TextChanged += (_, _) => { _hits = G.Search(SearchBox.Text); _selected = 0; RenderSearch(); };
        SearchBox.GotFocus += (_, _) => { _hits = G.Search(SearchBox.Text); RenderSearch(); };
        SearchBox.LostFocus += (_, _) => DispatcherQueue.TryEnqueue(async () => { await Task.Delay(120); if (FocusManager.GetFocusedElement(XamlRoot) != SearchBox) CloseSearch(); });
        SearchBox.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case VirtualKey.Down: _selected = Math.Min(_hits.Count - 1, _selected + 1); RenderSearch(); e.Handled = true; break;
                case VirtualKey.Up: _selected = Math.Max(0, _selected - 1); RenderSearch(); e.Handled = true; break;
                case VirtualKey.Enter: if (_selected < _hits.Count) { PickSearch(_hits[_selected].Id); e.Handled = true; } break;
                case VirtualKey.Escape: SearchBox.Text = ""; CloseSearch(); Stage.Focus(FocusState.Programmatic); e.Handled = true; break;
            }
        };
    }

    /// <summary>Runs a search as the journey runner does: type, then Enter.</summary>
    internal void SearchAndPick(string query)
    {
        _hits = G.Search(query);
        _selected = 0;
        if (_hits.Count > 0) PickSearch(_hits[0].Id);
    }

    private void RenderSearch()
    {
        SearchResults.Children.Clear();
        SearchPopup.IsOpen = true;
        if (_hits.Count == 0)
        {
            var empty = Text(SearchBox.Text.Trim().Length > 0 ? "No entity matches" : "Type to search screens, members, states…", "T12", "Ink3");
            empty.Margin = new Thickness(8, 6, 8, 6);
            SearchResults.Children.Add(empty);
            return;
        }
        for (var i = 0; i < _hits.Count; i++)
        {
            var n = _hits[i];
            var row = new Grid { Padding = new Thickness(8, 6, 8, 6), CornerRadius = new CornerRadius(4), ColumnSpacing = 8, Background = i == _selected ? B("FocusSoft") : new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var type = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
            type.Children.Add(Glyph(n.Type, Res<Brush>(HueKey(n.Type))));
            type.Children.Add(Text(NodeType.Label(n.Type), "T11", "Ink3"));
            row.Children.Add(type);
            var name = Text(n.Name, "T12"); Grid.SetColumn(name, 1); row.Children.Add(name);
            var ctx = Text(string.Join(" › ", G.ContextChain(n.Id).SkipLast(1).Select(c => c.Name)), "T11", "Ink3"); ctx.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(ctx, 2); row.Children.Add(ctx);
            var id = n.Id;
            row.PointerPressed += (_, e) => { PickSearch(id); e.Handled = true; };
            SearchResults.Children.Add(row);
        }
    }

    private void PickSearch(string id)
    {
        Focus(id);
        CloseSearch();
        Stage.Focus(FocusState.Programmatic);
    }

    private void CloseSearch() => SearchPopup.IsOpen = false;
}
