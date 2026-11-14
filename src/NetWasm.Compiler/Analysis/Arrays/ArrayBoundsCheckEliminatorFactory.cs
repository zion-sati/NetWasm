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
