using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal sealed class DispatchTargetResolverFactory : IDispatchTargetResolverFactory
{
    public IDispatchTargetResolver Create(
        ITypeFinder types,
        ITypeDefinitionResolver typeDefinitions,
        IMethodRepository methods,
        IMethodImplementationResolver methodImplementations,
        IImplementedInterfaceResolver implementedInterfaces,
        IMethodInstanceResolver methodInstances,
        ISymbolFormatter symbols,
        ITypeRelationshipClassifier relationships,
        IBaseTypeResolver baseTypes) =>
        new DispatchTargetResolver(
            types,
            typeDefinitions,
            methods,
            methodImplementations,
            implementedInterfaces,
            methodInstances,
            symbols,
            relationships,
            baseTypes);
}
