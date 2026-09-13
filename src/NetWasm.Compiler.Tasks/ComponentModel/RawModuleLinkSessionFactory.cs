using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Node;
using NetWasm.Compiler.ComponentModel.Raw;

namespace NetWasm.Compiler.Tasks.ComponentModel;

internal sealed class RawModuleLinkSessionFactory(
    Func<ExternalToolCommand, BinaryenToolRunnerConfiguration,
        IRawModuleLinkSession> create) : IRawModuleLinkSessionFactory
{
    private readonly Func<ExternalToolCommand, BinaryenToolRunnerConfiguration,
        IRawModuleLinkSession> _create = create ??
        throw new ArgumentNullException(nameof(create));

    public IRawModuleLinkSession Create(
        ExternalToolCommand wasmToolsCommand,
        BinaryenToolRunnerConfiguration binaryenConfiguration)
    {
        ArgumentNullException.ThrowIfNull(wasmToolsCommand);
        ArgumentNullException.ThrowIfNull(binaryenConfiguration);
        return _create(wasmToolsCommand, binaryenConfiguration);
    }
}

internal sealed class RawModuleLinkSession(
    ServiceProvider services,
    IRawModuleLinker linker) : IRawModuleLinkSession
{
    private readonly IRawModuleLinker _linker = linker ??
        throw new ArgumentNullException(nameof(linker));

    public void Link(RawModuleLinkRequest request) => _linker.Link(request);

    public void Dispose() => services.Dispose();
}
