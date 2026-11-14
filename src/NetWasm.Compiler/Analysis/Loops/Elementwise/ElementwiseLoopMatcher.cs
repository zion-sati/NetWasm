using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using static NetWasm.Compiler.Analysis.Loops.Elementwise.ElementwiseCil;
using static NetWasm.Compiler.Analysis.Loops.Elementwise.ElementwiseNodes;

namespace NetWasm.Compiler.Analysis.Loops.Elementwise;

internal sealed class ElementwiseLoopMatcher(
    IElementwiseBodyParser parser,
    IElementwiseLaneSelector lanes) : IElementwiseLoopMatcher
{
    private const int MaximumBoundLength = 8;

    private readonly IElementwiseBodyParser _parser = parser ?? throw new ArgumentNullException(nameof(parser));
    private readonly IElementwiseLaneSelector _lanes = lanes ?? throw new ArgumentNullException(nameof(lanes));

    public ElementwiseLoop? Match(ImmutableArray<CilInstruction> instructions, int jump, int condition, ImmutableArray<CliValueKind> locals)
    {
        if (instructions[condition] is not { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var counter } })
        {
            return null;
        }
        var branch = -1;
        for (var index = condition + 1; index < instructions.Length && index <= condition + MaximumBoundLength + 1; index++)
        {
            if (instructions[index].Operation is CilOperation.BranchIfLessThanSigned or CilOperation.BranchIfLessThanOrEqualSigned)
            {
                branch = index;
                break;
            }
        }
        if (branch < 0 || instructions[branch].Operand is not CilOperand.BranchTarget { Offset: var start } ||
            start != instructions[jump + 1].Offset)
        {
            return null;
        }
        var increment = condition - 4;
        if (instructions[increment] is not { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var read } } ||
            read != counter ||
            instructions[increment + 1] is not { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 { Value: 1 } } ||
            instructions[increment + 2].Operation != CilOperation.Add ||
            instructions[increment + 3] is not { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var written } } ||
            written != counter)
        {
            return null;
        }

        var body = instructions.AsSpan(jump + 1, increment - jump - 1).ToArray();
        var writtenLocals = body
            .Select(static instruction => instruction is { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var local } }
                ? local
                : -1)
            .Where(static local => local >= 0)
            .Append(counter)
            .ToHashSet();
        if (body.Any(static instruction => instruction.Operation is CilOperation.StoreArgument or
                CilOperation.LoadLocalAddress or CilOperation.LoadArgumentAddress))
        {
            return null;
        }
        var bound = instructions.AsSpan(condition + 1, branch - condition - 1).ToArray();
        // A loop that only folds elements as they are is the reduction rewriter's,
        // which folds narrower elements and minimums and maximums too.
        if (End(bound, instructions[branch].Operation, writtenLocals) is not { } end ||
            _parser.Parse(body, counter, writtenLocals) is not ElementwiseBody(var stores, var folds, var aliases, var copies,
                var checks) parsed ||
            stores.Count + folds.Count == 0 ||
            stores.Count == 0 && folds.All(static fold => fold.Value is Element))
        {
            return null;
        }
        return _lanes.Select(parsed, locals) is { } kind
            ? new ElementwiseLoop(counter, end, stores, folds, aliases, copies, kind, checks)
            : null;
    }

    // The instructions that push end: the bound of `i < bound`, or C + 1 for
    // `i <= C` with a constant C below int.MaxValue.
    private static CilInstruction[]? End(CilInstruction[] bound, CilOperation branch, HashSet<int> written)
    {
        if (branch == CilOperation.BranchIfLessThanOrEqualSigned)
        {
            return bound is [{ Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 { Value: < int.MaxValue and var last } }]
                ? [Instruction(CilOperation.LoadInt32, new CilOperand.ConstantI4(last + 1))]
                : null;
        }
        if (bound.Length == 0) return null;
        var depth = 0;
        foreach (var instruction in bound)
        {
            switch (instruction.Operation)
            {
                case CilOperation.Nop:
                    break;
                case CilOperation.LoadInt32 or CilOperation.LoadArgument:
                    depth++;
                    break;
                case CilOperation.LoadLocal when instruction.Operand is CilOperand.Index { Value: var local }:
                    if (written.Contains(local)) return null;
                    depth++;
                    break;
                case CilOperation.Add or CilOperation.Subtract:
                    if (--depth < 1) return null;
                    break;
                case CilOperation.LoadArrayLength or CilOperation.ConvertInt32:
                    if (depth < 1) return null;
                    break;
                default:
                    return null;
            }
        }
        return depth == 1 ? [.. bound.Select(Copy)] : null;
    }
}
