// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Analysis.Loops;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Arrays;

/// <summary>Creates the array bounds check eliminator, enabled by ArrayBoundsCheckElimination: the one place that maps the optimization settings to that proof pass, for the optimizer's
/// pipeline and for a reduced body's proof chain alike.</summary>
internal interface IArrayBoundsCheckEliminatorFactory
{
    // With nonZeroReads, a versioned loop's copy also skips the null checks of the span reads its test covers, where
    // ByRefNullCheckElimination versions loops.
    IArrayBoundsCheckEliminator Create(IControlFlowGraphBuilder graphs, ITypedStackValidator stacks, IControlFlowDominanceAnalyzer dominance,
        WasmOptimizationSettings settings, ArrayLoopNonZeroReadServices? nonZeroReads = null);
}

// What finding and marking a versioned loop's non-zero reads takes: Unsafe.Add recognized, and the NonZeroAddress mark.
internal sealed class ArrayLoopNonZeroReadServices(
    ICalledMethodResolver calledMethods,
    IRuntimeIntrinsicRegistry intrinsics,
    IVectorLoopMethodResolver methods)
{
    public ICalledMethodResolver CalledMethods { get; } = calledMethods ?? throw new ArgumentNullException(nameof(calledMethods));

    public IRuntimeIntrinsicRegistry Intrinsics { get; } = intrinsics ?? throw new ArgumentNullException(nameof(intrinsics));

    public IVectorLoopMethodResolver Methods { get; } = methods ?? throw new ArgumentNullException(nameof(methods));
}
