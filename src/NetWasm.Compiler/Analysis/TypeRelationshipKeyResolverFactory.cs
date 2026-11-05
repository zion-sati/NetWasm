using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal sealed class TypeRelationshipKeyResolverFactory : ITypeRelationshipKeyResolverFactory
{
    public ITypeRelationshipKeyResolver Create(
        ITypeFinder types,
        ITypeDefinitionResolver typeDefinitions,
        ITypeIdentityResolver identities,
        IImplementedInterfaceResolver interfaces,
        IBaseTypeResolver baseTypes) =>
        new TypeRelationshipKeyResolver(types, typeDefinitions, identities, interfaces, baseTypes);
}
