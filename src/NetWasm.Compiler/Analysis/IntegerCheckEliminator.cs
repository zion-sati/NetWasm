using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface IIntegerCheckEliminator
{
    CilMethodBody Rewrite(CilMethodBody body);
}

// Integer instructions check their operands before they compute: a division
// or remainder for a zero divisor, and a signed one for MinValue divided by
// -1; checked arithmetic for a result that does not fit; a checked conversion
// for a value its target cannot hold. Where the ranges the operands can hold
// rule a check out, the body's integer check proof says so and the check is
// not emitted: a divisor that a loop starts at 3 and steps by 2 while it is at
// most value / divisor is never zero, a dividend that the method has returned
// for when below 2 is never MinValue, and a checked sum of two bytes always
// fits. The proof describes the final instructions, so this runs after every
// pass that rewrites them.
internal sealed class IntegerCheckEliminator(
    IControlFlowGraphBuilder graphs,
    ITypedStackValidator stacks,
    IIntegerRangeAnalysis ranges,
    bool enabled) : IIntegerCheckEliminator
{
    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
    private readonly ITypedStackValidator _stacks = stacks ?? throw new ArgumentNullException(nameof(stacks));
    private readonly IIntegerRangeAnalysis _ranges = ranges ?? throw new ArgumentNullException(nameof(ranges));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var clean = body.IntegerCheckProof is null ? body : body with { IntegerCheckProof = null };
        var checks = clean.Instructions.Where(static instruction => instruction.Operation is
            CilOperation.Divide or CilOperation.DivideUnsigned or CilOperation.Remainder or CilOperation.RemainderUnsigned or
            CilOperation.AddChecked or CilOperation.SubtractChecked or CilOperation.MultiplyChecked or
            CilOperation.AddCheckedUnsigned or CilOperation.SubtractCheckedUnsigned or CilOperation.MultiplyCheckedUnsigned ||
            instruction.Operand is CilOperand.NumericConversion { Checked: true, Native: false }).ToArray();
        if (!enabled || checks.Length == 0) return clean;
        var controlFlow = _stacks.Validate(_graphs.Build(clean));
        var operands = _ranges.StacksAt(controlFlow, checks.Select(static check => check.Offset).ToHashSet());
        var nonZero = ImmutableHashSet.CreateBuilder<int>();
        var nonOverflowing = ImmutableHashSet.CreateBuilder<int>();
        foreach (var check in checks)
        {
            if (!operands.TryGetValue(check.Offset, out var stack) || stack[^1] is not { } top) continue;
            var kinds = controlFlow.InstructionEntryStacks[check.Offset];
            if (check.Operand is CilOperand.NumericConversion conversion)
            {
                // An unsigned source is its signed range while that is not negative.
                if ((!conversion.SourceUnsigned || top.Low >= 0) && top.Within(IntegerRangeAnalysis.Target(conversion)))
                {
                    nonOverflowing.Add(check.Offset);
                }
                continue;
            }
            // Operands of two sizes, as a native int with an int, are not followed.
            if (kinds[^1] != kinds[^2] || stack[^2] is not { } left) continue;
            var full = IntegerRange.Of(kinds[^1])!.Value;
            if (IntegerRangeAnalysis.Exact(check.Operation, left, top) is { } exact)
            {
                var unsigned = check.Operation is CilOperation.AddCheckedUnsigned or CilOperation.SubtractCheckedUnsigned or
                    CilOperation.MultiplyCheckedUnsigned;
                var fits = unsigned
                    ? left.Low >= 0 && top.Low >= 0 && exact.Within(new(0, (full.High << 1) + 1))
                    : exact.Within(full);
                if (fits) nonOverflowing.Add(check.Offset);
                continue;
            }
            if (!top.Contains(0)) nonZero.Add(check.Offset);
            // Only a signed division of the most negative value by -1 overflows.
            if (check.Operation is CilOperation.Divide or CilOperation.Remainder && (!top.Contains(-1) || left.Low > full.Low))
            {
                nonOverflowing.Add(check.Offset);
            }
        }
        return nonZero.Count == 0 && nonOverflowing.Count == 0 ? clean : clean with
        {
            IntegerCheckProof = new(clean.Instructions, nonZero.ToImmutable(), nonOverflowing.ToImmutable()),
        };
    }
}
