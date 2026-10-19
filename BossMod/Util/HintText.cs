using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BossMod;

// Display-layer localization for combat hints.
// Framework/shared-component strings are few and go through Loc.T; the ~2500 encounter modules produce thousands of one-off English
// strings (many built at runtime), which a whole-string dictionary can never cover. So on top of Loc.T we run a glossary pass that swaps
// well-known mechanic vocabulary ("Raidwide", "tankbuster", ...) for its zh-TW term. It is deliberately applied ONLY to the string that
// is about to be handed to ImGui: the hint lists are never modified, so no code path that inspects or compares hint strings is affected.
public static class HintText
{
    private static Dictionary<string, string> _terms = new(StringComparer.OrdinalIgnoreCase);
    private static Regex? _matcher;

    // hint strings are recomputed and redrawn every frame, so memoize the (pure) translation instead of re-running the regex
    private static readonly Dictionary<string, string> _cache = new(StringComparer.Ordinal);
    private const int _cacheLimit = 4096;

    public static void Load(string langCode)
    {
        _terms = new(StringComparer.OrdinalIgnoreCase);
        _matcher = null;
        _cache.Clear();

        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream($"BossModReborn.loc.hintterms.{langCode}.json");
        if (stream == null)
            return;

        try
        {
            using var doc = JsonDocument.Parse(stream);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Name.StartsWith('_') || prop.Value.ValueKind != JsonValueKind.String)
                    continue; // '_'-prefixed keys are notes for translators
                var value = prop.Value.GetString();
                if (!string.IsNullOrEmpty(prop.Name) && !string.IsNullOrEmpty(value))
                    _terms[prop.Name] = value;
            }
        }
        catch (Exception e)
        {
            Service.Log($"[HintText] Failed to load hint glossary for '{langCode}': {e}");
            return;
        }

        if (_terms.Count == 0)
            return;

        // longest key first, so that multi-word entries win over their own substrings ("line stack" before "stack")
        var keys = new List<string>(_terms.Keys);
        keys.Sort((a, b) => b.Length.CompareTo(a.Length));

        var sb = new StringBuilder();
        foreach (var k in keys)
        {
            if (sb.Length > 0)
                sb.Append('|');
            // only anchor on word boundaries where the term actually starts/ends with a word character,
            // otherwise \b would refuse to match terms with leading/trailing punctuation
            if (char.IsLetterOrDigit(k[0]))
                sb.Append("\\b");
            sb.Append(Regex.Escape(k));
            if (char.IsLetterOrDigit(k[^1]))
                sb.Append("\\b");
        }

        try
        {
            _matcher = new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        catch (Exception e)
        {
            Service.Log($"[HintText] Failed to build hint glossary matcher: {e}");
            _matcher = null;
        }
    }

    // translate a hint for display; safe to call with an already-translated string (nothing will match)
    public static string Translate(string text)
    {
        if (text.Length == 0 || !BossModuleManager.Config.TranslateHints)
            return text;

        if (_cache.TryGetValue(text, out var cached))
            return cached;

        var res = Loc.T(text); // whole-string translation first, it always reads better than a per-word swap
        if (ReferenceEquals(res, text) && _matcher != null)
            res = _matcher.Replace(text, TranslateTerm);

        if (_cache.Count >= _cacheLimit)
            _cache.Clear(); // hints can embed player names and timers, so the key space is unbounded - just start over
        _cache[text] = res;
        return res;
    }

    private static string TranslateTerm(Match m) => _terms.TryGetValue(m.Value, out var v) ? v : m.Value;
}
