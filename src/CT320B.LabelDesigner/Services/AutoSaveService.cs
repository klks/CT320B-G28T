using System.Text.Json;
using CT320B.LabelDesigner.Core.Model;
using CT320B.LabelDesigner.Core.Serialization;

namespace CT320B.LabelDesigner.Services;

/// <summary>A recovered tab: the document plus its original file path / title.</summary>
public sealed record RecoveredDocument(LabelDocument Document, string? OriginalPath, string Title);

/// <summary>
/// Periodic crash-recovery autosave of open tabs (Phase 14b). On a timer it snapshots every <i>dirty</i>
/// tab to <see cref="AppPaths.RecoveryDir"/> (one file per tab + a manifest of original paths/titles) and
/// prunes snapshots for tabs that were saved or closed. On a clean shutdown the shell calls
/// <see cref="ClearAll"/>; if files survive (a crash), <see cref="LoadRecovery"/> offers them at startup.
/// </summary>
public sealed class AutoSaveService : IDisposable
{
    /// <summary>A live view of one open tab for snapshotting.</summary>
    public sealed record TabSnapshot(string Id, string? OriginalPath, string Title, bool Dirty, LabelDocument Document);

    private sealed record ManifestEntry(string Id, string? Path, string Title);

    private const string ManifestName = "manifest.json";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly Func<IReadOnlyList<TabSnapshot>> _provider;

    public AutoSaveService(Func<IReadOnlyList<TabSnapshot>> provider, int intervalMs = 20_000)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _timer.Interval = intervalMs;
        _timer.Tick += (_, _) => Snapshot();
    }

    public void Start() => _timer.Start();

    /// <summary>Writes a snapshot of all currently-dirty tabs and prunes the rest. Best-effort.</summary>
    public void Snapshot()
    {
        try
        {
            IReadOnlyList<TabSnapshot> tabs = _provider();
            string dir = AppPaths.EnsureRecoveryDir();
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var manifest = new List<ManifestEntry>();

            foreach (TabSnapshot tab in tabs)
            {
                if (!tab.Dirty) continue;
                string file = Path.Combine(dir, tab.Id + AppPaths.Extension);
                File.WriteAllText(file, LabelJson.Serialize(tab.Document));
                keep.Add(Path.GetFileName(file));
                manifest.Add(new ManifestEntry(tab.Id, tab.OriginalPath, tab.Title));
            }

            if (manifest.Count == 0) { ClearAll(); return; }

            File.WriteAllText(Path.Combine(dir, ManifestName), JsonSerializer.Serialize(manifest, Json));
            keep.Add(ManifestName);

            // Drop snapshots for tabs that were saved or closed since the last tick.
            foreach (string path in Directory.EnumerateFiles(dir))
                if (!keep.Contains(Path.GetFileName(path)))
                    TryDelete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            /* best-effort; a failed autosave must never disrupt editing */
        }
    }

    /// <summary>Deletes all recovery files (called on a clean shutdown).</summary>
    public static void ClearAll()
    {
        try
        {
            if (!Directory.Exists(AppPaths.RecoveryDir)) return;
            foreach (string path in Directory.EnumerateFiles(AppPaths.RecoveryDir)) TryDelete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Reads any surviving recovery snapshots (a crash left them behind). Empty if none.</summary>
    public static IReadOnlyList<RecoveredDocument> LoadRecovery()
    {
        var result = new List<RecoveredDocument>();
        try
        {
            string manifestPath = Path.Combine(AppPaths.RecoveryDir, ManifestName);
            if (!File.Exists(manifestPath)) return result;
            List<ManifestEntry>? entries = JsonSerializer.Deserialize<List<ManifestEntry>>(File.ReadAllText(manifestPath), Json);
            if (entries is null) return result;
            foreach (ManifestEntry e in entries)
            {
                string file = Path.Combine(AppPaths.RecoveryDir, e.Id + AppPaths.Extension);
                if (!File.Exists(file)) continue;
                try { result.Add(new RecoveredDocument(LabelJson.Load(file), e.Path, e.Title)); }
                catch (Exception ex) when (ex is IOException or JsonException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return result;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public void Dispose() => _timer.Dispose();
}
