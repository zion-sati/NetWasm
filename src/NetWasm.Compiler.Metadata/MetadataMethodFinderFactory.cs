using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public sealed class MetadataMethodFinderFactory(
    IMetadataCompilationMaterializationFactory materializations,
    IMethodRepositoryFactory repositories) : IMethodFinderFactory
{
    private readonly IMetadataCompilationMaterializationFactory _materializations =
        materializations ?? throw new ArgumentNullException(nameof(materializations));
    private readonly IMethodRepositoryFactory _repositories =
        repositories ?? throw new ArgumentNullException(nameof(repositories));

    public IMethodFinder Create(MetadataCompilationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var materialized = _materializations.Create(snapshot);
        return new MetadataMethodFinder(
            materialized.Types.Values.ToImmutableArray(),
            _repositories.Create(snapshot),
            new MetadataAvailabilityValidator(new MetadataLifetime()));
    }
}
