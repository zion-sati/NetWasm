// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Loops;

// Where branches land, for a pass that computes a value ahead of a loop's entry jump: the value is ready only if that
// jump is the loop's one way in. The decoder lowers every switch into a chain of branches (CilSwitchLowerer), so a
// branch target is every landing.
internal static class LoopEntries
{
    // Each instruction's index, by the indices of the branches that land on it.
    public static Dictionary<int, List<int>> BranchSources(ImmutableArray<CilInstruction> instructions, Dictionary<int, int> indices)
    {
        var sources = new Dictionary<int, List<int>>();
        for (var index = 0; index < instructions.Length; index++)
        {
            if (instructions[index].Operand is CilOperand.BranchTarget { Offset: var offset })
            {
                if (!indices.TryGetValue(offset, out var target)) continue;
                if (!sources.TryGetValue(target, out var list)) sources[target] = list = [];
                list.Add(index);
            }
        }
        return sources;
    }

    // A branch from outside the loop (before its entry jump, or after its backward branch) lands in its body or test.
    public static bool EnteredElsewhere(Dictionary<int, List<int>> sources, int jump, int end)
    {
        for (var index = jump + 1; index <= end; index++)
        {
            if (sources.TryGetValue(index, out var from) && from.Any(source => source < jump || source > end)) return true;
        }
        return false;
    }
}
