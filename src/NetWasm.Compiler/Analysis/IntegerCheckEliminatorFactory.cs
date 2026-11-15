// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow;

namespace NetWasm.Compiler.Analysis;

internal sealed class IntegerCheckEliminatorFactory : IIntegerCheckEliminatorFactory
{
    public IIntegerCheckEliminator Create(IControlFlowGraphBuilder graphs, ITypedStackValidator stacks, ICalledMethodResolver calledMethods,
        WasmOptimizationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new IntegerCheckEliminator(graphs, stacks, new IntegerRangeAnalysis(new StackEffectResolver(calledMethods)),
            settings.IntegerCheckElimination == IntegerCheckEliminationPolicy.ProvenRanges);
    }
}
