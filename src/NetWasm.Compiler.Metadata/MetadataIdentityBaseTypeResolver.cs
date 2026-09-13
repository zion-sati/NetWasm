using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class MetadataIdentityBaseTypeResolver(
    IBaseTypeIdentityResolver baseTypeIdentities) : IMetadataIdentityBaseTypeResolver
{
    public CliTypeIdentity? GetBaseType(CliTypeIdentity type)
    {
        var baseType = baseTypeIdentities.GetBaseTypeIdentity(type);
        return baseType;
    }
}
