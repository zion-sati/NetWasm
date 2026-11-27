// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions;

internal interface ISolePredecessorResolver
{
    CilInstruction? Resolve(InstructionEmissionRequest request);
}

// Strategy: the instruction that alone leads to this one, falling through to it, when no branch
// or switch also enters here. A lexical predecessor is not proof when another path skips it.
// A method's branch targets are gathered once, and the instruction is found by a binary search (a
// structured method's instructions are in increasing offset order): a scan of the method for each
// query made a method with N delegate constructions cost N squared.
internal sealed class SolePredecessorResolver : ISolePredecessorResolver
{
    private readonly ConditionalWeakTable<StructuredMethodHeader, HashSet<int>> _branchTargets = new();

    public CilInstruction? Resolve(InstructionEmissionRequest request)
    {
        var body = request.Header.Instructions;
        var offset = request.Instruction.Offset;
        var index = IndexOf(body, request.Instruction);
        if (index <= 0 || body[index - 1].NextOffset != offset)
            return null;
        if (_branchTargets.GetValue(request.Header, BranchTargets).Contains(offset))
            return null;
        return body[index - 1];
    }

    // The instruction's index, or -1: offsets are unique and part of an instruction's identity.
    private static int IndexOf(ImmutableArray<CilInstruction> body, CilInstruction instruction)
    {
        var low = 0;
        var high = body.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) >> 1);
            var at = body[middle].Offset;
            if (at == instruction.Offset) return body[middle] == instruction ? middle : -1;
            if (at < instruction.Offset) low = middle + 1;
            else high = middle - 1;
        }
        return -1;
    }

    private static HashSet<int> BranchTargets(StructuredMethodHeader header)
    {
        var targets = new HashSet<int>();
        foreach (var candidate in header.Instructions)
        {
            if (candidate.Operand is CilOperand.BranchTarget branch) targets.Add(branch.Offset);
        }
        return targets;
    }
}
