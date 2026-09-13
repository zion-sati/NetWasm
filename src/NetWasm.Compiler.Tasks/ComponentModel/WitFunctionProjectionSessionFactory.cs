using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Catalogs;

namespace NetWasm.Compiler.Tasks.ComponentModel;

internal sealed class WitFunctionProjectionSessionFactory(
    Func<ExternalToolCommand, WitFunctionProjectionSession> factory) :
    IWitFunctionProjectionSessionFactory
{
    private readonly Func<ExternalToolCommand, WitFunctionProjectionSession> _factory = factory ??
        throw new ArgumentNullException(nameof(factory));

    public IWitFunctionProjectionSession Create(ExternalToolCommand wasmToolsCommand)
    {
        ArgumentNullException.ThrowIfNull(wasmToolsCommand);
        return _factory(wasmToolsCommand);
    }
}

internal sealed class WitFunctionProjectionSession(
    ServiceProvider services,
    IWitDocumentReader documents,
    IWitWorldFunctionProjector functions) : IWitFunctionProjectionSession
{
    private readonly ServiceProvider _services = services ??
        throw new ArgumentNullException(nameof(services));
    private readonly IWitDocumentReader _documents = documents ??
        throw new ArgumentNullException(nameof(documents));
    private readonly IWitWorldFunctionProjector _functions = functions ??
        throw new ArgumentNullException(nameof(functions));

    public WitWorldFunctionProjection Project(string witPath, string? world)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(witPath);
        var document = _documents.Read(witPath);
        return _functions.Project(document, document.SelectWorld(world));
    }

    public void Dispose() => _services.Dispose();
}
