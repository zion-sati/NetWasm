// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// Roslyn compiles `a && b` and `a || b` in a condition as two branches. When the second condition only computes
// numbers from locals, arguments and constants, both conditions are computed and combined into one branch, as
// LLVM's SimplifyCFG folds a branch into its common destination, so the arms the branch chooses between can become a
// select: `if (x <= y && x <= z)` branches once on (x > y) | (x > z).
//   && : b1 -> T, then b2 -> T              becomes  t1 | t2, branch to T.
//   || : b1 -> F (just past b2), b2 -> T    becomes  !t1 & t2, branch to T.
// The second condition's instructions run whether or not the first branch was taken, which only pure ones may.
internal sealed class ConditionChainRewriter(IControlFlowGraphBuilder graphs, ITypedStackValidator stacks) :
    IMethodRewriteRule
{
    // Instructions the second condition may compute its operands with.
    internal const int ConditionInstructionLimit = 6;
    // Each merge removes a branch; a chain of n conditions merges in n - 1 rounds.
    private const int RoundLimit = 4;

    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
    private readonly ITypedStackValidator _stacks = stacks ?? throw new ArgumentNullException(nameof(stacks));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (body.ArrayBoundsProof is not null || body.IntegerCheckProof is not null)
        {
            return body;
        }
        for (var round = 0; round < RoundLimit && BranchShapes.CountForwardConditionalBranches(body) >= 2; round++)
        {
            var rewritten = RewriteOnce(body);
            if (ReferenceEquals(rewritten, body))
            {
                break;
            }
            body = rewritten;
        }
        return body;
    }

    private CilMethodBody RewriteOnce(CilMethodBody body)
    {
        var entryStacks = _stacks.Validate(_graphs.Build(body)).InstructionEntryStacks;
        var indices = BranchShapes.Indices(body);
        var entries = BranchShapes.EntryCounts(body);
        var spans = new List<BranchShapes.Span>();
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            if (Match(body, indices, entries, entryStacks, index) is { } span)
            {
                spans.Add(span);
                index = span.Last;
            }
        }
        return spans.Count == 0 ? body : BranchShapes.Splice(body, spans);
    }

    private static BranchShapes.Span? Match(
        CilMethodBody body,
        Dictionary<int, int> indices,
        Dictionary<int, int> entries,
        ImmutableDictionary<int, ImmutableArray<CliValueKind>> entryStacks,
        int firstIndex)
    {
        var instructions = body.Instructions;
        var first = instructions[firstIndex];
        if (!BranchShapes.IsForwardConditionalBranch(first) ||
            !entryStacks.TryGetValue(first.Offset, out var firstEntry) ||
            BranchShapes.Condition(first.Operation, firstEntry) is not { } firstCondition)
        {
            return null;
        }
        // The second branch: the first conditional branch after the first, past only pure instructions.
        var secondIndex = firstIndex + 1;
        while (secondIndex < instructions.Length && secondIndex - firstIndex - 1 <= ConditionInstructionLimit &&
               !BranchShapes.IsForwardConditionalBranch(instructions[secondIndex]))
        {
            secondIndex++;
        }
        if (secondIndex >= instructions.Length || secondIndex - firstIndex - 1 > ConditionInstructionLimit ||
            BranchShapes.Entered(instructions, entries, firstIndex + 1, secondIndex + 1) ||
            !entryStacks.TryGetValue(instructions[secondIndex].Offset, out var secondEntry) ||
            BranchShapes.Condition(instructions[secondIndex].Operation, secondEntry) is not { } secondCondition ||
            !BranchShapes.IsPureValue(body, entryStacks, firstIndex + 1, secondIndex, secondCondition.Operands))
        {
            return null;
        }
        var second = instructions[secondIndex];
        var firstTarget = ((CilOperand.BranchTarget)first.Operand).Offset;
        var secondTarget = ((CilOperand.BranchTarget)second.Operand).Offset;
        bool firstNegated;
        if (firstTarget == secondTarget)
        {
            firstNegated = false;
        }
        else if (secondIndex + 1 < instructions.Length && firstTarget == instructions[secondIndex + 1].Offset &&
                 indices.ContainsKey(secondTarget))
        {
            firstNegated = true;
        }
        else
        {
            return null;
        }

        var replacement = ImmutableArray.CreateBuilder<CilInstruction>();
        void Add(CilOperation operation, CilOperand operand) =>
            replacement.Add(BranchShapes.Synthetic(first, operation, operand));
        // Leave 1 when the branch is taken (or, negated, when it is not) and 0 otherwise: an int32 brtrue tests may hold
        // any value, which a bitwise and would not combine as a truth value.
        void AddTaken(BranchShapes.BranchCondition condition, bool negate)
        {
            foreach (var (operation, operand) in condition.Instructions)
            {
                Add(operation, operand);
            }
            var wantTrue = condition.TakenWhenTrue != negate;
            if (condition.Instructions.IsEmpty || !wantTrue)
            {
                Add(CilOperation.LoadInt32, new CilOperand.ConstantI4(0));
                Add(wantTrue ? CilOperation.CompareGreaterThanUnsigned : CilOperation.CompareEqual, new CilOperand.None());
            }
        }
        AddTaken(firstCondition, firstNegated);
        replacement.AddRange(instructions[(firstIndex + 1)..secondIndex]);
        AddTaken(secondCondition, negate: false);
        Add(firstNegated ? CilOperation.BitwiseAnd : CilOperation.BitwiseOr, new CilOperand.None());
        Add(CilOperation.BranchIfTrue, new CilOperand.BranchTarget(secondTarget));
        return new(firstIndex, secondIndex, replacement.ToImmutable());
    }
}
