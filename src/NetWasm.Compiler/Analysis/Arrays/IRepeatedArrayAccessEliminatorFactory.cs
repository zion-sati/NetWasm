using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow;

namespace NetWasm.Compiler.Analysis.Arrays;

/// <summary>Creates the repeated array access eliminator, enabled by ArrayBoundsCheckElimination: the one place that maps the optimization settings to that proof pass, for the optimizer's
/// pipeline and for a reduced body's proof chain alike.</summary>
internal interface IRepeatedArrayAccessEliminatorFactory
{
    IRepeatedArrayAccessEliminator Create(IControlFlowGraphBuilder graphs, ITypedStackValidator stacks, IControlFlowDominanceAnalyzer dominance, WasmOptimizationSettings settings);
}
