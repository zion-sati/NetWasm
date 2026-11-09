using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel.Catalogs;
using NetWasm.Hosting.Capabilities;
using NetWasm.Hosting.Deployment;

namespace NetWasm.Hosting.Generator;

internal interface IHostingPlatformCatalogProjector
{
    HostingPlatformCatalog Project(WitInterfaceCatalog catalog);
}

internal sealed class HostingPlatformCatalogProjector(
    IWasiCapabilityClassifier capabilities,
    ITextHasher hashes) : IHostingPlatformCatalogProjector
{
    private readonly IWasiCapabilityClassifier _capabilities = capabilities ??
        throw new ArgumentNullException(nameof(capabilities));
    private readonly ITextHasher _hashes = hashes ??
        throw new ArgumentNullException(nameof(hashes));

    public HostingPlatformCatalog Project(WitInterfaceCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (catalog.Interfaces.IsDefaultOrEmpty
            || string.IsNullOrWhiteSpace(catalog.World)
            || string.IsNullOrWhiteSpace(catalog.NormalizedWitJson))
        {
            throw new InvalidDataException("WIT interface catalog is incomplete.");
        }

        var providers = ImmutableArray.CreateBuilder<NetWasmPlatformImportProvider>();
        foreach (var contract in catalog.Interfaces)
        {
            if (contract is null || contract.Functions.IsDefault)
            {
                throw new InvalidDataException("WIT interface contract is incomplete.");
            }
            // An interface the world imports with no functions (wasi:cli/terminal-input) defines
            // resources, which a JavaScript host supplies as classes: it is a provider too.
            var capability = _capabilities.Classify(contract.Module);
            providers.Add(new(
                contract.Module,
                capability,
                [.. contract.Functions.Select(ProjectFunction)]));
        }
        return new(
            2,
            catalog.World,
            _hashes.Hash(catalog.NormalizedWitJson),
            [.. providers]);
    }

    private static DeploymentFunction ProjectFunction(WitInterfaceFunction function)
    {
        if (function is null)
        {
            throw new InvalidDataException("WIT interface function is missing.");
        }
        return new(
            function.Interface,
            function.Name,
            function.Parameters,
            function.Results);
    }
}
