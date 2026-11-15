// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class DispatcherBranchTablePlanner : IDispatcherBranchTablePlanner
{
    private const int MinimumCaseCount = 32;
    private const int MinimumAcyclicCaseCount = 8;

    public DispatcherBranchTablePlan? Plan(StructuredDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);

        var orderedBlocks = OrderBlocks(dispatcher.Blocks, out var isAcyclic);
        var candidates = dispatcher.Exits
            .Select(exit => new Candidate(exit.Target.Value, exit, null))
            .Concat(orderedBlocks.Select(block => new Candidate(
                block.Occurrence.Block.Value,
                null,
                block)))
            .ToImmutableArray();
        if (candidates.Any(candidate => candidate.Target < 0))
        {
            return null;
        }

        var maximumTarget = candidates.Max(candidate => candidate.Target);
        if ((long)maximumTarget + 1 > (long)candidates.Length * 4)
        {
            return null;
        }

        var blockTargets = dispatcher.Blocks
            .Select(block => block.Occurrence.Block)
            .ToHashSet();
        var emptyExitTargets = dispatcher.Exits
            .Where(exit => exit.Body.Regions.IsEmpty)
            .Select(exit => exit.Target)
            .ToHashSet();
        var directableTransitionCount = isAcyclic
            ? dispatcher.Blocks.Sum(block =>
                CountDirectable(block.WhenTrue) + CountDirectable(block.WhenFalse))
            : 0;
        if (candidates.Length < MinimumCaseCount &&
            (candidates.Length < MinimumAcyclicCaseCount ||
             !isAcyclic ||
             directableTransitionCount < (candidates.Length + 1) / 2))
        {
            return null;
        }

        var indices = new Dictionary<int, int>();
        for (var index = 0; index < candidates.Length; index++)
        {
            if (!indices.TryAdd(candidates[index].Target, index))
            {
                return null;
            }
        }

        var defaultDepth = checked((uint)candidates.Length);
        var targets = Enumerable.Repeat(
            defaultDepth,
            checked(maximumTarget + 1)).ToArray();
        for (var index = 0; index < candidates.Length; index++)
        {
            targets[candidates[index].Target] = checked(
                (uint)(candidates.Length - index - 1));
        }

        var cases = candidates
            .Select((candidate, index) => new DispatcherCasePlan(
                candidate.Target,
                candidate.Exit,
                candidate.Block,
                PlanTransition(candidate.Block?.WhenTrue, index, candidates, indices),
                PlanTransition(candidate.Block?.WhenFalse, index, candidates, indices)))
            .ToImmutableArray();
        return new DispatcherBranchTablePlan(cases, [.. targets]);

        int CountDirectable(StructuredBlockId? target) =>
            target is { } successor &&
            (blockTargets.Contains(successor) || emptyExitTargets.Contains(successor))
                ? 1
                : 0;
    }

    private static ImmutableArray<StructuredDispatcherBlock> OrderBlocks(
        ImmutableArray<StructuredDispatcherBlock> blocks,
        out bool isAcyclic)
    {
        var byTarget = blocks.ToDictionary(
            block => block.Occurrence.Block.Value);
        var predecessors = blocks.ToDictionary(
            block => block.Occurrence.Block.Value,
            _ => new List<int>());
        var outgoing = blocks.ToDictionary(
            block => block.Occurrence.Block.Value,
            block => Successors(block).Count(byTarget.ContainsKey));
        foreach (var block in blocks)
        {
            var source = block.Occurrence.Block.Value;
            foreach (var successor in Successors(block).Where(byTarget.ContainsKey))
            {
                predecessors[successor].Add(source);
            }
        }

        var ready = new SortedSet<int>(outgoing
            .Where(pair => pair.Value == 0)
            .Select(pair => pair.Key));
        var ordered = ImmutableArray.CreateBuilder<StructuredDispatcherBlock>(
            blocks.Length);
        while (ready.Count != 0)
        {
            var target = ready.Max;
            ready.Remove(target);
            ordered.Add(byTarget[target]);
            foreach (var predecessor in predecessors[target])
            {
                if (--outgoing[predecessor] == 0)
                {
                    ready.Add(predecessor);
                }
            }
        }

        isAcyclic = ordered.Count == blocks.Length;
        return isAcyclic ? ordered.ToImmutable() : [.. blocks.Reverse()];

        static IEnumerable<int> Successors(StructuredDispatcherBlock block)
        {
            if (block.WhenTrue is { } whenTrue)
            {
                yield return whenTrue.Value;
            }
            if (block.WhenFalse is { } whenFalse)
            {
                yield return whenFalse.Value;
            }
        }
    }

    private static DispatcherTransitionPlan? PlanTransition(
        StructuredBlockId? target,
        int sourceIndex,
        ImmutableArray<Candidate> candidates,
        Dictionary<int, int> indices)
    {
        if (target is not { } successor)
        {
            return null;
        }

        uint? directDepth = null;
        if (indices.TryGetValue(successor.Value, out var targetIndex) &&
            targetIndex < sourceIndex)
        {
            var candidate = candidates[targetIndex];
            if (candidate.Block is not null || candidate.Exit!.Body.Regions.IsEmpty)
            {
                directDepth = checked((uint)(sourceIndex - targetIndex - 1));
            }
        }
        return new DispatcherTransitionPlan(successor, directDepth);
    }

    private sealed record Candidate(
        int Target,
        StructuredDispatcherExit? Exit,
        StructuredDispatcherBlock? Block);
}
