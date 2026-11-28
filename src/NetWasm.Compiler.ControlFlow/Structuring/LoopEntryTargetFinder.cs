// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.ControlFlow.Structuring;

// The blocks of a loop that its own domain enters from outside the loop. A block of another domain can only reach a
// domain's code by leaving a handler nested in it, the way a catch leaves to its continuation; the exception region
// that handler belongs to structures that edge, so it does not enter the loop. That holds for a loop in a catch or
// finally handler as it does for one in the method body. Each predecessor's domain is one lookup in the method's
// index, so the search is linear in the edges into the loop.
internal sealed class LoopEntryTargetFinder : ILoopEntryTargetFinder
{
    public ImmutableHashSet<int> Find(ControlFlowGraph graph,
        ImmutableHashSet<int> component,
        ControlFlowDomain domain,
        ControlFlowDomains domains) =>
        component
            .Where(node => graph.Predecessors[node].Any(predecessor =>
                graph.ReachableBlocks.Contains(predecessor) &&
                !component.Contains(predecessor) &&
                !domains.ClaimsOutside(domain, predecessor)))
            .ToImmutableHashSet();
}
