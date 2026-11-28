// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.NullChecks;

internal interface IByRefNullCheckEliminatorFactory
{
    IByRefNullCheckEliminator Create(IControlFlowGraphBuilder graphs, ITypedStackValidator stacks,
        IControlFlowDominanceAnalyzer dominance, ICalledMethodResolver calledMethods, IRuntimeIntrinsicRegistry intrinsics,
        WasmOptimizationSettings settings);
}
