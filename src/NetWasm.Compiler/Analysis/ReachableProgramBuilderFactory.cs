// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.GarbageCollection;
using NetWasm.Compiler.Analysis.Delegates;

namespace NetWasm.Compiler.Analysis;

internal sealed class ReachableProgramBuilderFactory : IReachableProgramBuilderFactory
{
    public IReachableProgramBuilder Create(
        IAllocationCapabilityAnalyzer allocationCapabilities,
        IManagedByRefEffectAnalyzer byRefEffects,
        IDelegateBindingPlanner delegateBindings) =>
        new ReachableProgramBuilder(
            allocationCapabilities,
            byRefEffects,
            delegateBindings);
}
