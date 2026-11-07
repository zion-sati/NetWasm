using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Loops;

internal interface IElementwiseLoopVectorizer
{
    CilMethodBody Rewrite(CilMethodBody body);
}

// Roslyn's `for (...; i < end; i++) { a[i] = b[i + 1] * c + i; ... }` computes
// each element from elements at the counter plus a constant, the counter and
// values the loop does not change. Ahead of the loop's entry jump, when
// i < end, every array is non-null and every access lies within its array for i
// and end - 1, a Vector128 loop runs four (or, for long and double, two)
// iterations at a time while that many remain, and the loop then runs what is
// left. The body holds nothing but those element stores, copies of the counter
// into locals (`k = i`) and the increment; elements are int, uint, long, ulong,
// float or double, one type per loop, and the operations are lane-wise and exact:
// wrapping integer add, subtract, multiply, bitwise operations and shifts by a
// constant, and floating add, subtract, multiply, divide and negate, so no
// iteration can throw and every lane computes what its iteration would. Values
// the loop does not change, including pure Math and MathF functions of them, are
// computed once and broadcast. Iterations depend on one another only through an
// array written at one offset and read or written at another; the same array
// that way is not vectorized, and two arrays that way run the vector loop only
// when they are different objects. A statement `x = x op value` folds such a
// value into an integer local x with a wrapping addition or a bitwise
// operation, each associative and commutative: the vector loop folds lanes into
// a vector that starts at the operation's identity, x then takes the fold of its
// lanes, and the loop folds what is left. A fold reads only arrays the loop does
// not write. A copy of an element into a local, as foreach makes, is that
// element where the body reads it, and the vector loop leaves it the last one.
// A span's element, as its inlined indexer reads it, is Unsafe.Add of the span's
// reference at the counter plus a constant; the indexer's check that index,
// unsigned, lies below a length the loop does not change, around a block that
// only throws, holds for every iteration the vector loop runs when the counter
// plus the constant is not negative there and lies below the length at end - 1,
// which the entry check tests. A loop that reads a span stores nothing: a store
// could reach memory the span covers.
//
// Roslyn's `do { x += p | 1; p = ref Unsafe.Add(ref p, 1); n--; } while (n != 0)`
// walks a reference over n elements instead. Where the loop's only other entry
// falls into its head and its body only folds and copies the element p
// points to, a vector loop ahead of the head runs while n, unsigned, holds
// `lanes` elements, advancing p and n alike; it leaves past the loop when n
// reaches zero, and otherwise the loop runs what is left. A loop entered with
// n below `lanes`, zero included, runs as before.
internal sealed class ElementwiseLoopVectorizer(
    IVectorLoopMethodResolver methods,
    ICalledMethodResolver calledMethods,
    bool enabled) : IElementwiseLoopVectorizer
{
    private const int MaximumOffset = 1 << 16;
    private const int MaximumBoundLength = 8;

    private readonly IVectorLoopMethodResolver _methods = methods ?? throw new ArgumentNullException(nameof(methods));
    private readonly ICalledMethodResolver _calledMethods = calledMethods ?? throw new ArgumentNullException(nameof(calledMethods));

    private abstract record Node;

    // The counter times Scale plus Offset; a constant has Scale 0.
    private sealed record Linear(int Scale, int Offset) : Node;

    // A value the loop does not change, pushed by Code.
    private sealed record Invariant(ImmutableArray<CilInstruction> Code, CliValueKind Kind) : Node;

    // Array[counter + Offset], of the element type Type; or, ByReference, the
    // element Offset after the one the reference Array points to.
    private sealed record Element(CilInstruction Array, int Offset, CliTypeIdentity Type, bool ByReference = false,
        SpanIndexer? Span = null) : Node;

    // How the body indexed a span's reference: Unsafe.Add, after the conversion of
    // its index to a native integer, if any.
    private sealed record SpanIndexer(CilInstruction Add, CilInstruction? Conversion);

    // Unsafe.Add of a span's reference at the counter plus Offset, which only an
    // element load may read.
    private sealed record SpanAddress(CilInstruction Reference, int Offset, CliTypeIdentity Type, SpanIndexer Indexer) : Node;

    // The counter plus Offset as a native integer, which only Unsafe.Add may take.
    private sealed record NativeIndex(int Offset, CilInstruction Conversion) : Node;

    // The check that the counter plus Offset, unsigned, lies below Length.
    private sealed record Check(int Offset, CilInstruction Length);

    private sealed record Unary(CilOperation Operation, Node Operand) : Node;

    private sealed record Binary(CilOperation Operation, Node Left, Node Right) : Node;

    // A shift by the constant Count.
    private sealed record Shift(CilOperation Operation, Node Value, int Count) : Node;

    // A local's value before this iteration's fold of it.
    private sealed record Accumulator(int Local) : Node;

    // The walking reference, which only an element load may read.
    private sealed record Reference(CilInstruction Load) : Node;

    private sealed record Store(CilInstruction Array, int Offset, CliTypeIdentity Type, Node Value);

    // Local = Local Operation Value.
    private sealed record Fold(int Local, CilOperation Operation, Node Value);

    private sealed record Loop(
        int Counter,
        CilInstruction[] End,
        List<Store> Stores,
        List<Fold> Folds,
        Dictionary<int, Linear> Aliases,
        Dictionary<int, Element> Copies,
        CliValueKind Kind,
        List<Check> Checks);

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!enabled || !body.Instructions.Any(static instruction =>
                instruction.Operation is CilOperation.StoreArrayElement or CilOperation.LoadArrayElement or CilOperation.LoadObject))
        {
            return body;
        }

        var instructions = body.Instructions;
        var indices = Enumerable.Range(0, instructions.Length).ToDictionary(index => instructions[index].Offset);
        var branches = instructions
            .Select(static instruction => instruction.Operand)
            .OfType<CilOperand.BranchTarget>()
            .GroupBy(static target => target.Offset)
            .ToDictionary(static group => group.Key, static group => group.Count());
        var locals = body.Locals.ToBuilder();
        var localTypes = body.LocalSignatureTypes.ToBuilder();
        List<CilInstruction>[]? replacements = null;
        var depth = 0;
        for (var jump = 0; jump < instructions.Length; jump++)
        {
            if (instructions[jump] is not { Operation: CilOperation.Branch, Operand: CilOperand.BranchTarget { Offset: var test } } ||
                branches[test] != 1 ||
                !indices.TryGetValue(test, out var condition) || condition <= jump + 4 ||
                Match(instructions, jump, condition, body.Locals) is not { } loop)
            {
                continue;
            }
            replacements ??= [.. instructions.Select(static instruction => new List<CilInstruction> { instruction })];
            var prefix = Prefix(test, loop, locals, localTypes, out var loopDepth);
            replacements[jump] = [.. prefix, instructions[jump]];
            depth = Math.Max(depth, loopDepth);
        }
        foreach (var counted in CountedWalks.Find(body, _calledMethods))
        {
            // The instruction ahead of a walk falls into it, so it is no element
            // loop's entry jump, which the prefixes above replace.
            var start = counted.Head;
            if (MatchReference(counted, body.Locals) is not { } walk)
            {
                continue;
            }
            replacements ??= [.. instructions.Select(static instruction => new List<CilInstruction> { instruction })];
            replacements[start - 1] = ReferencePrefix(
                instructions[start - 1], walk, instructions[counted.Back + 1].Offset, locals, localTypes, out var loopDepth);
            depth = Math.Max(depth, loopDepth);
        }
        return replacements is null
            ? body
            : RenumberedInstructions.Splice(body, replacements) with
            {
                MaxStack = body.MaxStack + depth,
                Locals = locals.ToImmutable(),
                LocalSignatureTypes = localTypes.ToImmutable(),
            };
    }

    private Loop? Match(ImmutableArray<CilInstruction> instructions, int jump, int condition, ImmutableArray<CliValueKind> locals)
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
            Parse(body, counter, writtenLocals) is not var (stores, folds, aliases, copies, checks) ||
            stores.Count + folds.Count == 0 ||
            stores.Count == 0 && folds.All(static fold => fold.Value is Element))
        {
            return null;
        }

        // One lane type for every element the loop touches; folds of integers
        // into locals of that kind, of arrays the loop does not write.
        var kinds = stores.Select(static store => Lane(store.Type))
            .Concat(stores.SelectMany(static store => Elements(store.Value)).Select(static element => Lane(element.Type)))
            .Concat(folds.SelectMany(static fold => Elements(fold.Value)).Select(static element => Lane(element.Type)))
            .Distinct()
            .ToArray();
        // A loop that only folds 64-bit lanes does two elements a vector
        // iteration, which the array unroller's four scalar ones outrun
        // (StrengthReduction's SumLongsArray: 3.25 us unrolled, 4.54 as vectors).
        if (kinds is not [{ } kind] || kind == CliValueKind.Unknown ||
            stores.Count == 0 && kind is CliValueKind.I8 ||
            stores.Any(store => Kind(store.Value) != kind || !Supported(store.Value, kind)) ||
            folds.Any(fold => kind is not (CliValueKind.I4 or CliValueKind.I8) || locals[fold.Local] != kind ||
                Kind(fold.Value) is var value && value != kind && value != CliValueKind.Unknown ||
                !Supported(fold.Value, kind) ||
                Elements(fold.Value).Any(element => stores.Any(store => SameArray(store.Array, element.Array)))))
        {
            return null;
        }
        var spans = stores.SelectMany(static store => Elements(store.Value))
            .Concat(folds.SelectMany(static fold => Elements(fold.Value)))
            .Concat(copies.Values)
            .Any(static element => element.Span is not null);
        return Hazards(stores) is null || spans && stores.Count != 0 ||
            copies.Values.Any(copy => Lane(copy.Type) != kind || stores.Any(store => SameArray(store.Array, copy.Array)))
            ? null
            : new Loop(counter, end, stores, folds, aliases, copies, kind, checks);
    }

    // A loop that walks Reference over Count elements: Advance moves the
    // reference one element on.
    private sealed record Walk(
        int Reference,
        int Count,
        CilInstruction Advance,
        List<Fold> Folds,
        Dictionary<int, Element> Copies,
        CliValueKind Kind);

    // A walk the vector loop can take: its body only folds and copies the
    // element p points to, of 32-bit or 64-bit integers. It has no counter to
    // index an array with, so every element it reads is one through p, which
    // must read it as the type p walks.
    private Walk? MatchReference(CountedWalk counted, ImmutableArray<CliValueKind> locals)
    {
        var (body, pointer, element) = (counted.Body, counted.Reference, counted.Element);
        var written = body
            .Select(static instruction => instruction is { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var local } }
                ? local
                : -1)
            .Where(static local => local >= 0)
            .ToHashSet();
        if (Parse(body, -1, written, pointer) is not var (stores, folds, _, copies, _) || stores.Count != 0 || folds.Count == 0 ||
            Lane(element) is var kind && kind is not (CliValueKind.I4 or CliValueKind.I8) ||
            folds.SelectMany(static fold => Elements(fold.Value)).Concat(copies.Values)
                .Any(found => found.Type.CanonicalName != element.CanonicalName) ||
            folds.Any(fold => locals[fold.Local] != kind ||
                Kind(fold.Value) is var value && value != kind && value != CliValueKind.Unknown || !Supported(fold.Value, kind)))
        {
            return null;
        }
        return new Walk(pointer, counted.Count, counted.Advance, folds, copies, kind);
    }

    // The instruction that falls into a walk's head, then the vector loop ahead
    // of the head; `exit` follows the walk.
    private List<CilInstruction> ReferencePrefix(
        CilInstruction entry,
        Walk walk,
        int exit,
        ImmutableArray<CliValueKind>.Builder locals,
        ImmutableArray<CliTypeIdentity>.Builder localTypes,
        out int depth)
    {
        var code = new VectorCode(_methods, walk.Kind, locals, localTypes, counter: null);
        var prefix = code.Code;
        prefix.Add(entry);
        var lanes = code.Lanes;
        void Count() => code.Add(CilOperation.LoadLocal, new CilOperand.Index(walk.Count));
        void Advance(int elements)
        {
            code.Add(CilOperation.LoadLocal, new CilOperand.Index(walk.Reference));
            code.Constant(elements);
            prefix.Add(Copy(walk.Advance));
        }

        // Fewer than `lanes` elements, zero included, run as before.
        Count();
        code.Constant(lanes);
        var skip = prefix.Count;
        code.Add(CilOperation.BranchIfLessThanUnsigned);
        foreach (var fold in walk.Folds) code.Hoist(fold.Value);
        var folded = code.Identities(walk.Folds);

        var loopStart = prefix.Count;
        code.Fold(folded);
        // An element copy keeps the last element, as the loop leaves it.
        foreach (var (local, copy) in walk.Copies)
        {
            Advance(lanes - 1);
            code.Add(CilOperation.LoadObject, new CilOperand.TypeIdentity(copy.Type));
            code.Add(CilOperation.StoreLocal, new CilOperand.Index(local));
        }
        Advance(lanes);
        code.Add(CilOperation.StoreLocal, new CilOperand.Index(walk.Reference));
        Count();
        code.Constant(lanes);
        code.Add(CilOperation.Subtract);
        code.Add(CilOperation.StoreLocal, new CilOperand.Index(walk.Count));
        Count();
        code.Constant(lanes);
        code.Add(CilOperation.BranchIfGreaterThanOrEqualUnsigned, RenumberedInstructions.Local(loopStart));
        code.Reduce(folded);
        // Nothing left: the loop is done.
        Count();
        code.Add(CilOperation.BranchIfFalse, new CilOperand.BranchTarget(exit));
        prefix[skip] = prefix[skip] with { Operand = RenumberedInstructions.Local(prefix.Count) };

        depth = Depth([], walk.Folds);
        return prefix;
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

    // The element stores, folds, counter copies, element copies and span index
    // checks, from the body's instructions.
    private (List<Store> Stores, List<Fold> Folds, Dictionary<int, Linear> Aliases, Dictionary<int, Element> Copies,
        List<Check> Checks)? Parse(
        CilInstruction[] body, int counter, HashSet<int> written, int reference = -1)
    {
        var checks = new List<Check>();
        var stack = new List<Node>();
        var stores = new List<Store>();
        var folds = new List<Fold>();
        var aliases = new Dictionary<int, Linear>();
        var copies = new Dictionary<int, Element>();
        var accumulated = new HashSet<int>();
        Node Pop()
        {
            var node = stack[^1];
            stack.RemoveAt(stack.Count - 1);
            return node;
        }

        for (var position = 0; position < body.Length; position++)
        {
            var instruction = body[position];
            switch (instruction.Operation)
            {
                case CilOperation.Nop:
                    break;
                // A span indexer's check of the counter plus a constant around its throw.
                case CilOperation.BranchIfLessThanUnsigned when instruction.Operand is CilOperand.BranchTarget { Offset: var target }:
                    {
                        if (stack.Count < 2 || Pop() is not Invariant { Code: [var length] } ||
                            Pop() is not Linear { Scale: 1, Offset: var offset })
                        {
                            return null;
                        }
                        var resume = position + 1;
                        while (resume < body.Length && body[resume].Offset != target) resume++;
                        if (resume == body.Length || !Throws(body.AsSpan(position + 1, resume - position - 1))) return null;
                        checks.Add(new Check(offset, length));
                        position = resume - 1;
                        break;
                    }
                case CilOperation.ConvertNativeUInt or CilOperation.ConvertNativeInt:
                    if (stack.Count < 1 || Pop() is not Linear { Scale: 1, Offset: var counterOffset }) return null;
                    stack.Add(new NativeIndex(counterOffset, Copy(instruction)));
                    break;
                case CilOperation.LoadLocal when instruction.Operand is CilOperand.Index { Value: var local }:
                    if (local == counter)
                    {
                        stack.Add(new Linear(1, 0));
                    }
                    else if (local == reference)
                    {
                        stack.Add(new Reference(instruction));
                    }
                    else if (aliases.TryGetValue(local, out var alias))
                    {
                        stack.Add(alias);
                    }
                    else if (copies.TryGetValue(local, out var copied))
                    {
                        stack.Add(copied);
                    }
                    else if (written.Contains(local))
                    {
                        // Read before this iteration writes it: the last iteration's
                        // value, which only the local's fold may read.
                        accumulated.Add(local);
                        stack.Add(new Accumulator(local));
                    }
                    else
                    {
                        stack.Add(new Invariant([Copy(instruction)], CliValueKind.Unknown));
                    }
                    break;
                case CilOperation.LoadArgument:
                    stack.Add(new Invariant([Copy(instruction)], CliValueKind.Unknown));
                    break;
                case CilOperation.LoadInt32 when instruction.Operand is CilOperand.ConstantI4 { Value: var value }:
                    stack.Add(new Linear(0, value));
                    break;
                case CilOperation.LoadInt64:
                    stack.Add(new Invariant([Copy(instruction)], CliValueKind.I8));
                    break;
                case CilOperation.LoadFloat32:
                    stack.Add(new Invariant([Copy(instruction)], CliValueKind.F4));
                    break;
                case CilOperation.LoadFloat64:
                    stack.Add(new Invariant([Copy(instruction)], CliValueKind.F8));
                    break;
                case CilOperation.LoadArrayElement when instruction.Operand is CilOperand.TypeIdentity { Value: var loaded }:
                    {
                        if (stack.Count < 2 || Pop() is not Linear { Scale: 1, Offset: var offset } ||
                            Pop() is not Invariant { Code: [var array] } || !IsArray(array) ||
                            Lane(loaded) == CliValueKind.Unknown || Math.Abs(offset) >= MaximumOffset)
                        {
                            return null;
                        }
                        stack.Add(new Element(array, offset, loaded));
                        break;
                    }
                // *p, where p walks the elements, or a span's element.
                case CilOperation.LoadObject when instruction.Operand is CilOperand.TypeIdentity { Value: var loaded }:
                    if (stack.Count < 1 || Lane(loaded) == CliValueKind.Unknown) return null;
                    switch (Pop())
                    {
                        case Reference { Load: var load }:
                            stack.Add(new Element(load, 0, loaded, ByReference: true));
                            break;
                        case SpanAddress address when address.Type.CanonicalName == loaded.CanonicalName:
                            stack.Add(new Element(address.Reference, address.Offset, loaded, Span: address.Indexer));
                            break;
                        default:
                            return null;
                    }
                    break;
                case CilOperation.StoreArrayElement when instruction.Operand is CilOperand.TypeIdentity { Value: var stored }:
                    {
                        if (stack.Count < 3) return null;
                        var value = Pop();
                        if (Pop() is not Linear { Scale: 1, Offset: var offset } ||
                            Pop() is not Invariant { Code: [var array] } || !IsArray(array) ||
                            Lane(stored) == CliValueKind.Unknown || Math.Abs(offset) >= MaximumOffset)
                        {
                            return null;
                        }
                        stores.Add(new Store(array, offset, stored, value));
                        break;
                    }
                case CilOperation.StoreLocal when instruction.Operand is CilOperand.Index { Value: var local }:
                    {
                        if (stack.Count < 1 || local == counter) return null;
                        var value = Pop();
                        var fresh = !folds.Any(fold => fold.Local == local) && !accumulated.Contains(local) &&
                            !aliases.ContainsKey(local) && !copies.ContainsKey(local);
                        // k = i + c, which later index and value expressions read.
                        if (value is Linear { Scale: not 0 } copy && fresh)
                        {
                            aliases[local] = copy;
                            break;
                        }
                        // s = a[i + c], as foreach copies an element.
                        if (value is Element element && fresh)
                        {
                            copies[local] = element;
                            break;
                        }
                        // x = x op value, once an iteration.
                        if (value is Binary
                            {
                                Operation: CilOperation.Add or CilOperation.BitwiseOr or CilOperation.BitwiseXor or
                                CilOperation.BitwiseAnd
                            } fold &&
                            (fold.Left, fold.Right) switch
                            {
                                (Accumulator { Local: var left }, var folded) when left == local => folded,
                                (var folded, Accumulator { Local: var right }) when right == local => folded,
                                _ => null,
                            } is { } operand &&
                            !Accumulators(operand).Any() && !aliases.ContainsKey(local) && !folds.Any(other => other.Local == local))
                        {
                            folds.Add(new Fold(local, fold.Operation, operand));
                            break;
                        }
                        return null;
                    }
                case CilOperation.Add or CilOperation.Subtract or CilOperation.Multiply or CilOperation.Divide or
                    CilOperation.BitwiseAnd or CilOperation.BitwiseOr or CilOperation.BitwiseXor:
                    {
                        if (stack.Count < 2) return null;
                        var right = Pop();
                        if (Combine(instruction.Operation, Pop(), right) is not { } combined) return null;
                        stack.Add(combined);
                        break;
                    }
                case CilOperation.ShiftLeft or CilOperation.ShiftRightSigned or CilOperation.ShiftRightUnsigned:
                    {
                        if (stack.Count < 2 || Pop() is not Linear { Scale: 0, Offset: var count }) return null;
                        var value = Pop();
                        stack.Add(value is Invariant invariant
                            ? Hoisted(invariant, Instruction(CilOperation.LoadInt32, new CilOperand.ConstantI4(count)), Copy(instruction))
                            : new Shift(instruction.Operation, value, count));
                        break;
                    }
                case CilOperation.Negate or CilOperation.OnesComplement:
                    {
                        if (stack.Count < 1) return null;
                        var operand = Pop();
                        stack.Add(operand switch
                        {
                            Linear { Scale: 0, Offset: var value } => new Linear(0, instruction.Operation == CilOperation.Negate
                                ? unchecked(-value) : ~value),
                            Invariant invariant => Hoisted(invariant, Copy(instruction)),
                            _ => new Unary(instruction.Operation, operand),
                        });
                        break;
                    }
                case CilOperation.ConvertFloat32 or CilOperation.ConvertFloat64 or CilOperation.ConvertInt64:
                    {
                        // Only of values the loop does not change.
                        if (stack.Count < 1 || AsInvariant(Pop()) is not { } invariant) return null;
                        stack.Add(new Invariant([.. invariant.Code, Copy(instruction)], instruction.Operation switch
                        {
                            CilOperation.ConvertFloat32 => CliValueKind.F4,
                            CilOperation.ConvertFloat64 => CliValueKind.F8,
                            _ => CliValueKind.I8,
                        }));
                        break;
                    }
                // Unsafe.Add(ref reference, index), index the counter plus a constant.
                case CilOperation.Call when _calledMethods.Resolve(instruction) is { } add && IsUnsafeAdd(add):
                    {
                        if (stack.Count < 2) return null;
                        var (elementOffset, conversion) = Pop() switch
                        {
                            NativeIndex native when add.Signature.ParameterTypes[1] == CliValueKind.NativeInt =>
                                ((int?)native.Offset, native.Conversion),
                            Linear { Scale: 1 } linear when add.Signature.ParameterTypes[1] == CliValueKind.I4 => (linear.Offset, null),
                            _ => (null, (CilInstruction?)null),
                        };
                        if (elementOffset is not { } offset || Pop() is not Invariant { Code: [var span] })
                        {
                            return null;
                        }
                        stack.Add(new SpanAddress(span, offset, add.MethodArguments[0], new SpanIndexer(Copy(instruction), conversion)));
                        break;
                    }
                case CilOperation.Call:
                    {
                        if (_calledMethods.Resolve(instruction) is not { } target || !LoopInvariantCallHoister.IsPure(target)) return null;
                        var count = target.Signature.ParameterTypes.Length;
                        if (stack.Count < count) return null;
                        var arguments = new List<Invariant>();
                        for (var index = 0; index < count; index++)
                        {
                            if (AsInvariant(Pop()) is not { } argument) return null;
                            arguments.Insert(0, argument);
                        }
                        stack.Add(new Invariant([.. arguments.SelectMany(static argument => argument.Code), Copy(instruction)],
                            target.Signature.ReturnType));
                        break;
                    }
                default:
                    return null;
            }
        }
        // A local's earlier value reaches nothing but its fold, and the walking
        // reference nothing but element loads.
        return stack.Count == 0 && !stores.Any(static store => Accumulators(store.Value).Any()) &&
            !stores.Select(static store => store.Value).Concat(folds.Select(static fold => fold.Value)).Any(static value => References(value))
            ? (stores, folds, aliases, copies, checks)
            : null;
    }

    // A block that only throws: a call that takes nothing, then optionally null, then throw.
    private bool Throws(ReadOnlySpan<CilInstruction> block)
    {
        var index = 0;
        while (index < block.Length && block[index].Operation == CilOperation.Nop) index++;
        if (index == block.Length || block[index].Operation != CilOperation.Call ||
            _calledMethods.Resolve(block[index]) is not { } thrower || thrower.Signature.ParameterTypes.Length != 0)
        {
            return false;
        }
        index++;
        if (index < block.Length && block[index].Operation == CilOperation.LoadNull) index++;
        return index == block.Length - 1 && block[index].Operation == CilOperation.Throw;
    }

    // Unsafe.Add<T>(ref T, int), (ref T, nint) or (ref T, nuint).
    private static bool IsUnsafeAdd(MethodInstanceModel method) => method is
    {
        DeclaringType.FullName: "System.Runtime.CompilerServices.Unsafe",
        Definition.Name: "Add",
        MethodArguments: [_],
        Signature.ParameterTypes: [CliValueKind.ManagedAddress, CliValueKind.I4 or CliValueKind.NativeInt],
    };

    private static bool References(Node node) => node switch
    {
        Reference => true,
        Unary unary => References(unary.Operand),
        Binary binary => References(binary.Left) || References(binary.Right),
        Shift shift => References(shift.Value),
        _ => false,
    };

    private static IEnumerable<Accumulator> Accumulators(Node node) => node switch
    {
        Accumulator accumulator => [accumulator],
        Unary unary => Accumulators(unary.Operand),
        Binary binary => Accumulators(binary.Left).Concat(Accumulators(binary.Right)),
        Shift shift => Accumulators(shift.Value),
        _ => [],
    };

    private static Node? Combine(CilOperation operation, Node left, Node right)
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

    private static Invariant Hoisted(Invariant value, params CilInstruction[] code) =>
        new([.. value.Code, .. code], value.Kind);

    private static Invariant Hoisted(Invariant value, ImmutableArray<CilInstruction> code, CliValueKind other) =>
        new([.. value.Code, .. code], value.Kind == CliValueKind.Unknown ? other : value.Kind);

    private static Invariant? AsInvariant(Node node) => node switch
    {
        Invariant invariant => invariant,
        Linear { Scale: 0, Offset: var value } => new([Instruction(CilOperation.LoadInt32, new CilOperand.ConstantI4(value))], CliValueKind.I4),
        _ => null,
    };

    // The pairs of arrays the vector loop needs to be different objects, or null
    // when the same array is written at one offset and accessed at another.
    private static List<(CilInstruction, CilInstruction)>? Hazards(List<Store> stores)
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

    private List<CilInstruction> Prefix(
        int test,
        Loop loop,
        ImmutableArray<CliValueKind>.Builder locals,
        ImmutableArray<CliTypeIdentity>.Builder localTypes,
        out int depth)
    {
        var code = new VectorCode(_methods, loop.Kind, locals, localTypes, loop.Counter);
        var prefix = code.Code;
        var lanes = code.Lanes;
        var testTarget = new CilOperand.BranchTarget(test);
        var stores = loop.Stores;
        var accesses = stores.Select(static store => (store.Array, store.Offset))
            .Concat(stores.SelectMany(static store => Elements(store.Value)).Select(static element => (element.Array, element.Offset)))
            .Concat(loop.Folds.SelectMany(static fold => Elements(fold.Value)).Select(static element => (element.Array, element.Offset)))
            .Concat(loop.Copies.Values.Select(static copy => (copy.Array, copy.Offset)))
            .DistinctBy(static access => (access.Array.Operation, access.Array.Operand, access.Offset))
            .Where(access => !SpanReferences(loop).Any(span => SameArray(span, access.Array)))
            .ToArray();
        var arrays = accesses.Select(static access => access.Array)
            .DistinctBy(static array => (array.Operation, array.Operand))
            .ToArray();

        // i < end, the arrays non-null, every access at i and at end - 1 within
        // its array, and arrays that would depend on one another different.
        code.Counter(0);
        code.Emit(loop.End);
        code.Add(CilOperation.BranchIfGreaterThanOrEqualSigned, testTarget);
        foreach (var array in arrays)
        {
            prefix.Add(Copy(array));
            code.Add(CilOperation.BranchIfFalse, testTarget);
        }
        foreach (var (array, offset) in accesses)
        {
            code.Counter(offset);
            prefix.Add(Copy(array));
            code.Add(CilOperation.LoadArrayLength);
            code.Add(CilOperation.ConvertInt32);
            code.Add(CilOperation.BranchIfGreaterThanOrEqualUnsigned, testTarget);
            code.Emit(loop.End);
            code.Constant(offset - 1);
            code.Add(CilOperation.Add);
            prefix.Add(Copy(array));
            code.Add(CilOperation.LoadArrayLength);
            code.Add(CilOperation.ConvertInt32);
            code.Add(CilOperation.BranchIfGreaterThanOrEqualUnsigned, testTarget);
        }
        // Every span index check holds from i to end - 1.
        foreach (var check in loop.Checks)
        {
            code.Counter(check.Offset);
            code.Constant(0);
            code.Add(CilOperation.BranchIfLessThanSigned, testTarget);
            code.Emit(loop.End);
            code.Constant(check.Offset - 1);
            code.Add(CilOperation.Add);
            prefix.Add(Copy(check.Length));
            code.Add(CilOperation.BranchIfGreaterThanOrEqualUnsigned, testTarget);
        }
        foreach (var (first, second) in Hazards(stores)!)
        {
            prefix.Add(Copy(first));
            prefix.Add(Copy(second));
            code.Add(CilOperation.BranchIfEqual, testTarget);
        }

        foreach (var store in stores) code.Hoist(store.Value);
        foreach (var fold in loop.Folds) code.Hoist(fold.Value);
        var folded = code.Identities(loop.Folds);

        // The vector loop, while `lanes` iterations remain.
        var loopStart = prefix.Count;
        code.Emit(loop.End);
        code.Counter(0);
        code.Add(CilOperation.Subtract);
        code.Constant(lanes);
        var exit = prefix.Count;
        code.Add(CilOperation.BranchIfLessThanSigned, testTarget);
        foreach (var store in stores)
        {
            code.Value(store.Value);
            prefix.Add(Copy(store.Array));
            code.Call(_methods.Resolve(VectorLoopMethod.ArrayData, store.Type));
            code.Counter(store.Offset);
            code.Add(CilOperation.ConvertNativeUInt);
            code.Call(_methods.Resolve(VectorLoopMethod.Store, code.Lane));
        }
        code.Fold(folded);
        // An element copy keeps the last iteration's element, as the loop leaves it.
        foreach (var (local, copy) in loop.Copies)
        {
            prefix.Add(Copy(copy.Array));
            code.Counter(copy.Offset + lanes - 1);
            if (copy.Span is { } indexer)
            {
                if (indexer.Conversion is { } conversion) prefix.Add(Copy(conversion));
                prefix.Add(Copy(indexer.Add));
                code.Add(CilOperation.LoadObject, new CilOperand.TypeIdentity(copy.Type));
            }
            else
            {
                code.Add(CilOperation.LoadArrayElement, new CilOperand.TypeIdentity(copy.Type));
            }
            code.Add(CilOperation.StoreLocal, new CilOperand.Index(local));
        }
        // A counter copy keeps the last iteration's value, as the loop leaves it.
        foreach (var (local, alias) in loop.Aliases)
        {
            code.Counter(lanes - 1);
            if (alias.Scale != 1)
            {
                code.Constant(alias.Scale);
                code.Add(CilOperation.Multiply);
            }
            if (alias.Offset != 0)
            {
                code.Constant(alias.Offset);
                code.Add(CilOperation.Add);
            }
            code.Add(CilOperation.StoreLocal, new CilOperand.Index(local));
        }
        code.Counter(lanes);
        code.Add(CilOperation.StoreLocal, new CilOperand.Index(loop.Counter));
        code.Add(CilOperation.Branch, RenumberedInstructions.Local(loopStart));
        // The vector loop leaves to the folds of each vector's lanes into its
        // local, then to the loop's entry jump, which follows the prefix.
        prefix[exit] = prefix[exit] with { Operand = RenumberedInstructions.Local(prefix.Count) };
        code.Reduce(folded);

        depth = Depth(loop.Stores, loop.Folds);
        return prefix;
    }

    private static IEnumerable<CilInstruction> SpanReferences(Loop loop) =>
        loop.Folds.SelectMany(static fold => Elements(fold.Value)).Concat(loop.Copies.Values)
            .Where(static element => element.Span is not null)
            .Select(static element => element.Array);

    private static int Depth(List<Store> stores, List<Fold> folds) =>
        stores.Select(static store => Depth(store.Value))
            .Concat(folds.Select(static fold => Depth(fold.Value) + 1))
            .Max() + 8;

    // The vector code a prefix emits: its values the loop does not change,
    // broadcast once, its folds' vectors, and each node's lanes.
    private sealed class VectorCode
    {
        private readonly IVectorLoopMethodResolver _methods;
        private readonly ImmutableArray<CliValueKind>.Builder _locals;
        private readonly ImmutableArray<CliTypeIdentity>.Builder _localTypes;
        private readonly int? _counter;
        private readonly CliValueKind _kind;
        private readonly Dictionary<string, int> _broadcasts = [];
        private int? _iota;

        internal VectorCode(
            IVectorLoopMethodResolver methods,
            CliValueKind kind,
            ImmutableArray<CliValueKind>.Builder locals,
            ImmutableArray<CliTypeIdentity>.Builder localTypes,
            int? counter)
        {
            _methods = methods;
            _kind = kind;
            _locals = locals;
            _localTypes = localTypes;
            _counter = counter;
            Lane = LaneType(kind);
            Lanes = 16 / Size(kind);
            VectorType = methods.Resolve(VectorLoopMethod.Broadcast, Lane).Signature.ReturnSignatureType;
        }

        internal List<CilInstruction> Code { get; } = [];

        internal CliTypeIdentity Lane { get; }

        internal int Lanes { get; }

        internal CliTypeIdentity VectorType { get; }

        internal void Add(CilOperation operation, CilOperand? operand = null) =>
            Code.Add(Instruction(operation, operand ?? new CilOperand.None()));

        internal void Emit(IEnumerable<CilInstruction> code)
        {
            foreach (var instruction in code) Code.Add(Copy(instruction));
        }

        internal void Constant(int value) => Add(CilOperation.LoadInt32, new CilOperand.ConstantI4(value));

        internal void Call(MethodInstanceModel method) => Add(CilOperation.Call, new CilOperand.MethodInstance(method));

        // The counter plus offset.
        internal void Counter(int offset)
        {
            Add(CilOperation.LoadLocal, new CilOperand.Index(_counter!.Value));
            if (offset == 0) return;
            Constant(offset);
            Add(CilOperation.Add);
        }

        internal int NewLocal(CliTypeIdentity type)
        {
            _locals.Add(type.StackKind);
            _localTypes.Add(type);
            return _locals.Count - 1;
        }

        internal int Broadcast(ImmutableArray<CilInstruction> code)
        {
            var key = string.Join(';', code.Select(static instruction => $"{instruction.Operation}:{instruction.Operand}"));
            if (_broadcasts.TryGetValue(key, out var existing)) return existing;
            Emit(code);
            Call(_methods.Resolve(VectorLoopMethod.Broadcast, Lane));
            var local = NewLocal(VectorType);
            Add(CilOperation.StoreLocal, new CilOperand.Index(local));
            return _broadcasts[key] = local;
        }

        // Broadcasts a node's values the loop does not change; the lanes' own
        // counter offsets 0, 1, 2 and 3 for a value that reads the counter.
        internal void Hoist(Node node)
        {
            switch (node)
            {
                case Invariant invariant:
                    Broadcast(invariant.Code);
                    break;
                case Linear { Scale: 0, Offset: var value }:
                    Broadcast([Instruction(CilOperation.LoadInt32, new CilOperand.ConstantI4(value))]);
                    break;
                case Linear linear:
                    if (_iota is null)
                    {
                        Constant(0);
                        Constant(1);
                        Constant(2);
                        Constant(3);
                        Call(_methods.Resolve(VectorLoopMethod.CreateInt32, Lane));
                        _iota = NewLocal(VectorType);
                        Add(CilOperation.StoreLocal, new CilOperand.Index(_iota.Value));
                    }
                    if (linear.Scale != 1) Broadcast([Instruction(CilOperation.LoadInt32, new CilOperand.ConstantI4(linear.Scale))]);
                    if (linear.Offset != 0) Broadcast([Instruction(CilOperation.LoadInt32, new CilOperand.ConstantI4(linear.Offset))]);
                    break;
                case Unary unary:
                    Hoist(unary.Operand);
                    break;
                case Binary binary:
                    Hoist(binary.Left);
                    Hoist(binary.Right);
                    break;
                case Shift shift:
                    Hoist(shift.Value);
                    break;
            }
        }

        // Pushes a node's lanes.
        internal void Value(Node node)
        {
            switch (node)
            {
                case Element { ByReference: true } element:
                    Code.Add(Copy(element.Array));
                    Constant(element.Offset);
                    Add(CilOperation.ConvertNativeUInt);
                    Call(_methods.Resolve(VectorLoopMethod.Load, Lane));
                    break;
                // Extended to a native int as the scalar index was: Unsafe.Add of an
                // int sign-extends it.
                case Element { Span: { } indexer } element:
                    Code.Add(Copy(element.Array));
                    Counter(element.Offset);
                    Code.Add(indexer.Conversion is { } conversion
                        ? Copy(conversion)
                        : Instruction(CilOperation.ConvertNativeInt, new CilOperand.None()));
                    Call(_methods.Resolve(VectorLoopMethod.Load, Lane));
                    break;
                case Element element:
                    Code.Add(Copy(element.Array));
                    Call(_methods.Resolve(VectorLoopMethod.ArrayData, element.Type));
                    Counter(element.Offset);
                    Add(CilOperation.ConvertNativeUInt);
                    Call(_methods.Resolve(VectorLoopMethod.Load, Lane));
                    break;
                case Invariant invariant:
                    Add(CilOperation.LoadLocal, new CilOperand.Index(Broadcast(invariant.Code)));
                    break;
                case Linear { Scale: 0, Offset: var value }:
                    Add(CilOperation.LoadLocal, new CilOperand.Index(
                        Broadcast([Instruction(CilOperation.LoadInt32, new CilOperand.ConstantI4(value))])));
                    break;
                case Linear linear:
                    // scale * (i + lane) + offset.
                    Counter(0);
                    Call(_methods.Resolve(VectorLoopMethod.Broadcast, Lane));
                    Add(CilOperation.LoadLocal, new CilOperand.Index(_iota!.Value));
                    Call(_methods.Resolve(new VectorLoopOperator("op_Addition"), Lane));
                    if (linear.Scale != 1)
                    {
                        Add(CilOperation.LoadLocal, new CilOperand.Index(
                            Broadcast([Instruction(CilOperation.LoadInt32, new CilOperand.ConstantI4(linear.Scale))])));
                        Call(_methods.Resolve(new VectorLoopOperator("op_Multiply"), Lane));
                    }
                    if (linear.Offset != 0)
                    {
                        Add(CilOperation.LoadLocal, new CilOperand.Index(
                            Broadcast([Instruction(CilOperation.LoadInt32, new CilOperand.ConstantI4(linear.Offset))])));
                        Call(_methods.Resolve(new VectorLoopOperator("op_Addition"), Lane));
                    }
                    break;
                case Unary unary:
                    Value(unary.Operand);
                    Call(_methods.Resolve(new VectorLoopOperator(unary.Operation == CilOperation.Negate ? "op_UnaryNegation" : "op_OnesComplement"), Lane));
                    break;
                case Binary binary:
                    Value(binary.Left);
                    Value(binary.Right);
                    Call(_methods.Resolve(new VectorLoopOperator(Operator(binary.Operation)), Lane));
                    break;
                default:
                    // The remaining node shifts a value.
                    var shift = (Shift)node;
                    Value(shift.Value);
                    Constant(shift.Count);
                    Call(_methods.Resolve(new VectorLoopOperator(shift.Operation switch
                    {
                        CilOperation.ShiftLeft => "op_LeftShift",
                        CilOperation.ShiftRightSigned => "op_RightShift",
                        _ => "op_UnsignedRightShift",
                    }), Lane));
                    break;
            }
        }

        // Each fold's vector, at its operation's identity: no bits for an
        // addition, an or and an exclusive or, every bit for an and.
        internal List<(Fold Fold, int Vector)> Identities(List<Fold> folds)
        {
            var folded = new List<(Fold Fold, int Vector)>();
            foreach (var fold in folds)
            {
                Constant(fold.Operation == CilOperation.BitwiseAnd ? -1 : 0);
                if (_kind == CliValueKind.I8) Add(CilOperation.ConvertInt64);
                Call(_methods.Resolve(VectorLoopMethod.Broadcast, Lane));
                var vector = NewLocal(VectorType);
                Add(CilOperation.StoreLocal, new CilOperand.Index(vector));
                folded.Add((fold, vector));
            }
            return folded;
        }

        // One vector iteration of each fold.
        internal void Fold(List<(Fold Fold, int Vector)> folded)
        {
            foreach (var (fold, vector) in folded)
            {
                Add(CilOperation.LoadLocal, new CilOperand.Index(vector));
                Value(fold.Value);
                Call(_methods.Resolve(new VectorLoopOperator(Operator(fold.Operation)), Lane));
                Add(CilOperation.StoreLocal, new CilOperand.Index(vector));
            }
        }

        // Each vector's lanes folded into its local.
        internal void Reduce(List<(Fold Fold, int Vector)> folded)
        {
            foreach (var (fold, vector) in folded)
            {
                Add(CilOperation.LoadLocal, new CilOperand.Index(fold.Local));
                if (fold.Operation == CilOperation.Add)
                {
                    Add(CilOperation.LoadLocal, new CilOperand.Index(vector));
                    Call(_methods.Resolve(VectorLoopMethod.Sum, Lane));
                }
                else
                {
                    for (var index = 0; index < Lanes; index++)
                    {
                        Add(CilOperation.LoadLocal, new CilOperand.Index(vector));
                        Constant(index);
                        Call(_methods.Resolve(VectorLoopMethod.GetElement, Lane));
                        if (index > 0) Add(fold.Operation);
                    }
                }
                Add(fold.Operation);
                Add(CilOperation.StoreLocal, new CilOperand.Index(fold.Local));
            }
        }
    }

    private static string Operator(CilOperation operation) => operation switch
    {
        CilOperation.Add => "op_Addition",
        CilOperation.Subtract => "op_Subtraction",
        CilOperation.Multiply => "op_Multiply",
        CilOperation.Divide => "op_Division",
        CilOperation.BitwiseAnd => "op_BitwiseAnd",
        CilOperation.BitwiseOr => "op_BitwiseOr",
        _ => "op_ExclusiveOr",
    };

    private static IEnumerable<Element> Elements(Node node) => node switch
    {
        Element element => [element],
        Unary unary => Elements(unary.Operand),
        Binary binary => Elements(binary.Left).Concat(Elements(binary.Right)),
        Shift shift => Elements(shift.Value),
        _ => [],
    };

    private static int Depth(Node node) => node switch
    {
        Unary unary => Depth(unary.Operand),
        Binary binary => Math.Max(Depth(binary.Left), Depth(binary.Right) + 1),
        Shift shift => Depth(shift.Value) + 1,
        _ => 4,
    };

    // The stack kind of a value. An argument or local of unknown kind takes its
    // operation's other operand's; IL's binary operations take operands of one
    // kind, and Supported checks every operand against the loop's.
    private static CliValueKind Kind(Node node) => node switch
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
    private static bool Supported(Node node, CliValueKind kind) => node switch
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

    // The lane kind of an array element type: 32-bit and 64-bit integers and
    // floating values; others are Unknown.
    private static CliValueKind Lane(CliTypeIdentity type) => type.CanonicalName switch
    {
        "primitive:i4" or "primitive:u4" => CliValueKind.I4,
        "primitive:i8" or "primitive:u8" => CliValueKind.I8,
        "primitive:f4" => CliValueKind.F4,
        "primitive:f8" => CliValueKind.F8,
        _ => CliValueKind.Unknown,
    };

    // The vectors' lane types are signed, as IL's integer operations are.
    private static CliTypeIdentity LaneType(CliValueKind kind) => kind switch
    {
        CliValueKind.I4 => CliTypeIdentity.Primitive("i4", CliValueKind.I4),
        CliValueKind.I8 => CliTypeIdentity.Primitive("i8", CliValueKind.I8),
        CliValueKind.F4 => CliTypeIdentity.Primitive("f4", CliValueKind.F4),
        _ => CliTypeIdentity.Primitive("f8", CliValueKind.F8),
    };

    private static int Size(CliValueKind kind) => kind is CliValueKind.I8 or CliValueKind.F8 ? 8 : 4;

    private static bool IsArray(CilInstruction instruction) =>
        instruction.Operation is CilOperation.LoadLocal or CilOperation.LoadArgument;

    private static bool SameArray(CilInstruction left, CilInstruction right) =>
        left.Operation == right.Operation && Equals(left.Operand, right.Operand);

    private static CilInstruction Copy(CilInstruction instruction) => Instruction(instruction.Operation, instruction.Operand);

    private static CilInstruction Instruction(CilOperation operation, CilOperand operand) => new(0, 1, operation, operand);
}
