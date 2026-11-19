// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow;

namespace NetWasm.Compiler.Analysis.Arrays;

/// <summary>Creates the repeated array access eliminator, enabled by ArrayBoundsCheckElimination: the one place that maps the optimization settings to that proof pass, for the optimizer's
/// pipeline and for a reduced body's proof chain alike.</summary>
internal interface IRepeatedArrayAccessEliminatorFactory
{
    IRepeatedArrayAccessEliminator Create(IControlFlowGraphBuilder graphs, ITypedStackValidator stacks,
        IControlFlowDominanceAnalyzer dominance, ICalledMethodResolver calledMethods, WasmOptimizationSettings settings);
}
