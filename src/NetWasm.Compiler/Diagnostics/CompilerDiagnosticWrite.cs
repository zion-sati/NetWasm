namespace NetWasm.Compiler.Diagnostics;

internal sealed record CompilerDiagnosticWrite(
    string Path,
    CompilerDiagnosticEntry Entry);
