using Microsoft.Extensions.Logging;
using System;

namespace NetWasm.Compiler.Diagnostics;

// The compiler's own logging provider: the diagnostic log file. Hosts add
// theirs, such as nwc's console.
internal sealed class CompilerDiagnosticFileLoggerProvider(
    ICompilerDiagnosticSink sink,
    ICompilerDiagnosticScopePusher scopePusher,
    ICompilerDiagnosticScopeReader scopeReader) : ILoggerProvider
{
    private readonly ICompilerDiagnosticSink _sink = sink ??
        throw new ArgumentNullException(nameof(sink));
    private readonly ICompilerDiagnosticScopePusher _scopePusher = scopePusher ??
        throw new ArgumentNullException(nameof(scopePusher));
    private readonly ICompilerDiagnosticScopeReader _scopeReader = scopeReader ??
        throw new ArgumentNullException(nameof(scopeReader));

    public ILogger CreateLogger(string categoryName)
    {
        ArgumentNullException.ThrowIfNull(categoryName);
        return new CompilerDiagnosticFileLogger(categoryName, _sink, _scopePusher, _scopeReader);
    }

    public void Dispose()
    {
    }
}
