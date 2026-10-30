using System;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Constants;

// A reachable block that ends with an unconditional branch to a later block,
// across nothing but unreachable blocks, falls through instead: the branch and
// the dead instructions become nops, so the blocks merge and a constant pushed
// before the branch meets the instruction after it. Code a folded check cut
// off, such as a throw, is what usually lies between. No exception region may
// begin or end inside the span.
internal sealed class DeadJumpRewriter(IControlFlowGraphBuilder graphs) : IMethodRewriteRule
{
    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!body.Instructions.Any(static instruction => instruction.Operation == CilOperation.Branch)) return body;
        var graph = _graphs.Build(body);
        var blocks = graph.Blocks;
        var boundaries = body.ExceptionRegions
            .SelectMany(static region => new[]
            {
                region.TryOffset, region.TryOffset + region.TryLength,
                region.HandlerOffset, region.HandlerOffset + region.HandlerLength,
                region.FilterOffset ?? -1,
            })
            .ToHashSet();
        var nops = new System.Collections.Generic.HashSet<int>();
        for (var index = 0; index < blocks.Length; index++)
        {
            var block = blocks[index];
            if (!graph.ReachableBlocks.Contains(block.Index) ||
                block.Terminator is not { Operation: CilOperation.Branch, Operand: CilOperand.BranchTarget { Offset: var target } } ||
                target <= block.Terminator.Offset)
            {
                continue;
            }
            var next = index + 1;
            while (next < blocks.Length && blocks[next].StartOffset < target && !graph.ReachableBlocks.Contains(blocks[next].Index)) next++;
            if (next >= blocks.Length || blocks[next].StartOffset != target ||
                boundaries.Any(offset => offset > block.Terminator.Offset && offset < target))
            {
                continue;
            }
            nops.Add(block.Terminator.Offset);
            for (var dead = index + 1; dead < next; dead++)
            {
                foreach (var instruction in blocks[dead].Instructions) nops.Add(instruction.Offset);
            }
            index = next - 1;
        }
        return nops.Count == 0 ? body : body with
        {
            Instructions = [.. body.Instructions.Select(instruction => nops.Contains(instruction.Offset)
                ? instruction with { Operation = CilOperation.Nop, Operand = new CilOperand.None() }
                : instruction)],
        };
    }
}
