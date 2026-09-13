using System.Threading;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerDiagnosticScopeState
{
    public AsyncLocal<CompilerDiagnosticScopeFrame?> Current { get; } = new();
}

internal sealed record CompilerDiagnosticScopeFrame(
    CompilerDiagnosticScope Scope,
    CompilerDiagnosticScopeFrame? Parent);
