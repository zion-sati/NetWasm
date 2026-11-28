// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow;

// Splits a body into basic blocks and connects them. Offsets are bounded by the body's
// size, so leaders, boundaries and blocks by offset are indexed directly; the work is
// linear in the body and its exception regions.
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
        var instructions = methodBody.Instructions;
        if (instructions.IsEmpty)
        {
            throw Invalid(methodBody, 0, "method body is empty");
        }

        var endOffset = instructions[^1].NextOffset;
        _exceptionRegions.Validate(methodBody, endOffset);
        var leaders = new bool[endOffset + 1];
        // A branch target outside the body is never an instruction boundary.
        int? outsideTarget = null;
        AddLeader(instructions[0].Offset);
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
        foreach (var instruction in instructions)
        {
            if (instruction.Operand is CilOperand.BranchTarget target)
            {
                AddLeader(target.Offset);
                if (instruction.NextOffset < endOffset)
                {
                    AddLeader(instruction.NextOffset);
                }
            }
            else if (IsTerminal(instruction.Operation) &&
                     instruction.NextOffset < endOffset)
            {
                AddLeader(instruction.NextOffset);
            }
        }

        var boundaries = new bool[endOffset + 1];
        foreach (var instruction in instructions)
        {
            if ((uint)instruction.Offset < (uint)boundaries.Length) boundaries[instruction.Offset] = true;
        }
        // The lowest leader that is not an instruction boundary is reported, as a sorted
        // walk of the leaders would find it.
        if (outsideTarget is < 0)
        {
            throw Invalid(methodBody, outsideTarget.Value, "branch target is not an instruction boundary");
        }
        for (var offset = 0; offset < endOffset; offset++)
        {
            if (leaders[offset] && !boundaries[offset])
            {
                throw Invalid(methodBody, offset, "branch target is not an instruction boundary");
            }
        }
        if (outsideTarget is int beyond)
        {
            throw Invalid(methodBody, beyond, "branch target is not an instruction boundary");
        }

        // Instructions are in offset order, so each leader starts the next block.
        var blockCount = 0;
        foreach (var instruction in instructions)
        {
            if (leaders[instruction.Offset]) blockCount++;
        }
        var blocks = ImmutableArray.CreateBuilder<BasicBlock>(blockCount);
        var blockAtOffset = new int[endOffset + 1];
        Array.Fill(blockAtOffset, -1);
        var start = 0;
        for (var index = 1; index <= instructions.Length; index++)
        {
            if (index < instructions.Length && !leaders[instructions[index].Offset])
                continue;
            var block = new BasicBlock(blocks.Count, instructions[start].Offset,
                ImmutableArray.Create(instructions, start, index - start));
            blockAtOffset[block.StartOffset] = block.Index;
            blocks.Add(block);
            start = index;
        }

        var immutableBlocks = blocks.MoveToImmutable();
        var successors = BuildSuccessors(methodBody, immutableBlocks, blockAtOffset);
        var predecessors = BuildPredecessors(successors);
        var exceptionalSuccessors = BuildExceptionalSuccessors(methodBody, immutableBlocks, blockAtOffset);
        var exceptionalPredecessors = BuildPredecessors(exceptionalSuccessors);
        var reachable = FindReachable(methodBody, immutableBlocks, successors, blockAtOffset);
        return new ControlFlowGraph(
            methodBody,
            immutableBlocks,
            BlockEdgeDictionary.Over(successors),
            BlockEdgeDictionary.Over(predecessors),
            BlockEdgeDictionary.Over(exceptionalSuccessors),
            BlockEdgeDictionary.Over(exceptionalPredecessors),
            reachable);

        void AddLeader(int offset)
        {
            if ((uint)offset < (uint)endOffset)
            {
                leaders[offset] = true;
            }
            else if (outsideTarget is not int known || offset < known)
            {
                outsideTarget = offset;
            }
        }

        void AddBoundary(int offset)
        {
            if (offset < endOffset)
            {
                AddLeader(offset);
            }
        }
    }

    // Per block, the handler (or filter) entries of the regions whose try range holds an
    // instruction that may transfer control exceptionally; distinct and in order.
    private static ImmutableArray<int>[] BuildExceptionalSuccessors(
        CilMethodBody body,
        ImmutableArray<BasicBlock> blocks,
        int[] blockAtOffset)
    {
        // Blocks in offset order meet the protected blocks overlapping them, kept open from where each starts until a
        // block starts at or past its end: each block reads only its own regions, not every region of the method.
        var result = new ImmutableArray<int>[blocks.Length];
        var targets = new List<int>();
        var regions = body.ExceptionRegions;
        var byStart = Enumerable.Range(0, regions.Length).OrderBy(index => regions[index].TryOffset).ToArray();
        var open = new List<int>();
        var next = 0;
        foreach (var block in blocks.OrderBy(block => block.StartOffset))
        {
            targets.Clear();
            if (block.Instructions.IsEmpty)
            {
                result[block.Index] = [];
                continue;
            }
            var first = block.Instructions[0].Offset;
            var last = block.Instructions[^1].Offset;
            while (next < byStart.Length && regions[byStart[next]].TryOffset <= last) open.Add(byStart[next++]);
            open.RemoveAll(index => regions[index].TryOffset + regions[index].TryLength <= first);
            foreach (var index in open)
            {
                var region = regions[index];
                var tryEnd = region.TryOffset + region.TryLength;
                foreach (var instruction in block.Instructions)
                {
                    if (instruction.Offset >= region.TryOffset && instruction.Offset < tryEnd &&
                        CilSafepointClassifier.MayTransferControlExceptionally(instruction))
                    {
                        targets.Add(blockAtOffset[region.FilterOffset ?? region.HandlerOffset]);
                        break;
                    }
                }
            }
            result[block.Index] = SortedDistinct(targets);
        }
        return result;
    }

    private static ImmutableArray<int>[] BuildSuccessors(
        CilMethodBody methodBody,
        ImmutableArray<BasicBlock> blocks,
        int[] blockAtOffset)
    {
        var successors = new ImmutableArray<int>[blocks.Length];
        foreach (var block in blocks)
        {
            var terminator = block.Terminator;
            successors[block.Index] = terminator.Operation switch
            {
                CilOperation.Return or CilOperation.Throw or CilOperation.Rethrow or
                    CilOperation.EndFinally or CilOperation.EndFilter =>
                    [],
                CilOperation.Branch or CilOperation.Leave =>
                    [BranchTargetIndex(terminator, blockAtOffset)],
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
                    Distinct(
                        BranchTargetIndex(terminator, blockAtOffset),
                        FallthroughIndex(methodBody, block, blocks)),
                _ => [FallthroughIndex(methodBody, block, blocks)],
            };
        }
        return successors;
    }

    private static ImmutableArray<int> Distinct(int first, int second) =>
        first == second ? [first] : [first, second];

    private static bool IsTerminal(CilOperation operation) => operation is
        CilOperation.Return or CilOperation.Throw or CilOperation.Rethrow or
        CilOperation.EndFinally or CilOperation.EndFilter;

    private static int BranchTargetIndex(CilInstruction instruction, int[] blockAtOffset) =>
        // Leader validation has already established that every target begins a block.
        blockAtOffset[((CilOperand.BranchTarget)instruction.Operand).Offset];

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

    // Each block's predecessors, in the order of their sources.
    private static ImmutableArray<int>[] BuildPredecessors(ImmutableArray<int>[] successors)
    {
        var counts = new int[successors.Length];
        foreach (var targets in successors)
        {
            foreach (var target in targets) counts[target]++;
        }
        var builders = new ImmutableArray<int>.Builder?[successors.Length];
        for (var source = 0; source < successors.Length; source++)
        {
            foreach (var target in successors[source])
            {
                (builders[target] ??= ImmutableArray.CreateBuilder<int>(counts[target])).Add(source);
            }
        }
        var predecessors = new ImmutableArray<int>[successors.Length];
        for (var block = 0; block < successors.Length; block++)
        {
            predecessors[block] = builders[block] is { } builder ? builder.MoveToImmutable() : [];
        }
        return predecessors;
    }

    // Blocks reachable from the entry through normal edges and the entries (handlers and
    // filters) of the live protected regions around reached blocks. A live region
    // conservatively activates all its entries, including cleanup handlers and both sides of
    // a filter; a dead region does not create roots merely because its metadata remains.
    private static ImmutableHashSet<int> FindReachable(
        CilMethodBody body,
        ImmutableArray<BasicBlock> blocks,
        ImmutableArray<int>[] successors,
        int[] blockAtOffset)
    {
        // A reached block brings the handlers and filters of the tries holding its start: through its innermost try
        // and that try's parents up to the first already brought (whose parents came with it), each try once. Tries
        // that overlap without nesting (invalid, which validation rejects) are tested at each block.
        var tries = body.ExceptionRegions
            .GroupBy(region => (Start: region.TryOffset, End: region.TryOffset + region.TryLength))
            .ToArray();
        var byStart = Enumerable.Range(0, blocks.Length).OrderBy(block => blocks[block].StartOffset).ToArray();
        var nesting = ProtectedRangeNesting.Build(
            [.. tries.Select(group => group.Key)],
            [.. byStart.Select(block => blocks[block].StartOffset)]);
        var innermost = new int[blocks.Length];
        if (nesting is not null)
        {
            for (var index = 0; index < byStart.Length; index++) innermost[byStart[index]] = nesting.Innermost[index];
        }
        var brought = new bool[tries.Length];
        var reached = new bool[blocks.Length];
        var pending = new Stack<int>();
        pending.Push(0);
        while (pending.TryPop(out var current))
        {
            if (reached[current])
            {
                continue;
            }
            reached[current] = true;
            foreach (var successor in successors[current])
            {
                pending.Push(successor);
            }
            if (nesting is not null)
            {
                for (var group = innermost[current]; group >= 0 && !brought[group]; group = nesting.Parents[group])
                {
                    brought[group] = true;
                    foreach (var region in tries[group])
                    {
                        pending.Push(blockAtOffset[region.HandlerOffset]);
                        if (region.FilterOffset is int filter)
                            pending.Push(blockAtOffset[filter]);
                    }
                }
                continue;
            }
            var start = blocks[current].StartOffset;
            foreach (var region in body.ExceptionRegions)
            {
                if (start < region.TryOffset || start >= region.TryOffset + region.TryLength)
                    continue;
                pending.Push(blockAtOffset[region.HandlerOffset]);
                if (region.FilterOffset is int filterOffset)
                    pending.Push(blockAtOffset[filterOffset]);
            }
        }
        var reachable = ImmutableHashSet.CreateBuilder<int>();
        for (var block = 0; block < reached.Length; block++)
        {
            if (reached[block]) reachable.Add(block);
        }
        return reachable.ToImmutable();
    }

    private static ImmutableArray<int> SortedDistinct(List<int> values)
    {
        if (values.Count == 0) return [];
        if (values.Count == 1) return [values[0]];
        values.Sort();
        var builder = ImmutableArray.CreateBuilder<int>(values.Count);
        foreach (var value in values)
        {
            if (builder.Count == 0 || builder[^1] != value) builder.Add(value);
        }
        return builder.ToImmutable();
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
