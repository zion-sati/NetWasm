using NetWasm.Compiler.Core.ManagedExecutables;

namespace NetWasm.Compiler.Browser.Results;

internal interface IBrowserCompilationResultProjector
{
    BrowserCompilationResult Project(
        CompilationResult result, CompilerOptions options, ManagedExecutableEntryPointAbi? entryPointAbi);
}
