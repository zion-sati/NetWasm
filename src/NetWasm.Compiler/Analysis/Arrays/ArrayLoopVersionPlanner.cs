// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Arrays;

// A counted loop whose body checks span indexes `start + index + k` against a length, as a span read at an offset
// within a chunk does, where start and the length stay put in the loop: start >= 0 and start + bound <= length,
// tested once before it, prove every one of those checks. Ranges lists each (start, length) pair to test; Checks are
// the branches they prove, and Entries the unconditional branches that enter the loop. No path from the loop's exits
// reads its index before writing it, so a copy may count with an index of its own.
//
// A check at `start + index + k`, with k below zero or past the lookahead, or at `start + index + k ± term`, where term
// also stays put in the loop, as a pixel's left neighbor is read at `start + index - 1` and the row above at
// `start + index - width`, is proven instead by an OffsetRange: start + initial + Low ± term >= 0 and
// start + bound + High - lookahead ± term <= length, tested once in 64 bits, bound the smallest and largest index
// every such check reads.
//
// A check at `index + k` alone, as a span is read at `values[index]` in a loop bounded by a count rather than by the
// span's length, is an OffsetRange with no start: bound + High - lookahead <= length proves it, and initial + Low is
// known not to be negative when it is planned.
internal sealed record ArrayLoopVersion(
    ArrayLoopInduction Loop,
    ImmutableArray<(int Start, int Length)> Ranges,
    ImmutableHashSet<int> Checks,
    ImmutableArray<int> Entries)
{
    public ImmutableArray<ArrayLoopOffsetRange> OffsetRanges { get; init; } = [];
}

// The checks at start + index + k (± Term) for k from Low to High, against Length. Start is NoStart where the checks
// are at index + k.
internal sealed record ArrayLoopOffsetRange(int Start, int Length, int Low, int High, int? Term, bool TermNegated)
{
    public const int NoStart = -1;
}

internal interface IArrayLoopVersionPlanner
{
    ImmutableArray<ArrayLoopVersion> Plan(ValidatedControlFlowGraph controlFlow);
}

internal sealed class ArrayLoopVersionPlanner(IArrayLoopInductionAnalyzer induction) : IArrayLoopVersionPlanner
{
    private readonly IArrayLoopInductionAnalyzer _induction = induction ??
        throw new ArgumentNullException(nameof(induction));

    public ImmutableArray<ArrayLoopVersion> Plan(ValidatedControlFlowGraph controlFlow)
    {
        ArgumentNullException.ThrowIfNull(controlFlow);
        var loops = _induction.Analyze(controlFlow);
        if (loops.IsEmpty) return [];
        var graph = controlFlow.Graph;
        var body = graph.MethodBody;
        var exposed = body.Instructions
            .Where(instruction => instruction.Operation == CilOperation.LoadLocalAddress)
            .Select(instruction => ((CilOperand.Index)instruction.Operand).Value)
            .ToHashSet();
        var versions = ImmutableArray.CreateBuilder<ArrayLoopVersion>();
        foreach (var loop in loops)
        {
            var stored = loop.LoopBlocks.SelectMany(block => graph.GetBlock(block).Instructions)
                .Where(instruction => instruction.Operation == CilOperation.StoreLocal)
                .Select(instruction => ((CilOperand.Index)instruction.Operand).Value)
                .ToHashSet();
            // A value the loop leaves alone, read from a local whose address nothing takes. The check compares two
            // int values, so start, the index and the length are int locals.
            bool Invariant(int local) => local != loop.IndexLocal && !stored.Contains(local) && !exposed.Contains(local);
            var (entry, derived) = Derived(controlFlow, loop);
            var ranges = new HashSet<(int Start, int Length)>();
            var offsetRanges = new Dictionary<(int Start, int Length, int? Term, bool TermNegated), (int Low, int High)>();
            var checks = ImmutableHashSet.CreateBuilder<int>();
            // Every block of the body: the guard's taken branch enters it, so each runs only once the guard holds.
            foreach (var blockIndex in loop.LoopBlocks.Where(block => block != loop.GuardBlock))
            {
                // The first block computes the sums; until it stores one, the local still holds the last iteration's.
                var seed = blockIndex == entry ? [] : derived;
                var origins = new List<ValueOrigin>();
                var copies = new Dictionary<int, ValueOrigin>(seed);
                var entered = false;
                foreach (var instruction in graph.GetBlock(blockIndex).Instructions)
                {
                    if (instruction.Offset == loop.IncrementOffset) break;
                    var stack = controlFlow.InstructionEntryStacks[instruction.Offset];
                    // A block may begin with values on the stack, as an element's address a compound assignment
                    // keeps across the reads of its right side; their origins are unknown, the locals' sums are not.
                    if (!entered)
                    {
                        origins.AddRange(Enumerable.Repeat(ValueOrigin.Unknown, stack.Length));
                        entered = true;
                    }
                    else if (origins.Count != stack.Length)
                    {
                        // The stack's origins are lost, not the sums the first block stored.
                        origins.Clear();
                        copies.Clear();
                        foreach (var (local, sum) in seed) copies.Add(local, sum);
                        origins.AddRange(Enumerable.Repeat(ValueOrigin.Unknown, stack.Length));
                    }
                    // `if ((uint)(start + index + k) >= (uint)length) throw`, a span indexer's check.
                    // A compare branch has its two operands on the stack, whose origins follow it.
                    if (instruction.Operation is CilOperation.BranchIfLessThanUnsigned or CilOperation.BranchIfLessThanSigned or
                            CilOperation.BranchIfGreaterThanOrEqualUnsigned or CilOperation.BranchIfGreaterThanOrEqualSigned &&
                        stack[^1] == CliValueKind.I4 && stack[^2] == CliValueKind.I4 &&
                        origins[^2] is { Local: { } first, Base: { } second, Offset: var added } checkedIndex &&
                        (first == loop.IndexLocal ? second : second == loop.IndexLocal ? first : -1) is var start &&
                        start >= 0 && Invariant(start) &&
                        origins[^1] is { Local: { } length, Offset: 0, Constant: false, Length: false, Base: null, Term: null } &&
                        length != start && Invariant(length))
                    {
                        if (checkedIndex.Term is null && added >= 0 && added <= loop.Lookahead)
                        {
                            ranges.Add((start, length));
                            checks.Add(instruction.Offset);
                        }
                        else if (checkedIndex.Term is not { } term ||
                                 term != loop.IndexLocal && term != start && term != length && Invariant(term))
                        {
                            var key = (start, length, checkedIndex.Term, checkedIndex.TermNegated);
                            offsetRanges[key] = offsetRanges.TryGetValue(key, out var known)
                                ? (Math.Min(known.Low, added), Math.Max(known.High, added))
                                : (added, added);
                            checks.Add(instruction.Offset);
                        }
                    }
                    // `if ((uint)(index + k) >= (uint)length) throw` against a length that is not the loop's bound:
                    // the bound's own checks need no copy, and no test passes where the bound is the length and k is
                    // past the lookahead.
                    else if (instruction.Operation is CilOperation.BranchIfLessThanUnsigned or CilOperation.BranchIfLessThanSigned or
                                 CilOperation.BranchIfGreaterThanOrEqualUnsigned or CilOperation.BranchIfGreaterThanOrEqualSigned &&
                             stack[^1] == CliValueKind.I4 && stack[^2] == CliValueKind.I4 &&
                             origins[^2] is { Local: { } counter, Base: null, Constant: false, Length: false, Offset: var ahead } &&
                             counter == loop.IndexLocal && (long)loop.Initial + ahead >= 0 &&
                             origins[^1] is { Local: { } measured, Offset: 0, Constant: false, Length: false, Base: null, Term: null } &&
                             measured != loop.LengthLocal && Invariant(measured))
                    {
                        var key = (ArrayLoopOffsetRange.NoStart, measured, (int?)null, false);
                        offsetRanges[key] = offsetRanges.TryGetValue(key, out var known)
                            ? (Math.Min(known.Low, ahead), Math.Max(known.High, ahead))
                            : (ahead, ahead);
                        checks.Add(instruction.Offset);
                    }
                    ValueOrigins.Track(instruction, origins, copies);
                }
            }
            if (checks.Count == 0 || LiveAfter(graph, loop)) continue;
            // The test replaces the loop's entries, each an unconditional branch to its guard; the initialization that
            // dominates the guard makes the loop reachable, so it has one.
            var entries = graph.Predecessors[loop.GuardBlock].Where(block => !loop.LoopBlocks.Contains(block))
                .Select(block => graph.GetBlock(block).Instructions[^1]).ToArray();
            if (entries.Any(instruction => instruction.Operation != CilOperation.Branch)) continue;
            versions.Add(new(loop, [.. ranges.OrderBy(range => range.Start).ThenBy(range => range.Length)],
                checks.ToImmutable(), [.. entries.Select(instruction => instruction.Offset)])
            {
                OffsetRanges =
                [
                    .. offsetRanges
                        .Select(pair => new ArrayLoopOffsetRange(pair.Key.Start, pair.Key.Length, pair.Value.Low,
                            pair.Value.High, pair.Key.Term, pair.Key.TermNegated))
                        .OrderBy(range => range.Start).ThenBy(range => range.Length)
                        .ThenBy(range => range.Term ?? -1).ThenBy(range => range.TermNegated),
                ],
            });
        }
        return versions.ToImmutable();
    }

    // A local the body's first block stores a `start + index` sum in, once an iteration: that block runs before every
    // other block of the body, so the sum lasts through the iteration, as `var at = start + index;` keeps it for each
    // of a statement's span reads, whose checks end blocks. A local the loop also stores elsewhere is no such sum.
    private static (int Entry, Dictionary<int, ValueOrigin> Derived) Derived(ValidatedControlFlowGraph controlFlow,
        ArrayLoopInduction loop)
    {
        var graph = controlFlow.Graph;
        var derived = new Dictionary<int, ValueOrigin>();
        if (graph.Successors[loop.GuardBlock].Where(loop.LoopBlocks.Contains).ToArray() is not [var entry] ||
            graph.Predecessors[entry].Any(block => block != loop.GuardBlock))
            return (-1, derived);
        var stores = loop.LoopBlocks.SelectMany(block => graph.GetBlock(block).Instructions)
            .Where(instruction => instruction.Operation == CilOperation.StoreLocal)
            .GroupBy(instruction => ((CilOperand.Index)instruction.Operand).Value)
            .ToDictionary(group => group.Key, group => group.Count());
        var origins = new List<ValueOrigin>();
        var copies = new Dictionary<int, ValueOrigin>();
        foreach (var instruction in graph.GetBlock(entry).Instructions)
        {
            if (instruction.Offset == loop.IncrementOffset) return (entry, derived);
            var stack = controlFlow.InstructionEntryStacks[instruction.Offset];
            if (origins.Count != stack.Length)
            {
                origins.Clear();
                copies.Clear();
                origins.AddRange(Enumerable.Repeat(ValueOrigin.Unknown, stack.Length));
            }
            // Taken as it is stored: what the block does after it, such as reading through an address, may lose the
            // stack's origins, but not the sum the local holds.
            if (instruction is { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var stored } } &&
                origins[^1] is { Local: not null, Base: not null, Constant: false, Length: false } sum &&
                sum.Local != stored && sum.Base != stored && sum.Term != stored && stores.GetValueOrDefault(stored) == 1)
                derived[stored] = sum;
            ValueOrigins.Track(instruction, origins, copies);
        }
        return (entry, derived);
    }

    // Whether a path from the loop's exits reads its index before writing it. A path back into the loop, as an
    // enclosing loop's takes, reads it at the guard unless the initialization writes it first.
    private static bool LiveAfter(ControlFlowGraph graph, ArrayLoopInduction loop)
    {
        var pending = new Stack<int>(loop.LoopBlocks.SelectMany(block => graph.Successors[block])
            .Where(block => !loop.LoopBlocks.Contains(block)));
        var seen = new HashSet<int>();
        while (pending.TryPop(out var block))
        {
            if (!seen.Add(block)) continue;
            var written = false;
            foreach (var instruction in graph.GetBlock(block).Instructions)
            {
                if (instruction is not
                    {
                        Operation: CilOperation.LoadLocal or CilOperation.StoreLocal,
                        Operand: CilOperand.Index { Value: var local },
                    } || local != loop.IndexLocal)
                    continue;
                if (instruction.Operation == CilOperation.LoadLocal) return true;
                written = true;
                break;
            }
            if (written) continue;
            foreach (var successor in graph.Successors[block]) pending.Push(successor);
        }
        return false;
    }
}
