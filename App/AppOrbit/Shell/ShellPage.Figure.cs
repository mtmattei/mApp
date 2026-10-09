using Microsoft.UI.Xaml.Media;

namespace AppOrbit;

/// <summary>The orbit figure at the top of the application view (figure.js mount). The Hairline port lands here; until then the slot keeps its size.</summary>
public sealed partial class ShellPage
{
    private FrameworkElement FigureHost()
    {
        var host = new Border
        {
            Height = 166, Margin = new Thickness(0, 12, 0, 0),
            BorderBrush = B("Rule2"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Background = B("Paper"),
        };
        ToolTipService.SetToolTip(host, "A screen taken apart the way this inspector reads it. Move across to open the gap, down to pick a layer, click to choose that lens.");
        return host;
    }
}
