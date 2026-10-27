using Microsoft.Extensions.Logging;
using System;

namespace NetWasm.Compiler.Cli;

// Writes what nwc reports to its console as csc writes it: progress as plain
// lines, and diagnostics as "error NW1234: message", which MSBuild reads as an
// error or a warning with its code. Only nwc's own entries and the compiler's
// progress reach it; the rest of the compiler's logging stays in its diagnostic
// log.
internal sealed class NwcConsoleLoggerProvider(IConsole console) : ILoggerProvider
{
    public const string ProgressCategory = "NetWasm.Compiler.Diagnostics.CompilerDiagnosticProgressReporter";
    private const string NwcCategoryPrefix = "NetWasm.Compiler.Cli.";

    private readonly IConsole _console = console ?? throw new ArgumentNullException(nameof(console));

    public static bool Shows(string? category, LogLevel level) =>
        level >= LogLevel.Information && category is not null &&
        (category == ProgressCategory || category.StartsWith(NwcCategoryPrefix, StringComparison.Ordinal));

    public ILogger CreateLogger(string categoryName) => new Logger(_console);

    public void Dispose()
    {
    }

    private sealed class Logger(IConsole console) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            var message = formatter(state, exception);
            console.WriteLine(logLevel switch
            {
                LogLevel.Warning => $"warning {Code(eventId)}: {message}",
                LogLevel.Error or LogLevel.Critical => $"error {Code(eventId)}: {message}",
                _ => message,
            });
        }

        private static string Code(EventId eventId) => eventId.Name ?? $"NW{eventId.Id:0000}";
    }
}
