namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerDiagnosticLogPathResolver
{
    string? Resolve(CompilerOptions options);
}
