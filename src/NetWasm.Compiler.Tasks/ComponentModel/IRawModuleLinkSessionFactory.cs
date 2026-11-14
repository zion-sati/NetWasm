using Microsoft.Extensions.Logging;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Node;

namespace NetWasm.Compiler.Tasks.ComponentModel;

internal interface IRawModuleLinkSessionFactory
{
    IRawModuleLinkSession Create(
        ExternalToolCommand wasmToolsCommand,
        BinaryenToolRunnerConfiguration binaryenConfiguration,
        ILoggerProvider progress);
}
