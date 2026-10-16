using AppOrbit.Figure;
using AppOrbit.Scene;
using Microsoft.UI.Xaml.Media;

namespace AppOrbit;

/// <summary>The orbit figure at the top of the application view (figure.js mount): a stage, a read-out, and a click that picks a lens.</summary>
public sealed partial class ShellPage
{
    private OrbitFigure? _figure;
    internal OrbitFigure? FigureForJourney => _figure;
    internal string FigureRead => _figureRead?.Text ?? "";
    private TextBlock? _figureRead;

    private FrameworkElement FigureHost()
    {
        var host = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        var stage = new Border { BorderBrush = B("Rule2"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Background = B("Paper") };
        var fig = new OrbitFigure { ReducedMotion = Store.State.ReducedMotion };
        fig.SetPalette(Palette.FromResources(ActualTheme == ElementTheme.Dark));
        fig.Picked += lens => SetLens(lens);
        stage.Child = fig;
        // Hairline's aspect ratio is 5:4; the stage follows the column's width
        stage.SizeChanged += (_, e) => fig.Height = e.NewSize.Width * 0.8;
        var read = new TextBlock { Text = "rest", Style = S("Mono11"), Foreground = B("Ink3"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(2, 4, 2, 0), MinHeight = 16 };
        fig.ReadChanged += r => read.Text = r;
        ToolTipService.SetToolTip(stage, "A screen taken apart the way this inspector reads it. Move across to open the gap, down to pick a layer, click to choose that lens.");
        AutomationProperties.SetName(fig, "A screen taken apart into its routes, view model, states and UI; moving across opens the gap, moving down picks a layer.");
        host.Children.Add(stage);
        host.Children.Add(read);
        _figure = fig;
        _figureRead = read;
        return host;
    }
}
