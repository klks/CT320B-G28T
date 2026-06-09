using System.Text;

namespace CT320B.LabelDesigner.Core.VariableData;

/// <summary>
/// A parsed delimited data table: the header <see cref="Columns"/> plus the data <see cref="Rows"/>,
/// each row a case-insensitive map of column name → cell value. Used as the mail-merge source for
/// batch printing. Parsing follows RFC 4180 (double-quoted fields may contain the delimiter, quotes
/// — doubled — and newlines) and auto-detects the delimiter (comma, tab or semicolon).
/// </summary>
public sealed class CsvData
{
    /// <summary>The header column names, in file order.</summary>
    public IReadOnlyList<string> Columns { get; }

    /// <summary>One map per data row (column name → value; missing trailing cells → empty). Lookups are
    /// case-insensitive.</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, string>> Rows { get; }

    private CsvData(IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyDictionary<string, string>> rows)
    {
        Columns = columns;
        Rows = rows;
    }

    /// <summary>Parses <paramref name="text"/> (first line = header). Empty input yields no columns/rows.</summary>
    public static CsvData Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        char delimiter = DetectDelimiter(text);
        List<List<string>> records = ParseRecords(text, delimiter);
        if (records.Count == 0) return new CsvData([], []);

        List<string> header = records[0].Select((h, i) => string.IsNullOrWhiteSpace(h) ? $"Column{i + 1}" : h.Trim()).ToList();
        var rows = new List<IReadOnlyDictionary<string, string>>(records.Count - 1);
        for (int r = 1; r < records.Count; r++)
        {
            List<string> fields = records[r];
            // Skip a wholly blank trailing line (common at EOF).
            if (fields.Count == 1 && fields[0].Length == 0) continue;
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int c = 0; c < header.Count; c++)
                map[header[c]] = c < fields.Count ? fields[c] : "";
            rows.Add(map);
        }
        return new CsvData(header, rows);
    }

    /// <summary>Reads and parses a CSV/TSV file.</summary>
    public static CsvData Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>Picks the delimiter whose count is highest on the first non-empty line.</summary>
    private static char DetectDelimiter(string text)
    {
        int nl = text.IndexOfAny(['\r', '\n']);
        string first = nl < 0 ? text : text[..nl];
        int commas = Count(first, ','), tabs = Count(first, '\t'), semis = Count(first, ';');
        if (tabs >= commas && tabs >= semis && tabs > 0) return '\t';
        if (semis > commas && semis > 0) return ';';
        return ',';

        static int Count(string s, char ch) { int n = 0; foreach (char c in s) if (c == ch) n++; return n; }
    }

    /// <summary>Splits the text into records of fields, honouring quoted fields per RFC 4180.</summary>
    private static List<List<string>> ParseRecords(string text, char delimiter)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;
        bool sawAny = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
            }
            else if (c == '"') { inQuotes = true; sawAny = true; }
            else if (c == delimiter) { record.Add(field.ToString()); field.Clear(); sawAny = true; }
            else if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                EndRecord();
            }
            else if (c == '\n') EndRecord();
            else { field.Append(c); sawAny = true; }
        }
        // Flush the final record if the file didn't end with a newline.
        if (field.Length > 0 || record.Count > 0 || sawAny) EndRecord();
        return records;

        void EndRecord()
        {
            record.Add(field.ToString());
            field.Clear();
            records.Add(record);
            record = [];
            sawAny = false;
        }
    }
}
