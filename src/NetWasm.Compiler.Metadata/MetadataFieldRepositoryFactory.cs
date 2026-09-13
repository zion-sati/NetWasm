using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public sealed class MetadataFieldRepositoryFactory(
    IMetadataCompilationMaterializationFactory materializations) : IFieldRepositoryFactory
{
    private readonly IMetadataCompilationMaterializationFactory _materializations =
        materializations ?? throw new ArgumentNullException(nameof(materializations));

    public IFieldRepository Create(MetadataCompilationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new MetadataFieldRepository(
            _materializations.Create(snapshot).Fields,
            new MetadataAvailabilityValidator(new MetadataLifetime()));
    }
}
