using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow;

internal sealed class CilControlFlowGraphBuilder : IControlFlowGraphBuilder
{
    private readonly ICilExceptionRegionValidator _exceptionRegions;

    public CilControlFlowGraphBuilder(ICilExceptionRegionValidator exceptionRegions)
    {
        _exceptionRegions = exceptionRegions ?? throw new ArgumentNullException(nameof(exceptionRegions));
    }

    public ControlFlowGraph Build(CilMethodBody methodBody)
    {
        ArgumentNullException.ThrowIfNull(methodBody);
        if (methodBody.Instructions.IsEmpty)
        {
            throw Invalid(methodBody, 0, "method body is empty");
        }

        var leaders = new SortedSet<int> { methodBody.Instructions[0].Offset };
        var endOffset = methodBody.Instructions[^1].NextOffset;
        _exceptionRegions.Validate(methodBody, endOffset);
        foreach (var region in methodBody.ExceptionRegions)
        {
            AddBoundary(region.TryOffset);
            AddBoundary(region.TryOffset + region.TryLength);
            AddBoundary(region.HandlerOffset);
            AddBoundary(region.HandlerOffset + region.HandlerLength);
            if (region.FilterOffset is int filterOffset)
            {
                AddBoundary(filterOffset);
            }
        }
        foreach (var instruction in methodBody.Instructions)
        {
            if (instruction.Operand is CilOperand.BranchTarget target)
            {
                leaders.Add(target.Offset);
                if (instruction.NextOffset < endOffset)
                {
                    leaders.Add(instruction.NextOffset);
                }
            }
            else if (IsTerminal(instruction.Operation) &&
                     instruction.NextOffset < endOffset)
            {
                leaders.Add(instruction.NextOffset);
            }
        }

        var instructionOffsets = methodBody.Instructions
            .Select(instruction => instruction.Offset)
            .ToImmutableHashSet();
        foreach (var leader in leaders)
        {
            if (!instructionOffsets.Contains(leader))
            {
                throw Invalid(methodBody, leader, "branch target is not an instruction boundary");
            }
        }

        var leaderArray = leaders.ToArray();
        var blocks = ImmutableArray.CreateBuilder<BasicBlock>(leaderArray.Length);
        for (var index = 0; index < leaderArray.Length; index++)
        {
            var start = leaderArray[index];
            var end = index + 1 < leaderArray.Length ? leaderArray[index + 1] : endOffset;
            blocks.Add(new BasicBlock(
                index,
                start,
                [
                    ..methodBody.Instructions
                        .Where(instruction => instruction.Offset >= start && instruction.Offset < end)
                ]));
        }

        var immutableBlocks = blocks.ToImmutable();
        var successors = BuildSuccessors(
            methodBody, immutableBlocks);
        var predecessors = BuildPredecessors(
            immutableBlocks.Length, successors);
        var blockIndexByOffset = immutableBlocks
            .ToImmutableDictionary(block => block.StartOffset, block => block.Index);
        var exceptionalSuccessors = BuildExceptionalSuccessors(
            methodBody,
            immutableBlocks,
            blockIndexByOffset);
        var exceptionalPredecessors = BuildPredecessors(
            immutableBlocks.Length,
            exceptionalSuccessors);
        var roots = methodBody.ExceptionRegions
            .SelectMany(region => region.FilterOffset is int filterOffset
                ? new[] { region.HandlerOffset, filterOffset }
                : [region.HandlerOffset])
            .Select(offset => blockIndexByOffset[offset])
            .Append(0)
            .ToImmutableHashSet();
        var reachable = FindReachable(successors, roots);
        return new ControlFlowGraph(
            methodBody,
            immutableBlocks,
            successors,
            predecessors,
            exceptionalSuccessors,
            exceptionalPredecessors,
            reachable);

        void AddBoundary(int offset)
        {
            if (offset < endOffset)
            {
                leaders.Add(offset);
            }
        }
    }

    private static ImmutableDictionary<int, ImmutableArray<int>>
        BuildExceptionalSuccessors(
            CilMethodBody body,
            ImmutableArray<BasicBlock> blocks,
            ImmutableDictionary<int, int> blockIndexByOffset) => blocks
        .ToImmutableDictionary(
            block => block.Index,
            block => body.ExceptionRegions
                .Where(region => block.Instructions.Any(instruction =>
                    instruction.Offset >= region.TryOffset &&
                    instruction.Offset < region.TryOffset + region.TryLength &&
                    CilSafepointClassifier.MayTransferControlExceptionally(instruction)))
                .Select(region => blockIndexByOffset[
                    region.FilterOffset ?? region.HandlerOffset])
                .Distinct()
                .Order()
                .ToImmutableArray());

    private static ImmutableDictionary<int, ImmutableArray<int>> BuildSuccessors(
        CilMethodBody methodBody,
        ImmutableArray<BasicBlock> blocks)
    {
        var indexByOffset = blocks.ToImmutableDictionary(
            block => block.StartOffset,
            block => block.Index);
        var successors = ImmutableDictionary.CreateBuilder<int, ImmutableArray<int>>();
        foreach (var block in blocks)
        {
            var terminator = block.Terminator;
            var targets = terminator.Operation switch
            {
                CilOperation.Return or CilOperation.Throw or CilOperation.Rethrow or
                    CilOperation.EndFinally or CilOperation.EndFilter =>
                    ImmutableArray<int>.Empty,
                CilOperation.Branch or CilOperation.Leave =>
                    [BranchTargetIndex(terminator, indexByOffset)],
                CilOperation.BranchIfTrue or CilOperation.BranchIfFalse or
                    CilOperation.BranchIfEqual or CilOperation.BranchIfNotEqual or
                    CilOperation.BranchIfGreaterThanSigned or
                    CilOperation.BranchIfGreaterThanUnsigned or
                    CilOperation.BranchIfGreaterThanOrEqualSigned or
                    CilOperation.BranchIfGreaterThanOrEqualUnsigned or
                    CilOperation.BranchIfLessThanSigned or
                    CilOperation.BranchIfLessThanUnsigned or
                    CilOperation.BranchIfLessThanOrEqualSigned or
                    CilOperation.BranchIfLessThanOrEqualUnsigned =>
                    [
                        BranchTargetIndex(terminator, indexByOffset),
                        FallthroughIndex(methodBody, block, blocks),
                    ],
                _ => [FallthroughIndex(methodBody, block, blocks)],
            };
            successors.Add(block.Index, [.. targets.Distinct()]);
        }
        return successors.ToImmutable();
    }

    private static bool IsTerminal(CilOperation operation) => operation is
        CilOperation.Return or CilOperation.Throw or CilOperation.Rethrow or
        CilOperation.EndFinally or CilOperation.EndFilter;

    private static int BranchTargetIndex(
        CilInstruction instruction,
        ImmutableDictionary<int, int> indexByOffset)
    {
        var offset = ((CilOperand.BranchTarget)instruction.Operand).Offset;
        // Leader validation has already established that every target begins a block.
        return indexByOffset[offset];
    }

    private static int FallthroughIndex(
        CilMethodBody methodBody,
        BasicBlock block,
        ImmutableArray<BasicBlock> blocks) =>
        block.Index + 1 < blocks.Length
            ? block.Index + 1
            : throw Invalid(
                methodBody,
                block.Terminator.Offset,
                "control can fall off the end of the method");

    private static ImmutableDictionary<int, ImmutableArray<int>> BuildPredecessors(
        int blockCount,
        ImmutableDictionary<int, ImmutableArray<int>> successors)
    {
        var builders = Enumerable.Range(0, blockCount)
            .Select(_ => ImmutableArray.CreateBuilder<int>())
            .ToArray();
        foreach ((var source, var targets) in successors)
        {
            foreach (var target in targets)
            {
                builders[target].Add(source);
            }
        }
        return Enumerable.Range(0, blockCount).ToImmutableDictionary(
            index => index,
            index => builders[index].ToImmutable());
    }

    private static ImmutableHashSet<int> FindReachable(
        ImmutableDictionary<int, ImmutableArray<int>> successors,
        IEnumerable<int> roots)
    {
        var reachable = ImmutableHashSet.CreateBuilder<int>();
        var pending = new Stack<int>();
        foreach (var root in roots)
        {
            pending.Push(root);
        }
        while (pending.TryPop(out var current))
        {
            if (!reachable.Add(current))
            {
                continue;
            }
            foreach (var successor in successors[current])
            {
                pending.Push(successor);
            }
        }
        return reachable.ToImmutable();
    }

    private static CompilerException Invalid(
        CilMethodBody methodBody,
        int offset,
        string message) => new(
        new CompilerDiagnostic(
            DiagnosticCode.InvalidCil,
            message,
            methodBody.Method.Name,
            offset));
}
