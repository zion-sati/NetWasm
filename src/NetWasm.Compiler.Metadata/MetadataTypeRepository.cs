using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class MetadataTypeRepository(
    ImmutableDictionary<EntityKey, TypeDefinitionModel> types,
    IMetadataAvailabilityValidator availability) : ITypeRepository
{
    public TypeDefinitionModel GetTypeDefinition(EntityKey key)
    {
        availability.Validate();
        return types.TryGetValue(key, out var type)
            ? type
            : throw MissingEntity(key);
    }

    private static CompilerException MissingEntity(EntityKey key) => new(
        new CompilerDiagnostic(
            DiagnosticCode.UnsupportedMetadata,
            $"type '{key}' does not exist"));
}
