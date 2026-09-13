using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class CommonReachableBlockFinder(
    IControlFlowPostDominatorFinder postDominators,
    IControlFlowDistanceFinder distances) : ICommonReachableBlockFinder
{
    private readonly IControlFlowPostDominatorFinder _postDominators = postDominators ?? throw new ArgumentNullException(nameof(postDominators));
    private readonly IControlFlowDistanceFinder _distances = distances ?? throw new ArgumentNullException(nameof(distances));

    public int? Find(ControlFlowStructuringState state,
        int first,
        int second,
        int? stop,
        ImmutableHashSet<int> allowed)
    {
        if (!allowed.Contains(first) || !allowed.Contains(second))
        {
            return null;
        }
        var postDominators =
            _postDominators.Find(state, stop, allowed);
        var firstDistances = _distances.Find(state, first, stop, allowed);
        var secondDistances = _distances.Find(state, second, stop, allowed);
        var common = postDominators[first]
            .Intersect(postDominators[second])
            .Where(firstDistances.ContainsKey)
            .Where(secondDistances.ContainsKey)
            .ToArray();
        if (common.Length == 0)
        {
            return null;
        }
        return common
            .OrderBy(node => firstDistances[node] + secondDistances[node])
            .ThenBy(node => Math.Max(firstDistances[node], secondDistances[node]))
            .ThenBy(node => node)
            .First();
    }
    public int? Find(ControlFlowStructuringState state,
        int[] starts,
        int? stop,
        ImmutableHashSet<int> allowed)
    {
        if (starts.Length == 0)
        {
            return null;
        }
        if (starts.Length == 1)
        {
            return starts[0];
        }
        var postDominators = _postDominators.Find(state, stop, allowed);
        var distances = starts.Select(start => _distances.Find(state, start, stop, allowed)).ToArray();
        var common = postDominators[starts[0]]
            .Where(node => distances.All(distance => distance.ContainsKey(node)))
            .ToArray();
        return common.Length == 0
            ? null
            : common
                .OrderBy(node => distances.Sum(distance => distance[node]))
                .ThenBy(node => distances.Max(distance => distance[node]))
                .ThenBy(node => node)
                .First();
    }
}
