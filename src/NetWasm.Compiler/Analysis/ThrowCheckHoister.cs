// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// An arm that stores one value and then checks it, throwing when the check fails, keeps its branch: the check cannot
// run speculatively inside the arm. Upstream's Math.Abs is written this way (if (value < 0) { value = -value; if
// (value < 0) throw; }), and on data whose sign varies, such as an image codec's residuals, that branch mispredicts
// where the JIT and LLVM emit a conditional move. The check only runs when the arm does, so it moves after the arm
// and tests both: t = c; if (!t) v = x; if (!(t || p)) throw, with c the outer branch's condition and p the check's,
// each true when its branch skips. When the check repeats the outer branch on the same operands, as Abs's does, the
// path that skips the arm changes none of them and would pass the check too, so the outer branch skips to the check
// instead and needs no t. ConditionalSelectRewriter, which runs next, turns the arm into a select, and the branch left
// is taken only on the way to the throw. The arm computes its value from locals, arguments and constants, as an
// if-converted arm does, and so do the check's operands, since they now run whichever way c went.
internal sealed class ThrowCheckHoister(IControlFlowGraphBuilder graphs, ITypedStackValidator stacks) : IMethodRewriteRule
{
    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
    private readonly ITypedStackValidator _stacks = stacks ?? throw new ArgumentNullException(nameof(stacks));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        // The proofs are keyed by instruction offset, which a rewrite renumbers.
        if (body.ArrayBoundsProof is not null || body.IntegerCheckProof is not null ||
            BranchShapes.CountForwardConditionalBranches(body) < 2)
        {
            return body;
        }
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
        int branchIndex)
    {
        var instructions = body.Instructions;
        var branch = instructions[branchIndex];
        // The outer branch skips the arm to the join; nothing but its operands is on the stack.
        if (!BranchShapes.IsForwardConditionalBranch(branch) ||
            !indices.TryGetValue(((CilOperand.BranchTarget)branch.Operand).Offset, out var join) ||
            Skip(entryStacks, branch) is not { } outer ||
            BranchShapes.Entered(instructions, entries, branchIndex + 1, join))
        {
            return null;
        }
        // The arm stores one value, as an if-converted arm does.
        var store = branchIndex + 1;
        while (store < join && store - branchIndex <= ConditionalSelectRewriter.ArmInstructionLimit + 1 &&
               instructions[store].Operation is not (CilOperation.StoreLocal or CilOperation.StoreArgument))
        {
            store++;
        }
        if (store >= join || instructions[store].Operation is not (CilOperation.StoreLocal or CilOperation.StoreArgument) ||
            store - branchIndex - 1 > ConditionalSelectRewriter.ArmInstructionLimit ||
            BranchShapes.PureValue(body, entryStacks, branchIndex + 1, store, 1) is not { } kind ||
            Kind(body, instructions[store]) != kind)
        {
            return null;
        }
        // Then the check, which skips to the same join, on operands that only compute numbers.
        var check = store + 1;
        while (check < join && check - store <= ConditionalSelectRewriter.ArmInstructionLimit + 1 &&
               !BranchShapes.IsForwardConditionalBranch(instructions[check]))
        {
            check++;
        }
        if (check >= join || !BranchShapes.IsForwardConditionalBranch(instructions[check]) ||
            ((CilOperand.BranchTarget)instructions[check].Operand).Offset != instructions[join].Offset ||
            Skip(entryStacks, instructions[check]) is not { } inner ||
            !BranchShapes.IsPureValue(body, entryStacks, store + 1, check, inner.Operands))
        {
            return null;
        }
        // Then the failure, which runs straight into a throw.
        if (instructions[join - 1].Operation != CilOperation.Throw)
        {
            return null;
        }
        // A branch or a leave names its target; a return names none.
        for (var index = check + 1; index < join - 1; index++)
        {
            if (instructions[index].Operation == CilOperation.Return || instructions[index].Operand is CilOperand.BranchTarget)
            {
                return null;
            }
        }

        if (RepeatsOuterBranch(body, entries, branchIndex, store + 1, check))
        {
            return new(branchIndex, branchIndex,
                [branch with { Operand = new CilOperand.BranchTarget(instructions[store + 1].Offset) }]);
        }

        var conditionLocal = new CilOperand.Index(body.Locals.Length);
        var replacement = ImmutableArray.CreateBuilder<CilInstruction>();
        void Add(CilOperation operation, CilOperand operand) =>
            replacement.Add(BranchShapes.Synthetic(branch, operation, operand));
        void AddSkip(BranchShapes.BranchCondition condition)
        {
            foreach (var (operation, operand) in condition.Instructions)
            {
                Add(operation, operand);
            }
            if (!condition.TakenWhenTrue)
            {
                Add(CilOperation.LoadInt32, new CilOperand.ConstantI4(0));
                Add(CilOperation.CompareEqual, new CilOperand.None());
            }
        }
        // t = c; if (!t) v = x;
        AddSkip(outer);
        Add(CilOperation.StoreLocal, conditionLocal);
        Add(CilOperation.LoadLocal, conditionLocal);
        Add(CilOperation.BranchIfTrue, new CilOperand.BranchTarget(instructions[store + 1].Offset));
        replacement.AddRange(instructions[(branchIndex + 1)..(store + 1)]);
        // if (!(p || t)) throw;
        replacement.AddRange(instructions[(store + 1)..check]);
        AddSkip(inner);
        Add(CilOperation.LoadLocal, conditionLocal);
        Add(CilOperation.BitwiseOr, new CilOperand.None());
        Add(CilOperation.BranchIfTrue, new CilOperand.BranchTarget(instructions[join].Offset));
        replacement.AddRange(instructions[(check + 1)..join]);
        return new(branchIndex, join - 1, replacement.ToImmutable(), UsesConditionLocal: true);
    }

    // Whether [checkStart, check] is the outer branch and the instructions that compute its operands, which nothing
    // enters, so the path that skips the arm reaches the check with the values the outer branch compared.
    private static bool RepeatsOuterBranch(
        CilMethodBody body,
        Dictionary<int, int> entries,
        int branchIndex,
        int checkStart,
        int check)
    {
        var instructions = body.Instructions;
        var operands = check - checkStart;
        var outerStart = branchIndex - operands;
        if (outerStart < 0 || instructions[check].Operation != instructions[branchIndex].Operation ||
            BranchShapes.Entered(instructions, entries, outerStart + 1, branchIndex + 1))
        {
            return false;
        }
        for (var index = 0; index < operands; index++)
        {
            var (outer, inner) = (instructions[outerStart + index], instructions[checkStart + index]);
            if (outer.Operation != inner.Operation || !Equals(outer.Operand, inner.Operand))
            {
                return false;
            }
        }
        return true;
    }

    // The condition under which a branch skips, when only its operands are on the stack.
    private static BranchShapes.BranchCondition? Skip(
        ImmutableDictionary<int, ImmutableArray<CliValueKind>> entryStacks,
        CilInstruction branch) =>
        entryStacks.TryGetValue(branch.Offset, out var entry) &&
        BranchShapes.Condition(branch.Operation, entry) is { } condition && entry.Length == condition.Operands
            ? condition
            : null;

    // The kind of the slot a stloc or starg names, which validated IL gives the method.
    private static CliValueKind? Kind(CilMethodBody body, CilInstruction store)
    {
        var index = ((CilOperand.Index)store.Operand).Value;
        return store.Operation == CilOperation.StoreArgument ? BranchShapes.ArgumentKind(body, index) : body.Locals[index];
    }
}
