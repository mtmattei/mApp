using AppOrbit.Graph;
using SkiaSharp;

namespace AppOrbit.Scene;

/// <summary>
/// The tokens the Skia scene draws with, snapshotted on the UI thread per theme. The keys are the
/// ones in Themes/Tokens.xaml; the fallback values are the same numbers, so a resource lookup that
/// fails on a platform still paints the prototype's palette.
/// </summary>
public sealed record Palette(
    SKColor Paper, SKColor Paper2, SKColor Paper3,
    SKColor Ink, SKColor Ink2, SKColor Ink3,
    SKColor Rule, SKColor Rule2,
    SKColor Focus, SKColor FocusSoft, SKColor Danger, SKColor Ok,
    SKColor HueScreen, SKColor HueFeature, SKColor HueVm, SKColor HueState, SKColor HueRoute, SKColor HueComp,
    SKColor UiAccent, SKColor UiAccentSoft, SKColor Shadow, bool IsDark)
{
    public static readonly Palette Light = new(
        SKColor.Parse("#F5F4EF"), SKColor.Parse("#ECEBE4"), SKColor.Parse("#E3E2DA"),
        SKColor.Parse("#1D2430"), SKColor.Parse("#4A5160"), SKColor.Parse("#7A8190"),
        SKColor.Parse("#CFD2D8"), SKColor.Parse("#E1E3E7"),
        SKColor.Parse("#2457D6"), SKColor.Parse("#2457D6").WithAlpha(31), SKColor.Parse("#B4432F"), SKColor.Parse("#2F7D4F"),
        SKColor.Parse("#1D2430"), SKColor.Parse("#5B6472"), SKColor.Parse("#4B55B8"), SKColor.Parse("#A66A00"), SKColor.Parse("#0F7B74"), SKColor.Parse("#7A4E8C"),
        SKColor.Parse("#7A4A2E"), SKColor.Parse("#7A4A2E").WithAlpha(31), SKColor.Parse("#141820").WithAlpha(64), false);

    public static readonly Palette Dark = new(
        SKColor.Parse("#15181E"), SKColor.Parse("#1C2028"), SKColor.Parse("#232834"),
        SKColor.Parse("#E8E9EC"), SKColor.Parse("#B6BAC4"), SKColor.Parse("#848A97"),
        SKColor.Parse("#3A3F4A"), SKColor.Parse("#2A2F39"),
        SKColor.Parse("#7AA2FF"), SKColor.Parse("#7AA2FF").WithAlpha(41), SKColor.Parse("#E0735F"), SKColor.Parse("#6CC08B"),
        SKColor.Parse("#E8E9EC"), SKColor.Parse("#9AA3B2"), SKColor.Parse("#9AA3FF"), SKColor.Parse("#E0A93A"), SKColor.Parse("#4FC1B7"), SKColor.Parse("#C08AD6"),
        SKColor.Parse("#D9A27F"), SKColor.Parse("#D9A27F").WithAlpha(46), SKColors.Black.WithAlpha(179), true);

    public SKColor HueOf(string? type) => type switch
    {
        NodeType.Screen => HueScreen,
        NodeType.Feature => HueFeature,
        NodeType.ViewModel or NodeType.Property or NodeType.Command => HueVm,
        NodeType.State => HueState,
        NodeType.Route => HueRoute,
        NodeType.Component or NodeType.Instance => HueComp,
        _ => Ink2,
    };

    /// <summary>scene.js arrowClass / link colours per relation.</summary>
    public SKColor LinkColor(string relation) => relation switch
    {
        "route" or Relation.NavigatesTo => HueRoute,
        Relation.BindsTo or Relation.Invokes or Relation.DependsOn or Relation.Exposes or Relation.UsesViewModel => HueVm,
        Relation.HasState or Relation.TransitionsTo => HueState,
        Relation.InstanceOf or Relation.Contains => HueComp,
        _ => Ink3,
    };

    /// <summary>Reads the theme's Color.* keys from the merged token dictionary; falls back to the built-in values.</summary>
    public static Palette FromResources(bool dark)
    {
        var fallback = dark ? Dark : Light;
        try
        {
            ResourceDictionary? theme = null;
            foreach (var md in Application.Current.Resources.MergedDictionaries)
            {
                if (md.ThemeDictionaries.TryGetValue(dark ? "Dark" : "Light", out var td) && td is ResourceDictionary rd) { theme = rd; break; }
            }
            if (theme == null) return fallback;
            SKColor C(string key, SKColor fb) =>
                theme.TryGetValue(key, out var v) && v is Windows.UI.Color c ? new SKColor(c.R, c.G, c.B, c.A) : fb;
            return new Palette(
                C("Color.Paper", fallback.Paper), C("Color.Paper2", fallback.Paper2), C("Color.Paper3", fallback.Paper3),
                C("Color.Ink", fallback.Ink), C("Color.Ink2", fallback.Ink2), C("Color.Ink3", fallback.Ink3),
                C("Color.Rule", fallback.Rule), C("Color.Rule2", fallback.Rule2),
                C("Color.Focus", fallback.Focus), C("Color.FocusSoft", fallback.FocusSoft), C("Color.Danger", fallback.Danger), C("Color.Ok", fallback.Ok),
                C("Color.Hue.Screen", fallback.HueScreen), C("Color.Hue.Feature", fallback.HueFeature), C("Color.Hue.Vm", fallback.HueVm),
                C("Color.Hue.State", fallback.HueState), C("Color.Hue.Route", fallback.HueRoute), C("Color.Hue.Comp", fallback.HueComp),
                C("Color.UiAccent", fallback.UiAccent), C("Color.UiAccentSoft", fallback.UiAccentSoft), C("Color.Shadow", fallback.Shadow), dark);
        }
        catch
        {
            return fallback;
        }
    }
}
