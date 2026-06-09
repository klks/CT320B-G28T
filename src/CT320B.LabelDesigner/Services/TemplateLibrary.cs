using System.Text.Json;
using CT320B.LabelDesigner.Core.Model;
using CT320B.LabelDesigner.Core.Serialization;

namespace CT320B.LabelDesigner.Services;

/// <summary>A document file on disk (a saved label or a template, either <c>.ct320b.json</c> or <c>.ddl</c>).</summary>
public sealed record LabelFileEntry(string Name, string Path, DateTime Modified);

/// <summary>
/// The templates &amp; labels library (Phase 8): saves/opens <c>.ct320b.json</c> documents, manages a
/// per-user saved-labels store and a recent-files list under <c>%AppData%</c> (Decision D6), and lists
/// the bundled <b>User</b> and <b>Public</b> template folders that ship beside the exe. Pure file/IO +
/// model — no WinForms.
/// </summary>
public sealed class TemplateLibrary
{
    private const int MaxRecent = 10;
    private readonly List<string> _recent;

    public TemplateLibrary()
    {
        AppPaths.EnsureDirectories();
        _recent = LoadRecent();
    }

    /// <summary>Most-recently-used document paths (existing files only), newest first.</summary>
    public IReadOnlyList<string> RecentFiles => _recent;

    /// <summary>Lists the saved labels in the per-user library, newest first.</summary>
    public IReadOnlyList<LabelFileEntry> SavedLabels() => ListDir(AppPaths.LabelsDir);

    /// <summary>Lists user templates (<c>.ct320b.json</c> + <c>.ddl</c>): the bundled beside-exe
    /// <c>Templates\User</c> plus the user-writable <c>%AppData%\…\Templates</c> drop folder, name-sorted.</summary>
    public IReadOnlyList<LabelFileEntry> UserTemplates() =>
        ListTemplateFiles(AppPaths.UserTemplatesDir)
            .Concat(ListTemplateFiles(AppPaths.MyTemplatesDir))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Lists the bundled public Clabel <c>.ddl</c> templates from <c>Templates\Public</c>.</summary>
    public IReadOnlyList<LabelFileEntry> PublicTemplates() => ListTemplateFiles(AppPaths.PublicTemplatesDir);

    /// <summary>Lists template files (<c>.ct320b.json</c> + <c>.ddl</c>) under <paramref name="dir"/>
    /// recursively, name-sorted.</summary>
    private static IReadOnlyList<LabelFileEntry> ListTemplateFiles(string dir)
    {
        if (!Directory.Exists(dir)) return [];
        return Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".ddl", StringComparison.OrdinalIgnoreCase)
                     || p.EndsWith(AppPaths.Extension, StringComparison.OrdinalIgnoreCase))
            .Select(p => new LabelFileEntry(StripExtension(System.IO.Path.GetFileName(p)), p, File.GetLastWriteTime(p)))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Loads a document from a path and records it in the recent list.</summary>
    public LabelDocument Open(string path)
    {
        LabelDocument doc = LabelJson.Load(path);
        AddRecent(path);
        return doc;
    }

    /// <summary>Saves a document to a path and records it in the recent list.</summary>
    public void Save(LabelDocument document, string path)
    {
        LabelJson.Save(document, path);
        AddRecent(path);
    }

    /// <summary>Saves a copy into the saved-labels library under <paramref name="name"/> and returns
    /// its path.</summary>
    public string SaveToLibrary(LabelDocument document, string name)
    {
        string path = System.IO.Path.Combine(AppPaths.LabelsDir, SafeFileName(name));
        Save(document, path);
        return path;
    }

    /// <summary>Saves a copy as a user template (in <c>Templates\User</c>) under <paramref name="name"/>
    /// and returns its path.</summary>
    public string SaveAsTemplate(LabelDocument document, string name)
    {
        Directory.CreateDirectory(AppPaths.UserTemplatesDir);
        string path = System.IO.Path.Combine(AppPaths.UserTemplatesDir, SafeFileName(name));
        LabelJson.Save(document, path);   // templates aren't "recent documents"
        return path;
    }

    /// <summary>A default save path in the labels library for a document's name.</summary>
    public static string DefaultLibraryPath(string name) =>
        System.IO.Path.Combine(AppPaths.LabelsDir, SafeFileName(name));

    // --- recent files ---

    private void AddRecent(string path)
    {
        string full = System.IO.Path.GetFullPath(path);
        _recent.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
        _recent.Insert(0, full);
        if (_recent.Count > MaxRecent) _recent.RemoveRange(MaxRecent, _recent.Count - MaxRecent);
        SaveRecent();
    }

    private List<string> LoadRecent()
    {
        try
        {
            if (!File.Exists(AppPaths.RecentFile)) return [];
            List<string>? list = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(AppPaths.RecentFile));
            return list?.Where(File.Exists).ToList() ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private void SaveRecent()
    {
        try { File.WriteAllText(AppPaths.RecentFile, JsonSerializer.Serialize(_recent)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best-effort */ }
    }

    private static IReadOnlyList<LabelFileEntry> ListDir(string dir)
    {
        if (!Directory.Exists(dir)) return [];
        return Directory.EnumerateFiles(dir, "*" + AppPaths.Extension)
            .Select(p => new LabelFileEntry(StripExtension(System.IO.Path.GetFileName(p)), p, File.GetLastWriteTime(p)))
            .OrderByDescending(e => e.Modified)
            .ToList();
    }

    private static string StripExtension(string fileName) =>
        fileName.EndsWith(AppPaths.Extension, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^AppPaths.Extension.Length]
            : System.IO.Path.GetFileNameWithoutExtension(fileName);

    private static string SafeFileName(string name)
    {
        string trimmed = string.IsNullOrWhiteSpace(name) ? "label" : name.Trim();
        foreach (char c in System.IO.Path.GetInvalidFileNameChars())
            trimmed = trimmed.Replace(c, '_');
        return trimmed + AppPaths.Extension;
    }
}
