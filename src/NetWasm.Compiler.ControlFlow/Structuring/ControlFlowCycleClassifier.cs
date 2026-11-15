// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class ControlFlowCycleClassifier : IControlFlowCycleClassifier
{
    public bool Classify(
        ControlFlowGraph graph,
        ImmutableHashSet<int> component) =>
        component.Count > 1 || graph.Successors[component.Single()].Contains(component.Single());
}
