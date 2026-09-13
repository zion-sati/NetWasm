namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerDiagnosticScopeReader(CompilerDiagnosticScopeState state)
    : ICompilerDiagnosticScopeReader
{
    public CompilerDiagnosticScope? Read() => state.Current.Value?.Scope;
}
