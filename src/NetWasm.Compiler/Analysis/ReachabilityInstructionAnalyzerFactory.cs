// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Metadata;

using NetWasm.Compiler.IntermediateRepresentation.Calls;

namespace NetWasm.Compiler.Analysis;

internal sealed class ReachabilityInstructionAnalyzerFactory(
    INullableTypeResolver nullableTypes,
    IManagedCallSiteFactory managedCallSites,
    INativeCallbackDeclarationValidator nativeCallbacks) :
    IReachabilityInstructionAnalyzerFactory
{
    public ReachabilityInstructionAnalyzerFactory(
        INullableTypeResolver nullableTypes,
        IManagedCallSiteFactory managedCallSites) : this(
            nullableTypes,
            managedCallSites,
            new NativeCallbackDeclarationValidator())
    {
    }

    public IReachabilityInstructionAnalyzer Create(
        ITypeFinder typeFinder,
        ITypeIdentityResolver typeIdentities,
        ICalledMethodResolver calledMethods,
        ITypeOperandResolver typeOperands,
        IDispatchSiteKeyBuilder dispatchSiteKeys,
        IDelegateMethodClassifier delegateMethods,
        IRuntimeIntrinsicRegistry intrinsics) =>
        new StaticInitializationDependencyDecorator(new ReachabilityInstructionAnalyzer(
            typeFinder,
            typeIdentities,
            calledMethods,
            typeOperands,
            dispatchSiteKeys,
            delegateMethods,
            nullableTypes,
            managedCallSites,
            nativeCallbacks,
            new DispatchReceiverBoundResolver(typeOperands, nullableTypes), intrinsics), typeFinder);
}
