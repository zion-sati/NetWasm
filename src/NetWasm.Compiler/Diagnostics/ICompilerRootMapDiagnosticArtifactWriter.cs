namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerRootMapDiagnosticArtifactWriter
{
    void WriteRootMaps(CompilerOptions options, ReachableProgram program);
}
