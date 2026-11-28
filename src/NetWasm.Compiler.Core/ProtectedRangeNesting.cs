// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Linq;

namespace NetWasm.Compiler.Core;

// Ranges that are nested or apart, as a method's protected blocks, handlers and filters are (ECMA-335 II.19): each
// position's innermost range and each range's parent, the innermost other range holding it. The ranges around a
// position are its innermost range and that range's parents, so a pass that needs them per instruction or block walks
// that chain (stopping at the first range it has already handled) rather than testing every range at every position.
// Innermost: per position, in the order given, the index of the innermost range holding it, or -1.
// Parents: per range, in the order given, the index of its parent, or -1.
public sealed record ProtectedRangeNesting(int[] Innermost, int[] Parents)
{
    // One sort of the ranges and one stack sweep over them and the positions. Each range is given once; a range is
    // [Start, End). Null when two ranges overlap without one holding the other (invalid IL, which a caller answers the
    // slow way and later validation rejects) or when the positions do not ascend.
    public static ProtectedRangeNesting? Build(IReadOnlyList<(int Start, int End)> ranges, IReadOnlyList<int> positions)
    {
        for (var index = 1; index < positions.Count; index++)
        {
            if (positions[index] < positions[index - 1]) return null;
        }
        var order = Enumerable.Range(0, ranges.Count)
            .OrderBy(range => ranges[range].Start)
            .ThenByDescending(range => ranges[range].End)
            .ToArray();
        var parents = new int[ranges.Count];
        var open = new Stack<int>();
        foreach (var range in order)
        {
            while (open.Count > 0 && ranges[open.Peek()].End <= ranges[range].Start) open.Pop();
            if (open.Count > 0 && ranges[open.Peek()].End < ranges[range].End) return null;
            parents[range] = open.Count > 0 ? open.Peek() : -1;
            open.Push(range);
        }
        var innermost = new int[positions.Count];
        open.Clear();
        var next = 0;
        for (var index = 0; index < positions.Count; index++)
        {
            var position = positions[index];
            while (open.Count > 0 && ranges[open.Peek()].End <= position) open.Pop();
            while (next < order.Length && ranges[order[next]].Start <= position)
            {
                var range = order[next++];
                while (open.Count > 0 && ranges[open.Peek()].End <= ranges[range].Start) open.Pop();
                if (ranges[range].End > position) open.Push(range);
            }
            innermost[index] = open.Count > 0 ? open.Peek() : -1;
        }
        return new(innermost, parents);
    }
}
