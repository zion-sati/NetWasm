using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal sealed class BaseTypeResolverFactory : IBaseTypeResolverFactory
{
    public IBaseTypeResolver Create(
        ITypeFinder types,
        ITypeIdentityResolver identities,
        IBaseTypeIdentityResolver baseTypes) =>
        new BaseTypeResolver(types, identities, baseTypes);
}
