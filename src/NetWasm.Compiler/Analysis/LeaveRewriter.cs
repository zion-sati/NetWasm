// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// leave exits any number of enclosing blocks, none included, and empties the evaluation stack
// (ECMA-335 III.3.46). Structured control flow treats a leave as an exit from the blocks it
// leaves, made on an empty stack, so each value a leave discards is popped before it, and one
// that exits no block becomes the branch it then is.
internal sealed class LeaveRewriter(IControlFlowGraphBuilder graphs, ITypedStackValidator stacks)
    : IMethodRewriteRule
{
    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
    private readonly ITypedStackValidator _stacks = stacks ?? throw new ArgumentNullException(nameof(stacks));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!body.Instructions.Any(static instruction => instruction.Operation == CilOperation.Leave))
        {
            return body;
        }

        var entryStacks = _stacks.Validate(_graphs.Build(body)).InstructionEntryStacks;
        var innermost = InnermostBlocks(body);
        var replacements = body.Instructions.Select(static instruction => new List<CilInstruction> { instruction }).ToArray();
        var changed = false;
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            if (body.Instructions[index] is not { Operation: CilOperation.Leave, Operand: CilOperand.BranchTarget target } leave)
            {
                continue;
            }
            var depth = entryStacks.TryGetValue(leave.Offset, out var stack) ? stack.Length : 0;
            var exits = innermost is null
                ? !Blocks(body, leave.Offset).SetEquals(Blocks(body, target.Offset))
                : innermost[leave.Offset] != innermost[target.Offset];
            if (depth == 0 && exits)
            {
                continue;
            }
            changed = true;
            replacements[index] =
            [
                .. Enumerable.Range(0, depth).Select(_ => new CilInstruction(0, 1, CilOperation.Pop, new CilOperand.None())),
                exits ? leave : leave with { Operation = CilOperation.Branch },
            ];
        }
        return changed ? RenumberedInstructions.Splice(body, replacements) : body;
    }

    // The innermost try, handler or filter block each leave and its target lie in (-1 for none), from one sweep of the
    // blocks in start order; null when two blocks overlap without one holding the other, which valid IL never has.
    // Blocks that nest are a tree, so the blocks an offset lies in are its innermost block's ancestors, and two
    // offsets lie in the same blocks exactly when they lie in the same innermost block. Comparing every leave's blocks,
    // each found by scanning every region, made a method with N protected blocks cost N squared.
    private static Dictionary<int, int>? InnermostBlocks(CilMethodBody body)
    {
        var distinct = new HashSet<(int Start, int End)>();
        foreach (var region in body.ExceptionRegions)
        {
            distinct.Add((region.TryOffset, region.TryOffset + region.TryLength));
            distinct.Add((region.HandlerOffset, region.HandlerOffset + region.HandlerLength));
            if (region.FilterOffset is int filter) distinct.Add((filter, region.HandlerOffset));
        }
        var blocks = distinct.Where(static block => block.Start < block.End).ToArray();
        // An enclosing block before the blocks it holds.
        Array.Sort(blocks, static (left, right) =>
            left.Start != right.Start ? left.Start.CompareTo(right.Start) : right.End.CompareTo(left.End));
        var offsets = new SortedSet<int>();
        foreach (var instruction in body.Instructions)
        {
            if (instruction is not { Operation: CilOperation.Leave, Operand: CilOperand.BranchTarget target }) continue;
            offsets.Add(instruction.Offset);
            offsets.Add(target.Offset);
        }
        var innermost = new Dictionary<int, int>(offsets.Count);
        var open = new Stack<int>();
        var next = 0;
        foreach (var offset in offsets)
        {
            for (; next < blocks.Length && blocks[next].Start <= offset; next++)
            {
                while (open.Count > 0 && blocks[open.Peek()].End <= blocks[next].Start) open.Pop();
                if (open.Count > 0 && blocks[next].End > blocks[open.Peek()].End) return null;
                open.Push(next);
            }
            while (open.Count > 0 && blocks[open.Peek()].End <= offset) open.Pop();
            innermost[offset] = open.Count > 0 ? open.Peek() : -1;
        }
        return innermost;
    }

    // The try, handler and filter blocks an offset lies in.
    private static HashSet<(int Start, int End)> Blocks(CilMethodBody body, int offset)
    {
        var blocks = new HashSet<(int Start, int End)>();
        foreach (var region in body.ExceptionRegions)
        {
            Add(region.TryOffset, region.TryOffset + region.TryLength);
            Add(region.HandlerOffset, region.HandlerOffset + region.HandlerLength);
            if (region.FilterOffset is int filter)
            {
                Add(filter, region.HandlerOffset);
            }
        }
        return blocks;

        void Add(int start, int end)
        {
            if (start <= offset && offset < end)
            {
                blocks.Add((start, end));
            }
        }
    }
}
