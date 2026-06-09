using System.Globalization;
using System.Text.Json;
using System.Threading;

namespace CT320B.LabelDesigner.Services;

/// <summary>A UI language: a culture code, a display name, and its string table (which may be partial —
/// missing keys fall back to English).</summary>
public sealed class LanguageInfo
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyDictionary<string, string> Strings { get; init; }
}

/// <summary>
/// File-backed, user-extensible UI localisation. Every language lives in its own JSON file in the
/// <c>lang</c> folder beside the executable (<see cref="AppPaths.LangDir"/>): <c>&lt;code&gt;.json</c> with a
/// <c>name</c> and a <c>strings</c> map. Drop a new file in to add a language; the shipped <c>en.json</c>
/// is the template and the fallback. <see cref="T"/> returns the active language's text, falling back to
/// English then the key, so a partial translation never breaks the UI. The language is chosen in the device
/// bar and applied at startup (<see cref="Apply"/>); changing it needs a restart. Font names, format
/// placeholders and technical tokens are intentionally not translated.
/// </summary>
public static class Loc
{
    private static IReadOnlyDictionary<string, string> _english = new Dictionary<string, string>();
    private static List<LanguageInfo> _languages = [];
    private static bool _loaded;

    /// <summary>The active UI language.</summary>
    public static LanguageInfo Current { get; private set; } =
        new() { Code = "en", Name = "English", Strings = new Dictionary<string, string>() };

    /// <summary>All languages found in the <c>lang</c> folder (English first, then by display name).</summary>
    public static IReadOnlyList<LanguageInfo> Available { get { EnsureLoaded(); return _languages; } }

    // Loads every <code>.json from the lang folder once. Idempotent; best-effort (a missing/locked folder
    // just yields no languages, and T() then returns the keys).
    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        var list = new List<LanguageInfo>();
        try
        {
            string dir = AppPaths.LangDir;
            if (Directory.Exists(dir))
            {
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                foreach (string path in Directory.EnumerateFiles(dir, "*.json"))
                {
                    string code = Path.GetFileNameWithoutExtension(path);
                    if (code.StartsWith('_')) continue;   // _template.json etc. aren't languages
                    LangFile? f;
                    try { f = JsonSerializer.Deserialize<LangFile>(File.ReadAllText(path), opts); }
                    catch (JsonException) { continue; }
                    if (f?.Strings is null) continue;
                    list.Add(new LanguageInfo
                    {
                        Code = code,
                        Name = string.IsNullOrWhiteSpace(f.Name) ? code : f.Name!,
                        Strings = new Dictionary<string, string>(f.Strings, StringComparer.Ordinal),
                    });
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best-effort */ }

        // English first (it's the fallback baseline), then the rest alphabetically by display name.
        list.Sort((a, b) =>
            a.Code.Equals("en", StringComparison.OrdinalIgnoreCase) ? -1 :
            b.Code.Equals("en", StringComparison.OrdinalIgnoreCase) ? 1 :
            string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

        if (list.Count == 0) list.Add(Current);   // never empty, so Current/_english stay valid
        _languages = list;
        _english = (list.FirstOrDefault(l => l.Code.Equals("en", StringComparison.OrdinalIgnoreCase)) ?? list[0]).Strings;
        if (Current.Strings.Count == 0) Current = list[0];
    }

    /// <summary>Sets the active language by code and the thread cultures (so framework dialogs localise too).</summary>
    public static void Apply(string code)
    {
        EnsureLoaded();
        Current = _languages.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase)) ?? _languages[0];
        try
        {
            var culture = CultureInfo.GetCultureInfo(Current.Code);
            Thread.CurrentThread.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }
        catch (CultureNotFoundException) { /* a custom code with no .NET culture → keep OS culture */ }
    }

    /// <summary>The translation for <paramref name="key"/> (active language → English → key).</summary>
    public static string T(string key)
    {
        EnsureLoaded();
        if (Current.Strings.TryGetValue(key, out string? v) && !string.IsNullOrEmpty(v)) return v;
        if (_english.TryGetValue(key, out string? en) && !string.IsNullOrEmpty(en)) return en;
        return key;
    }

    /// <summary><see cref="T"/> then <see cref="string.Format(string, object?[])"/> with <paramref name="args"/>.</summary>
    public static string F(string key, params object?[] args) => string.Format(T(key), args);

    private sealed class LangFile
    {
        public string? Name { get; set; }
        public Dictionary<string, string>? Strings { get; set; }
    }
}
