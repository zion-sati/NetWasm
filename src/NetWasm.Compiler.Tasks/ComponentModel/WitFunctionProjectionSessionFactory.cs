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

    public WitWorldFunctionProjection Project(
        string witPath,
        string? world,
        string? applicationWitPath,
        string? applicationWorld,
        string? sourceWorkerWorld)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(witPath);
        var document = _documents.Read(witPath);
        var packagedWorld = document.SelectWorld(world);
        if (applicationWitPath is null)
        {
            return _functions.Project(
                document,
                packagedWorld,
                applicationWorld is null ? null : document.SelectWorld(applicationWorld));
        }
        var applicationDocument = _documents.Read(applicationWitPath);
        return _functions.Project(
            document,
            packagedWorld,
            applicationDocument,
            applicationDocument.SelectWorld(applicationWorld),
            applicationDocument.SelectWorld(sourceWorkerWorld));
    }

    public void Dispose() => _services.Dispose();
}
