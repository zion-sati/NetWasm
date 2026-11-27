// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.GarbageCollection;
using NetWasm.Compiler.Analysis.Delegates;
using NetWasm.Compiler.Diagnostics;

namespace NetWasm.Compiler.Analysis;

internal sealed class ReachableProgramBuilderFactory(IAnalysisStepRecorder steps) : IReachableProgramBuilderFactory
{
    private readonly IAnalysisStepRecorder _steps = steps ?? throw new ArgumentNullException(nameof(steps));

    public IReachableProgramBuilder Create(
        IAllocationCapabilityAnalyzer allocationCapabilities,
        IManagedByRefEffectAnalyzer byRefEffects,
        IDelegateBindingPlanner delegateBindings,
        IObjectDispatchSharing objectDispatchSharing,
        IDispatchSiteTargetSelector siteTargets) =>
        new ReachableProgramBuilder(
            allocationCapabilities,
            byRefEffects,
            delegateBindings,
            objectDispatchSharing,
            siteTargets,
            _steps);
}
