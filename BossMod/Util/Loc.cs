using System.Reflection;
using System.Text.Json;

namespace BossMod;

public static class Loc
{
    private static Dictionary<string, string> _strings = [];

    public static void Load(string langCode)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"BossModReborn.loc.{langCode}.json";
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            return;

        using var doc = JsonDocument.Parse(stream);
        var dict = new Dictionary<string, string>();
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Value.TryGetProperty("message", out var msg))
                dict[prop.Name] = msg.GetString() ?? prop.Name;
        }
        _strings = dict;
    }

    // Returns the localized string for key, or fallback if not found.
    public static string T(string key, string fallback = "") =>
        _strings.TryGetValue(key, out var val) ? val : fallback;

    // Overload for strings that are their own key - the English source text is both the lookup
    // key and the fallback. Same convention ConfigUI already uses for PropertyDisplay labels
    // (Loc.T(props.Label, props.Label)); used by the combat hints, which are far too numerous
    // to give hand-written keys without inviting key/fallback mismatches.
    public static string T(string english) =>
        _strings.TryGetValue(english, out var val) ? val : english;
}
