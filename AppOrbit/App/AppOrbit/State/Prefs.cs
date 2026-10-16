using System.Text.Json;
using Windows.Storage;

namespace AppOrbit.State;

/// <summary>store.js loadPrefs/savePrefs, over ApplicationData.LocalSettings. Every read is guarded: a missing store means per-session prefs.</summary>
public sealed record Prefs(string? Lens, string? Mode, string? View, bool? ReducedMotion, string? WorkspaceRoot, string? Fidelity)
{
    private const string Key = "app-orbit.prefs";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static Prefs Empty => new(null, null, null, null, null, null);

    public static Prefs Load()
    {
        try
        {
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue(Key, out var raw) && raw is string s)
                return JsonSerializer.Deserialize<Prefs>(s, Json) ?? Empty;
        }
        catch
        {
            // storage unavailable: preferences are per session
        }
        return Empty;
    }

    public static void Save(AppState s)
    {
        try
        {
            var p = new Prefs(s.Lens, s.Mode, s.View, s.ReducedMotion, s.WorkspaceRoot, s.Fidelity);
            ApplicationData.Current.LocalSettings.Values[Key] = JsonSerializer.Serialize(p, Json);
        }
        catch
        {
            // per session only
        }
    }

    /// <summary>A free-form JSON blob under a key (card offsets).</summary>
    public static T LoadJson<T>(string key, T fallback)
    {
        try
        {
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue(key, out var raw) && raw is string s)
                return JsonSerializer.Deserialize<T>(s, Json) ?? fallback;
        }
        catch { }
        return fallback;
    }

    public static void SaveJson<T>(string key, T value)
    {
        try { ApplicationData.Current.LocalSettings.Values[key] = JsonSerializer.Serialize(value, Json); }
        catch { }
    }
}
