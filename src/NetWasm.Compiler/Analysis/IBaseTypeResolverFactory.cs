using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal interface IBaseTypeResolverFactory
{
    IBaseTypeResolver Create(
        ITypeFinder types,
        ITypeIdentityResolver identities,
        IBaseTypeIdentityResolver baseTypes);
}
