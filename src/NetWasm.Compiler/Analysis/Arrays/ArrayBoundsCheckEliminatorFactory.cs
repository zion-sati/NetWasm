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
        var induction = new ArrayLoopInductionAnalyzer(new ControlFlowNaturalLoopAnalyzer(dominance), dominance);
        // Versioning copies a loop, so it runs only where loops are unrolled, trading size for speed.
        var versioner = settings.LoopUnrolling == LoopUnrollingPolicy.Bounded
            ? new ArrayLoopVersioner(graphs, stacks, new ArrayLoopVersionPlanner(induction),
                ArrayLoopVersioner.LoopInstructionBudget)
            : null;
        return new ArrayBoundsCheckEliminator(graphs, stacks,
            new ArrayBoundsCheckEliminationPlanner(induction, dominance),
            settings.ArrayBoundsCheckElimination == ArrayBoundsCheckEliminationPolicy.Conservative,
            versioner);
    }
}
