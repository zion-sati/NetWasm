// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.AddressTakenValues;

internal interface IStructLocalJoinFinder
{
    // The instructions whose whole value reaches the store at join along each path into it, as an inlined conditional
    // construction leaves them (AsSpan's empty span or the span it builds); null where any other path enters, or a
    // path's value is not one the caller accepts, or fewer than two paths do.
    ImmutableArray<int>? Find(ImmutableArray<CilInstruction> instructions, int join, Func<int, bool> accepts);
}

// A path into the join is the instruction falling into it, or an unconditional branch to it, each right after the
// instruction that pushed its value; a conditional branch or a switch into the join carries other values too. A branch
// to such a branch, as a nested conditional's inner arms leave one, carries the values that reach that branch: Roslyn
// pushes the receiver of `this.P = a ? b : c ? d : e` first, and after inlining, the arm that builds b branches to the
// branch that ends the arm building d, so both reach the join through it.
internal sealed class StructLocalJoinFinder : IStructLocalJoinFinder
{
    public ImmutableArray<int>? Find(ImmutableArray<CilInstruction> instructions, int join, Func<int, bool> accepts)
    {
        ArgumentNullException.ThrowIfNull(accepts);
        if ((uint)join >= (uint)instructions.Length) throw new ArgumentOutOfRangeException(nameof(join));
        // Each instruction's index by offset, and the branches and switches into each offset, read once.
        var entries = new Dictionary<int, List<int>>();
        for (var index = 0; index < instructions.Length; index++)
        {
            switch (instructions[index].Operand)
            {
                case CilOperand.BranchTarget { Offset: var target }:
                    Entries(target).Add(index);
                    break;
                case CilOperand.SwitchTargets { Offsets: var targets }:
                    foreach (var target in targets) Entries(target).Add(index);
                    break;
            }
        }
        List<int> Entries(int offset) => entries.TryGetValue(offset, out var list) ? list : entries[offset] = [];

        var sources = ImmutableArray.CreateBuilder<int>();
        // The values reaching at: the instruction falling into it and those reaching each unconditional branch to it.
        // False where any other path enters, a value is rejected, or (for a branch the paths go through) none does.
        // Each branch it follows has its one target on the way to the join, so the walk cannot come back to one.
        bool Collect(int at, bool required)
        {
            var entered = false;
            if (at > 0 && instructions[at - 1].Operation is not (CilOperation.Branch or CilOperation.Leave or
                CilOperation.Return or CilOperation.Throw or CilOperation.Rethrow or CilOperation.EndFinally or
                CilOperation.Switch))
            {
                if (!accepts(at - 1)) return false;
                sources.Add(at - 1);
                entered = true;
            }
            if (entries.TryGetValue(instructions[at].Offset, out var branches))
            {
                foreach (var branch in branches)
                {
                    if (instructions[branch].Operation != CilOperation.Branch || !Collect(branch, required: true))
                    {
                        return false;
                    }
                    entered = true;
                }
            }
            return entered || !required;
        }
        return Collect(join, required: false) && sources.Count >= 2 ? sources.ToImmutable() : null;
    }
}
