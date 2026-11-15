// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.GarbageCollection;

namespace NetWasm.Compiler.Analysis;

internal interface IAllocationCapabilityAnalyzerFactory
{
    IAllocationCapabilityAnalyzer Create(
        ITypeRepository types,
        IFieldRepository fields,
        IMethodRepository methods,
        IRuntimeIntrinsicRegistry intrinsics,
        ITypeClassifier typeClassifier);
}
