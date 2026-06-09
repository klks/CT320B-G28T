namespace CT320B.LabelDesigner.Services;

/// <summary>Severity of a log/toast entry (drives colour + whether a toast pops).</summary>
public enum LogSeverity { Info, Success, Warning, Error }

/// <summary>One timestamped log line.</summary>
public sealed record LogEntry(DateTime Time, LogSeverity Severity, string Message);

/// <summary>
/// Process-wide, in-memory event log (Phase 14a). The app and the printer library (via
/// <see cref="AppLoggerProvider"/>) push entries here; the UI subscribes to <see cref="EntryAdded"/>
/// to show toasts and fill the log panel, replacing blocking message boxes for non-fatal events.
/// Thread-safe; <see cref="EntryAdded"/> may fire on a background thread (the UI marshals).
/// </summary>
public static class AppLog
{
    private const int MaxEntries = 1000;
    private static readonly object Gate = new();
    private static readonly LinkedList<LogEntry> Entries = new();

    /// <summary>Raised after an entry is appended (possibly off the UI thread).</summary>
    public static event Action<LogEntry>? EntryAdded;

    /// <summary>A snapshot of the retained entries, oldest first.</summary>
    public static IReadOnlyList<LogEntry> Snapshot()
    {
        lock (Gate) return Entries.ToList();
    }

    public static void Info(string message) => Add(LogSeverity.Info, message);
    public static void Success(string message) => Add(LogSeverity.Success, message);
    public static void Warn(string message) => Add(LogSeverity.Warning, message);
    public static void Error(string message) => Add(LogSeverity.Error, message);

    public static void Add(LogSeverity severity, string message)
    {
        var entry = new LogEntry(DateTime.Now, severity, message ?? "");
        lock (Gate)
        {
            Entries.AddLast(entry);
            while (Entries.Count > MaxEntries) Entries.RemoveFirst();
        }
        EntryAdded?.Invoke(entry);
    }
}
