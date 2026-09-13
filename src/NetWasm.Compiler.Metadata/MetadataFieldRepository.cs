using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class MetadataFieldRepository(
    ImmutableDictionary<EntityKey, FieldDefinitionModel> fields,
    IMetadataAvailabilityValidator availability) : IFieldRepository
{
    public FieldDefinitionModel GetField(EntityKey key)
    {
        availability.Validate();
        return fields.TryGetValue(key, out var field)
            ? field
            : throw MissingEntity(key);
    }

    private static CompilerException MissingEntity(EntityKey key) => new(
        new CompilerDiagnostic(
            DiagnosticCode.UnsupportedMetadata,
            $"field '{key}' does not exist"));
}
