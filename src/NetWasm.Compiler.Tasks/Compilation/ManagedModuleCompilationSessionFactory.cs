using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tasks.Compilation;

internal sealed class ManagedModuleCompilationSessionFactory(
    Func<ExternalToolCommand, IManagedModuleCompilationSession> create) :
    IManagedModuleCompilationSessionFactory
{
    private readonly Func<ExternalToolCommand, IManagedModuleCompilationSession> _create =
        create ?? throw new ArgumentNullException(nameof(create));

    public IManagedModuleCompilationSession Create(ExternalToolCommand wasmToolsCommand)
    {
        ArgumentNullException.ThrowIfNull(wasmToolsCommand);
        return _create(wasmToolsCommand);
    }
}
