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
// when they are different objects.
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

    // Array[counter + Offset], of the element type Type.
    private sealed record Element(CilInstruction Array, int Offset, CliTypeIdentity Type) : Node;

    private sealed record Unary(CilOperation Operation, Node Operand) : Node;

    private sealed record Binary(CilOperation Operation, Node Left, Node Right) : Node;

    // A shift by the constant Count.
    private sealed record Shift(CilOperation Operation, Node Value, int Count) : Node;

    private sealed record Store(CilInstruction Array, int Offset, CliTypeIdentity Type, Node Value);

    private sealed record Loop(
        int Counter,
        CilInstruction[] End,
        List<Store> Stores,
        Dictionary<int, Linear> Aliases,
        CliValueKind Kind);

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!enabled || !body.Instructions.Any(static instruction => instruction.Operation == CilOperation.StoreArrayElement))
        {
            return body;
        }

        var instructions = body.Instructions;
        var indices = Enumerable.Range(0, instructions.Length).ToDictionary(index => instructions[index].Offset);
        var branches = instructions
            .SelectMany(static instruction => instruction.Operand switch
            {
                CilOperand.BranchTarget target => [target.Offset],
                CilOperand.SwitchTargets targets => targets.Offsets,
                _ => ImmutableArray<int>.Empty,
            })
            .GroupBy(static offset => offset)
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
                Match(instructions, jump, condition) is not { } loop)
            {
                continue;
            }
            replacements ??= [.. instructions.Select(static instruction => new List<CilInstruction> { instruction })];
            var prefix = Prefix(test, loop, locals, localTypes, out var loopDepth);
            replacements[jump] = [.. prefix, instructions[jump]];
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

    private Loop? Match(ImmutableArray<CilInstruction> instructions, int jump, int condition)
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
        if (End(bound, instructions[branch].Operation, writtenLocals) is not { } end ||
            Parse(body, counter, writtenLocals) is not var (stores, aliases) ||
            stores.Count == 0)
        {
            return null;
        }

        // One lane type for every element the loop touches.
        var kinds = stores.Select(static store => Lane(store.Type))
            .Concat(stores.SelectMany(static store => Elements(store.Value)).Select(static element => Lane(element.Type)))
            .Distinct()
            .ToArray();
        if (kinds is not [{ } kind] || kind == CliValueKind.Unknown ||
            stores.Any(store => Kind(store.Value) != kind || !Supported(store.Value, kind)))
        {
            return null;
        }
        return Hazards(stores) is null ? null : new Loop(counter, end, stores, aliases, kind);
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

    // The element stores and counter copies, from the body's instructions.
    private (List<Store> Stores, Dictionary<int, Linear> Aliases)? Parse(CilInstruction[] body, int counter, HashSet<int> written)
    {
        var stack = new List<Node>();
        var stores = new List<Store>();
        var aliases = new Dictionary<int, Linear>();
        Node Pop()
        {
            var node = stack[^1];
            stack.RemoveAt(stack.Count - 1);
            return node;
        }

        foreach (var instruction in body)
        {
            switch (instruction.Operation)
            {
                case CilOperation.Nop:
                    break;
                case CilOperation.LoadLocal when instruction.Operand is CilOperand.Index { Value: var local }:
                    if (local == counter)
                    {
                        stack.Add(new Linear(1, 0));
                    }
                    else if (aliases.TryGetValue(local, out var alias))
                    {
                        stack.Add(alias);
                    }
                    else if (written.Contains(local))
                    {
                        // Read before this iteration writes it: the last iteration's value.
                        return null;
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
                    // k = i + c, which later index and value expressions read.
                    if (stack.Count < 1 || local == counter || Pop() is not Linear { Scale: not 0 } copy) return null;
                    aliases[local] = copy;
                    break;
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
        return stack.Count == 0 ? (stores, aliases) : null;
    }

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
        var prefix = new List<CilInstruction>();
        void Add(CilOperation operation, CilOperand? operand = null) =>
            prefix.Add(Instruction(operation, operand ?? new CilOperand.None()));
        void Emit(IEnumerable<CilInstruction> code) { foreach (var instruction in code) prefix.Add(Copy(instruction)); }
        void Constant(int value) => Add(CilOperation.LoadInt32, new CilOperand.ConstantI4(value));
        void Call(MethodInstanceModel method) => Add(CilOperation.Call, new CilOperand.MethodInstance(method));
        void Counter(int offset)
        {
            Add(CilOperation.LoadLocal, new CilOperand.Index(loop.Counter));
            if (offset == 0) return;
            Constant(offset);
            Add(CilOperation.Add);
        }
        int NewLocal(CliTypeIdentity type)
        {
            locals.Add(type.StackKind);
            localTypes.Add(type);
            return locals.Count - 1;
        }

        var lane = LaneType(loop.Kind);
        var lanes = 16 / Size(loop.Kind);
        var testTarget = new CilOperand.BranchTarget(test);
        var stores = loop.Stores;
        var accesses = stores.Select(static store => (store.Array, store.Offset))
            .Concat(stores.SelectMany(static store => Elements(store.Value)).Select(static element => (element.Array, element.Offset)))
            .DistinctBy(static access => (access.Array.Operation, access.Array.Operand, access.Offset))
            .ToArray();
        var arrays = accesses.Select(static access => access.Array)
            .DistinctBy(static array => (array.Operation, array.Operand))
            .ToArray();

        // i < end, the arrays non-null, every access at i and at end - 1 within
        // its array, and arrays that would depend on one another different.
        Counter(0);
        Emit(loop.End);
        Add(CilOperation.BranchIfGreaterThanOrEqualSigned, testTarget);
        foreach (var array in arrays)
        {
            prefix.Add(Copy(array));
            Add(CilOperation.BranchIfFalse, testTarget);
        }
        foreach (var (array, offset) in accesses)
        {
            Counter(offset);
            prefix.Add(Copy(array));
            Add(CilOperation.LoadArrayLength);
            Add(CilOperation.ConvertInt32);
            Add(CilOperation.BranchIfGreaterThanOrEqualUnsigned, testTarget);
            Emit(loop.End);
            Constant(offset - 1);
            Add(CilOperation.Add);
            prefix.Add(Copy(array));
            Add(CilOperation.LoadArrayLength);
            Add(CilOperation.ConvertInt32);
            Add(CilOperation.BranchIfGreaterThanOrEqualUnsigned, testTarget);
        }
        foreach (var (first, second) in Hazards(stores)!)
        {
            prefix.Add(Copy(first));
            prefix.Add(Copy(second));
            Add(CilOperation.BranchIfEqual, testTarget);
        }

        // Values the loop does not change, broadcast once; the lanes' own counter
        // offsets 0, 1, 2 and 3 for a value that reads the counter.
        var broadcasts = new Dictionary<string, int>();
        var vectorType = _methods.Resolve(VectorLoopMethod.Broadcast, lane).Signature.ReturnSignatureType;
        int Broadcast(ImmutableArray<CilInstruction> code)
        {
            var key = string.Join(';', code.Select(static instruction => $"{instruction.Operation}:{instruction.Operand}"));
            if (broadcasts.TryGetValue(key, out var existing)) return existing;
            Emit(code);
            Call(_methods.Resolve(VectorLoopMethod.Broadcast, lane));
            var local = NewLocal(vectorType);
            Add(CilOperation.StoreLocal, new CilOperand.Index(local));
            return broadcasts[key] = local;
        }
        int? iota = null;
        void Hoist(Node node)
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
                    if (iota is null)
                    {
                        Constant(0);
                        Constant(1);
                        Constant(2);
                        Constant(3);
                        Call(_methods.Resolve(VectorLoopMethod.CreateInt32, lane));
                        iota = NewLocal(vectorType);
                        Add(CilOperation.StoreLocal, new CilOperand.Index(iota.Value));
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
        foreach (var store in stores) Hoist(store.Value);

        // The vector loop, while `lanes` iterations remain.
        var loopStart = prefix.Count;
        Emit(loop.End);
        Counter(0);
        Add(CilOperation.Subtract);
        Constant(lanes);
        var exit = prefix.Count;
        Add(CilOperation.BranchIfLessThanSigned, testTarget);
        void Value(Node node)
        {
            switch (node)
            {
                case Element element:
                    prefix.Add(Copy(element.Array));
                    Call(_methods.Resolve(VectorLoopMethod.ArrayData, element.Type));
                    Counter(element.Offset);
                    Add(CilOperation.ConvertNativeUInt);
                    Call(_methods.Resolve(VectorLoopMethod.Load, lane));
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
                    Call(_methods.Resolve(VectorLoopMethod.Broadcast, lane));
                    Add(CilOperation.LoadLocal, new CilOperand.Index(iota!.Value));
                    Call(_methods.Resolve(new VectorLoopOperator("op_Addition"), lane));
                    if (linear.Scale != 1)
                    {
                        Add(CilOperation.LoadLocal, new CilOperand.Index(
                            Broadcast([Instruction(CilOperation.LoadInt32, new CilOperand.ConstantI4(linear.Scale))])));
                        Call(_methods.Resolve(new VectorLoopOperator("op_Multiply"), lane));
                    }
                    if (linear.Offset != 0)
                    {
                        Add(CilOperation.LoadLocal, new CilOperand.Index(
                            Broadcast([Instruction(CilOperation.LoadInt32, new CilOperand.ConstantI4(linear.Offset))])));
                        Call(_methods.Resolve(new VectorLoopOperator("op_Addition"), lane));
                    }
                    break;
                case Unary unary:
                    Value(unary.Operand);
                    Call(_methods.Resolve(new VectorLoopOperator(unary.Operation == CilOperation.Negate ? "op_UnaryNegation" : "op_OnesComplement"), lane));
                    break;
                case Binary binary:
                    Value(binary.Left);
                    Value(binary.Right);
                    Call(_methods.Resolve(new VectorLoopOperator(binary.Operation switch
                    {
                        CilOperation.Add => "op_Addition",
                        CilOperation.Subtract => "op_Subtraction",
                        CilOperation.Multiply => "op_Multiply",
                        CilOperation.Divide => "op_Division",
                        CilOperation.BitwiseAnd => "op_BitwiseAnd",
                        CilOperation.BitwiseOr => "op_BitwiseOr",
                        _ => "op_ExclusiveOr",
                    }), lane));
                    break;
                case Shift shift:
                    Value(shift.Value);
                    Constant(shift.Count);
                    Call(_methods.Resolve(new VectorLoopOperator(shift.Operation switch
                    {
                        CilOperation.ShiftLeft => "op_LeftShift",
                        CilOperation.ShiftRightSigned => "op_RightShift",
                        _ => "op_UnsignedRightShift",
                    }), lane));
                    break;
            }
        }
        foreach (var store in stores)
        {
            Value(store.Value);
            prefix.Add(Copy(store.Array));
            Call(_methods.Resolve(VectorLoopMethod.ArrayData, store.Type));
            Counter(store.Offset);
            Add(CilOperation.ConvertNativeUInt);
            Call(_methods.Resolve(VectorLoopMethod.Store, lane));
        }
        // A counter copy keeps the last iteration's value, as the loop leaves it.
        foreach (var (local, alias) in loop.Aliases)
        {
            Counter(lanes - 1);
            if (alias.Scale != 1)
            {
                Constant(alias.Scale);
                Add(CilOperation.Multiply);
            }
            if (alias.Offset != 0)
            {
                Constant(alias.Offset);
                Add(CilOperation.Add);
            }
            Add(CilOperation.StoreLocal, new CilOperand.Index(local));
        }
        Counter(lanes);
        Add(CilOperation.StoreLocal, new CilOperand.Index(loop.Counter));
        Add(CilOperation.Branch, RenumberedInstructions.Local(loopStart));
        // The vector loop leaves to the loop's entry jump, which follows the prefix.
        prefix[exit] = prefix[exit] with { Operand = RenumberedInstructions.Local(prefix.Count) };

        depth = loop.Stores.Max(static store => Depth(store.Value)) + 8;
        return prefix;
    }

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

    // The stack kind of a value; Unknown where the operands disagree.
    private static CliValueKind Kind(Node node) => node switch
    {
        Linear => CliValueKind.I4,
        Invariant invariant => invariant.Kind,
        Element element => Lane(element.Type),
        Unary unary => Kind(unary.Operand),
        Binary binary => (Kind(binary.Left), Kind(binary.Right)) switch
        {
            var (left, right) when left == right => left,
            (CliValueKind.Unknown, var right) => right,
            (var left, CliValueKind.Unknown) => left,
            _ => CliValueKind.Unknown,
        },
        Shift shift => Kind(shift.Value),
        _ => CliValueKind.Unknown,
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
        Shift shift => kind is CliValueKind.I4 or CliValueKind.I8 && Supported(shift.Value, kind),
        _ => false,
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
