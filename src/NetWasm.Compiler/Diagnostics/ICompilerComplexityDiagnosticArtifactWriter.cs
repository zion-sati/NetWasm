namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerComplexityDiagnosticArtifactWriter
{
    void WriteComplexity(CompilerOptions options, CompilerComplexityReport report);
}
