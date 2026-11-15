// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal interface IMethodSpecializerFactory
{
    IMethodSpecializer Create(
        ITypeDefinitionResolver typeDefinitions,
        ITypeRepository typeRepository,
        IMethodRepository methods,
        IMethodInstanceResolver methodInstances,
        ISymbolFormatter symbols,
        IFieldRepository fields,
        ITypeFinder types,
        ICalledMethodResolver calledMethods,
        IMethodImplementationResolver methodImplementations,
        ITypeRelationshipClassifier relationships,
        IMethodBodyReader methodBodies,
        WasmOptimizationSettings optimizationSettings,
        IRuntimeIntrinsicRegistry intrinsics,
        PlatformWorldCapabilities world,
        ITypeIdentityResolver typeIdentities);
}
