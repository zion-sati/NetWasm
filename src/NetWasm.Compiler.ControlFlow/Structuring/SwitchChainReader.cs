// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

// A switch the decoder turned into a chain of compares: the head, the chain's other blocks (each case's compare but
// the first, its pop and branch, and the final pop when it is a block of its own), each case's target and the block
// its branch is in, the default, and the block the default is entered from.
internal sealed record SwitchChain(BasicBlock Head, ImmutableArray<BasicBlock> Blocks, ImmutableArray<int> Cases,
    ImmutableArray<int> Stubs, int Default, int DefaultEntry)
{
    // Each target once, in case order, then the default.
    public ImmutableArray<int> Targets { get; } = [.. Cases.Append(Default).Distinct()];

    // The blocks each target is entered from: the stubs of the cases that branch to it, and the end of the chain for
    // the default.
    public Dictionary<int, HashSet<int>> Entries()
    {
        var entries = new Dictionary<int, HashSet<int>>();
        void Add(int target, int entry)
        {
            if (!entries.TryGetValue(target, out var blocks)) entries.Add(target, blocks = []);
            blocks.Add(entry);
        }
        for (var index = 0; index < Cases.Length; index++)
        {
            Add(Cases[index], Stubs[index]);
        }
        Add(Default, DefaultEntry);
        return entries;
    }
}

internal interface ISwitchChainReader
{
    // The chain the block heads, if it ends with a case's compare of an int32 value and the chain's blocks are entered
    // only through it.
    SwitchChain? Read(ValidatedControlFlowGraph validated, BasicBlock head);
}

internal sealed class SwitchChainReader : ISwitchChainReader
{
    public SwitchChain? Read(ValidatedControlFlowGraph validated, BasicBlock head)
    {
        if (head.Instructions is not [.., { Operation: CilOperation.Duplicate } test, { Operation: CilOperation.LoadInt32 },
            { Operation: CilOperation.BranchIfNotEqual }] ||
            !validated.InstructionEntryStacks.TryGetValue(test.Offset, out var stack) ||
            stack is not [.., CliValueKind.I4])
        {
            return null;
        }
        var graph = validated.Graph;
        var instructions = graph.MethodBody.Instructions;
        var start = IndexOf(instructions, test.Offset);
        var count = LoweredSwitchChain.CaseCount(instructions, start);
        if (count == 0)
        {
            return null;
        }
        var blocks = new List<BasicBlock>();
        var stubs = new int[count];
        var previous = head;
        for (var index = 0; index < count; index++)
        {
            var at = start + index * LoweredSwitchChain.CaseLength;
            if (index > 0)
            {
                var condition = graph.GetBlockAtOffset(instructions[at].Offset);
                if (condition.Instructions.Length != 3 || !EnteredOnce(graph, condition.Index))
                {
                    return null;
                }
                blocks.Add(condition);
                previous = condition;
            }
            var stub = graph.GetBlockAtOffset(instructions[at + 3].Offset);
            if (stub.Instructions.Length != 2 || !EnteredOnce(graph, stub.Index))
            {
                return null;
            }
            blocks.Add(stub);
            stubs[index] = stub.Index;
        }
        var final = graph.GetBlockAtOffset(instructions[start + count * LoweredSwitchChain.CaseLength].Offset);
        if (!EnteredOnce(graph, final.Index))
        {
            return null;
        }
        // The final pop belongs to the chain when it is a block of its own; otherwise it starts the default's code.
        var finalOwned = final.Instructions.Length == 1 && graph.Successors[final.Index] is [_];
        if (finalOwned)
        {
            blocks.Add(final);
        }
        var cases = Enumerable.Range(0, count)
            .Select(index => graph.GetBlockAtOffset(LoweredSwitchChain.Target(instructions, start, index)).Index)
            .ToImmutableArray();
        return new SwitchChain(head, [.. blocks], cases, [.. stubs], finalOwned ? graph.Successors[final.Index][0] : final.Index,
            finalOwned ? final.Index : previous.Index);
    }

    // Each of the chain's blocks is entered from the one before it; entered once, from nowhere else.
    private static bool EnteredOnce(ControlFlowGraph graph, int block) => graph.Predecessors[block] is [_];

    private static int IndexOf(ImmutableArray<CilInstruction> instructions, int offset)
    {
        var lower = 0;
        var upper = instructions.Length - 1;
        while (lower < upper)
        {
            var middle = lower + (upper - lower) / 2;
            if (instructions[middle].Offset < offset) lower = middle + 1;
            else upper = middle;
        }
        return lower;
    }
}
