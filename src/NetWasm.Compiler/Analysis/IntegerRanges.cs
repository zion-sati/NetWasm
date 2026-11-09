using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// The values an integer can hold, low to high, with 128-bit bounds so that
// adding or multiplying 64-bit bounds is exact.
internal readonly record struct IntegerRange(Int128 Low, Int128 High)
{
    public static readonly IntegerRange Int32 = new(int.MinValue, int.MaxValue);
    public static readonly IntegerRange Int64 = new(long.MinValue, long.MaxValue);

    public static IntegerRange? Of(CliValueKind kind) => kind switch
    {
        CliValueKind.I4 => Int32,
        CliValueKind.I8 => Int64,
        _ => null,
    };

    public bool Contains(Int128 value) => Low <= value && value <= High;

    public bool Within(IntegerRange other) => other.Low <= Low && High <= other.High;

    public IntegerRange Union(IntegerRange other) => new(Int128.Min(Low, other.Low), Int128.Max(High, other.High));

    public IntegerRange? Intersect(IntegerRange other)
    {
        var (low, high) = (Int128.Max(Low, other.Low), Int128.Min(High, other.High));
        return low <= high ? new(low, high) : null;
    }
}

// The ranges of the 32- and 64-bit integers a method's locals, arguments and
// evaluation stack hold at its instructions, found by running the method over
// ranges: constants, loads and stores, arithmetic that cannot wrap, divisions,
// masks and shifts, conversions between integer sizes, and each conditional
// branch narrowing the slots it compares on the edge it takes. A slot is
// followed when it is an integer no wider than 64 bits whose address the
// method never takes and that no handler stores, since a finally a leave runs
// is not on the path to the leave's target. Anything else pushes an integer
// that may hold any value of its size. A loop head whose ranges keep growing
// widens them, first to the nearest bound another range at the head has, as a
// counter's to the quotient it is tested against, then to its size's limits,
// so the analysis ends, and a handler starts with every slot holding any value
// of its type.
internal interface IIntegerRangeAnalysis
{
    // The ranges of the stack values each of the given instructions starts with, top last.
    IReadOnlyDictionary<int, ImmutableArray<IntegerRange?>> StacksAt(ValidatedControlFlowGraph controlFlow, IReadOnlySet<int> offsets);
}

internal sealed class IntegerRangeAnalysis(IStackEffectResolver effects) : IIntegerRangeAnalysis
{
    // Joins into a block's entry before its growing bounds widen, and the
    // widenings to another range's bound before they widen to their limits.
    private const int JoinsBeforeWidening = 2;
    private const int ThresholdWidenings = 4;

    private readonly IStackEffectResolver _effects = effects ?? throw new ArgumentNullException(nameof(effects));

    // A stack value: its range when it is an integer, and the slot it was loaded from while the slot still holds it.
    private readonly record struct Value(IntegerRange? Range, int Slot);

    private sealed record State(IntegerRange?[] Slots, ImmutableList<Value> Stack);

    public IReadOnlyDictionary<int, ImmutableArray<IntegerRange?>> StacksAt(
        ValidatedControlFlowGraph controlFlow, IReadOnlySet<int> offsets)
    {
        ArgumentNullException.ThrowIfNull(controlFlow);
        ArgumentNullException.ThrowIfNull(offsets);
        var graph = controlFlow.Graph;
        var body = graph.MethodBody;
        var signature = body.MethodInstance?.Signature ?? body.Method.Signature;
        var arguments = (body.Method.IsStatic ? 0 : 1) + signature.ParameterSignatureTypes.Length;
        var types = (body.Method.IsStatic ? [] : new CliTypeIdentity?[] { null })
            .Concat(signature.ParameterSignatureTypes.Select(static type => (CliTypeIdentity?)type))
            .Concat(body.LocalSignatureTypes.Select(static type => (CliTypeIdentity?)type))
            .ToArray();
        var untracked = Untracked(body, arguments);
        var bounds = types.Select((type, slot) => untracked.Contains(slot) || type is null ? null : Bounds(type)).ToArray();
        var blocksByOffset = graph.Blocks.ToDictionary(static block => block.StartOffset, static block => block.Index);
        var handlers = body.ExceptionRegions
            .SelectMany(static region => region.FilterOffset is { } filter ? [region.HandlerOffset, filter] : new[] { region.HandlerOffset })
            .ToHashSet();

        var entries = new Dictionary<int, State>();
        var joins = new Dictionary<int, int>();
        var pending = new SortedSet<int>();
        var roots = graph.Blocks.Where(block => graph.ReachableBlocks.Contains(block.Index) &&
            (block.Index == graph.Entry.Index || handlers.Contains(block.StartOffset))).ToArray();
        var heads = LoopHeads(graph, roots.Select(static block => block.Index));
        foreach (var block in roots)
        {
            entries[block.Index] = new(
                [.. bounds],
                [.. controlFlow.EntryStacks[block.Index].Select(static kind => new Value(IntegerRange.Of(kind), -1))]);
            pending.Add(block.Index);
        }
        var stacks = new Dictionary<int, ImmutableArray<IntegerRange?>>();
        while (pending.Count > 0)
        {
            var index = pending.Min;
            pending.Remove(index);
            foreach (var (successor, state) in Run(graph.GetBlock(index), entries[index], controlFlow, bounds, arguments,
                         blocksByOffset, offsets, stacks))
            {
                if (!entries.TryGetValue(successor, out var previous))
                {
                    entries[successor] = state;
                    pending.Add(successor);
                    continue;
                }
                var count = joins.GetValueOrDefault(successor);
                var widening = !heads.Contains(successor) || count < JoinsBeforeWidening ? Widening.None
                    : count < JoinsBeforeWidening + ThresholdWidenings ? Widening.Thresholds
                    : Widening.Limits;
                var joined = Join(previous, state, bounds, controlFlow.EntryStacks[successor], widening);
                if (Same(previous, joined)) continue;
                joins[successor] = joins.GetValueOrDefault(successor) + 1;
                entries[successor] = joined;
                pending.Add(successor);
            }
        }
        return stacks;
    }

    // The blocks a depth-first walk from the roots reaches again while inside
    // them: every cycle passes through one, so widening there ends the analysis.
    private static HashSet<int> LoopHeads(ControlFlowGraph graph, IEnumerable<int> roots)
    {
        var heads = new HashSet<int>();
        var finished = new HashSet<int>();
        var open = new HashSet<int>();
        foreach (var root in roots)
        {
            if (finished.Contains(root)) continue;
            var walk = new Stack<(int Block, int Next)>([(root, 0)]);
            open.Add(root);
            while (walk.TryPop(out var frame))
            {
                var successors = graph.Successors[frame.Block];
                if (frame.Next == successors.Length)
                {
                    open.Remove(frame.Block);
                    finished.Add(frame.Block);
                    continue;
                }
                walk.Push(frame with { Next = frame.Next + 1 });
                var successor = successors[frame.Next];
                if (open.Contains(successor))
                {
                    heads.Add(successor);
                }
                else if (!finished.Contains(successor))
                {
                    open.Add(successor);
                    walk.Push((successor, 0));
                }
            }
        }
        return heads;
    }

    // Integers whose stores keep their value, and the small ones a store truncates to their range.
    private static IntegerRange? Bounds(CliTypeIdentity type) => type.CanonicalName switch
    {
        "primitive:i4" or "primitive:u4" => IntegerRange.Int32,
        "primitive:i8" or "primitive:u8" => IntegerRange.Int64,
        "primitive:i1" => new(sbyte.MinValue, sbyte.MaxValue),
        "primitive:u1" => new(byte.MinValue, byte.MaxValue),
        "primitive:i2" => new(short.MinValue, short.MaxValue),
        "primitive:u2" => new(ushort.MinValue, ushort.MaxValue),
        _ => null,
    };

    // Slots whose address the method takes, or that a handler or filter stores.
    private static HashSet<int> Untracked(CilMethodBody body, int arguments)
    {
        var untracked = new HashSet<int>();
        foreach (var instruction in body.Instructions)
        {
            var slot = instruction.Operation switch
            {
                CilOperation.LoadArgumentAddress => ((CilOperand.Index)instruction.Operand).Value,
                CilOperation.LoadLocalAddress => arguments + ((CilOperand.Index)instruction.Operand).Value,
                CilOperation.StoreArgument when InHandler(body, instruction.Offset) => ((CilOperand.Index)instruction.Operand).Value,
                CilOperation.StoreLocal when InHandler(body, instruction.Offset) => arguments + ((CilOperand.Index)instruction.Operand).Value,
                _ => -1,
            };
            if (slot >= 0) untracked.Add(slot);
        }
        return untracked;
    }

    private static bool InHandler(CilMethodBody body, int offset) => body.ExceptionRegions.Any(region =>
        offset >= region.HandlerOffset && offset < region.HandlerOffset + region.HandlerLength ||
        region.FilterOffset is { } filter && offset >= filter && offset < region.HandlerOffset);

    // The block's instructions from its entry state, recording the stacks asked for, and the states it leaves on
    // each edge out; an edge whose branch cannot go that way leaves none.
    private IEnumerable<(int Successor, State State)> Run(BasicBlock block, State entry, ValidatedControlFlowGraph controlFlow,
        IntegerRange?[] bounds, int arguments, Dictionary<int, int> blocksByOffset, IReadOnlySet<int> offsets,
        Dictionary<int, ImmutableArray<IntegerRange?>> stacks)
    {
        var slots = (IntegerRange?[])entry.Slots.Clone();
        var stack = entry.Stack.ToBuilder();
        foreach (var instruction in block.Instructions.Take(block.Instructions.Length - 1))
        {
            Record(instruction, stack, offsets, stacks);
            Step(instruction, slots, stack, controlFlow, bounds, arguments);
        }
        var terminator = block.Instructions[^1];
        Record(terminator, stack, offsets, stacks);
        switch (terminator.Operation)
        {
            case CilOperation.Return or CilOperation.Throw or CilOperation.Rethrow or CilOperation.EndFinally or CilOperation.EndFilter:
                yield break;
            case CilOperation.Branch:
                yield return (Target(terminator, blocksByOffset), new(slots, stack.ToImmutable()));
                yield break;
            case CilOperation.Leave:
                yield return (Target(terminator, blocksByOffset), new(slots, []));
                yield break;
        }
        if (Relation(terminator.Operation, true) is not { } taken)
        {
            Step(terminator, slots, stack, controlFlow, bounds, arguments);
            yield return (block.Index + 1, new(slots, stack.ToImmutable()));
            yield break;
        }
        var operands = terminator.Operation is CilOperation.BranchIfTrue or CilOperation.BranchIfFalse ? 1 : 2;
        var compared = stack.Skip(stack.Count - operands).ToArray();
        stack.RemoveRange(stack.Count - operands, operands);
        var rest = stack.ToImmutable();
        foreach (var (successor, relation) in new[]
                 {
                     (Target(terminator, blocksByOffset), taken),
                     (block.Index + 1, Relation(terminator.Operation, false)!.Value),
                 })
        {
            var narrowed = (IntegerRange?[])slots.Clone();
            if (Narrow(narrowed, compared, relation)) yield return (successor, new(narrowed, rest));
        }
    }

    private static int Target(CilInstruction instruction, Dictionary<int, int> blocksByOffset) =>
        blocksByOffset[((CilOperand.BranchTarget)instruction.Operand).Offset];

    private static void Record(CilInstruction instruction, ImmutableList<Value>.Builder stack, IReadOnlySet<int> offsets,
        Dictionary<int, ImmutableArray<IntegerRange?>> stacks)
    {
        if (!offsets.Contains(instruction.Offset)) return;
        var ranges = stack.Select(static value => value.Range).ToImmutableArray();
        // A block reached on several paths records the last; its entry state by then holds every path.
        stacks[instruction.Offset] = ranges;
    }

    // What one instruction that is not a branch does to the slots and the stack.
    private void Step(CilInstruction instruction, IntegerRange?[] slots, ImmutableList<Value>.Builder stack,
        ValidatedControlFlowGraph controlFlow, IntegerRange?[] bounds, int arguments)
    {
        var after = controlFlow.InstructionEntryStacks.GetValueOrDefault(instruction.NextOffset);
        var kind = after.IsDefaultOrEmpty ? CliValueKind.Unknown : after[^1];
        switch (instruction.Operation)
        {
            case CilOperation.Nop or CilOperation.Constrained or CilOperation.Readonly or CilOperation.Volatile or
                CilOperation.Unaligned:
                return;
            case CilOperation.LoadInt32:
                stack.Add(new(Single(((CilOperand.ConstantI4)instruction.Operand).Value), -1));
                return;
            case CilOperation.LoadInt64:
                stack.Add(new(Single(((CilOperand.ConstantI8)instruction.Operand).Value), -1));
                return;
            case CilOperation.LoadLocal or CilOperation.LoadArgument:
                var loaded = Slot(instruction, arguments);
                stack.Add(new(slots[loaded] ?? IntegerRange.Of(kind), loaded));
                return;
            case CilOperation.StoreLocal or CilOperation.StoreArgument:
                var stored = Slot(instruction, arguments);
                var value = Pop(stack);
                if (bounds[stored] is { } range)
                {
                    slots[stored] = value.Range is { } known && known.Within(range) ? known : range;
                }
                // The slot no longer holds what was loaded from it.
                for (var index = 0; index < stack.Count; index++)
                {
                    if (stack[index].Slot == stored) stack[index] = stack[index] with { Slot = -1 };
                }
                return;
            case CilOperation.Duplicate:
                stack.Add(stack[^1]);
                return;
        }
        if (Computed(instruction, stack, kind, controlFlow) is { } computed)
        {
            stack.Add(new(computed, -1));
            return;
        }
        // Anything else leaves values of their types, or, when its effect is unknown, the stack the next instruction
        // starts with.
        if (_effects.Resolve(instruction) is { } effect)
        {
            stack.RemoveRange(stack.Count - effect.Pops, effect.Pops);
            if (effect.Pushes > 0) stack.Add(new(IntegerRange.Of(kind), -1));
            return;
        }
        stack.Clear();
        if (!after.IsDefault) stack.AddRange(after.Select(static kind => new Value(IntegerRange.Of(kind), -1)));
    }

    private static IntegerRange Single(Int128 value) => new(value, value);

    private static int Slot(CilInstruction instruction, int arguments) =>
        ((CilOperand.Index)instruction.Operand).Value +
        (instruction.Operation is CilOperation.LoadLocal or CilOperation.StoreLocal ? arguments : 0);

    private static Value Pop(ImmutableList<Value>.Builder stack)
    {
        var value = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        return value;
    }

    // The range an arithmetic, bitwise, comparison or conversion instruction pushes, or null for one this does not
    // follow, which leaves its operands on the stack.
    private static IntegerRange? Computed(CilInstruction instruction, ImmutableList<Value>.Builder stack, CliValueKind kind,
        ValidatedControlFlowGraph controlFlow)
    {
        if (IntegerRange.Of(kind) is not { } full) return null;
        var operation = instruction.Operation;
        if (operation is CilOperation.ConvertInt32 or CilOperation.ConvertInt64 or CilOperation.ConvertNumeric)
        {
            var conversion = instruction.Operand as CilOperand.NumericConversion;
            if (IntegerRange.Of(controlFlow.InstructionEntryStacks[instruction.Offset][^1]) is null || conversion is { Native: true })
            {
                return null;
            }
            var value = Pop(stack).Range!.Value;
            if (conversion is { SourceUnsigned: true } && value.Low < 0) return full;
            var target = conversion is null ? full : Target(conversion);
            // A checked conversion throws for values outside its target, an unchecked one wraps them into it.
            var converted = value.Within(target) ? value
                : conversion is { Checked: true } ? value.Intersect(target) ?? target
                : target;
            return converted.Within(full) ? converted : full;
        }
        if (operation is CilOperation.CompareEqual or CilOperation.CompareGreaterThanSigned or
            CilOperation.CompareGreaterThanUnsigned or CilOperation.CompareLessThanSigned or CilOperation.CompareLessThanUnsigned)
        {
            stack.RemoveRange(stack.Count - 2, 2);
            return new(0, 1);
        }
        if (operation is not (CilOperation.Add or CilOperation.Subtract or CilOperation.Multiply or
            CilOperation.AddChecked or CilOperation.SubtractChecked or CilOperation.MultiplyChecked or
            CilOperation.AddCheckedUnsigned or CilOperation.SubtractCheckedUnsigned or CilOperation.MultiplyCheckedUnsigned or
            CilOperation.Divide or CilOperation.DivideUnsigned or CilOperation.Remainder or CilOperation.RemainderUnsigned or
            CilOperation.BitwiseAnd or CilOperation.ShiftRightSigned or CilOperation.ShiftRightUnsigned))
        {
            return null;
        }
        var right = Pop(stack).Range;
        var left = Pop(stack).Range;
        if (left is not { } a || right is not { } b) return full;
        var unsigned = operation is CilOperation.DivideUnsigned or CilOperation.RemainderUnsigned or
            CilOperation.AddCheckedUnsigned or CilOperation.SubtractCheckedUnsigned or CilOperation.MultiplyCheckedUnsigned;
        if (unsigned && (a.Low < 0 || b.Low < 0)) return full;
        var bits = kind == CliValueKind.I4 ? 32 : 64;
        var result = operation switch
        {
            CilOperation.Divide or CilOperation.DivideUnsigned => Quotient(a, b),
            CilOperation.Remainder or CilOperation.RemainderUnsigned => Remainder(a, b),
            CilOperation.BitwiseAnd => a.Low >= 0 && b.Low >= 0 ? new(0, Int128.Min(a.High, b.High))
                : a.Low >= 0 ? new(0, a.High)
                : b.Low >= 0 ? new(0, b.High)
                : null,
            CilOperation.ShiftRightSigned => b.Low == b.High && b.Low >= 0 && b.Low < bits
                ? new(a.Low >> (int)b.Low, a.High >> (int)b.Low)
                : null,
            CilOperation.ShiftRightUnsigned => b.Low == b.High && b.Low >= 1 && b.Low < bits
                ? a.Low >= 0 ? new(a.Low >> (int)b.Low, a.High >> (int)b.Low) : new(0, (Int128.One << bits) - 1 >> (int)b.Low)
                : null,
            _ => Exact(operation, a, b),
        };
        if (result is not { } exact) return full;
        // Checked arithmetic throws for results outside its range, signed or
        // unsigned; unchecked arithmetic that may wrap can leave any value.
        if (operation is CilOperation.AddChecked or CilOperation.SubtractChecked or CilOperation.MultiplyChecked or
            CilOperation.AddCheckedUnsigned or CilOperation.SubtractCheckedUnsigned or CilOperation.MultiplyCheckedUnsigned)
        {
            exact = exact.Intersect(unsigned ? new(0, (Int128.One << bits) - 1) : full) ?? full;
        }
        return exact.Within(full) ? exact : full;
    }

    // The exact range of a sum, difference or product, before it wraps or throws.
    public static IntegerRange? Exact(CilOperation operation, IntegerRange a, IntegerRange b) => operation switch
    {
        CilOperation.Add or CilOperation.AddChecked or CilOperation.AddCheckedUnsigned => new(a.Low + b.Low, a.High + b.High),
        CilOperation.Subtract or CilOperation.SubtractChecked or CilOperation.SubtractCheckedUnsigned =>
            new(a.Low - b.High, a.High - b.Low),
        CilOperation.Multiply or CilOperation.MultiplyChecked or CilOperation.MultiplyCheckedUnsigned =>
            Corners(a, b, static (x, y) => x * y),
        _ => null,
    };

    // The values an integer conversion's target holds.
    public static IntegerRange Target(CilOperand.NumericConversion conversion) => conversion.DestinationUnsigned
        ? new(0, (Int128.One << conversion.BitWidth) - 1)
        : new(-(Int128.One << (conversion.BitWidth - 1)), (Int128.One << (conversion.BitWidth - 1)) - 1);

    private static IntegerRange Corners(IntegerRange a, IntegerRange b, Func<Int128, Int128, Int128> operation)
    {
        Int128[] values = [operation(a.Low, b.Low), operation(a.Low, b.High), operation(a.High, b.Low), operation(a.High, b.High)];
        return new(values.Min(), values.Max());
    }

    // A quotient truncates toward zero, so over each sign of divisor it is
    // monotonic in both operands and its extremes are at the corners. A divisor
    // of only zero always throws, and what it pushes is never read.
    private static IntegerRange? Quotient(IntegerRange a, IntegerRange b) => Divisors(b)
        .Select(divisors => Corners(a, divisors, static (x, y) => x / y))
        .Aggregate((IntegerRange?)null, static (union, range) => union?.Union(range) ?? range);

    // A remainder is smaller than its divisor and has its dividend's sign.
    private static IntegerRange? Remainder(IntegerRange a, IntegerRange b)
    {
        if (!Divisors(b).Any()) return null;
        var magnitude = Int128.Max(Int128.Abs(b.Low), Int128.Abs(b.High)) - 1;
        return new(Int128.Max(Int128.Min(a.Low, 0), -magnitude), Int128.Min(Int128.Max(a.High, 0), magnitude));
    }

    // The divisor's negative and positive parts, without zero.
    private static IEnumerable<IntegerRange> Divisors(IntegerRange divisor)
    {
        if (divisor.Low <= -1) yield return new(divisor.Low, Int128.Min(divisor.High, -1));
        if (divisor.High >= 1) yield return new(Int128.Max(divisor.Low, 1), divisor.High);
    }

    // Whether a branch that starts with these stack ranges, top last, always goes to its target (true) or never
    // does (false), as its edges narrow them; null when it can go either way or does not branch on a comparison.
    public static bool? Taken(CilOperation operation, ImmutableArray<IntegerRange?> stack)
    {
        if (Relation(operation, true) is not { } taken) return null;
        var operands = operation is CilOperation.BranchIfTrue or CilOperation.BranchIfFalse ? 1 : 2;
        var compared = stack.Skip(stack.Length - operands).Select(static range => new Value(range, -1)).ToArray();
        var target = Narrow([], compared, taken);
        return target == Narrow([], compared, Relation(operation, false)!.Value) ? null : target;
    }

    private enum Comparison { Equal, NotEqual, Less, LessOrEqual, Greater, GreaterOrEqual, Unsigned }

    // What a branch's operands satisfy on its taken or fall-through edge, or null for an instruction that does not
    // branch on a comparison. An unsigned comparison of values that may be negative narrows nothing.
    private static Comparison? Relation(CilOperation operation, bool taken) => operation switch
    {
        CilOperation.BranchIfTrue => taken ? Comparison.NotEqual : Comparison.Equal,
        CilOperation.BranchIfFalse => taken ? Comparison.Equal : Comparison.NotEqual,
        CilOperation.BranchIfEqual => taken ? Comparison.Equal : Comparison.NotEqual,
        CilOperation.BranchIfNotEqual => taken ? Comparison.NotEqual : Comparison.Equal,
        CilOperation.BranchIfLessThanSigned => taken ? Comparison.Less : Comparison.GreaterOrEqual,
        CilOperation.BranchIfLessThanOrEqualSigned => taken ? Comparison.LessOrEqual : Comparison.Greater,
        CilOperation.BranchIfGreaterThanSigned => taken ? Comparison.Greater : Comparison.LessOrEqual,
        CilOperation.BranchIfGreaterThanOrEqualSigned => taken ? Comparison.GreaterOrEqual : Comparison.Less,
        CilOperation.BranchIfLessThanUnsigned or CilOperation.BranchIfLessThanOrEqualUnsigned or
            CilOperation.BranchIfGreaterThanUnsigned or CilOperation.BranchIfGreaterThanOrEqualUnsigned => Comparison.Unsigned,
        _ => null,
    };

    // Narrows the compared slots to what the edge's comparison allows; false when no values allow it.
    private static bool Narrow(IntegerRange?[] slots, Value[] compared, Comparison relation)
    {
        if (relation == Comparison.Unsigned || compared.Any(static value => value.Range is null)) return true;
        var a = compared[0].Range!.Value;
        // A branch on one value compares it with zero.
        var b = compared.Length == 2 ? compared[1].Range!.Value : Single(0);
        IntegerRange? left = a, right = b;
        switch (relation)
        {
            case Comparison.Equal:
                left = right = a.Intersect(b);
                break;
            case Comparison.NotEqual:
                left = b.Low == b.High ? Without(a, b.Low) : a;
                right = a.Low == a.High ? Without(b, a.Low) : b;
                break;
            case Comparison.Less:
                (left, right) = (Below(a, b.High - 1), Above(b, a.Low + 1));
                break;
            case Comparison.LessOrEqual:
                (left, right) = (Below(a, b.High), Above(b, a.Low));
                break;
            case Comparison.Greater:
                (left, right) = (Above(a, b.Low + 1), Below(b, a.High - 1));
                break;
            case Comparison.GreaterOrEqual:
                (left, right) = (Above(a, b.Low), Below(b, a.High));
                break;
        }
        if (left is not { } narrowedLeft || right is not { } narrowedRight) return false;
        return Assign(slots, compared[0].Slot, narrowedLeft) &&
            (compared.Length == 1 || Assign(slots, compared[1].Slot, narrowedRight));
    }

    private static IntegerRange? Below(IntegerRange range, Int128 high) => range.Intersect(new(range.Low, high));

    private static IntegerRange? Above(IntegerRange range, Int128 low) => range.Intersect(new(low, range.High));

    // The range without one value, which only an end can lose.
    private static IntegerRange? Without(IntegerRange range, Int128 value) =>
        range.Low == value ? Above(range, value + 1) : range.High == value ? Below(range, value - 1) : range;

    private static bool Assign(IntegerRange?[] slots, int slot, IntegerRange range)
    {
        if (slot < 0 || slots[slot] is not { } current) return true;
        slots[slot] = current.Intersect(range);
        return slots[slot] is not null;
    }

    private enum Widening { None, Thresholds, Limits }

    private static State Join(State previous, State next, IntegerRange?[] bounds, ImmutableArray<CliValueKind> kinds,
        Widening widening)
    {
        // The bounds the slots' ranges have on either side, which a growing bound may stop at.
        Int128[] thresholds = widening == Widening.Thresholds
            ? [.. previous.Slots.Concat(next.Slots).OfType<IntegerRange>().SelectMany(static range => new[] { range.Low, range.High })]
            : [];
        var slots = previous.Slots.Select((range, slot) => Merge(range, next.Slots[slot], bounds[slot], widening, thresholds))
            .ToArray();
        var stack = previous.Stack.Select((value, index) => new Value(
                Merge(value.Range, next.Stack[index].Range, IntegerRange.Of(kinds[index]), widening, thresholds),
                value.Slot == next.Stack[index].Slot ? value.Slot : -1))
            .ToImmutableList();
        return new(slots, stack);
    }

    // The union of two ranges; widening sends each bound that grew to the
    // nearest threshold beyond it, or to its type's limit. A value has a range
    // exactly when it is an integer, and paths that meet agree on their types.
    private static IntegerRange? Merge(IntegerRange? previous, IntegerRange? next, IntegerRange? limits, Widening widening,
        Int128[] thresholds)
    {
        if (previous is not { } old) return null;
        var union = old.Union(next!.Value);
        if (widening == Widening.None || limits is not { } limit) return union;
        var low = union.Low >= old.Low ? union.Low
            : thresholds.Where(threshold => threshold < union.Low && threshold >= limit.Low).DefaultIfEmpty(limit.Low).Max();
        var high = union.High <= old.High ? union.High
            : thresholds.Where(threshold => threshold > union.High && threshold <= limit.High).DefaultIfEmpty(limit.High).Min();
        return new(low, high);
    }

    private static bool Same(State previous, State next) =>
        previous.Slots.SequenceEqual(next.Slots) && previous.Stack.SequenceEqual(next.Stack);
}
