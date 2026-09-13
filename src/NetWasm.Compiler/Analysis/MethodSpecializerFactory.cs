using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal sealed class MethodSpecializerFactory : IMethodSpecializerFactory
{
    public IMethodSpecializer Create(
        ITypeDefinitionResolver typeDefinitions,
        IMethodRepository methods,
        IMethodInstanceResolver methodInstances,
        ISymbolFormatter symbols,
        IFieldRepository fields,
        ITypeFinder types,
        ICalledMethodResolver calledMethods,
        IMethodImplementationResolver methodImplementations) =>
        new MethodSpecializer(
        [
            new SharedReturnEpilogueRewriter(),
            new Int32LocalInitializerRewriter(),
            new RectangularArrayCallRewriter(),
            new IntrinsicCallRewriter(calledMethods, symbols),
            new InterlockedCallRewriter(calledMethods, symbols),
            new DelegateCallRewriter(calledMethods, symbols),
            new ActivatorCallRewriter(
                typeDefinitions,
                methods,
                methodInstances,
                symbols),
            new InitializeArrayCallRewriter(
                fields,
                calledMethods,
                symbols),
            new ConstrainedCallRewriter(
                typeDefinitions,
                methods,
                methodInstances,
                symbols,
                new ConstrainedCallTargetResolver(
                    types,
                    methods,
                    methodImplementations)),
        ]);
}
