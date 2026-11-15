// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow;

namespace NetWasm.Compiler.Analysis.Arrays;

internal sealed class ArrayBoundsCheckEliminatorFactory : IArrayBoundsCheckEliminatorFactory
{
    public IArrayBoundsCheckEliminator Create(IControlFlowGraphBuilder graphs, ITypedStackValidator stacks, IControlFlowDominanceAnalyzer dominance, WasmOptimizationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new ArrayBoundsCheckEliminator(graphs, stacks,
            new ArrayBoundsCheckEliminationPlanner(
                new ArrayLoopInductionAnalyzer(new ControlFlowNaturalLoopAnalyzer(dominance), dominance), dominance),
            settings.ArrayBoundsCheckElimination == ArrayBoundsCheckEliminationPolicy.Conservative);
    }
}
