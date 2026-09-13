using NetWasm.Compiler.Core;
using NetWasm.Compiler.Interop;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal interface IReachabilityImportClassifierFactory
{
    IReachabilityImportClassifier Create(
        ITypeFinder types,
        ITypeDefinitionResolver typeDefinitions,
        IMethodRepository methods,
        IDelegateTypeRecognizer delegateTypes,
        IJavaScriptAsyncBindingResolver javaScriptAsyncBindings,
        IRuntimeIntrinsicRegistry intrinsics,
        ISymbolFormatter symbols);
}
