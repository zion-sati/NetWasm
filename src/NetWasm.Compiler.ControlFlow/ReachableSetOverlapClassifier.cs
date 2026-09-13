using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow;

internal sealed class ReachableSetOverlapClassifier : IReachableSetOverlapClassifier
{
    public bool Overlaps(ImmutableArray<ImmutableHashSet<int>> reachableSets)
    {
        if (reachableSets.IsDefault)
        {
            throw new ArgumentException("reachable sets must be initialized", nameof(reachableSets));
        }

        var visited = new HashSet<int>();
        foreach (var reachable in reachableSets)
        {
            ArgumentNullException.ThrowIfNull(reachable);
            foreach (var block in reachable)
            {
                if (!visited.Add(block))
                {
                    return true;
                }
            }
        }
        return false;
    }
}
