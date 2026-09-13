using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public sealed class MetadataTypeRepositoryFactory(
    IMetadataCompilationMaterializationFactory materializations) : ITypeRepositoryFactory
{
    private readonly IMetadataCompilationMaterializationFactory _materializations =
        materializations ?? throw new ArgumentNullException(nameof(materializations));

    public ITypeRepository Create(MetadataCompilationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new MetadataTypeRepository(
            _materializations.Create(snapshot).Types,
            new MetadataAvailabilityValidator(new MetadataLifetime()));
    }
}
