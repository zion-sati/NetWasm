using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using static NetWasm.Compiler.Analysis.Loops.Elementwise.ElementwiseCil;

namespace NetWasm.Compiler.Analysis.Loops.Elementwise;

// What a node reads, folds and needs, and how two nodes combine.
internal static class ElementwiseNodes
{
    internal const int MaximumOffset = 1 << 16;

    internal static bool References(ElementwiseNode node) => node switch
    {
        Reference => true,
        Unary unary => References(unary.Operand),
        Binary binary => References(binary.Left) || References(binary.Right),
        Shift shift => References(shift.Value),
        _ => false,
    };

    internal static IEnumerable<Accumulator> Accumulators(ElementwiseNode node) => node switch
    {
        Accumulator accumulator => [accumulator],
        Unary unary => Accumulators(unary.Operand),
        Binary binary => Accumulators(binary.Left).Concat(Accumulators(binary.Right)),
        Shift shift => Accumulators(shift.Value),
        _ => [],
    };

    internal static ElementwiseNode? Combine(CilOperation operation, ElementwiseNode left, ElementwiseNode right)
    {
        if (left is Linear l && right is Linear r)
        {
            long scale, offset;
            switch (operation)
            {
                case CilOperation.Add:
                    (scale, offset) = ((long)l.Scale + r.Scale, (long)l.Offset + r.Offset);
                    break;
                case CilOperation.Subtract:
                    (scale, offset) = ((long)l.Scale - r.Scale, (long)l.Offset - r.Offset);
                    break;
                case CilOperation.Multiply when l.Scale == 0 || r.Scale == 0:
                    var (factor, term) = l.Scale == 0 ? (l.Offset, r) : (r.Offset, l);
                    (scale, offset) = ((long)term.Scale * factor, (long)term.Offset * factor);
                    break;
                case CilOperation.BitwiseAnd or CilOperation.BitwiseOr or CilOperation.BitwiseXor when l.Scale == 0 && r.Scale == 0:
                    return new Linear(0, operation switch
                    {
                        CilOperation.BitwiseAnd => l.Offset & r.Offset,
                        CilOperation.BitwiseOr => l.Offset | r.Offset,
                        _ => l.Offset ^ r.Offset,
                    });
                default:
                    return operation == CilOperation.Divide ? null : new Binary(operation, left, right);
            }
            return Math.Abs(scale) < MaximumOffset && Math.Abs(offset) < MaximumOffset
                ? new Linear((int)scale, (int)offset)
                : new Binary(operation, left, right);
        }
        // An integer division can throw, so it stays in the loop's iterations.
        if (AsInvariant(left) is { } invariantLeft && AsInvariant(right) is { } invariantRight &&
            (operation != CilOperation.Divide || invariantLeft.Kind is CliValueKind.F4 or CliValueKind.F8))
        {
            return Hoisted(invariantLeft, [.. invariantRight.Code, Copy(new CilInstruction(0, 1, operation, new CilOperand.None()))],
                invariantRight.Kind);
        }
        return new Binary(operation, left, right);
    }

    internal static Invariant Hoisted(Invariant value, params CilInstruction[] code) =>
        new([.. value.Code, .. code], value.Kind);

    internal static Invariant Hoisted(Invariant value, ImmutableArray<CilInstruction> code, CliValueKind other) =>
        new([.. value.Code, .. code], value.Kind == CliValueKind.Unknown ? other : value.Kind);

    internal static Invariant? AsInvariant(ElementwiseNode node) => node switch
    {
        Invariant invariant => invariant,
        Linear { Scale: 0, Offset: var value } => new([Instruction(CilOperation.LoadInt32, new CilOperand.ConstantI4(value))], CliValueKind.I4),
        _ => null,
    };

    // The pairs of arrays the vector loop needs to be different objects, or null
    // when the same array is written at one offset and accessed at another.
    internal static List<(CilInstruction, CilInstruction)>? Hazards(List<Store> stores)
    {
        var accesses = stores.Select(static store => (store.Array, store.Offset))
            .Concat(stores.SelectMany(static store => Elements(store.Value)).Select(static element => (element.Array, element.Offset)))
            .Distinct()
            .ToArray();
        var pairs = new List<(CilInstruction, CilInstruction)>();
        foreach (var store in stores)
        {
            foreach (var (array, offset) in accesses)
            {
                if (offset == store.Offset) continue;
                if (SameArray(array, store.Array)) return null;
                if (!pairs.Any(pair => SameArray(pair.Item1, store.Array) && SameArray(pair.Item2, array) ||
                        SameArray(pair.Item2, store.Array) && SameArray(pair.Item1, array)))
                {
                    pairs.Add((store.Array, array));
                }
            }
        }
        return pairs;
    }

    internal static IEnumerable<CilInstruction> SpanReferences(ElementwiseLoop loop) =>
        loop.Folds.SelectMany(static fold => Elements(fold.Value)).Concat(loop.Copies.Values)
            .Where(static element => element.Span is not null)
            .Select(static element => element.Array);

    internal static int Depth(List<Store> stores, List<Fold> folds) =>
        stores.Select(static store => Depth(store.Value))
            .Concat(folds.Select(static fold => Depth(fold.Value) + 1))
            .Max() + 8;

    internal static IEnumerable<Element> Elements(ElementwiseNode node) => node switch
    {
        Element element => [element],
        Unary unary => Elements(unary.Operand),
        Binary binary => Elements(binary.Left).Concat(Elements(binary.Right)),
        Shift shift => Elements(shift.Value),
        _ => [],
    };

    internal static int Depth(ElementwiseNode node) => node switch
    {
        Unary unary => Depth(unary.Operand),
        Binary binary => Math.Max(Depth(binary.Left), Depth(binary.Right) + 1),
        Shift shift => Depth(shift.Value) + 1,
        _ => 4,
    };

    // The stack kind of a value. An argument or local of unknown kind takes its
    // operation's other operand's; IL's binary operations take operands of one
    // kind, and Supported checks every operand against the loop's.
    internal static CliValueKind Kind(ElementwiseNode node) => node switch
    {
        Linear => CliValueKind.I4,
        Invariant invariant => invariant.Kind,
        Element element => Lane(element.Type),
        Unary unary => Kind(unary.Operand),
        Binary binary => Kind(binary.Left) is var left and not CliValueKind.Unknown ? left : Kind(binary.Right),
        // The remaining node shifts a value.
        _ => Kind(((Shift)node).Value),
    };

    // Whether every operation has an exact lane-wise vector form for the kind.
    internal static bool Supported(ElementwiseNode node, CliValueKind kind) => node switch
    {
        Linear => kind == CliValueKind.I4,
        Invariant invariant => invariant.Kind is var value && (value == kind || value == CliValueKind.Unknown),
        Element element => Lane(element.Type) == kind,
        Unary unary => (kind is CliValueKind.I4 or CliValueKind.I8 || unary.Operation == CilOperation.Negate) &&
            Supported(unary.Operand, kind),
        Binary binary => (kind is CliValueKind.I4 or CliValueKind.I8
                ? binary.Operation != CilOperation.Divide
                : binary.Operation is CilOperation.Add or CilOperation.Subtract or CilOperation.Multiply or CilOperation.Divide) &&
            Supported(binary.Left, kind) && Supported(binary.Right, kind),
        // The remaining node shifts a value.
        _ => kind is CliValueKind.I4 or CliValueKind.I8 && Supported(((Shift)node).Value, kind),
    };
}
