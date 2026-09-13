namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerSuccessDiagnosticArtifactWriter
{
    void WriteSuccess(CompilerOptions options);
}
