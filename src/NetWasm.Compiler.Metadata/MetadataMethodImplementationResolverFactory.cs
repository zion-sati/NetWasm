using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata.RuntimeProvidedMembers;

namespace NetWasm.Compiler.Metadata;

public sealed class MetadataMethodImplementationResolverFactory(
    IMetadataCompilationMaterializationFactory materializations,
    ITypeDefinitionResolverFactory definitions,
    ITypeRepositoryFactory types,
    IMethodRepositoryFactory methods) : IMethodImplementationResolverFactory
{
    private readonly IMetadataCompilationMaterializationFactory _materializations =
        materializations ?? throw new ArgumentNullException(nameof(materializations));
    private readonly ITypeDefinitionResolverFactory _definitions =
        definitions ?? throw new ArgumentNullException(nameof(definitions));
    private readonly ITypeRepositoryFactory _types =
        types ?? throw new ArgumentNullException(nameof(types));
    private readonly IMethodRepositoryFactory _methods =
        methods ?? throw new ArgumentNullException(nameof(methods));

    public IMethodImplementationResolver Create(MetadataCompilationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var materialized = _materializations.Create(snapshot);
        var availability = new MetadataAvailabilityValidator(new MetadataLifetime());
        var definitions = _definitions.Create(snapshot);
        var identities = new MetadataTypeIdentityResolver(_types.Create(snapshot));
        var signatures = new MetadataSignatureTypeResolver(
            new MetadataTypeResolver(definitions), identities);
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
        return new MetadataMethodImplementationResolver(
            new MetadataAssemblyResolver(
                materialized.MetadataAssemblies,
                materialized.ReferenceAssemblyAliases,
                availability),
            references,
            definitions);
    }
}
