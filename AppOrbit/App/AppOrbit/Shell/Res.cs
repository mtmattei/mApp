using Microsoft.UI.Xaml.Media;

namespace AppOrbit;

/// <summary>
/// Resource lookup for code-behind. A page's own Resources does not see App.xaml, and the token
/// brushes live in ThemeDictionaries, so this reads the application dictionary and then the merged
/// dictionaries' theme entry for the element's actual theme.
/// </summary>
public sealed partial class ShellPage
{
    private T Res<T>(string key)
    {
        if (Application.Current.Resources.TryGetValue(key, out var v) && v is T t) return t;
        var theme = ActualTheme == ElementTheme.Dark ? "Dark" : "Light";
        foreach (var md in Application.Current.Resources.MergedDictionaries)
        {
            if (md.TryGetValue(key, out v) && v is T t2) return t2;
            if (md.ThemeDictionaries.TryGetValue(theme, out var td) && td is ResourceDictionary rd && rd.TryGetValue(key, out v) && v is T t3) return t3;
        }
        throw new KeyNotFoundException($"Resource '{key}' ({typeof(T).Name}) is not in Tokens.xaml or Controls.xaml.");
    }

    /// <summary>
    /// A token brush for content that is built in code and thrown away on the next render (inspector rows,
    /// crumbs, code lines, search results): its own brush of the same colour. Every consumer of a shared
    /// brush adds an entry to that brush's composition context, and Uno does not drop the entry when the
    /// consumer is collected, so sharing one brush across thousands of short-lived elements grows without
    /// bound. Long-lived controls keep the shared resource. The shell rebuilds this content on a theme change.
    /// </summary>
    /// <summary>A button built in code for one render: its own brushes, so the style's shared ones are not touched (see Own).</summary>
    private T Fresh<T>(T b, string foreground) where T : Control
    {
        b.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        b.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        b.Foreground = Own(foreground);
        return b;
    }

    private Brush Own(string key) => Res<Brush>(key) is SolidColorBrush s ? new SolidColorBrush(s.Color) { Opacity = s.Opacity } : Res<Brush>(key);
}
