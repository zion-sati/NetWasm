using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;
using NetWasm.Compiler.Interop;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal sealed class ReachabilityImportClassifierFactory(
    IEnumMetadataRequirementClassifier enumMetadataRequirements,
    INativeDeclarationValidator nativeDeclarations) : IReachabilityImportClassifierFactory
{
    private readonly IEnumMetadataRequirementClassifier _enumMetadataRequirements =
        enumMetadataRequirements ?? throw new ArgumentNullException(nameof(enumMetadataRequirements));
    private readonly INativeDeclarationValidator _nativeDeclarations =
        nativeDeclarations ?? throw new ArgumentNullException(nameof(nativeDeclarations));

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
            symbols,
            _enumMetadataRequirements,
            _nativeDeclarations);
}
