using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal interface ITypeRelationshipKeyResolverFactory
{
    ITypeRelationshipKeyResolver Create(
        ITypeFinder types,
        ITypeDefinitionResolver typeDefinitions,
        ITypeIdentityResolver identities,
        IImplementedInterfaceResolver interfaces,
        IBaseTypeResolver baseTypes);
}
