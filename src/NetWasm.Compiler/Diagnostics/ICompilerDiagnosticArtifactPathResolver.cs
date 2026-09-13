namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerDiagnosticArtifactPathResolver
{
    string? ResolvePath(CompilerOptions options, string artifactName);
}
