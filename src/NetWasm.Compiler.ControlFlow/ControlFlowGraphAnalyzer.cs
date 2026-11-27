// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow;

public sealed class ControlFlowGraphAnalyzer(
    IControlFlowDominanceAnalyzer dominance,
    IControlFlowComponentAnalyzer components,
    IControlFlowNaturalLoopAnalyzer naturalLoops) : IControlFlowGraphAnalyzer
{
    private readonly IControlFlowDominanceAnalyzer _dominance =
        dominance ?? throw new ArgumentNullException(nameof(dominance));
    private readonly IControlFlowComponentAnalyzer _components =
        components ?? throw new ArgumentNullException(nameof(components));
    private readonly IControlFlowNaturalLoopAnalyzer _naturalLoops =
        naturalLoops ?? throw new ArgumentNullException(nameof(naturalLoops));

    public ControlFlowGraphAnalysis Analyze(
        ControlFlowGraph graph,
        int entry,
        ImmutableHashSet<int> blocks)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(blocks);
        if (!blocks.Contains(entry))
        {
            throw new ArgumentException("the analysis entry must belong to its block set");
        }
        return new ControlFlowGraphAnalysis(
            entry,
            blocks,
            _dominance.Analyze(graph, entry, blocks),
            _components.Analyze(graph, blocks),
            _naturalLoops.Analyze(graph, entry, blocks));
    }
}
