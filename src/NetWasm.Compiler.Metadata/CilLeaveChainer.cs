// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal interface ICilLeaveChainer
{
    // The body with every leave that exits more than one protected block or catch handler, through blocks that every
    // such leave passes towards the same target and that hold no later block after the one it leaves, split into
    // leaves that each exit one, through a leave placed at the end of each block the original passes out of.
    ChainedLeaveBody Chain(
        ImmutableArray<CilInstruction> instructions,
        ImmutableArray<CilExceptionRegion> exceptionRegions);
}

internal sealed record ChainedLeaveBody(
    ImmutableArray<CilInstruction> Instructions,
    ImmutableArray<CilExceptionRegion> ExceptionRegions,
    bool Changed);

// The exception group structurer gives a group's leave whose target lies past the group's continuation boundary a
// continuation running to the method's end. Roslyn leaves a try/catch inside a try/finally straight to the statement
// after the outer finally, past the inner group's boundary, so every such group structured the rest of the method
// again: quadratic in the method's nested blocks, and worse where those continuations hold a shared exit, as a
// runtime-async method's suspension exit is. A leave that exits one block at a time, as a state machine's IL already
// has it, keeps each continuation inside the block around it. Leaving through the chain runs the finally handlers in
// the same order, innermost first, and nothing else reaches the added leaves: no instruction falls through the end of a
// protected block or a handler.
// Cost: the scopes and the try starts sorted once, one sweep over the instructions, and for each leave a walk out
// through the scopes it exits with a binary search per step; the added leaves are shared by every leave that passes the
// same scope towards the same target.
internal sealed class CilLeaveChainer : ICilLeaveChainer
{
    public ChainedLeaveBody Chain(
        ImmutableArray<CilInstruction> instructions,
        ImmutableArray<CilExceptionRegion> exceptionRegions)
    {
        if (exceptionRegions.IsEmpty || !instructions.Any(instruction => instruction.Operation == CilOperation.Leave))
        {
            return new ChainedLeaveBody(instructions, exceptionRegions, Changed: false);
        }

        var scopes = Scopes(exceptionRegions, out var parents, out var depths);
        var innermost = InnermostScopes(instructions, scopes);
        // The scopes each leave exits, innermost first, for the leaves that exit more than one.
        var exits = new Dictionary<int, (List<int> Scopes, int Target)>();
        for (var index = 0; index < instructions.Length; index++)
        {
            if (instructions[index] is not { Operation: CilOperation.Leave, Operand: CilOperand.BranchTarget { Offset: var leaveTarget } })
            {
                continue;
            }
            var exited = new List<int>();
            for (var scope = innermost[index]; scope >= 0 && !Contains(scopes[scope], leaveTarget); scope = parents[scope])
            {
                exited.Add(scope);
            }
            if (exited.Count >= 2) exits.Add(index, (exited, leaveTarget));
        }
        // A scope passed on the way to two targets keeps its leaves as they are: one exit per scope is the shape that
        // keeps the structurer's continuations local (a state machine's), and a nested group with two continuations
        // has none to dispatch between them. Every leave that exits more than one scope counts, chained or not.
        const int Several = int.MinValue;
        var passedTo = new Dictionary<int, int>();
        foreach (var (passed, target) in exits.Values)
        {
            for (var step = 1; step < passed.Count; step++)
            {
                passedTo[passed[step]] = passedTo.TryGetValue(passed[step], out var known) && known != target ? Several : target;
            }
        }
        // A leave whose hop to the end of a scope would pass a later try/catch or try/finally in that scope keeps its
        // target: the structurer keeps a group's continuation local only up to its next sibling, so a hop over one
        // leaves the group as the original leave did, with a second exit beside it. Each step is a binary search of the
        // try starts after the exited group's last handler.
        var tryStarts = exceptionRegions.Select(region => region.TryOffset).Distinct().Order().ToArray();
        var groupEnds = GroupEnds(exceptionRegions);
        bool PassesLaterGroup(List<int> exited)
        {
            for (var step = 1; step < exited.Count; step++)
            {
                var after = groupEnds[scopes[exited[step - 1]]];
                var index = Array.BinarySearch(tryStarts, after);
                if (index < 0) index = ~index;
                if (index < tryStarts.Length && tryStarts[index] < scopes[exited[step]].End) return true;
            }
            return false;
        }
        foreach (var index in exits.Keys.Where(index => PassesLaterGroup(exits[index].Scopes)).ToArray())
        {
            exits.Remove(index);
        }
        // The leaves to add, by the scope each sits at the end of and the final target, and where each original leave
        // goes first.
        var added = new Dictionary<(int Scope, int Target), int>();
        var addedList = new List<(int Scope, int Target)>();
        // Each added leave reports the first original leave that passes through it, for stack traces and line tables.
        var sources = new Dictionary<(int Scope, int Target), int>();
        var firstHops = new Dictionary<int, (int Scope, int Target)>();
        foreach (var (index, (exited, target)) in exits.OrderBy(pair => pair.Key))
        {
            var instruction = instructions[index];
            if (exited.Skip(1).Any(scope => passedTo[scope] != target))
            {
                continue;
            }
            for (var step = 1; step < exited.Count; step++)
            {
                if (added.TryAdd((exited[step], target), addedList.Count))
                {
                    addedList.Add((exited[step], target));
                    sources.Add((exited[step], target), instruction.SourceOffset);
                }
            }
            firstHops.Add(index, (exited[1], target));
        }
        if (firstHops.Count == 0)
        {
            return new ChainedLeaveBody(instructions, exceptionRegions, Changed: false);
        }

        // Each added leave sits at the end of its scope, before the instruction that follows the scope; where nested
        // scopes end at one offset, an inner scope's leaves come before an outer one's, so each lies inside its own
        // scope and outside the scopes within it.
        var addedAt = addedList
            .Select((key, number) => (Key: key, Number: number))
            .GroupBy(entry => scopes[entry.Key.Scope].End)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(entry => depths[entry.Key.Scope]).ThenBy(entry => entry.Number)
                    .Select(entry => entry.Key).ToArray());
        var methodEnd = instructions[^1].NextOffset;
        var starts = new Dictionary<int, int>(instructions.Length + 1);
        var addedIndices = new Dictionary<(int Scope, int Target), int>(addedList.Count);
        // For each offset where leaves were added, the index of its first added leave.
        var addedBases = new Dictionary<int, int>(addedAt.Count);
        var position = 0;
        void Place(int offset)
        {
            if (!addedAt.TryGetValue(offset, out var keys)) return;
            addedBases.Add(offset, position);
            foreach (var key in keys) addedIndices.Add(key, position++);
        }
        foreach (var instruction in instructions)
        {
            Place(instruction.Offset);
            starts.Add(instruction.Offset, position++);
        }
        Place(methodEnd);
        starts.Add(methodEnd, position);

        var result = ImmutableArray.CreateBuilder<CilInstruction>(position);
        void EmitAdded(int offset)
        {
            if (!addedAt.TryGetValue(offset, out var keys)) return;
            foreach (var key in keys)
            {
                // The next scope out on the way to the target, or the target itself once the scope around it is reached.
                var next = parents[key.Scope];
                var destination = next >= 0 && !Contains(scopes[next], key.Target) &&
                    addedIndices.TryGetValue((next, key.Target), out var hop)
                    ? hop
                    : starts[key.Target];
                result.Add(new CilInstruction(result.Count, result.Count + 1, CilOperation.Leave,
                    new CilOperand.BranchTarget(destination))
                {
                    OriginalOffset = sources[key],
                });
            }
        }
        for (var index = 0; index < instructions.Length; index++)
        {
            var instruction = instructions[index];
            EmitAdded(instruction.Offset);
            var operand = firstHops.TryGetValue(index, out var firstHop)
                ? new CilOperand.BranchTarget(addedIndices[firstHop])
                : Rewrite(instruction.Operand, starts);
            result.Add(instruction with
            {
                Offset = result.Count,
                NextOffset = result.Count + 1,
                Operand = operand,
                OriginalOffset = instruction.SourceOffset,
            });
        }
        EmitAdded(methodEnd);

        // A scope's end takes in the leaves added at it for itself and the scopes within it.
        int End(int offset, int scopeDepth)
        {
            if (!addedAt.TryGetValue(offset, out var keys)) return starts[offset];
            return addedBases[offset] + keys.Count(key => depths[key.Scope] >= scopeDepth);
        }
        var scopeDepths = new Dictionary<(int Start, int End), int>(scopes.Length);
        for (var scope = 0; scope < scopes.Length; scope++) scopeDepths[scopes[scope]] = depths[scope];
        var regions = exceptionRegions.Select(region =>
        {
            var tryEnd = region.TryOffset + region.TryLength;
            var handlerEnd = region.HandlerOffset + region.HandlerLength;
            var tryStart = starts[region.TryOffset];
            var handlerStart = starts[region.HandlerOffset];
            return region with
            {
                TryOffset = tryStart,
                TryLength = End(tryEnd, scopeDepths[(region.TryOffset, tryEnd)]) - tryStart,
                HandlerOffset = handlerStart,
                HandlerLength = End(handlerEnd, scopeDepths[(region.HandlerOffset, handlerEnd)]) - handlerStart,
                FilterOffset = region.FilterOffset is int filter ? starts[filter] : null,
            };
        }).ToImmutableArray();
        return new ChainedLeaveBody(result.MoveToImmutable(), regions, Changed: true);
    }

    // Where the group each scope belongs to ends: the end of the last handler of the try it is, or of the try its
    // handler belongs to.
    private static Dictionary<(int Start, int End), int> GroupEnds(ImmutableArray<CilExceptionRegion> regions)
    {
        var tryEnds = new Dictionary<(int Start, int End), int>();
        foreach (var region in regions)
        {
            var key = (region.TryOffset, region.TryOffset + region.TryLength);
            tryEnds[key] = Math.Max(tryEnds.GetValueOrDefault(key), region.HandlerOffset + region.HandlerLength);
        }
        var ends = new Dictionary<(int Start, int End), int>(tryEnds);
        foreach (var region in regions)
        {
            var handler = (region.HandlerOffset, region.HandlerOffset + region.HandlerLength);
            var end = tryEnds[(region.TryOffset, region.TryOffset + region.TryLength)];
            ends[handler] = Math.Max(ends.GetValueOrDefault(handler), end);
        }
        return ends;
    }

    // Every protected block and handler as a scope, sorted outer first, with the innermost scope around each and its
    // depth. Blocks are nested or apart (ECMA-335 II.19), so one stack sweep finds the parents.
    private static (int Start, int End)[] Scopes(
        ImmutableArray<CilExceptionRegion> regions,
        out int[] parents,
        out int[] depths)
    {
        var scopes = regions
            .SelectMany(region => new[]
            {
                (Start: region.TryOffset, End: region.TryOffset + region.TryLength),
                (Start: region.HandlerOffset, End: region.HandlerOffset + region.HandlerLength),
            })
            .Distinct()
            .OrderBy(scope => scope.Start)
            .ThenByDescending(scope => scope.End)
            .ToArray();
        parents = new int[scopes.Length];
        depths = new int[scopes.Length];
        var open = new Stack<int>();
        for (var index = 0; index < scopes.Length; index++)
        {
            while (open.Count > 0 && !Holds(scopes[open.Peek()], scopes[index])) open.Pop();
            parents[index] = open.Count > 0 ? open.Peek() : -1;
            depths[index] = open.Count;
            open.Push(index);
        }
        return scopes;
    }

    // The innermost scope holding each instruction, or -1, in one sweep: scopes open in start order and close at
    // their ends, and the open ones are nested.
    private static int[] InnermostScopes(ImmutableArray<CilInstruction> instructions, (int Start, int End)[] scopes)
    {
        var innermost = new int[instructions.Length];
        var open = new Stack<int>();
        var next = 0;
        for (var index = 0; index < instructions.Length; index++)
        {
            var offset = instructions[index].Offset;
            while (open.Count > 0 && scopes[open.Peek()].End <= offset) open.Pop();
            while (next < scopes.Length && scopes[next].Start <= offset)
            {
                while (open.Count > 0 && !Holds(scopes[open.Peek()], scopes[next])) open.Pop();
                if (scopes[next].End > offset) open.Push(next);
                next++;
            }
            innermost[index] = open.Count > 0 ? open.Peek() : -1;
        }
        return innermost;
    }

    private static bool Holds((int Start, int End) outer, (int Start, int End) inner) =>
        outer.Start <= inner.Start && inner.End <= outer.End;

    private static bool Contains((int Start, int End) scope, int offset) =>
        offset >= scope.Start && offset < scope.End;

    private static CilOperand Rewrite(CilOperand operand, Dictionary<int, int> starts) => operand switch
    {
        CilOperand.BranchTarget target => new CilOperand.BranchTarget(starts[target.Offset]),
        CilOperand.SwitchTargets targets => new CilOperand.SwitchTargets([.. targets.Offsets.Select(offset => starts[offset])]),
        _ => operand,
    };
}
