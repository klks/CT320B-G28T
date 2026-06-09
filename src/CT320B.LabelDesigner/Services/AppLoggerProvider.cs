using Microsoft.Extensions.Logging;

namespace CT320B.LabelDesigner.Services;

/// <summary>
/// Adapts the printer library's <see cref="ILogger"/> output into <see cref="AppLog"/> so device
/// connect/print diagnostics flow into the app's log panel and toasts (Phase 14a). Map: Error/Critical
/// → <see cref="LogSeverity.Error"/>, Warning → Warning, everything else → Info. Below
/// <see cref="LogLevel.Information"/> is dropped to keep the panel readable.
/// </summary>
public sealed class AppLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new AppLogger();

    public void Dispose() { }

    private sealed class AppLogger : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            string message = formatter(state, exception);
            if (exception is not null) message += $" — {exception.Message}";
            AppLog.Add(Map(logLevel), message);
        }

        private static LogSeverity Map(LogLevel level) => level switch
        {
            LogLevel.Error or LogLevel.Critical => LogSeverity.Error,
            LogLevel.Warning => LogSeverity.Warning,
            _ => LogSeverity.Info,
        };
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
