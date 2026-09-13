using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal sealed class BaseTypeResolver(
    ITypeFinder types,
    ITypeIdentityResolver identities,
    IBaseTypeIdentityResolver baseTypes) : IBaseTypeResolver
{
    public CliTypeIdentity? Resolve(CliTypeIdentity type) =>
        type.Shape is CliTypeShape.SzArray or CliTypeShape.Array
            ? identities.GetTypeIdentity(types.FindType("System.Array").Key)
            : baseTypes.GetBaseTypeIdentity(type);
}
