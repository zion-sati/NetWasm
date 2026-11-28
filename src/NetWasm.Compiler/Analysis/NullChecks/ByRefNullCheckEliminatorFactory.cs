// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.NullChecks;

internal sealed class ByRefNullCheckEliminatorFactory : IByRefNullCheckEliminatorFactory
{
    public IByRefNullCheckEliminator Create(IControlFlowGraphBuilder graphs, ITypedStackValidator stacks,
        IControlFlowDominanceAnalyzer dominance, ICalledMethodResolver calledMethods, IRuntimeIntrinsicRegistry intrinsics,
        WasmOptimizationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new ByRefNullCheckEliminator(graphs, stacks,
            new ByRefNullCheckPlanner(dominance, new StackEffectResolver(calledMethods), calledMethods, intrinsics),
            settings.ByRefNullCheckElimination != ByRefNullCheckEliminationPolicy.Disabled);
    }
}
