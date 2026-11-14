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
