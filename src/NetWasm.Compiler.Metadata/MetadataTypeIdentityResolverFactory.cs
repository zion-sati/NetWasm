using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public sealed class MetadataTypeIdentityResolverFactory(
    ITypeRepositoryFactory repositories) : ITypeIdentityResolverFactory
{
    private readonly ITypeRepositoryFactory _repositories =
        repositories ?? throw new ArgumentNullException(nameof(repositories));

    public ITypeIdentityResolver Create(MetadataCompilationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new MetadataTypeIdentityResolver(_repositories.Create(snapshot));
    }
}
