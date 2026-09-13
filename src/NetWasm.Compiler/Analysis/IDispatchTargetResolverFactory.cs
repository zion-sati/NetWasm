using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal interface IDispatchTargetResolverFactory
{
    IDispatchTargetResolver Create(
        ITypeFinder types,
        ITypeDefinitionResolver typeDefinitions,
        IMethodRepository methods,
        IMethodImplementationResolver methodImplementations,
        IImplementedInterfaceResolver implementedInterfaces,
        IMethodInstanceResolver methodInstances,
        ISymbolFormatter symbols,
        ITypeRelationshipClassifier relationships,
        IBaseTypeResolver baseTypes);
}
