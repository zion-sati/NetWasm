// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow;

namespace NetWasm.Compiler.Analysis.NullChecks;

internal sealed class ConstructedObjectNullCheckEliminatorFactory : IConstructedObjectNullCheckEliminatorFactory
{
    public IConstructedObjectNullCheckEliminator Create(IControlFlowGraphBuilder graphs, ITypedStackValidator stacks, IControlFlowDominanceAnalyzer dominance,
        ICalledMethodResolver calledMethods, WasmOptimizationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new ConstructedObjectNullCheckEliminator(graphs, stacks,
            new ConstructedReceiverPlanner(dominance, new StackEffectResolver(calledMethods), calledMethods),
            settings.NewObjectNullCheckElimination != NewObjectNullCheckEliminationPolicy.Disabled);
    }
}
