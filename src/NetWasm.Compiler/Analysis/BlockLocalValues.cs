// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// Where a forward analysis over a method's blocks keeps each local's value. A local some block reads before it writes
// it carries its value from block to block, so the state between blocks has a slot for it. Every other local is
// written before it is read in each block that reads it, as each condition's local in Debug IL is, so its value lives
// only within the block that wrote it. A state with every local at every block costs locals times blocks: quadratic
// in a long else-if chain in Debug IL, where each condition has a local of its own.
internal sealed class BlockLocalValues<T>
{
    private readonly int[] _slots;
    private readonly T[] _withinBlock;
    private readonly List<int> _written = [];

    // The locals the analysis follows are those `follows` accepts; the rest have no slot and are never read.
    internal BlockLocalValues(ImmutableArray<BasicBlock> blocks, int locals, Func<int, bool> follows)
    {
        ArgumentNullException.ThrowIfNull(follows);
        _slots = new int[locals];
        _withinBlock = new T[locals];
        var crossing = new bool[locals];
        var written = new HashSet<int>();
        foreach (var block in blocks)
        {
            written.Clear();
            foreach (var instruction in block.Instructions)
            {
                if (instruction is not { Operand: CilOperand.Index local } || (uint)local.Value >= (uint)locals) continue;
                if (instruction.Operation == CilOperation.StoreLocal) written.Add(local.Value);
                else if (instruction.Operation == CilOperation.LoadLocal && !written.Contains(local.Value)) crossing[local.Value] = true;
            }
        }
        for (var local = 0; local < locals; local++)
        {
            _slots[local] = crossing[local] && follows(local) ? Crossing++ : -1;
        }
    }

    // The locals the state from block to block holds, and so the length of its array.
    internal int Crossing { get; }

    internal T Read(T[] state, int local) => _slots[local] is var slot and >= 0 ? state[slot] : _withinBlock[local];

    internal void Write(T[] state, int local, T value)
    {
        if (_slots[local] is var slot and >= 0)
        {
            state[slot] = value;
            return;
        }
        _withinBlock[local] = value;
        _written.Add(local);
    }

    internal T Read(LocalValueTrie<T> state, int local) => _slots[local] is var slot and >= 0 ? state.Get(slot) : _withinBlock[local];

    // The state with the local's value written: a new state for a local that crosses blocks, the same one otherwise.
    internal LocalValueTrie<T> Write(LocalValueTrie<T> state, int local, T value)
    {
        if (_slots[local] is var slot and >= 0)
        {
            return state.Set(slot, value);
        }
        _withinBlock[local] = value;
        _written.Add(local);
        return state;
    }

    // Forgets the local's value in a state another block starts with; a value within this block is not in it.
    internal void Forget(T[] state, int local)
    {
        if (_slots[local] is var slot and >= 0) state[slot] = default!;
    }

    // The state with the local's value forgotten; a value within this block is not in it.
    internal LocalValueTrie<T> Forget(LocalValueTrie<T> state, int local) =>
        _slots[local] is var slot and >= 0 ? state.Set(slot, default!) : state;

    // Values written within a block do not outlive it.
    internal void EndBlock()
    {
        foreach (var local in _written)
        {
            _withinBlock[local] = default!;
        }
        _written.Clear();
    }
}
