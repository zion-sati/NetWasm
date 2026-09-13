using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tasks.Compilation;

internal interface IManagedModuleCompilationSessionFactory
{
    IManagedModuleCompilationSession Create(ExternalToolCommand wasmToolsCommand);
}
