using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public sealed class MetadataTypeClassifierFactory(
    ITypeIdentityResolverFactory identities,
    ITypeDefinitionResolverFactory definitions,
    IMetadataIdentityBaseTypeResolverFactory baseTypes) : ITypeClassifierFactory
{
    private readonly ITypeIdentityResolverFactory _identities =
        identities ?? throw new ArgumentNullException(nameof(identities));
    private readonly ITypeDefinitionResolverFactory _definitions =
        definitions ?? throw new ArgumentNullException(nameof(definitions));
    private readonly IMetadataIdentityBaseTypeResolverFactory _baseTypes =
        baseTypes ?? throw new ArgumentNullException(nameof(baseTypes));

    public ITypeClassifier Create(MetadataCompilationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new MetadataTypeClassifier(
            _identities.Create(snapshot),
            _definitions.Create(snapshot),
            _baseTypes.Create(snapshot));
    }
}
