namespace NetWasm.Compiler.Browser.Results;

internal interface IBrowserCompilationResultProjector
{
    BrowserCompilationResult Project(CompilationResult result, CompilerOptions options);
}
