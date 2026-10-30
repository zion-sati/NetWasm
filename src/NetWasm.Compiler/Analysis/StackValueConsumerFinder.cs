using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface IStackValueConsumerFinder
{
    // The instruction in the producer's block that pops the value it pushed,
    // or null when something reads it first or an instruction is not followed.
    // Instructions at the offsets in entries start blocks.
    int? Find(ImmutableArray<CilInstruction> instructions, HashSet<int> entries, int producer);
}

internal sealed class StackValueConsumerFinder(IStackEffectResolver effects) : IStackValueConsumerFinder
{
    private readonly IStackEffectResolver _effects = effects ?? throw new ArgumentNullException(nameof(effects));

    public int? Find(ImmutableArray<CilInstruction> instructions, HashSet<int> entries, int producer)
    {
        // Values pushed above the producer's.
        var above = 0;
        for (var index = producer + 1; index < instructions.Length; index++)
        {
            if (entries.Contains(instructions[index].Offset) ||
                _effects.Resolve(instructions[index]) is not var (pops, pushes) || pops > above + 1)
            {
                return null;
            }
            if (pops == above + 1)
            {
                return index;
            }
            // The value also leaves on the branch, to be taken elsewhere.
            if (instructions[index].Operation is CilOperation.BranchIfTrue or CilOperation.BranchIfFalse)
            {
                return null;
            }
            above += pushes - pops;
        }
        return null;
    }
}
