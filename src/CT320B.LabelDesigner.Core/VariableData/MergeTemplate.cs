using System.Text;

namespace CT320B.LabelDesigner.Core.VariableData;

/// <summary>
/// Resolves <c>{token}</c> placeholders in a field's text against a row of named values. Token names
/// are matched case-insensitively after trimming; an unknown token resolves to an empty string. A
/// literal brace is written by doubling it (<c>{{</c> → <c>{</c>, <c>}}</c> → <c>}</c>), so designs
/// that legitimately contain braces aren't mangled.
/// </summary>
public static class MergeTemplate
{
    /// <summary>True when <paramref name="text"/> contains at least one <c>{token}</c> placeholder.</summary>
    public static bool HasTokens(string? text) => ExtractTokens(text).Count > 0;

    /// <summary>Returns the distinct token names referenced in <paramref name="text"/> (in order of first
    /// appearance, original casing preserved), ignoring doubled-brace literals.</summary>
    public static IReadOnlyList<string> ExtractTokens(string? text)
    {
        var tokens = new List<string>();
        if (string.IsNullOrEmpty(text)) return tokens;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '{')
            {
                if (i + 1 < text.Length && text[i + 1] == '{') { i += 2; continue; }   // escaped {{
                int end = text.IndexOf('}', i + 1);
                if (end < 0) break;
                string name = text[(i + 1)..end].Trim();
                if (name.Length > 0 && seen.Add(name)) tokens.Add(name);
                i = end + 1;
            }
            else if (c == '}' && i + 1 < text.Length && text[i + 1] == '}') { i += 2; }  // escaped }}
            else i++;
        }
        return tokens;
    }

    /// <summary>Substitutes every <c>{token}</c> in <paramref name="text"/> with its value from
    /// <paramref name="row"/> (case-insensitive lookup; missing → empty), honouring <c>{{</c>/<c>}}</c>
    /// escapes. Returns <paramref name="text"/> unchanged when it has no placeholders.</summary>
    public static string Resolve(string? text, IReadOnlyDictionary<string, string> row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0 && text.IndexOf('}') < 0) return text ?? "";

        var sb = new StringBuilder(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '{')
            {
                if (i + 1 < text.Length && text[i + 1] == '{') { sb.Append('{'); i += 2; continue; }
                int end = text.IndexOf('}', i + 1);
                if (end < 0) { sb.Append(text, i, text.Length - i); break; }   // unterminated → literal
                string name = text[(i + 1)..end].Trim();
                sb.Append(Lookup(row, name));
                i = end + 1;
            }
            else if (c == '}' && i + 1 < text.Length && text[i + 1] == '}') { sb.Append('}'); i += 2; }
            else { sb.Append(c); i++; }
        }
        return sb.ToString();
    }

    private static string Lookup(IReadOnlyDictionary<string, string> row, string name)
    {
        if (row.TryGetValue(name, out string? v)) return v ?? "";
        // Case-insensitive fallback for dictionaries not built with an ordinal-ignore-case comparer.
        foreach (KeyValuePair<string, string> kv in row)
            if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)) return kv.Value ?? "";
        return "";
    }
}
