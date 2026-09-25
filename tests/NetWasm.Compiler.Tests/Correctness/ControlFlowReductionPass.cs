using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class ControlFlowReductionPass : IGeneratedCilReductionPass
{
    private static readonly HashSet<CilOperation> UnaryBranches =
    [
        CilOperation.BranchIfTrue,
        CilOperation.BranchIfFalse,
    ];

    private static readonly HashSet<CilOperation> BinaryBranches =
    [
        CilOperation.BranchIfEqual,
        CilOperation.BranchIfNotEqual,
        CilOperation.BranchIfGreaterThanSigned,
        CilOperation.BranchIfGreaterThanUnsigned,
        CilOperation.BranchIfGreaterThanOrEqualSigned,
        CilOperation.BranchIfGreaterThanOrEqualUnsigned,
        CilOperation.BranchIfLessThanSigned,
        CilOperation.BranchIfLessThanUnsigned,
        CilOperation.BranchIfLessThanOrEqualSigned,
        CilOperation.BranchIfLessThanOrEqualUnsigned,
    ];

    public string Name => "edges-and-blocks";

    public IEnumerable<GeneratedCilReductionCase> Generate(
        GeneratedCilReductionCase candidate)
    {
        if (!candidate.Program.ExceptionRegions.IsEmpty)
        {
            yield break;
        }
        for (var blockIndex = 0;
             blockIndex < candidate.Program.Blocks.Length;
             blockIndex++)
        {
            var block = candidate.Program.Blocks[blockIndex];
            var terminal = block.Instructions[^1];
            var popCount = UnaryBranches.Contains(terminal.Operation) ||
                           terminal.Operation == CilOperation.Switch
                ? 1
                : BinaryBranches.Contains(terminal.Operation)
                    ? 2
                    : 0;
            if (popCount == 0)
            {
                continue;
            }
            foreach (var target in Targets(
                         candidate.Program,
                         blockIndex,
                         terminal))
            {
                var replacement = Enumerable
                    .Repeat(
                        new GeneratedCilInstruction(
                            CilOperation.Pop,
                            new GeneratedCilOperand.None()),
                        popCount)
                    .Append(new(
                        CilOperation.Branch,
                        new GeneratedCilOperand.BlockTarget(target)))
                    .ToImmutableArray();
                var rewritten = candidate.Program with
                {
                    Blocks = candidate.Program.Blocks.SetItem(
                        blockIndex,
                        block with
                        {
                            Instructions = block.Instructions
                                .RemoveAt(block.Instructions.Length - 1)
                                .AddRange(replacement),
                        }),
                };
                yield return candidate with
                {
                    Program = PruneUnreachable(rewritten),
                };
            }
        }
    }

    private static IEnumerable<int> Targets(
        GeneratedCilProgram program,
        int blockIndex,
        GeneratedCilInstruction terminal)
    {
        if (terminal.Operand is GeneratedCilOperand.BlockTarget target)
        {
            yield return target.BlockId;
        }
        else if (terminal.Operand is GeneratedCilOperand.BlockTargets targets)
        {
            foreach (var blockId in targets.BlockIds)
            {
                yield return blockId;
            }
        }
        if (blockIndex < program.Blocks.Length - 1)
        {
            yield return program.Blocks[blockIndex + 1].Id;
        }
    }

    private static GeneratedCilProgram PruneUnreachable(GeneratedCilProgram program)
    {
        var byId = program.Blocks.ToDictionary(block => block.Id);
        var reachable = new HashSet<int>();
        var pending = new Queue<int>();
        pending.Enqueue(program.Blocks[0].Id);
        while (pending.TryDequeue(out var blockId))
        {
            if (!reachable.Add(blockId) || !byId.TryGetValue(blockId, out var block))
            {
                continue;
            }
            var index = program.Blocks.IndexOf(block);
            var terminal = block.Instructions[^1];
            foreach (var successor in Successors(program, index, terminal))
            {
                pending.Enqueue(successor);
            }
        }
        return program with
        {
            Blocks = [.. program.Blocks.Where(block => reachable.Contains(block.Id))],
        };
    }

    private static IEnumerable<int> Successors(
        GeneratedCilProgram program,
        int blockIndex,
        GeneratedCilInstruction terminal)
    {
        if (terminal.Operand is GeneratedCilOperand.BlockTarget target)
        {
            yield return target.BlockId;
        }
        else if (terminal.Operand is GeneratedCilOperand.BlockTargets targets)
        {
            foreach (var blockId in targets.BlockIds)
            {
                yield return blockId;
            }
        }
        if ((UnaryBranches.Contains(terminal.Operation) ||
             BinaryBranches.Contains(terminal.Operation) ||
             terminal.Operation == CilOperation.Switch) &&
            blockIndex < program.Blocks.Length - 1)
        {
            yield return program.Blocks[blockIndex + 1].Id;
        }
    }
}
