namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerDiagnosticProvenanceProvider
{
    object ProvideProvenance(CompilerOptions options);
}
