namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerDiagnosticProvenanceProvider
{
    CompilerProvenanceArtifact ProvideProvenance(CompilerOptions options);
}
