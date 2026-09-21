using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal sealed class LayoutBaseTypeResolver(
    ITypeFinder types,
    ITypeIdentityResolver identities,
    IMetadataIdentityBaseTypeResolver baseTypes) : IBaseTypeResolver
{
    public CliTypeIdentity? Resolve(CliTypeIdentity type) =>
        type.Shape is CliTypeShape.SzArray or CliTypeShape.Array
            ? identities.GetTypeIdentity(types.FindType("System.Array").Key)
            : baseTypes.GetBaseType(type);
}
