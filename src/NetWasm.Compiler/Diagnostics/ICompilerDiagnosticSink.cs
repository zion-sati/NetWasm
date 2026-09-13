namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerDiagnosticSink
{
    void Write(CompilerDiagnosticWrite write);
}
