// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Analysis.Loops;
using NetWasm.Compiler.Analysis.NullChecks;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Arrays;

internal interface IArrayLoopVersioner
{
    // The body with each planned loop versioned, and the offsets of the checks its fast copies prove.
    (CilMethodBody Body, ImmutableHashSet<int> Proven) Version(CilMethodBody body);
}

// Versions a loop whose span checks hold whenever start >= 0 and start + bound <= length: its entries go to that
// test, which runs a copy of the loop whose checks then always pass, or else the loop as written, so an index out of
// range still throws at the iteration that reaches it, after the iterations before it. Each start, bound and length
// widens to 64 bits, so the test cannot overflow; the bound widens unsigned, as an unsigned guard reads it, and a
// negative bound under a signed guard runs no iteration either way. The copy is appended after the body, as the
// unroller appends its fast loop, and only loops within the size budget are copied. The copy counts with an index of
// its own, which starts where the loop's does, so each loop has one initialization and one increment of its index
// and stays a counted loop for the passes after this one. Where the plan names runs of elements its reads lie in, the
// test also fails when one may hold address 0, and the copy marks those reads NonZeroAddress, so their accesses check
// nothing; that takes the methods that mark them.
internal sealed class ArrayLoopVersioner(
    IControlFlowGraphBuilder graphs,
    ITypedStackValidator stacks,
    IArrayLoopVersionPlanner planner,
    int loopInstructionBudget,
    IVectorLoopMethodResolver? nonZeroAddresses = null) : IArrayLoopVersioner
{
    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
    private readonly ITypedStackValidator _stacks = stacks ?? throw new ArgumentNullException(nameof(stacks));
    private readonly IArrayLoopVersionPlanner _planner = planner ?? throw new ArgumentNullException(nameof(planner));

    // A copy at most this large: a hot inner loop's body, as the unroller's expanded budget bounds its own copies.
    internal const int LoopInstructionBudget = 256;

    public (CilMethodBody Body, ImmutableHashSet<int> Proven) Version(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentOutOfRangeException.ThrowIfNegative(loopInstructionBudget);
        var controlFlow = _stacks.Validate(_graphs.Build(body));
        var graph = controlFlow.Graph;
        var versions = _planner.Plan(controlFlow);
        if (versions.IsEmpty) return (body, []);

        var code = body.Instructions.ToBuilder();
        var locals = body.Locals.ToBuilder();
        var signatures = body.LocalSignatureTypes.ToBuilder();
        var redirects = new Dictionary<int, int>();
        var proven = ImmutableHashSet.CreateBuilder<int>();
        var versioned = new HashSet<int>();
        var next = body.Instructions[^1].NextOffset;
        // The source offset the test's instructions report, the guard's.
        var source = 0;
        // Inner loops first: an enclosing loop's copy would hold an inner loop as written, and an inner loop is the
        // hotter of the two.
        foreach (var version in versions.OrderBy(version => version.Loop.LoopBlocks.Count))
        {
            var loop = version.Loop;
            var blocks = loop.LoopBlocks.Select(graph.GetBlock).OrderBy(block => block.StartOffset).ToArray();
            // An enclosing or enclosed loop already copied would need its own copy of this one.
            if (blocks.Any(block => versioned.Contains(block.Index)) ||
                blocks.Sum(block => block.Instructions.Length) > loopInstructionBudget)
                continue;
            versioned.UnionWith(loop.LoopBlocks);
            var guard = graph.GetBlock(loop.GuardBlock).StartOffset;
            source = graph.GetBlock(loop.GuardBlock).Instructions[0].SourceOffset;
            foreach (var entry in version.Entries) redirects.Add(entry, next);
            // Each failing condition pushes 1, and their or takes the loop as written in one branch: several branches to
            // one target per loop send the structurer's join search through every candidate. Every condition is
            // evaluated; a bound read from a null array throws as the loop's own condition would, before anything else.
            var failures = 0;
            void Failed()
            {
                if (failures++ != 0) Append(CilOperation.BitwiseOr, new CilOperand.None());
            }
            foreach (var (start, length) in version.Ranges)
            {
                // start < 0 runs the loop as written.
                Append(CilOperation.LoadLocal, new CilOperand.Index(start));
                Append(CilOperation.LoadInt32, new CilOperand.ConstantI4(0));
                Append(CilOperation.CompareLessThanSigned, new CilOperand.None());
                Failed();
                // (long)start + (ulong)bound > length runs it as written too.
                Append(CilOperation.LoadLocal, new CilOperand.Index(start));
                Append(CilOperation.ConvertInt64, new CilOperand.None());
                AppendBound(loop);
                Append(CilOperation.Add, new CilOperand.None());
                Append(CilOperation.LoadLocal, new CilOperand.Index(length));
                Append(CilOperation.ConvertInt64, new CilOperand.None());
                Append(CilOperation.CompareGreaterThanSigned, new CilOperand.None());
                Failed();
            }
            foreach (var range in version.OffsetRanges)
            {
                // A range with no start reads no index below initial + Low, which its plan knows is not negative.
                var started = range.Start != ArrayLoopOffsetRange.NoStart;
                if (started)
                {
                    // The smallest index read, (long)start + initial + Low ± term, below zero runs the loop as written.
                    Append(CilOperation.LoadLocal, new CilOperand.Index(range.Start));
                    Append(CilOperation.ConvertInt64, new CilOperand.None());
                    Append(CilOperation.LoadInt64, new CilOperand.ConstantI8((long)loop.Initial + range.Low));
                    Append(CilOperation.Add, new CilOperand.None());
                    AppendTerm(range);
                    Append(CilOperation.LoadInt64, new CilOperand.ConstantI8(0));
                    Append(CilOperation.CompareLessThanSigned, new CilOperand.None());
                    Failed();
                    // One past the largest, (long)start + (ulong)bound + High - lookahead ± term, past the length does
                    // too.
                    Append(CilOperation.LoadLocal, new CilOperand.Index(range.Start));
                    Append(CilOperation.ConvertInt64, new CilOperand.None());
                    AppendBound(loop);
                    Append(CilOperation.Add, new CilOperand.None());
                }
                else
                {
                    // One past the largest, (ulong)bound + High - lookahead, past the length runs the loop as written.
                    AppendBound(loop);
                }
                Append(CilOperation.LoadInt64, new CilOperand.ConstantI8((long)range.High - loop.Lookahead));
                Append(CilOperation.Add, new CilOperand.None());
                AppendTerm(range);
                Append(CilOperation.LoadLocal, new CilOperand.Index(range.Length));
                Append(CilOperation.ConvertInt64, new CilOperand.None());
                Append(CilOperation.CompareGreaterThanSigned, new CilOperand.None());
                Failed();
            }
            // Each run whose reads the copy marks: from the reference, length elements, none at address 0.
            var marked = nonZeroAddresses is null ? ImmutableDictionary<int, CliTypeIdentity>.Empty : version.NonZeroReads.Reads;
            if (!marked.IsEmpty)
            {
                foreach (var run in version.NonZeroReads.Runs)
                {
                    NonZeroAddressRange.PushMayHoldZero(
                        (operation, operand) => Append(operation, operand ?? new CilOperand.None()),
                        () => Append(CilOperation.LoadLocal, new CilOperand.Index(run.Reference)),
                        () => Append(CilOperation.LoadLocal, new CilOperand.Index(run.Length)),
                        run.Element);
                    Failed();
                }
            }
            if (failures != 0) Append(CilOperation.BranchIfTrue, new CilOperand.BranchTarget(guard));
            var index = locals.Count;
            locals.Add(CliValueKind.I4);
            signatures.Add(CliTypeIdentity.FromStackKind(CliValueKind.I4));
            Append(CilOperation.LoadInt32, new CilOperand.ConstantI4(loop.Initial));
            Append(CilOperation.StoreLocal, new CilOperand.Index(index));
            // The copy: each block at a new offset, branches within the loop to their copies, and a block that falls
            // through followed by a branch to where it fell.
            var copied = new Dictionary<int, int>();
            var position = next + 1;
            foreach (var block in blocks)
            {
                copied.Add(block.StartOffset, position);
                position += block.Instructions.Length + (FallsThrough(block.Instructions[^1]) ? 1 : 0) +
                    block.Instructions.Count(instruction => marked.ContainsKey(instruction.Offset));
            }
            Append(CilOperation.Branch, new CilOperand.BranchTarget(copied[guard]));
            foreach (var block in blocks)
            {
                foreach (var instruction in block.Instructions)
                {
                    if (version.Checks.Contains(instruction.Offset)) proven.Add(next);
                    code.Add(instruction with
                    {
                        Offset = next,
                        NextOffset = checked(next + 1),
                        Operand = instruction is
                        {
                            Operation: CilOperation.LoadLocal or CilOperation.StoreLocal,
                            Operand: CilOperand.Index { Value: var local },
                        } && local == loop.IndexLocal
                            ? new CilOperand.Index(index)
                            : Remap(instruction.Operand, copied),
                        OriginalOffset = instruction.SourceOffset,
                    });
                    next++;
                    if (marked.TryGetValue(instruction.Offset, out var element))
                    {
                        Append(CilOperation.Call, new CilOperand.MethodInstance(
                            nonZeroAddresses!.Resolve(VectorLoopMethod.NonZeroAddress, element)));
                    }
                }
                var last = block.Instructions[^1];
                if (FallsThrough(last))
                {
                    Append(CilOperation.Branch, new CilOperand.BranchTarget(
                        copied.TryGetValue(last.NextOffset, out var fallthrough) ? fallthrough : last.NextOffset));
                }
            }
        }
        if (proven.Count == 0) return (body, []);
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            if (redirects.TryGetValue(code[index].Offset, out var target))
                code[index] = code[index] with { Operand = new CilOperand.BranchTarget(target) };
        }
        var result = body with
        {
            Instructions = code.ToImmutable(),
            MaxStack = Math.Max(4, body.MaxStack),
            Locals = locals.ToImmutable(),
            LocalSignatureTypes = signatures.ToImmutable(),
            ArrayBoundsProof = null,
        };
        _stacks.Validate(_graphs.Build(result));
        return (result, proven.ToImmutable());

        void Append(CilOperation operation, CilOperand operand)
        {
            code.Add(new(next, checked(next + 1), operation, operand) { OriginalOffset = source });
            next++;
        }

        // The loop's bound, widened unsigned as an unsigned guard reads it.
        void AppendBound(ArrayLoopInduction loop)
        {
            if (loop.LengthLocal >= 0)
            {
                Append(CilOperation.LoadLocal, new CilOperand.Index(loop.LengthLocal));
            }
            else
            {
                Append(CilOperation.LoadLocal, new CilOperand.Index(loop.ArrayLocal));
                Append(CilOperation.LoadArrayLength, new CilOperand.None());
                Append(CilOperation.ConvertInt32, new CilOperand.None());
            }
            Append(CilOperation.ConvertInt64Unsigned, new CilOperand.None());
        }

        void AppendTerm(ArrayLoopOffsetRange range)
        {
            if (range.Term is not { } term) return;
            Append(CilOperation.LoadLocal, new CilOperand.Index(term));
            Append(CilOperation.ConvertInt64, new CilOperand.None());
            Append(range.TermNegated ? CilOperation.Subtract : CilOperation.Add, new CilOperand.None());
        }
    }

    // A loop's block ends in a branch back or falls through: one that returns or throws never reaches the loop's back
    // edge, so it is no block of the loop, and a loop with a protected region is never planned.
    private static bool FallsThrough(CilInstruction instruction) => instruction.Operation != CilOperation.Branch;

    // A branch within the loop goes to its copy; decoding has already turned each switch into branches.
    private static CilOperand Remap(CilOperand operand, Dictionary<int, int> copied) =>
        operand is CilOperand.BranchTarget target && copied.TryGetValue(target.Offset, out var moved)
            ? new CilOperand.BranchTarget(moved)
            : operand;
}
