using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata.RuntimeProvidedMembers;

namespace NetWasm.Compiler.Metadata;

public sealed class MetadataMethodInstanceResolverFactory(
    IMetadataCompilationMaterializationFactory materializations,
    ITypeDefinitionResolverFactory definitions,
    ITypeRepositoryFactory types,
    IMethodRepositoryFactory methods) : IMethodInstanceResolverFactory
{
    private readonly IMetadataCompilationMaterializationFactory _materializations =
        materializations ?? throw new ArgumentNullException(nameof(materializations));
    private readonly ITypeDefinitionResolverFactory _definitions =
        definitions ?? throw new ArgumentNullException(nameof(definitions));
    private readonly ITypeRepositoryFactory _types =
        types ?? throw new ArgumentNullException(nameof(types));
    private readonly IMethodRepositoryFactory _methods =
        methods ?? throw new ArgumentNullException(nameof(methods));

    public IMethodInstanceResolver Create(MetadataCompilationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var materialized = _materializations.Create(snapshot);
        var availability = new MetadataAvailabilityValidator(new MetadataLifetime());
        var definitions = _definitions.Create(snapshot);
        var signatures = new MetadataSignatureTypeResolver(
            new MetadataTypeResolver(definitions),
            new MetadataTypeIdentityResolver(_types.Create(snapshot)));
        var signatureComparer = new SignatureTypeComparer();
        var stackTypes = new MetadataStackTypeResolver(definitions);
        var references = new MetadataMethodReferenceResolver(
            new MetadataEntityHandleReader(),
            signatures,
            signatureComparer,
            _types.Create(snapshot),
            definitions,
            _methods.Create(snapshot),
            new ArrayMethodResolver(signatureComparer),
            stackTypes);
        return new MetadataMethodInstanceResolver(
            new ManagedAssemblyResolver(
                materialized.Assemblies.ToImmutableDictionary(
                    assembly => assembly.Identity.Name,
                    StringComparer.Ordinal),
                materialized.ReferenceAssemblyAliases,
                availability),
            references);
    }
}
