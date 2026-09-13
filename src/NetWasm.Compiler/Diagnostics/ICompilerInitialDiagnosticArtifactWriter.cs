namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerInitialDiagnosticArtifactWriter
{
    void WriteInitialArtifacts(CompilerOptions options);
}
