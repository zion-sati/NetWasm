using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public sealed class MetadataImplementedInterfaceResolverFactory(
    IMetadataCompilationMaterializationFactory materializations,
    ITypeDefinitionResolverFactory definitions,
    ITypeIdentityResolverFactory identities) : IImplementedInterfaceResolverFactory
{
    private readonly IMetadataCompilationMaterializationFactory _materializations =
        materializations ?? throw new ArgumentNullException(nameof(materializations));
    private readonly ITypeDefinitionResolverFactory _definitions =
        definitions ?? throw new ArgumentNullException(nameof(definitions));
    private readonly ITypeIdentityResolverFactory _identities =
        identities ?? throw new ArgumentNullException(nameof(identities));

    public IImplementedInterfaceResolver Create(MetadataCompilationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var materialized = _materializations.Create(snapshot);
        var availability = new MetadataAvailabilityValidator(new MetadataLifetime());
        var definitions = _definitions.Create(snapshot);
        var metadataTypes = new MetadataTypeResolver(definitions);
        return new MetadataImplementedInterfaceResolver(
            definitions,
            new MetadataAssemblyResolver(
                materialized.MetadataAssemblies,
                materialized.ReferenceAssemblyAliases,
                availability),
            new MetadataSignatureTypeResolver(
                metadataTypes,
                _identities.Create(snapshot)));
    }
}
