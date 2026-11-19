// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// If-conversion: a conditional branch whose arms only compute a number from locals, arguments and constants, either
// leaving it on the stack (c ? x : y), storing it to one local or argument (if (c) v = x; else v = y; and if (c) v = x;) or
// returning it (if (c) return x; return y;), becomes a select. The engine then computes both arms and keeps one, instead of predicting a branch that data such
// as image pixels makes unpredictable, as LLVM's SimplifyCFG does for C. An arm may not read memory, call, divide or
// convert a floating-point value to an integer: none of those may run when their branch was not taken. A select
// joins two numbers of one kind, so references and managed pointers keep their branches.
//
// When the condition's own operands are computed the same way just before the branch, they move after the arms and
// the whole conditional becomes one expression, which an enclosing conditional can select in turn; otherwise the
// condition waits in a new int32 local while the arms run.
internal sealed class ConditionalSelectRewriter(IControlFlowGraphBuilder graphs, ITypedStackValidator stacks) :
    IMethodRewriteRule
{
    // Instructions an arm, or a condition's operands, may compute a value with: about what a mispredicted branch costs.
    internal const int ArmInstructionLimit = 6;
    // A converted branch can make the branch around it convertible; nested conditionals go a few levels deep.
    private const int RoundLimit = 4;

    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
    private readonly ITypedStackValidator _stacks = stacks ?? throw new ArgumentNullException(nameof(stacks));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        // The proofs are keyed by instruction offset, which a conversion renumbers.
        if (body.ArrayBoundsProof is not null || body.IntegerCheckProof is not null)
        {
            return body;
        }
        for (var round = 0; round < RoundLimit && BranchShapes.CountForwardConditionalBranches(body) > 0; round++)
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
        var claimed = -1;
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            if (Match(body, indices, entries, entryStacks, index, claimed + 1) is { } span)
            {
                spans.Add(span);
                claimed = span.Last;
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
        int branchIndex,
        int earliestStart)
    {
        var instructions = body.Instructions;
        var branch = instructions[branchIndex];
        if (!BranchShapes.IsForwardConditionalBranch(branch) ||
            !indices.TryGetValue(((CilOperand.BranchTarget)branch.Operand).Offset, out var target) ||
            !entryStacks.TryGetValue(branch.Offset, out var entryStack) ||
            BranchShapes.Condition(branch.Operation, entryStack) is not { } condition ||
            BranchShapes.Entered(instructions, entries, branchIndex + 1, target))
        {
            return null;
        }

        Arm? taken;
        Arm? fallthrough;
        int last;
        var returns = false;
        if (instructions[target - 1].Operation == CilOperation.Return)
        {
            // Two returns: each arm computes the value it returns, the taken one up to the next return.
            var returnIndex = target;
            while (returnIndex < instructions.Length && returnIndex - target <= ArmInstructionLimit &&
                   instructions[returnIndex].Operation != CilOperation.Return)
            {
                returnIndex++;
            }
            if (returnIndex >= instructions.Length || instructions[returnIndex].Operation != CilOperation.Return ||
                entries.GetValueOrDefault(instructions[target].Offset) != 1 ||
                BranchShapes.Entered(instructions, entries, target + 1, returnIndex + 1))
            {
                return null;
            }
            fallthrough = ReadArm(body, entryStacks, branchIndex + 1, target - 1);
            taken = ReadArm(body, entryStacks, target, returnIndex);
            last = returnIndex;
            returns = true;
            if (fallthrough is not { Store: null } || taken is not { Store: null } || fallthrough.Kind != taken.Kind)
            {
                return null;
            }
        }
        else if (instructions[target - 1] is { Operation: CilOperation.Branch, Operand: CilOperand.BranchTarget join } &&
            indices.TryGetValue(join.Offset, out var joinIndex) && joinIndex > target)
        {
            // A diamond: the fallthrough arm, a branch to the join, then the taken arm, which only the branch enters.
            // Other paths may reach the join, as returns sharing an epilogue do: the select falls into it.
            if (entries.GetValueOrDefault(instructions[target].Offset) != 1 ||
                BranchShapes.Entered(instructions, entries, target + 1, joinIndex))
            {
                return null;
            }
            fallthrough = ReadArm(body, entryStacks, branchIndex + 1, target - 1);
            taken = ReadArm(body, entryStacks, target, joinIndex);
            last = joinIndex - 1;
            // Arms that meet at one stack slot or one local have that slot's or local's kind.
            if (fallthrough is null || taken is null || fallthrough.Store != taken.Store)
            {
                return null;
            }
        }
        else
        {
            // A triangle: the fallthrough arm stores to a local or argument the taken path leaves as it is; the branch's
            // target is the join, which other paths may reach.
            fallthrough = ReadArm(body, entryStacks, branchIndex + 1, target);
            if (fallthrough is not { Store: { } slot })
            {
                return null;
            }
            taken = new([BranchShapes.Synthetic(branch, slot.Load, new CilOperand.Index(slot.Index))], fallthrough.Kind, slot);
            last = target - 1;
        }

        if (taken.Store is { } carried && (ReadsSlot(taken, carried) || ReadsSlot(fallthrough, carried)) &&
            CarriedByLoop(body, branchIndex, carried))
        {
            return null;
        }

        var whenTrue = condition.TakenWhenTrue ? taken : fallthrough;
        var whenFalse = condition.TakenWhenTrue ? fallthrough : taken;
        var replacement = ImmutableArray.CreateBuilder<CilInstruction>();
        void Add(CilOperation operation, CilOperand operand) =>
            replacement.Add(BranchShapes.Synthetic(branch, operation, operand));
        void AddCondition()
        {
            foreach (var (operation, operand) in condition.Instructions)
            {
                Add(operation, operand);
            }
        }
        int first;
        var usesConditionLocal = false;
        if (OperandStart(body, entries, entryStacks, branchIndex, condition.Operands, earliestStart) is { } operands)
        {
            first = operands;
            replacement.AddRange(whenTrue.Value);
            replacement.AddRange(whenFalse.Value);
            replacement.AddRange(instructions[operands..branchIndex]);
            AddCondition();
        }
        else
        {
            var conditionLocal = new CilOperand.Index(body.Locals.Length);
            first = branchIndex;
            usesConditionLocal = true;
            AddCondition();
            Add(CilOperation.StoreLocal, conditionLocal);
            replacement.AddRange(whenTrue.Value);
            replacement.AddRange(whenFalse.Value);
            Add(CilOperation.LoadLocal, conditionLocal);
        }
        Add(CilOperation.Select, new CilOperand.None());
        if (taken.Store is { } stored)
        {
            Add(stored.Operation, new CilOperand.Index(stored.Index));
        }
        if (returns)
        {
            Add(CilOperation.Return, new CilOperand.None());
        }
        return new(first, last, replacement.ToImmutable(), usesConditionLocal);
    }

    // Where the instructions that compute the branch's operands start, when those only compute numbers and nothing
    // enters them: they can then run after the arms.
    private static int? OperandStart(
        CilMethodBody body,
        Dictionary<int, int> entries,
        ImmutableDictionary<int, ImmutableArray<CliValueKind>> entryStacks,
        int branchIndex,
        int operands,
        int earliestStart)
    {
        for (var start = branchIndex - 1; start >= Math.Max(earliestStart, branchIndex - ArmInstructionLimit); start--)
        {
            if (BranchShapes.Entered(body.Instructions, entries, start + 1, branchIndex + 1))
            {
                return null;
            }
            if (BranchShapes.IsPureValue(body, entryStacks, start, branchIndex, operands))
            {
                return start;
            }
        }
        return null;
    }

    // Whether an arm reads the slot it stores to, as a conditional update (if (c) sum += x;) and the arm that leaves the
    // slot as it is both do: the select then joins the slot's old value with its new one.
    private static bool ReadsSlot(Arm arm, Slot slot) =>
        arm.Value.Any(instruction => instruction.Operation == slot.Load &&
            ((CilOperand.Index)instruction.Operand).Value == slot.Index);

    // Whether a loop around the branch carries the slot's value from one iteration into the next: the loop reads the
    // slot before it first writes it, or reaches the branch without touching it, as an accumulator (sum += x) is; a
    // slot the loop writes first, as t = values[i] does, holds that iteration's own value. A select that joins a
    // carried slot's old value puts the condition on the loop's dependency chain, each iteration waiting for the
    // comparison before the next update, where a branch the engine predicts lets the update run ahead: an accumulation
    // whose element test is nearly always true ran 1.25 times as long as a select. LLVM's SelectOptimize turns such a
    // select back into a branch for the same reason.
    private static bool CarriedByLoop(CilMethodBody body, int branchIndex, Slot slot)
    {
        var branch = body.Instructions[branchIndex].Offset;
        foreach (var loop in LoopSpans.Of(body))
        {
            if (loop.Start > branch || branch > loop.End)
            {
                continue;
            }
            var first = body.Instructions.FirstOrDefault(instruction =>
                loop.Start <= instruction.Offset && instruction.Offset < branch &&
                (instruction.Operation == slot.Load || instruction.Operation == slot.Operation) &&
                ((CilOperand.Index)instruction.Operand).Value == slot.Index);
            if (first is null || first.Operation == slot.Load)
            {
                return true;
            }
        }
        return false;
    }

    // An arm: the instructions that compute its value, the value's kind, and the local or argument it stores to, if any.
    private sealed record Arm(ImmutableArray<CilInstruction> Value, CliValueKind Kind, Slot? Store);

    // A local, or an argument, which a method may store to as it does to a local.
    private sealed record Slot(CilOperation Operation, int Index)
    {
        public CilOperation Load => Operation == CilOperation.StoreArgument ? CilOperation.LoadArgument : CilOperation.LoadLocal;
    }

    // [start, end) computes one number from locals, arguments and constants, and leaves it or stores it to a local or
    // argument.
    private static Arm? ReadArm(
        CilMethodBody body,
        ImmutableDictionary<int, ImmutableArray<CliValueKind>> entryStacks,
        int start,
        int end)
    {
        if (end <= start)
        {
            return null;
        }
        var instructions = body.Instructions;
        var store = instructions[end - 1] is
        {
            Operation: CilOperation.StoreLocal or CilOperation.StoreArgument,
            Operand: CilOperand.Index index,
        }
            ? new Slot(instructions[end - 1].Operation, index.Value)
            : null;
        var valueEnd = store is null ? end : end - 1;
        if (valueEnd - start is < 1 or > ArmInstructionLimit ||
            BranchShapes.PureValue(body, entryStacks, start, valueEnd, 1) is not { } kind)
        {
            return null;
        }
        // The value stored is the kind PureValue found, as the validated stack has it; the slot may be wider.
        if (store is { } stored && SlotKind(body, stored) != kind)
        {
            return null;
        }
        return new([.. instructions[start..valueEnd]], kind, store);
    }

    // The kind of the slot a stloc or starg names, which validated IL gives the method.
    private static CliValueKind? SlotKind(CilMethodBody body, Slot slot) =>
        slot.Operation == CilOperation.StoreArgument ? BranchShapes.ArgumentKind(body, slot.Index) : body.Locals[slot.Index];
}
