// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface ICilLocalLiveness
{
    // Whether some path from the instruction at index reads the local before writing it.
    bool IsLive(int local, int index);
}

// Whether a local is live where an instruction starts: some path from it reads the local (or takes its address) before
// writing it. Each local of the set spreads backward from its reads over the blocks it is live through, stopping at
// blocks that write it, so the work is the size of the locals' live ranges rather than the body times the locals, and
// a pass that asks about many loops in one method pays for one analysis. Bodies with switches or protected regions
// are not supported: their callers leave such bodies alone.
internal sealed class CilLocalLiveness : ICilLocalLiveness
{
    private readonly ImmutableArray<CilInstruction> _instructions;
    private readonly int[] _blockOf;
    private readonly int[] _blockEnds;
    // The blocks each local is live at the end of.
    private readonly Dictionary<int, HashSet<int>> _liveOut;

    private CilLocalLiveness(
        ImmutableArray<CilInstruction> instructions, int[] blockOf, int[] blockEnds, Dictionary<int, HashSet<int>> liveOut)
    {
        _instructions = instructions;
        _blockOf = blockOf;
        _blockEnds = blockEnds;
        _liveOut = liveOut;
    }

    public static CilLocalLiveness Compute(
        ImmutableArray<CilInstruction> instructions, IReadOnlyDictionary<int, int> indices, IEnumerable<int> locals)
    {
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentNullException.ThrowIfNull(locals);
        var tracked = locals.ToHashSet();

        // Blocks: a leader at the start, at each branch target and after each branch or exit.
        var leaders = new bool[instructions.Length + 1];
        leaders[0] = true;
        for (var index = 0; index < instructions.Length; index++)
        {
            if (instructions[index].Operand is CilOperand.BranchTarget { Offset: var target }) leaders[indices[target]] = true;
            if (instructions[index].Operand is CilOperand.BranchTarget || Ends(instructions[index].Operation)) leaders[index + 1] = true;
        }
        var starts = Enumerable.Range(0, instructions.Length).Where(index => leaders[index]).ToArray();
        var blockOf = new int[instructions.Length];
        var blockEnds = new int[starts.Length];
        for (var block = 0; block < starts.Length; block++)
        {
            blockEnds[block] = block + 1 < starts.Length ? starts[block + 1] : instructions.Length;
            for (var index = starts[block]; index < blockEnds[block]; index++) blockOf[index] = block;
        }
        var predecessors = Enumerable.Range(0, starts.Length).Select(static _ => new List<int>()).ToArray();
        for (var block = 0; block < starts.Length; block++)
        {
            var last = instructions[blockEnds[block] - 1];
            if (last.Operand is CilOperand.BranchTarget { Offset: var target }) predecessors[blockOf[indices[target]]].Add(block);
            if (last.Operation != CilOperation.Branch && last.Operation != CilOperation.Leave && !Ends(last.Operation) &&
                blockEnds[block] < instructions.Length)
            {
                predecessors[block + 1].Add(block);
            }
        }

        // Each tracked local's reads and the blocks that write it.
        var reads = tracked.ToDictionary(static local => local, static _ => new List<int>());
        var writers = tracked.ToDictionary(static local => local, static _ => new HashSet<int>());
        for (var index = 0; index < instructions.Length; index++)
        {
            if (Local(instructions[index]) is not var (local, write) || !tracked.Contains(local)) continue;
            if (write) writers[local].Add(blockOf[index]);
            else reads[local].Add(index);
        }

        var liveOut = new Dictionary<int, HashSet<int>>();
        foreach (var local in tracked)
        {
            var blocks = new HashSet<int>();
            var pending = new Stack<int>();
            void Enter(int block)
            {
                foreach (var predecessor in predecessors[block])
                {
                    if (blocks.Add(predecessor)) pending.Push(predecessor);
                }
            }
            // A read the block does not write the local before makes it live where the block starts.
            foreach (var read in reads[local])
            {
                var block = blockOf[read];
                var written = false;
                for (var index = starts[block]; index < read && !written; index++)
                {
                    written = Local(instructions[index]) is var (other, write) && other == local && write;
                }
                if (!written) Enter(block);
            }
            // Live at a block's end and not written in it: live where it starts too.
            while (pending.TryPop(out var block))
            {
                if (!writers[local].Contains(block)) Enter(block);
            }
            liveOut[local] = blocks;
        }
        return new(instructions, blockOf, blockEnds, liveOut);
    }

    // Whether some path from the instruction at index reads the local before writing it; a local outside the set the
    // analysis was computed for is an error.
    public bool IsLive(int local, int index)
    {
        var live = _liveOut[local];
        if (index >= _instructions.Length) return false;
        var block = _blockOf[index];
        for (var at = index; at < _blockEnds[block]; at++)
        {
            if (Local(_instructions[at]) is var (read, write) && read == local) return !write;
        }
        return live.Contains(block);
    }

    private static (int Local, bool Write)? Local(CilInstruction instruction) => instruction switch
    {
        { Operation: CilOperation.LoadLocal or CilOperation.LoadLocalAddress, Operand: CilOperand.Index { Value: var local } } => (local, false),
        { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var local } } => (local, true),
        _ => null,
    };

    private static bool Ends(CilOperation operation) =>
        operation is CilOperation.Return or CilOperation.Throw or CilOperation.Rethrow or CilOperation.EndFinally or
            CilOperation.Branch or CilOperation.Leave;
}
