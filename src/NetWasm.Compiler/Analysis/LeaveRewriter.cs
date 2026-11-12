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
        var replacements = body.Instructions.Select(static instruction => new List<CilInstruction> { instruction }).ToArray();
        var changed = false;
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            if (body.Instructions[index] is not { Operation: CilOperation.Leave, Operand: CilOperand.BranchTarget target } leave)
            {
                continue;
            }
            var depth = entryStacks.TryGetValue(leave.Offset, out var stack) ? stack.Length : 0;
            var exits = !Blocks(body, leave.Offset).SetEquals(Blocks(body, target.Offset));
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
