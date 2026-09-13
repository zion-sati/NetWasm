using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public sealed class MetadataEntityBaseTypeResolverFactory(
    IMetadataCompilationMaterializationFactory materializations,
    ITypeDefinitionResolverFactory definitions) : IMetadataEntityBaseTypeResolverFactory
{
    private readonly IMetadataCompilationMaterializationFactory _materializations =
        materializations ?? throw new ArgumentNullException(nameof(materializations));
    private readonly ITypeDefinitionResolverFactory _definitions =
        definitions ?? throw new ArgumentNullException(nameof(definitions));

    public IMetadataEntityBaseTypeResolver Create(MetadataCompilationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var materialized = _materializations.Create(snapshot);
        var availability = new MetadataAvailabilityValidator(new MetadataLifetime());
        return new MetadataBaseTypeResolver(
            new MetadataAssemblyResolver(
                materialized.MetadataAssemblies,
                materialized.ReferenceAssemblyAliases,
                availability),
            new MetadataTypeResolver(_definitions.Create(snapshot)));
    }
}
