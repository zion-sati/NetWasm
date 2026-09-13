using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class BranchReachabilityOverlapClassifier(
    IReachableBlockFinder reachableBlocks) : IBranchReachabilityOverlapClassifier
{
    private readonly IReachableBlockFinder _reachableBlocks = reachableBlocks ?? throw new ArgumentNullException(nameof(reachableBlocks));

    public bool Classify(ControlFlowStructuringState state,
        int first,
        int second,
        int? stop,
        ImmutableHashSet<int> allowed) =>
        _reachableBlocks.Find(state, first, stop, allowed)
            .Overlaps(_reachableBlocks.Find(state, second, stop, allowed));
}
