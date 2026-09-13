using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public sealed class MetadataBaseTypeIdentityResolverFactory(
    IMetadataCompilationMaterializationFactory materializations,
    ITypeDefinitionResolverFactory definitions,
    ITypeIdentityResolverFactory identities) : IBaseTypeIdentityResolverFactory
{
    private readonly IMetadataCompilationMaterializationFactory _materializations =
        materializations ?? throw new ArgumentNullException(nameof(materializations));
    private readonly ITypeDefinitionResolverFactory _definitions =
        definitions ?? throw new ArgumentNullException(nameof(definitions));
    private readonly ITypeIdentityResolverFactory _identities =
        identities ?? throw new ArgumentNullException(nameof(identities));

    public IBaseTypeIdentityResolver Create(MetadataCompilationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var materialized = _materializations.Create(snapshot);
        var availability = new MetadataAvailabilityValidator(new MetadataLifetime());
        var assemblies = new MetadataAssemblyResolver(
            materialized.MetadataAssemblies,
            materialized.ReferenceAssemblyAliases,
            availability);
        var definitions = _definitions.Create(snapshot);
        var metadataTypes = new MetadataTypeResolver(definitions);
        var signatures = new MetadataSignatureTypeResolver(
            metadataTypes,
            _identities.Create(snapshot));
        return new MetadataBaseTypeIdentityResolver(
            definitions,
            assemblies,
            signatures);
    }
}
