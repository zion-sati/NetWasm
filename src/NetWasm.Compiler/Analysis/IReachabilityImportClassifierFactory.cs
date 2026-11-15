// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core.GenericConstraints;
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
        IJavaScriptConversionResolver conversions,
        IRuntimeIntrinsicRegistry intrinsics,
        ISymbolFormatter symbols,
        IClosedGenericConstraintValidator constraints);
}
