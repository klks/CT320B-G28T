namespace CT320B.LabelDesigner.Services;

/// <summary>
/// Storage locations for the designer. Per-user data (saved labels, recent-files list) lives under
/// <c>%AppData%\CT320B.LabelDesigner\</c> (Decision D6); the <c>Templates\{User,Public}</c> template
/// folders ship beside the exe and are resolved relative to the app.
/// </summary>
public static class AppPaths
{
    /// <summary>The label/template document file extension.</summary>
    public const string Extension = ".ct320b.json";

    /// <summary>An <see cref="OpenFileDialog"/> / <see cref="SaveFileDialog"/> filter for label files.</summary>
    public const string FileFilter = "CT320B label (*.ct320b.json)|*.ct320b.json|All files (*.*)|*.*";

    /// <summary><c>%AppData%\CT320B.LabelDesigner</c>.</summary>
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CT320B.LabelDesigner");

    /// <summary>The saved-labels library.</summary>
    public static string LabelsDir { get; } = Path.Combine(Root, "Labels");

    /// <summary>A user-writable templates folder (<c>%AppData%\…\Templates</c>). Drop <c>.ct320b.json</c> /
    /// <c>.ddl</c> files here and they appear in the gallery on Refresh — unlike the bundled beside-exe
    /// folders, this isn't a build artifact, so additions show without a rebuild.</summary>
    public static string MyTemplatesDir { get; } = Path.Combine(Root, "Templates");

    /// <summary>User templates that ship beside the exe under <c>Templates\User\</c> (the user's own
    /// <c>.ddl</c> / <c>.ct320b.json</c> starting points). Resolved at runtime relative to the app.</summary>
    public static string UserTemplatesDir { get; } =
        Path.Combine(AppContext.BaseDirectory, "Templates", "User");

    /// <summary>Bundled, read-only "public" templates that ship beside the exe (Clabel <c>.ddl</c>
    /// files). Populated at build time from <c>Templates\Public\</c>; not user-writable.</summary>
    public static string PublicTemplatesDir { get; } =
        Path.Combine(AppContext.BaseDirectory, "Templates", "Public");

    /// <summary>Bundled clip-art / emoji PNGs (in category subfolders) that ship beside the exe under
    /// <c>Assets\Clipart\</c>, offered by the Insert ▸ Clip-art picker.</summary>
    public static string ClipartDir { get; } = Path.Combine(AppContext.BaseDirectory, "Assets", "Clipart");

    /// <summary>On-disk cache of images downloaded from the web (keyed by URL), so a template's CDN
    /// background/image isn't re-fetched every time it's opened.</summary>
    public static string ImageCacheDir { get; } = Path.Combine(Root, "ImageCache");

    /// <summary>The recent-files list (JSON).</summary>
    public static string RecentFile { get; } = Path.Combine(Root, "recent.json");

    /// <summary>Crash-recovery snapshots of open tabs (Phase 14b): one <c>.ct320b.json</c> per dirty tab
    /// plus a <c>manifest.json</c> recording each tab's original path/title.</summary>
    public static string RecoveryDir { get; } = Path.Combine(Root, "Recovery");

    /// <summary>UI language folder beside the exe: one <c>&lt;code&gt;.json</c> per language (shipped) plus any
    /// the user drops in (see <see cref="Loc"/>). Resolved relative to the app, like the template folders.</summary>
    public static string LangDir { get; } = Path.Combine(AppContext.BaseDirectory, "lang");

    /// <summary>Persisted app settings (window state, view prefs, per-printer calibration).</summary>
    public static string SettingsFile { get; } = Path.Combine(Root, "settings.json");

    /// <summary>Creates the per-user storage directories if they don't exist (idempotent). The
    /// app-relative <c>Templates\{User,Public}</c> folders ship with the app and aren't created here.</summary>
    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(LabelsDir);
        Directory.CreateDirectory(MyTemplatesDir);
    }

    /// <summary>Creates the recovery directory if needed and returns it.</summary>
    public static string EnsureRecoveryDir()
    {
        Directory.CreateDirectory(RecoveryDir);
        return RecoveryDir;
    }
}
