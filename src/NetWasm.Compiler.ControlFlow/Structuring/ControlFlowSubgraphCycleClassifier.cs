// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class ControlFlowSubgraphCycleClassifier(
    IControlFlowComponentAnalyzer components,
    IControlFlowCycleClassifier cycles) : IControlFlowSubgraphCycleClassifier
{
    private readonly IControlFlowComponentAnalyzer _components = components ??
        throw new ArgumentNullException(nameof(components));
    private readonly IControlFlowCycleClassifier _cycles = cycles ??
        throw new ArgumentNullException(nameof(cycles));

    public bool Classify(
        ControlFlowGraph graph,
        ImmutableHashSet<int> blocks)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(blocks);
        return _components.Analyze(graph, blocks)
            .Any(component => _cycles.Classify(graph, component));
    }
}
