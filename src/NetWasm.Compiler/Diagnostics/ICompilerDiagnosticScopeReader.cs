namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerDiagnosticScopeReader
{
    CompilerDiagnosticScope? Read();
}
