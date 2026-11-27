// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface IFinallyCloner
{
    CilMethodBody Rewrite(CilMethodBody body);
}

// A finally runs on every way out of its protected block: on a leave, as the block's code goes on, and when an exception
// leaves it. The normal ways out run a copy of the handler instead, as .NET's JIT clones a finally: each leave that exits
// the block goes to a copy of the handler made for its target, whose endfinally goes on to the target (through the copy
// of the next finally out when the leave exits that one too), and the handler, now a fault, runs only when an exception
// leaves the block. The normal path then keeps nothing of the finally's exceptional machinery (the exception cell, the
// test of whether to rethrow, the continuation the handler goes back to), and the copy is plain code the passes after
// this one see.
//
// A copy is placed after its handler, inside every block that encloses the finally's protected block, so an exception it
// throws reaches the handlers an exception from the finally would, and is not caught by the fault. A finally is copied
// when its handler holds no protected block, its protected block lies in no handler, every finally its protected block
// holds is copied too (a leave that exits several runs their copies in turn), and its copies fit the budget (the
// handler's instructions times its targets). A runtime-async suspension leaves through a finally without running it: the
// lowering's guard at the start of the handler, which the copy keeps, does that.
internal sealed class FinallyCloner(int budget) : IFinallyCloner
{
    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (budget <= 0 || !body.ExceptionRegions.Any(static region => region.Kind == CilExceptionRegionKind.Finally))
        {
            return body;
        }
        var instructions = body.Instructions;
        var regions = body.ExceptionRegions;
        var nesting = new ExceptionRegionNesting(instructions, regions);

        // The leaves out of protected blocks, and the finally blocks each exits, innermost first.
        var leaves = new List<(int Position, int Target)>();
        for (var position = 0; position < instructions.Length; position++)
        {
            if (instructions[position] is { Operation: CilOperation.Leave, Operand: CilOperand.BranchTarget target })
            {
                leaves.Add((position, target.Offset));
            }
        }
        var finallies = Enumerable.Range(0, regions.Length).Where(region => regions[region].Kind == CilExceptionRegionKind.Finally)
            .ToArray();
        // A finally is a candidate when its handler holds no protected block, handler or filter and its protected block
        // lies in no handler or filter.
        var cloned = finallies.Where(region => !nesting.HandlerHoldsABlock(region) && !nesting.InHandler(regions[region].TryOffset))
            .ToHashSet();
        if (cloned.Count == 0) return body;
        var handlerLength = cloned.ToDictionary(region => region, region => HandlerLength(nesting, regions[region]));
        Dictionary<int, SortedSet<int>> targets;
        // Dropping a finally over budget drops the copied ones whose protected blocks hold it, and a finally left with
        // fewer targets is one of those, so this settles in a round or two.
        while (true)
        {
            targets = cloned.ToDictionary(region => region, _ => new SortedSet<int>());
            foreach (var (position, target) in leaves)
            {
                foreach (var region in Exited(nesting, regions, instructions[position].Offset, target))
                {
                    if (!cloned.Contains(region)) break;
                    targets[region].Add(target);
                }
            }
            var over = cloned.Where(region => targets[region].Count == 0 ||
                (long)handlerLength[region] * targets[region].Count > budget).ToArray();
            // The protected blocks around a finally that is not copied, each marked once: its chain stops at a marked one.
            var holdsUncopied = new bool[regions.Length];
            foreach (var inner in finallies.Where(region => !cloned.Contains(region)))
            {
                for (var outer = nesting.EnclosingTry(inner); outer >= 0 && !holdsUncopied[outer]; outer = nesting.EnclosingTry(outer))
                {
                    holdsUncopied[outer] = true;
                }
            }
            var outside = cloned.Where(region => holdsUncopied[region]).ToArray();
            if (over.Length == 0 && outside.Length == 0) break;
            cloned.ExceptWith(over);
            cloned.ExceptWith(outside);
            if (cloned.Count == 0) return body;
        }

        // Layout: the original instructions in order, each cloned finally's copies placed before the instruction after its
        // handler; an original offset maps to its instruction's new position, after any copies placed before it.
        var copiesBefore = new Dictionary<int, List<int>>();
        foreach (var region in cloned.OrderBy(region => regions[region].TryLength))
        {
            var end = regions[region].HandlerOffset + regions[region].HandlerLength;
            if (!copiesBefore.TryGetValue(end, out var list)) copiesBefore.Add(end, list = []);
            list.Add(region);
        }
        var starts = new Dictionary<int, int>(instructions.Length + 1);
        var copyStart = new Dictionary<(int Region, int Target), int>();
        var regionCopiesStart = new Dictionary<int, int>();
        var count = 0;
        void PlaceCopies(int offset)
        {
            if (!copiesBefore.TryGetValue(offset, out var list)) return;
            foreach (var region in list)
            {
                regionCopiesStart[region] = count;
                foreach (var target in targets[region])
                {
                    copyStart[(region, target)] = count;
                    count += handlerLength[region] + 1;
                }
            }
        }
        for (var position = 0; position < instructions.Length; position++)
        {
            PlaceCopies(instructions[position].Offset);
            starts[instructions[position].Offset] = count++;
        }
        var endOffset = instructions[^1].NextOffset;
        PlaceCopies(endOffset);
        starts[endOffset] = count;

        // Where a leave out of `from` for target goes: the copy of the innermost cloned finally it exits, or the target.
        int Destination(int from, int target)
        {
            foreach (var region in Exited(nesting, regions, from, target))
            {
                return cloned.Contains(region) ? copyStart[(region, target)] : starts[target];
            }
            return starts[target];
        }

        var rewritten = ImmutableArray.CreateBuilder<CilInstruction>(count);
        void Emit(CilInstruction source, CilOperation operation, CilOperand operand)
        {
            rewritten.Add(new CilInstruction(rewritten.Count, rewritten.Count + 1, operation, operand)
            {
                OriginalOffset = source.SourceOffset,
                SequencePointOffset = source.SequencePointOffset,
            });
        }
        void EmitCopies(int offset)
        {
            if (!copiesBefore.TryGetValue(offset, out var list)) return;
            foreach (var region in list)
            {
                var handler = regions[region];
                var first = nesting.Position(handler.HandlerOffset);
                foreach (var target in targets[region])
                {
                    var start = copyStart[(region, target)];
                    var exit = start + handlerLength[region];
                    for (var position = first; position < first + handlerLength[region]; position++)
                    {
                        var instruction = instructions[position];
                        switch (instruction)
                        {
                            case { Operation: CilOperation.EndFinally }:
                                Emit(instruction, CilOperation.Branch, new CilOperand.BranchTarget(exit));
                                break;
                            case { Operand: CilOperand.BranchTarget branch }:
                                Emit(instruction, instruction.Operation,
                                    new CilOperand.BranchTarget(start + nesting.Position(branch.Offset) - first));
                                break;
                            case { Operand: CilOperand.SwitchTargets table }:
                                Emit(instruction, instruction.Operation, new CilOperand.SwitchTargets(
                                    [.. table.Offsets.Select(offset => start + nesting.Position(offset) - first)]));
                                break;
                            default:
                                Emit(instruction, instruction.Operation, instruction.Operand);
                                break;
                        }
                    }
                    // The copy goes on as the leave would after this finally: through the next finally out, or to the
                    // target, with a leave where that exits a block and a branch where it exits none (the passes before
                    // this one turned such leaves into branches, as structured control flow takes them). The handler lies
                    // in no handler, so the blocks it can leave are protected blocks.
                    var next = Destination(handler.HandlerOffset, target);
                    var leaves = next != starts[target] || nesting.TriesLeft(handler.HandlerOffset, target).Any();
                    Emit(instructions[first + handlerLength[region] - 1],
                        leaves ? CilOperation.Leave : CilOperation.Branch, new CilOperand.BranchTarget(next));
                }
            }
        }
        for (var position = 0; position < instructions.Length; position++)
        {
            var instruction = instructions[position];
            EmitCopies(instruction.Offset);
            switch (instruction)
            {
                case { Operation: CilOperation.Leave, Operand: CilOperand.BranchTarget target }:
                    Emit(instruction, CilOperation.Leave, new CilOperand.BranchTarget(Destination(instruction.Offset, target.Offset)));
                    break;
                case { Operand: CilOperand.BranchTarget branch }:
                    Emit(instruction, instruction.Operation, new CilOperand.BranchTarget(starts[branch.Offset]));
                    break;
                case { Operand: CilOperand.SwitchTargets table }:
                    Emit(instruction, instruction.Operation, new CilOperand.SwitchTargets([.. table.Offsets.Select(offset => starts[offset])]));
                    break;
                default:
                    Emit(instruction, instruction.Operation, instruction.Operand);
                    break;
            }
        }
        EmitCopies(endOffset);

        var mapped = ImmutableArray.CreateBuilder<CilExceptionRegion>(regions.Length);
        for (var region = 0; region < regions.Length; region++)
        {
            var moved = RenumberedInstructions.Region(regions[region], starts);
            mapped.Add(cloned.Contains(region)
                ? moved with { Kind = CilExceptionRegionKind.Fault, HandlerLength = regionCopiesStart[region] - moved.HandlerOffset }
                : moved);
        }
        return body with { Instructions = rewritten.MoveToImmutable(), ExceptionRegions = mapped.MoveToImmutable() };
    }

    // The finally blocks a leave from `from` to target exits, innermost first.
    private static IEnumerable<int> Exited(ExceptionRegionNesting nesting, ImmutableArray<CilExceptionRegion> regions,
        int from, int target) =>
        nesting.TriesLeft(from, target).Where(region => regions[region].Kind == CilExceptionRegionKind.Finally);

    private static int HandlerLength(ExceptionRegionNesting nesting, CilExceptionRegion region) =>
        nesting.Position(region.HandlerOffset + region.HandlerLength) - nesting.Position(region.HandlerOffset);
}
