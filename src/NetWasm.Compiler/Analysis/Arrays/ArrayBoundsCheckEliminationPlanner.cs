// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Arrays;

internal sealed class ArrayBoundsCheckEliminationPlanner(
    IArrayLoopInductionAnalyzer induction,
    IControlFlowDominanceAnalyzer dominance) : IArrayBoundsCheckEliminationPlanner
{
    private readonly IArrayLoopInductionAnalyzer _induction = induction ??
        throw new ArgumentNullException(nameof(induction));
    private readonly IControlFlowDominanceAnalyzer _dominance = dominance ??
        throw new ArgumentNullException(nameof(dominance));

    public ImmutableHashSet<int> Plan(ValidatedControlFlowGraph controlFlow)
    {
        ArgumentNullException.ThrowIfNull(controlFlow);
        var accesses = ImmutableHashSet.CreateBuilder<int>();
        PlanLastElements(controlFlow, accesses);
        var loops = _induction.Analyze(controlFlow);
        if (loops.IsEmpty) return accesses.ToImmutable();
        var graph = controlFlow.Graph;
        var dominators = _dominance.Analyze(graph, graph.Entry.Index, graph.ReachableBlocks);
        foreach (var loop in loops)
        {
            foreach (var blockIndex in loop.LoopBlocks)
            {
                if (blockIndex == loop.GuardBlock ||
                    !dominators[blockIndex].Contains(loop.BodyEntryBlock)) continue;
                var origins = new List<ValueOrigin>();
                var copies = new Dictionary<int, ValueOrigin>();
                foreach (var instruction in graph.GetBlock(blockIndex).Instructions)
                {
                    if (instruction.Offset == loop.IncrementOffset) break;
                    if (!controlFlow.InstructionEntryStacks.TryGetValue(instruction.Offset, out var stack))
                    {
                        origins.Clear();
                        copies.Clear();
                        continue;
                    }
                    if (origins.Count != stack.Length)
                    {
                        origins.Clear();
                        copies.Clear();
                        origins.AddRange(Enumerable.Repeat(ValueOrigin.Unknown, stack.Length));
                    }
                    var consumed = instruction.Operation switch
                    {
                        CilOperation.LoadArrayElement or CilOperation.LoadArrayElementReference or
                            CilOperation.LoadArrayElementAddress => 2,
                        CilOperation.StoreArrayElement or CilOperation.StoreArrayElementReference => 3,
                        _ => 0,
                    };
                    // `if ((uint)index >= (uint)length) throw`, a span indexer's
                    // check: index + k for k up to the lookahead is below the
                    // bound and not negative, so the branch always goes one way.
                    if (loop.LengthLocal >= 0 &&
                        instruction.Operation is CilOperation.BranchIfLessThanUnsigned or CilOperation.BranchIfLessThanSigned or
                            CilOperation.BranchIfGreaterThanOrEqualUnsigned or CilOperation.BranchIfGreaterThanOrEqualSigned &&
                        origins.Count >= 2 &&
                        origins[^2] is { Local: var compared, Offset: var added, Constant: false, Length: false, Base: null } &&
                        compared == loop.IndexLocal && added >= 0 && added <= loop.Lookahead &&
                        origins[^1] == ValueOrigin.FromLocal(loop.LengthLocal) &&
                        stack[^1] == CliValueKind.I4 && stack[^2] == CliValueKind.I4)
                        accesses.Add(instruction.Offset);
                    if (consumed != 0 && origins.Count >= consumed &&
                        origins[^consumed] == ValueOrigin.FromLocal(loop.ArrayLocal) &&
                        origins[^(consumed - 1)] is { Local: var index, Offset: var offset, Constant: false, Length: false, Base: null } &&
                        index == loop.IndexLocal && offset >= 0 && offset <= loop.Lookahead &&
                        stack[stack.Length - consumed + 1] == CliValueKind.I4)
                        accesses.Add(instruction.Offset);
                    ValueOrigins.Track(instruction, origins, copies);
                }
            }
        }
        return accesses.ToImmutable();
    }

    // `if (a.Length > 0) ... a[^1]`: the block entered only when the length is
    // nonzero proves a[a.Length - 1], and its guard's ldlen already checked a
    // for null. The proof lasts until a is stored.
    private static void PlanLastElements(ValidatedControlFlowGraph controlFlow, ImmutableHashSet<int>.Builder accesses)
    {
        var graph = controlFlow.Graph;
        var body = graph.MethodBody;
        foreach (var guard in graph.Blocks)
        {
            var code = guard.Instructions.Where(instruction => instruction.Operation != CilOperation.Nop).ToArray();
            if (code is not [.., { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index array },
                { Operation: CilOperation.LoadArrayLength },
                { Operation: CilOperation.BranchIfFalse or CilOperation.BranchIfTrue, Operand: CilOperand.BranchTarget target } branch] ||
                body.LocalSignatureTypes[array.Value].Shape != CliTypeShape.SzArray ||
                body.Instructions.Any(instruction => instruction is
                { Operation: CilOperation.LoadLocalAddress, Operand: CilOperand.Index exposed } && exposed.Value == array.Value))
            {
                continue;
            }
            var nonEmpty = graph.GetBlockAtOffset(branch.Operation == CilOperation.BranchIfTrue
                ? target.Offset
                : branch.NextOffset);
            if (graph.Predecessors[nonEmpty.Index] is not [var only] || only != guard.Index)
            {
                continue;
            }
            var origins = new List<ValueOrigin>();
            foreach (var instruction in nonEmpty.Instructions)
            {
                if (!controlFlow.InstructionEntryStacks.TryGetValue(instruction.Offset, out var stack) ||
                    instruction is { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index stored } && stored.Value == array.Value)
                {
                    break;
                }
                if (origins.Count != stack.Length)
                {
                    origins.Clear();
                    origins.AddRange(Enumerable.Repeat(ValueOrigin.Unknown, stack.Length));
                }
                if (instruction.Operation is CilOperation.LoadArrayElement or CilOperation.LoadArrayElementReference or
                        CilOperation.LoadArrayElementAddress && origins.Count >= 2 &&
                    origins[^2] == ValueOrigin.FromLocal(array.Value) &&
                    origins[^1] == new ValueOrigin(array.Value, -1, false, Length: true) &&
                    stack[^1] == CliValueKind.I4)
                {
                    accesses.Add(instruction.Offset);
                }
                ValueOrigins.Track(instruction, origins, []);
            }
        }
    }
}
