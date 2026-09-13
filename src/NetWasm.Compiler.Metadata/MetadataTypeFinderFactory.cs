using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public sealed class MetadataTypeFinderFactory(
    IMetadataCompilationMaterializationFactory materializations) : ITypeFinderFactory
{
    private readonly IMetadataCompilationMaterializationFactory _materializations =
        materializations ?? throw new ArgumentNullException(nameof(materializations));

    public ITypeFinder Create(MetadataCompilationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var materialized = _materializations.Create(snapshot);
        return new MetadataTypeFinder(
            materialized.Types.Values.ToImmutableArray(),
            new MetadataAvailabilityValidator(new MetadataLifetime()));
    }
}
