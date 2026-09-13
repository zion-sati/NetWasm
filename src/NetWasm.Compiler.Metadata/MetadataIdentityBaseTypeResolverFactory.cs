using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public sealed class MetadataIdentityBaseTypeResolverFactory(
    IBaseTypeIdentityResolverFactory identities) : IMetadataIdentityBaseTypeResolverFactory
{
    private readonly IBaseTypeIdentityResolverFactory _identities =
        identities ?? throw new ArgumentNullException(nameof(identities));

    public IMetadataIdentityBaseTypeResolver Create(MetadataCompilationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new MetadataIdentityBaseTypeResolver(
            _identities.Create(snapshot));
    }
}
