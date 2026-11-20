// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics.CodeAnalysis;

namespace NetWasm.Compiler.Diagnostics;

// Writes a category's entries to the diagnostic log of the active compilation
// scope, which a compilation opens only when it asks for a diagnostic log.
internal sealed class CompilerDiagnosticFileLogger(
    string category,
    ICompilerDiagnosticSink sink,
    ICompilerDiagnosticScopePusher scopePusher,
    ICompilerDiagnosticScopeReader scopeReader) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull =>
        state is CompilerDiagnosticScope scope
            ? scopePusher.Push(scope)
            : NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => Keeps(scopeReader.Read(), logLevel);

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        var scope = scopeReader.Read();
        if (!Keeps(scope, logLevel))
        {
            return;
        }

        sink.Write(new CompilerDiagnosticWrite(
            scope.Path!,
            new CompilerDiagnosticEntry(
                category,
                logLevel,
                eventId,
                formatter(state, exception),
                exception?.GetType().FullName,
                exception?.Message,
            exception?.StackTrace)));
    }

    private static bool Keeps([NotNullWhen(true)] CompilerDiagnosticScope? scope, LogLevel logLevel) =>
        logLevel is not LogLevel.None && scope is { IsEnabled: true } && logLevel >= scope.MinimumLevel;

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
