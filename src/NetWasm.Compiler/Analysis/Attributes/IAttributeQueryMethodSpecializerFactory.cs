using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.Attributes;

internal interface IAttributeQueryMethodSpecializerFactory
{
    IMethodSpecializer Create(
        IMethodSpecializer specializer,
        MetadataCompilationSnapshot metadata,
        ITypeRepository typeRepository,
        IFieldRepository fieldRepository,
        IMethodRepository methodRepository,
        ITypeFinder typeFinder,
        ITypeDefinitionResolver typeDefinitions,
        ITypeIdentityResolver typeIdentities,
        IMethodInstanceResolver methodInstances,
        ITypeRelationshipClassifier relationships,
        IBaseTypeResolver baseTypes,
        ICalledMethodResolver calls,
        ITypeOperandResolver types,
        ISymbolFormatter symbols);
}
