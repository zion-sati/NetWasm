using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class MetadataTypeIdentityResolver(
    ITypeRepository types) : ITypeIdentityResolver
{
    public CliTypeIdentity GetTypeIdentity(EntityKey type) =>
        CliTypeIdentity.FromDefinition(types.GetTypeDefinition(type));
}
