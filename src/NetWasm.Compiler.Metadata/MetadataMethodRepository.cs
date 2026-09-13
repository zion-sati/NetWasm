using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class MetadataMethodRepository(
    ImmutableDictionary<EntityKey, MethodDefinitionModel> methods,
    IMetadataAvailabilityValidator availability) : IMethodRepository
{
    public MethodDefinitionModel GetMethod(EntityKey key)
    {
        availability.Validate();
        return methods.TryGetValue(key, out var method)
            ? method
            : throw MissingEntity(key);
    }

    private static CompilerException MissingEntity(EntityKey key) => new(
        new CompilerDiagnostic(
            DiagnosticCode.UnsupportedMetadata,
            $"method '{key}' does not exist"));
}
