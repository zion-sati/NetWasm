using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tasks.ComponentModel;

internal interface IRawBindingSessionFactory
{
    IRawBindingSession Create(ExternalToolCommand wasmToolsCommand);
}
