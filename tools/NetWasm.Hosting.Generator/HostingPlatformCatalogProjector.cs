using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel.Catalogs;
using NetWasm.Hosting.Capabilities;
using NetWasm.Hosting.Deployment;

namespace NetWasm.Hosting.Generator;

internal interface IHostingPlatformCatalogProjector
{
    HostingPlatformCatalog Project(
        WitInterfaceCatalog catalog,
        Preview2ShimIdentity shim);
}

internal sealed class HostingPlatformCatalogProjector(
    IWasiPreview2CapabilityClassifier capabilities,
    ITextHasher hashes) : IHostingPlatformCatalogProjector
{
    private readonly IWasiPreview2CapabilityClassifier _capabilities = capabilities ??
        throw new ArgumentNullException(nameof(capabilities));
    private readonly ITextHasher _hashes = hashes ??
        throw new ArgumentNullException(nameof(hashes));

    public HostingPlatformCatalog Project(
        WitInterfaceCatalog catalog,
        Preview2ShimIdentity shim)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(shim);
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
            var capability = _capabilities.Classify(contract.Module);
            if (contract.Functions.IsEmpty)
            {
                continue;
            }
            providers.Add(new(
                contract.Module,
                capability,
                [.. contract.Functions.Select(ProjectFunction)]));
        }
        return new(
            1,
            catalog.World,
            _hashes.Hash(catalog.NormalizedWitJson),
            shim,
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
