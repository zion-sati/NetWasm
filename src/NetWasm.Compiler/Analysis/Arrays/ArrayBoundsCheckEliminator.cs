using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Arrays;

internal sealed class ArrayBoundsCheckEliminator(
    IControlFlowGraphBuilder graphs,
    ITypedStackValidator stacks,
    IArrayBoundsCheckEliminationPlanner planner,
    bool enabled) : IArrayBoundsCheckEliminator
{
    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
    private readonly ITypedStackValidator _stacks = stacks ?? throw new ArgumentNullException(nameof(stacks));
    private readonly IArrayBoundsCheckEliminationPlanner _planner = planner ?? throw new ArgumentNullException(nameof(planner));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var clean = body.ArrayBoundsProof is null ? body : body with { ArrayBoundsProof = null };
        if (!enabled) return clean;
        var proven = _planner.Plan(_stacks.Validate(_graphs.Build(clean)));
        // A proven index check's branch goes one way, so its operands are
        // dropped and the throw it skips is unreachable; the element accesses'
        // proof then describes the folded body.
        if (Checks(clean, proven).Any())
        {
            clean = Fold(clean, proven);
            proven = _planner.Plan(_stacks.Validate(_graphs.Build(clean)));
        }
        var accesses = proven.Except(Checks(clean, proven));
        return accesses.IsEmpty ? clean : clean with
        {
            ArrayBoundsProof = new(clean.Instructions, clean.Locals, clean.LocalSignatureTypes, accesses),
        };
    }

    // The proven offsets that are index checks rather than element accesses.
    private static IEnumerable<int> Checks(CilMethodBody body, ImmutableHashSet<int> proven) =>
        body.Instructions.Where(instruction => proven.Contains(instruction.Offset) && IsCheck(instruction))
            .Select(instruction => instruction.Offset);

    private static bool IsCheck(CilInstruction instruction) => instruction.Operation is
        CilOperation.BranchIfLessThanUnsigned or CilOperation.BranchIfLessThanSigned or
        CilOperation.BranchIfGreaterThanOrEqualUnsigned or CilOperation.BranchIfGreaterThanOrEqualSigned;

    // index < length always branches; index >= length never does. The loads
    // of index and length go with the branch where they are its own operands;
    // otherwise the branch pops them. The throw a taken branch skips, reached
    // from nowhere else, goes too, so the loop body stays one block and the
    // unroller sees it.
    private static CilMethodBody Fold(CilMethodBody body, ImmutableHashSet<int> proven)
    {
        var instructions = body.Instructions;
        var entered = Entered(body);
        var indices = Enumerable.Range(0, instructions.Length).ToDictionary(index => instructions[index].Offset);
        var replacements = instructions.Select(instruction => new List<CilInstruction> { instruction }).ToArray();
        for (var position = 0; position < instructions.Length; position++)
        {
            var branch = instructions[position];
            if (!proven.Contains(branch.Offset) || !IsCheck(branch)) continue;
            var taken = branch.Operation is CilOperation.BranchIfLessThanUnsigned or CilOperation.BranchIfLessThanSigned;
            List<CilInstruction> folded = taken ? [branch with { Operation = CilOperation.Branch }] : [];
            if (Operands(instructions, position, entered) is { } loads)
            {
                foreach (var load in loads) replacements[load] = [Nop(instructions[load])];
            }
            else
            {
                folded.InsertRange(0, [Pop(branch), Pop(branch)]);
            }
            var target = taken ? indices[((CilOperand.BranchTarget)branch.Operand).Offset] : position + 1;
            if (taken && target > position &&
                instructions[(position + 1)..target].All(skipped => !entered.ContainsKey(skipped.Offset)) &&
                entered[instructions[target].Offset] == 1)
            {
                for (var skipped = position + 1; skipped < target; skipped++) replacements[skipped] = [Nop(instructions[skipped])];
                folded.RemoveAt(folded.Count - 1);
            }
            replacements[position] = folded.Count == 0 ? [Nop(branch)] : folded;
        }
        return RenumberedInstructions.Splice(body, replacements);
    }

    // The positions of the instructions that load the check's index, plus a
    // constant, and its length, where nothing else runs or arrives between them.
    // The check's two operands precede it, so the walk ends at them, after at
    // most four loads.
    private static int[]? Operands(ImmutableArray<CilInstruction> instructions, int branch, Dictionary<int, int> entered)
    {
        var loads = new List<int>();
        for (var position = branch - 1; ; position--)
        {
            if (entered.ContainsKey(instructions[position + 1].Offset)) return null;
            if (instructions[position].Operation == CilOperation.Nop) continue;
            loads.Add(position);
            var shape = loads.Select(load => instructions[load].Operation).ToArray();
            if (shape is [CilOperation.LoadLocal, CilOperation.LoadLocal] or
                [CilOperation.LoadLocal, CilOperation.Add, CilOperation.LoadInt32, CilOperation.LoadLocal])
            {
                return [.. loads];
            }
            if (shape is not ([CilOperation.LoadLocal] or [CilOperation.LoadLocal, CilOperation.Add] or
                [CilOperation.LoadLocal, CilOperation.Add, CilOperation.LoadInt32])) return null;
        }
    }

    // How many branches and protected region bounds enter at each offset.
    private static Dictionary<int, int> Entered(CilMethodBody body) => body.Instructions
        .Select(static instruction => instruction.Operand)
        .OfType<CilOperand.BranchTarget>()
        .Select(static target => target.Offset)
        .Concat(body.ExceptionRegions.SelectMany(region => region.FilterOffset is { } filter
            ? new[] { region.TryOffset, region.TryOffset + region.TryLength, region.HandlerOffset, region.HandlerOffset + region.HandlerLength, filter }
            : [region.TryOffset, region.TryOffset + region.TryLength, region.HandlerOffset, region.HandlerOffset + region.HandlerLength]))
        .CountBy(offset => offset)
        .ToDictionary();

    private static CilInstruction Pop(CilInstruction instruction) =>
        instruction with { Operation = CilOperation.Pop, Operand = new CilOperand.None() };

    private static CilInstruction Nop(CilInstruction instruction) =>
        instruction with { Operation = CilOperation.Nop, Operand = new CilOperand.None() };
}
