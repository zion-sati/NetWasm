// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class ControlFlowPostDominatorFinder(IControlFlowPostDominanceAnalyzer postDominance)
    : IControlFlowPostDominatorFinder
{
    private readonly IControlFlowPostDominanceAnalyzer _postDominance = postDominance ?? throw new ArgumentNullException(nameof(postDominance));

    public DominatorTree Find(ControlFlowStructuringState state,
        int? stop,
        ImmutableHashSet<int> allowed)
    {
        if (!state.PostDominators.TryGetValue(allowed, out var byStop))
        {
            byStop = [];
            state.PostDominators.Add(allowed, byStop);
        }
        var stopKey = stop ?? -1;
        if (!byStop.TryGetValue(stopKey, out var result))
        {
            result = _postDominance.Analyze(state.ContinuationGraph, stop, allowed);
            byStop.Add(stopKey, result);
        }
        return result;
    }
}
