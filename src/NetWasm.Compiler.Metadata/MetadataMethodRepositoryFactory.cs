using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public sealed class MetadataMethodRepositoryFactory(
    IMetadataCompilationMaterializationFactory materializations,
    ITypeDefinitionResolverFactory definitions) : IMethodRepositoryFactory
{
    private readonly IMetadataCompilationMaterializationFactory _materializations =
        materializations ?? throw new ArgumentNullException(nameof(materializations));
    private readonly ITypeDefinitionResolverFactory _definitions =
        definitions ?? throw new ArgumentNullException(nameof(definitions));

    public IMethodRepository Create(MetadataCompilationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var stackTypes = new MetadataStackTypeResolver(_definitions.Create(snapshot));
        var methods = _materializations.Create(snapshot).Methods.ToImmutableDictionary(
            pair => pair.Key,
            pair => pair.Value with
            {
                Signature = stackTypes.Resolve(pair.Value.Signature),
            });
        return new MetadataMethodRepository(
            methods,
            new MetadataAvailabilityValidator(new MetadataLifetime()));
    }
}
