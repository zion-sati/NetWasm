using NetWasm.Compiler.ComponentModel.Catalogs;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tasks.ComponentModel;

internal interface IWitFunctionProjectionSession : IDisposable
{
    WitWorldFunctionProjection Project(string witPath, string? world);
}

internal interface IWitFunctionProjectionSessionFactory
{
    IWitFunctionProjectionSession Create(ExternalToolCommand wasmToolsCommand);
}
