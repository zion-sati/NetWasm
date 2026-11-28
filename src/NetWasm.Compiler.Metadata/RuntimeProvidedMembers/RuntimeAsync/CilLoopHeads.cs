// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync;

// Where control enters a loop: the instruction range from a backward branch's target to its last backward branch is
// entered from outside at its start, unless the start is reached only from inside and one instruction further in is
// reached from outside. Roslyn lays a for or while loop out that way, with its condition below its body and entered
// by a branch over the body (br cond; body: ...; cond: ...; blt body). A dispatch that passes through the loop on its
// way to a resume point enters it there too, so the loop keeps one entry and its control flow stays reducible.
// The method's edges are sorted by target once, so a range's entries are the edges into it found by a binary search,
// not a scan of the method per range.
internal sealed class CilLoopHeads
{
    // Every edge (fall-through, branch, switch arm) and the method's start, as (target, source), by target.
    private readonly (int Target, int Source)[] _edges;
    // Handlers and filters with the start of the protected block they belong to, by their offset.
    private readonly (int Offset, int ProtectedStart)[] _handlerEntries;

    internal CilLoopHeads(CilMethodBody body)
    {
        var instructions = body.Instructions;
        var edges = new List<(int Target, int Source)>();
        if (instructions.Length > 0) edges.Add((instructions[0].Offset, int.MinValue));
        for (var position = 0; position < instructions.Length; position++)
        {
            var instruction = instructions[position];
            if (CilLiveness.FallsThrough(instruction.Operation) && position + 1 < instructions.Length)
                edges.Add((instructions[position + 1].Offset, instruction.Offset));
            if (instruction.Operand is CilOperand.BranchTarget branch) edges.Add((branch.Offset, instruction.Offset));
            if (instruction.Operand is CilOperand.SwitchTargets table)
            {
                foreach (var offset in table.Offsets) edges.Add((offset, instruction.Offset));
            }
        }
        _edges = [.. edges.OrderBy(edge => edge.Target)];
        _handlerEntries =
        [
            .. body.ExceptionRegions
                .SelectMany(region => region.FilterOffset is { } filter
                    ? new[] { (region.HandlerOffset, region.TryOffset), (filter, region.TryOffset) }
                    : new[] { (region.HandlerOffset, region.TryOffset) })
                .OrderBy(entry => entry.Item1),
        ];
    }

    // The one offset in [start, end] that control reaches from outside the range, or start when there are several
    // (the method's own control flow is then irreducible already) or none.
    internal static int Head(CilMethodBody body, int start, int end) => new CilLoopHeads(body).HeadOf(start, end);

    internal int HeadOf(int start, int end)
    {
        // A handler or filter is entered from its protected block, so from outside when that block is outside; the
        // dispatch never enters one, so such a range keeps its start.
        for (var index = LowerBound(_handlerEntries, start, entry => entry.Offset);
             index < _handlerEntries.Length && _handlerEntries[index].Offset <= end;
             index++)
        {
            if (!Within(_handlerEntries[index].ProtectedStart, start, end)) return start;
        }
        int? entry = null;
        for (var index = LowerBound(_edges, start, edge => edge.Target);
             index < _edges.Length && _edges[index].Target <= end;
             index++)
        {
            var (target, source) = _edges[index];
            if (source != int.MinValue && Within(source, start, end)) continue;
            if (entry is { } known && known != target) return start;
            entry = target;
        }
        return entry ?? start;
    }

    private static int LowerBound<T>(T[] items, int value, Func<T, int> key)
    {
        var low = 0;
        var high = items.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (key(items[middle]) < value) low = middle + 1;
            else high = middle;
        }
        return low;
    }

    private static bool Within(int offset, int start, int end) => offset >= start && offset <= end;
}
