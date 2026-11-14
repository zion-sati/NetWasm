using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow;

namespace NetWasm.Compiler.Analysis.NullChecks;

/// <summary>Creates the constructed object null check eliminator, enabled by NewObjectNullCheckElimination: the one place that maps the optimization settings to that proof pass, for the optimizer's
/// pipeline and for a reduced body's proof chain alike.</summary>
internal interface IConstructedObjectNullCheckEliminatorFactory
{
    IConstructedObjectNullCheckEliminator Create(IControlFlowGraphBuilder graphs, ITypedStackValidator stacks, IControlFlowDominanceAnalyzer dominance,
        ICalledMethodResolver calledMethods, WasmOptimizationSettings settings);
}
