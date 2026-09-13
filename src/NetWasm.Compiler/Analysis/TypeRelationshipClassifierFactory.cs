using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal sealed class TypeRelationshipClassifierFactory :
    ITypeRelationshipClassifierFactory
{
    public ITypeRelationshipClassifier Create(
        ITypeFinder types,
        ITypeDefinitionResolver typeDefinitions,
        ITypeIdentityResolver identities,
        IImplementedInterfaceResolver interfaces,
        IBaseTypeResolver baseTypes) =>
        new TypeRelationshipClassifier(
            types,
            typeDefinitions,
            identities,
            interfaces,
            baseTypes);
}
