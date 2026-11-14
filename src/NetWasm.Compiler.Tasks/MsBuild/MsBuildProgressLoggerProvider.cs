using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.Extensions.Logging;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace NetWasm.Compiler.Tasks.MsBuild;

// Writes when a component step's wasm-opt run starts and ends to the build output, as nwc's console writes the
// compile's progress (zion-sati/NetWasm#237). The rest of the compiler's logging is not shown.
internal sealed class MsBuildProgressLoggerProvider(TaskLoggingHelper log) : ILoggerProvider
{
    public const string OptimizationCategory = "NetWasm.Compiler.ComponentModel.Linking.ReportingComponentCoreModuleOptimizer";

    private readonly TaskLoggingHelper _log = log ?? throw new ArgumentNullException(nameof(log));

    public static bool Shows(string? category, LogLevel level) =>
        level >= LogLevel.Information && category == OptimizationCategory;

    public ILogger CreateLogger(string categoryName) => new Logger(_log);

    public void Dispose()
    {
    }

    private sealed class Logger(TaskLoggingHelper log) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            log.LogMessage(MessageImportance.High, formatter(state, exception));
    }
}
