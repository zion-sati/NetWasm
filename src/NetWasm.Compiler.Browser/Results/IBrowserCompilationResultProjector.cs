using NetWasm.Compiler.Core.ManagedExecutables;

namespace NetWasm.Compiler.Browser.Results;

internal interface IBrowserCompilationResultProjector
{
    BrowserCompilationResult Project(
        ModuleCompilationResult result, CompilerOptions options, ManagedExecutableEntryPointAbi? entryPointAbi);
}
