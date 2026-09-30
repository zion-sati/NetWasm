using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal sealed class BaseTypeResolver(
    ITypeFinder types,
    ITypeIdentityResolver identities,
    IBaseTypeIdentityResolver baseTypes) : IBaseTypeResolver
{
    public CliTypeIdentity? Resolve(CliTypeIdentity type) => type.Shape switch
    {
        CliTypeShape.SzArray or CliTypeShape.Array =>
            identities.GetTypeIdentity(types.FindType("System.Array").Key),
        CliTypeShape.ManagedByReference or CliTypeShape.UnmanagedPointer or
            CliTypeShape.GenericTypeParameter or
            CliTypeShape.GenericMethodParameter => null,
        _ => baseTypes.GetBaseTypeIdentity(type),
    };
}
