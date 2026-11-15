// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal interface IReachabilityInstructionAnalyzerFactory
{
    IReachabilityInstructionAnalyzer Create(
        ITypeFinder typeFinder,
        ITypeIdentityResolver typeIdentities,
        ICalledMethodResolver calledMethods,
        ITypeOperandResolver typeOperands,
        IDispatchSiteKeyBuilder dispatchSiteKeys,
        IDelegateMethodClassifier delegateMethods,
        IRuntimeIntrinsicRegistry intrinsics);
}
