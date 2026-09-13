using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public sealed class MetadataTypeDefinitionResolverFactory(
    IMetadataCompilationMaterializationFactory materializations,
    ITypeFinderFactory finders) : ITypeDefinitionResolverFactory
{
    private readonly IMetadataCompilationMaterializationFactory _materializations =
        materializations ?? throw new ArgumentNullException(nameof(materializations));
    private readonly ITypeFinderFactory _finders =
        finders ?? throw new ArgumentNullException(nameof(finders));

    public ITypeDefinitionResolver Create(MetadataCompilationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var materialized = _materializations.Create(snapshot);
        return new MetadataTypeDefinitionResolver(
            materialized.Types.Values.ToImmutableArray(),
            materialized.ReferenceAssemblyAliases,
            _finders.Create(snapshot),
            new MetadataAvailabilityValidator(new MetadataLifetime()));
    }
}
