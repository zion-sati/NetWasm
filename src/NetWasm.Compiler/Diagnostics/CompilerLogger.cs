using Microsoft.Extensions.Logging;
using System;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerLogger<T>(
    ICompilerDiagnosticSink sink,
    ICompilerDiagnosticScopePusher scopePusher,
    ICompilerDiagnosticScopeReader scopeReader) : ILogger<T>
{
    private static readonly string Category = typeof(T).FullName!;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull =>
        state is CompilerDiagnosticScope scope
            ? scopePusher.Push(scope)
            : NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) =>
        logLevel is not LogLevel.None && scopeReader.Read() is { IsEnabled: true };

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        var scope = scopeReader.Read();
        if (logLevel is LogLevel.None || scope is not { IsEnabled: true })
        {
            return;
        }

        sink.Write(new CompilerDiagnosticWrite(
            scope.Path!,
            new CompilerDiagnosticEntry(
                Category,
                logLevel,
                eventId,
                formatter(state, exception),
                exception?.GetType().FullName,
                exception?.Message,
            exception?.StackTrace)));
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
