// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow;

namespace NetWasm.Compiler.Analysis.Arrays;

internal sealed class RepeatedArrayAccessEliminatorFactory : IRepeatedArrayAccessEliminatorFactory
{
    public IRepeatedArrayAccessEliminator Create(IControlFlowGraphBuilder graphs, ITypedStackValidator stacks, IControlFlowDominanceAnalyzer dominance, WasmOptimizationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new RepeatedArrayAccessEliminator(graphs, stacks, new RepeatedArrayAccessPlanner(dominance),
            settings.ArrayBoundsCheckElimination == ArrayBoundsCheckEliminationPolicy.Conservative);
    }
}
