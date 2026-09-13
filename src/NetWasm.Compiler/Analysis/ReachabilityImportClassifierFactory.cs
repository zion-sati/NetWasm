using NetWasm.Compiler.Core;
using NetWasm.Compiler.Interop;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal sealed class ReachabilityImportClassifierFactory : IReachabilityImportClassifierFactory
{
    public IReachabilityImportClassifier Create(
        ITypeFinder types,
        ITypeDefinitionResolver typeDefinitions,
        IMethodRepository methods,
        IDelegateTypeRecognizer delegateTypes,
        IJavaScriptAsyncBindingResolver javaScriptAsyncBindings,
        IRuntimeIntrinsicRegistry intrinsics,
        ISymbolFormatter symbols) =>
        new ReachabilityImportClassifier(
            types,
            typeDefinitions,
            methods,
            delegateTypes,
            javaScriptAsyncBindings,
            intrinsics,
            symbols);
}
