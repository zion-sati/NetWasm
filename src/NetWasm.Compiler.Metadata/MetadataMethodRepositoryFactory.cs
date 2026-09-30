using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public sealed class MetadataMethodRepositoryFactory(
    IMetadataCompilationMaterializationFactory materializations) : IMethodRepositoryFactory
{
    private readonly IMetadataCompilationMaterializationFactory _materializations =
        materializations ?? throw new ArgumentNullException(nameof(materializations));

    public IMethodRepository Create(MetadataCompilationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new MetadataMethodRepository(
            _materializations.Create(snapshot).Methods,
            new MetadataAvailabilityValidator(new MetadataLifetime()));
    }
}
