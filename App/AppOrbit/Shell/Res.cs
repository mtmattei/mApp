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
}
