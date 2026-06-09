using System.Text.Json;

namespace CT320B.LabelDesigner.Services;

/// <summary>
/// Persisted application settings (Phase 9) stored as JSON under <see cref="AppPaths.SettingsFile"/>:
/// window placement, the view toggles (grid/snap/safe-margin), the last device, and the print-offset
/// calibration <b>keyed by printer</b> so a unit's calibration applies to every label across sessions.
/// Loading/saving is best-effort (a corrupt or missing file falls back to defaults).
/// </summary>
public sealed class AppSettings
{
    /// <summary>The default print offset for an uncalibrated printer (this CT320B lands ~1 mm low+right).</summary>
    public const float DefaultOffsetXMm = -1f;
    public const float DefaultOffsetYMm = -1f;

    private const string DefaultKey = "(default)";

    public int WindowWidth { get; set; } = 1260;
    public int WindowHeight { get; set; } = 760;
    public int WindowX { get; set; } = int.MinValue;   // MinValue = centre on first run
    public int WindowY { get; set; } = int.MinValue;
    public bool Maximized { get; set; }

    public bool ShowGrid { get; set; }
    public bool SnapToGrid { get; set; }
    public bool ShowSafeMargin { get; set; } = true;

    /// <summary>Whether the bottom event-log panel is shown (Phase 14a).</summary>
    public bool ShowLog { get; set; }

    /// <summary>Measurement unit for rulers / size dialogs (Phase 14d). Storage is always mm.</summary>
    public Core.Model.MeasurementUnit Unit { get; set; } = Core.Model.MeasurementUnit.Millimeters;

    /// <summary>UI language code (e.g. "en", "fr", or a user-added code); applied at startup. Restart to change.</summary>
    public string LanguageCode { get; set; } = "en";

    /// <summary>Display key of the last connected device (informational).</summary>
    public string? LastDeviceKey { get; set; }

    /// <summary>Per-printer print offset: device key → [xMm, yMm].</summary>
    public Dictionary<string, float[]> PrinterOffsets { get; set; } = [];

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(AppPaths.SettingsFile)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile)) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            AppPaths.EnsureDirectories();
            File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best-effort */ }
    }

    /// <summary>The saved print offset (mm) for a printer key, or the default when none is stored.</summary>
    public (float X, float Y) OffsetFor(string? key)
    {
        if (PrinterOffsets.TryGetValue(key ?? DefaultKey, out float[]? v) && v.Length == 2)
            return (v[0], v[1]);
        return (DefaultOffsetXMm, DefaultOffsetYMm);
    }

    /// <summary>Records the print offset (mm) for a printer key.</summary>
    public void SetOffset(string? key, float x, float y) => PrinterOffsets[key ?? DefaultKey] = [x, y];
}
