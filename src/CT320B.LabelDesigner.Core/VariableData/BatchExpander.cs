using CT320B.LabelDesigner.Core.Model;
using CT320B.LabelDesigner.Core.Serialization;

namespace CT320B.LabelDesigner.Core.VariableData;

/// <summary>
/// The variable-data merge engine: turns one template <see cref="LabelDocument"/> into the run of
/// labels for a batch. The data sources are the document's <see cref="LabelDocument.Counters"/>
/// (auto-incrementing) and an optional table of merge <c>rows</c> (e.g. parsed from CSV). Each output
/// label is a deep clone of the template with every field's <c>{token}</c> placeholders resolved
/// (see <see cref="MergeTemplate"/>).
/// </summary>
public static class BatchExpander
{
    /// <summary>
    /// The number of labels the batch produces. With merge <paramref name="rows"/> it's one per row;
    /// without rows it's <paramref name="copies"/> (a counter-only run). Always at least 0.
    /// </summary>
    public static int RowCount(IReadOnlyList<IReadOnlyDictionary<string, string>>? rows, int copies) =>
        rows is { Count: > 0 } ? rows.Count : Math.Max(0, copies);

    /// <summary>
    /// Builds the merged token map for the 0-based <paramref name="index"/>: the merge row's columns
    /// (if any) plus each counter's value at that index (counters win on a name clash).
    /// </summary>
    public static IReadOnlyDictionary<string, string> RowAt(
        LabelDocument template, IReadOnlyList<IReadOnlyDictionary<string, string>>? rows, int index)
    {
        ArgumentNullException.ThrowIfNull(template);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (rows is { Count: > 0 } && index >= 0 && index < rows.Count)
            foreach (KeyValuePair<string, string> kv in rows[index]) map[kv.Key] = kv.Value;
        foreach (SerialCounter counter in template.Counters)
            if (!string.IsNullOrWhiteSpace(counter.Name)) map[counter.Name.Trim()] = counter.ValueAt(index);
        return map;
    }

    /// <summary>Deep-clones <paramref name="template"/> and resolves every element's placeholders against
    /// <paramref name="row"/>. Counters/merge metadata are not needed on the output, so they're dropped.</summary>
    public static LabelDocument Expand(LabelDocument template, IReadOnlyDictionary<string, string> row)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(row);
        LabelDocument doc = LabelJson.Clone(template);
        doc.Counters.Clear();
        foreach (LabelElement element in doc.Elements)
            element.ApplyDataBinding(s => MergeTemplate.Resolve(s, row));
        return doc;
    }

    /// <summary>Convenience: the fully-resolved label document for the given batch index.</summary>
    public static LabelDocument ExpandAt(
        LabelDocument template, IReadOnlyList<IReadOnlyDictionary<string, string>>? rows, int index) =>
        Expand(template, RowAt(template, rows, index));

    /// <summary>Lazily yields every resolved label in the batch, in order. Streams one document at a
    /// time so a large run never materializes all clones at once.</summary>
    public static IEnumerable<LabelDocument> Expand(
        LabelDocument template, IReadOnlyList<IReadOnlyDictionary<string, string>>? rows, int copies)
    {
        ArgumentNullException.ThrowIfNull(template);
        int n = RowCount(rows, copies);
        for (int i = 0; i < n; i++)
            yield return Expand(template, RowAt(template, rows, i));
    }

    /// <summary>The distinct token names referenced by any element in <paramref name="template"/>
    /// (in first-appearance order) — used by the UI to show which fields are bound and which counters /
    /// columns are still unused.</summary>
    public static IReadOnlyList<string> ReferencedTokens(LabelDocument template)
    {
        ArgumentNullException.ThrowIfNull(template);
        var tokens = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Scan(string? s) { foreach (string t in MergeTemplate.ExtractTokens(s)) if (seen.Add(t)) tokens.Add(t); }
        foreach (LabelElement e in template.Elements)
            switch (e)
            {
                case Model.Elements.TextElement t: Scan(t.Text); break;
                case Model.Elements.BarcodeElement b: Scan(b.Data); break;
                case Model.Elements.QrElement q: Scan(q.Data); break;
                case Model.Elements.TableElement tbl: foreach (string c in tbl.Cells) Scan(c); break;
            }
        return tokens;
    }
}
