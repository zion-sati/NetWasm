using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface IRangeBranchRewriter
{
    CilMethodBody Rewrite(CilMethodBody body);
}

// A branch whose operands' ranges settle which way it goes pops them and
// jumps, or pops them and falls through, and the code it cut off goes, as a
// constant branch's does. Upstream guards its scalar fallbacks with
// `length < Vector128<T>.Count`, so inside one `while (length >= 8)` never
// runs: its body is dead code that would still be compiled, and the loop rules
// that come next would still rewrite it.
internal sealed class RangeBranchRewriter(
    IControlFlowGraphBuilder graphs,
    ITypedStackValidator stacks,
    IIntegerRangeAnalysis ranges,
    IMethodRewriteRule cutOff,
    bool enabled) : IRangeBranchRewriter
{
    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
    private readonly ITypedStackValidator _stacks = stacks ?? throw new ArgumentNullException(nameof(stacks));
    private readonly IIntegerRangeAnalysis _ranges = ranges ?? throw new ArgumentNullException(nameof(ranges));
    private readonly IMethodRewriteRule _cutOff = cutOff ?? throw new ArgumentNullException(nameof(cutOff));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var branches = body.Instructions
            .Where(static instruction =>
                instruction.Operation is >= CilOperation.BranchIfTrue and <= CilOperation.BranchIfLessThanOrEqualUnsigned)
            .Select(static instruction => instruction.Offset)
            .ToHashSet();
        if (!enabled || branches.Count == 0) return body;
        var operands = _ranges.StacksAt(_stacks.Validate(_graphs.Build(body)), branches);
        var replacements = new List<CilInstruction>[body.Instructions.Length];
        var changed = false;
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            var instruction = body.Instructions[index];
            replacements[index] = [instruction];
            if (!operands.TryGetValue(instruction.Offset, out var stack) ||
                IntegerRangeAnalysis.Taken(instruction.Operation, stack) is not { } taken)
            {
                continue;
            }
            var pops = instruction.Operation is CilOperation.BranchIfTrue or CilOperation.BranchIfFalse ? 1 : 2;
            replacements[index] = [.. Enumerable.Repeat(instruction with { Operation = CilOperation.Pop, Operand = new CilOperand.None() }, pops)];
            if (taken) replacements[index].Add(instruction with { Operation = CilOperation.Branch });
            changed = true;
        }
        return changed ? _cutOff.Rewrite(RenumberedInstructions.Splice(body, replacements)) : body;
    }
}
