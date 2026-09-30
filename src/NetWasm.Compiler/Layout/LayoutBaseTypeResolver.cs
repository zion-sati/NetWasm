using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal sealed class LayoutBaseTypeResolver(
    ITypeFinder types,
    ITypeIdentityResolver identities,
    IMetadataIdentityBaseTypeResolver baseTypes) : IBaseTypeResolver
{
    public CliTypeIdentity? Resolve(CliTypeIdentity type) => type.Shape switch
    {
        CliTypeShape.SzArray or CliTypeShape.Array =>
            identities.GetTypeIdentity(types.FindType("System.Array").Key),
        CliTypeShape.ManagedByReference or CliTypeShape.UnmanagedPointer or
            CliTypeShape.GenericTypeParameter or
            CliTypeShape.GenericMethodParameter => null,
        _ => baseTypes.GetBaseType(type),
    };
}
