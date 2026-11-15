// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow;

namespace NetWasm.Compiler.Analysis;

/// <summary>Creates the integer check eliminator, enabled by IntegerCheckElimination: the one place that maps the optimization settings to that proof pass, for the optimizer's
/// pipeline and for a reduced body's proof chain alike.</summary>
internal interface IIntegerCheckEliminatorFactory
{
    IIntegerCheckEliminator Create(IControlFlowGraphBuilder graphs, ITypedStackValidator stacks, ICalledMethodResolver calledMethods,
        WasmOptimizationSettings settings);
}
