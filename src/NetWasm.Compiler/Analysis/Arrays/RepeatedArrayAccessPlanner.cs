using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using Slot = (bool IsArgument, int Index);

namespace NetWasm.Compiler.Analysis.Arrays;

internal interface IRepeatedArrayAccessPlanner
{
    RepeatedArrayAccessPlan Plan(ValidatedControlFlowGraph controlFlow);
}

// Checked: the same array and index were accessed earlier in the block, so
// both checks passed. NonNull: an earlier access to the same array, or to a
// field of the same object, passed its null check. CheckedDimensions: an
// earlier access to the same rectangular array in the block checked the same
// index against that dimension.
internal sealed record RepeatedArrayAccessPlan(ImmutableHashSet<int> Checked, ImmutableHashSet<int> NonNull)
{
    public static RepeatedArrayAccessPlan Empty { get; } = new([], []);

    public ImmutableHashSet<(int Offset, int Dimension)> CheckedDimensions { get; init; } = [];
}

// An access whose array and index an earlier access already checked cannot
// fail its checks again: without exception handlers, a failed check leaves the
// method. Within a block, an access repeats an earlier one while neither its
// array nor its index slot has been stored. Across blocks, a slot that is never
// stored or exposed keeps the reference an earlier access checked for null, so
// an access in a block dominated by one holding such an access skips its null
// check, and one whose index slot is stable too skips both checks when a
// dominating block accessed the same element. A slot stored once, in an entry
// block nothing returns to and before any read of it, holds one value too, as
// the locals field load reuse fills do. Field accesses take part as accesses
// of their object: an instance method that reads several fields of `this`
// checks it once. A rectangular array's dimensions share their bounds between
// accesses, so within a block an access skips the check of each dimension
// whose index an earlier access to the same array checked against it, as
// `a[j, k] = a[i, k] + a[j, k]` checks k once and j once.
internal sealed class RepeatedArrayAccessPlanner(IControlFlowDominanceAnalyzer dominance) : IRepeatedArrayAccessPlanner
{
    private readonly IControlFlowDominanceAnalyzer _dominance = dominance ??
        throw new ArgumentNullException(nameof(dominance));

    public RepeatedArrayAccessPlan Plan(ValidatedControlFlowGraph controlFlow)
    {
        ArgumentNullException.ThrowIfNull(controlFlow);
        var graph = controlFlow.Graph;
        if (!graph.MethodBody.ExceptionRegions.IsEmpty) return RepeatedArrayAccessPlan.Empty;
        var exposed = ExposedSlots(graph.MethodBody);
        var stable = StableSlots(graph, exposed);
        var checkedAccesses = ImmutableHashSet.CreateBuilder<int>();
        var nonNull = ImmutableHashSet.CreateBuilder<int>();
        var dimensions = ImmutableHashSet.CreateBuilder<(int Offset, int Dimension)>();
        var unproven = new List<(int Block, int Offset, Slot Array)>();
        var accessedBlocks = new Dictionary<Slot, HashSet<int>>();
        var unprovenPairs = new List<(int Block, int Offset, Slot Array, Origin Index)>();
        var pairBlocks = new Dictionary<(Slot Array, Origin Index), HashSet<int>>();
        foreach (var block in graph.Blocks.Where(block => graph.ReachableBlocks.Contains(block.Index)))
        {
            var origins = new List<Origin>();
            var checkedReferences = new HashSet<Slot>();
            var checkedPairs = new HashSet<(Slot Array, Origin Index)>();
            var checkedDimensions = new HashSet<(Slot Array, int Dimension, Origin Index)>();
            foreach (var instruction in block.Instructions)
            {
                if (!controlFlow.InstructionEntryStacks.TryGetValue(instruction.Offset, out var stack))
                {
                    origins.Clear();
                    continue;
                }
                if (origins.Count != stack.Length)
                {
                    origins.Clear();
                    origins.AddRange(Enumerable.Repeat(Origin.Unknown, stack.Length));
                }
                var consumed = instruction.Operation switch
                {
                    CilOperation.LoadArrayElement or CilOperation.LoadArrayElementReference or
                        CilOperation.LoadArrayElementAddress => 2,
                    CilOperation.StoreArrayElement or CilOperation.StoreArrayElementReference => 3,
                    _ => 0,
                };
                // A slot exposed by address can change without a store instruction.
                if (consumed != 0 && origins[^consumed].Slot is { } array && !exposed.Contains(array) &&
                    origins[^(consumed - 1)] is var index && !(index.Slot is { } indexSlot && exposed.Contains(indexSlot)) &&
                    stack[stack.Length - consumed + 1] == CliValueKind.I4)
                {
                    var stablePair = index.Known && stable.Contains(array) && (index.Slot is null || stable.Contains(index.Slot.Value));
                    if (index.Known && checkedPairs.Contains((array, index)))
                    {
                        checkedAccesses.Add(instruction.Offset);
                    }
                    else
                    {
                        Dereference(array, instruction.Offset, block.Index, checkedReferences.Contains(array));
                        if (stablePair) unprovenPairs.Add((block.Index, instruction.Offset, array, index));
                    }
                    if (stablePair)
                    {
                        if (!pairBlocks.TryGetValue((array, index), out var blocks)) pairBlocks[(array, index)] = blocks = [];
                        blocks.Add(block.Index);
                    }
                    checkedReferences.Add(array);
                    if (index.Known) checkedPairs.Add((array, index));
                }
                // The array beneath one index for each dimension, and the value for a store.
                if (Rank(instruction) is { } rank &&
                    (instruction.Operation == CilOperation.StoreRectangularArrayElement ? rank + 2 : rank + 1) is var taken &&
                    stack.Length >= taken && origins[^taken].Slot is { } rectangular && !exposed.Contains(rectangular))
                {
                    var first = stack.Length - taken + 1;
                    var usable = new List<(int Dimension, Origin Index)>();
                    for (var dimension = 0; dimension < rank; dimension++)
                    {
                        var position = origins[first + dimension];
                        if (!position.Known || position.Slot is { } positionSlot && exposed.Contains(positionSlot) ||
                            stack[first + dimension] != CliValueKind.I4)
                        {
                            continue;
                        }
                        if (checkedDimensions.Contains((rectangular, dimension, position))) dimensions.Add((instruction.Offset, dimension));
                        usable.Add((dimension, position));
                    }
                    Dereference(rectangular, instruction.Offset, block.Index, checkedReferences.Contains(rectangular));
                    checkedReferences.Add(rectangular);
                    foreach (var (dimension, position) in usable) checkedDimensions.Add((rectangular, dimension, position));
                }
                // An object's field access, the object beneath the value for a store.
                var receiver = instruction.Operation switch
                {
                    CilOperation.LoadField or CilOperation.LoadFieldAddress => 1,
                    CilOperation.StoreField => 2,
                    _ => 0,
                };
                if (receiver != 0 && stack.Length >= receiver && stack[^receiver] == CliValueKind.ManagedReference &&
                    origins[^receiver].Slot is { } owner && !exposed.Contains(owner))
                {
                    Dereference(owner, instruction.Offset, block.Index, checkedReferences.Contains(owner));
                    checkedReferences.Add(owner);
                }
                if (Stored(instruction) is { } stored)
                {
                    checkedReferences.Remove(stored);
                    checkedPairs.RemoveWhere(pair => pair.Array == stored || Nullable.Equals(pair.Index.Slot, stored));
                    checkedDimensions.RemoveWhere(entry => entry.Array == stored || Nullable.Equals(entry.Index.Slot, stored));
                }
                Track(instruction, origins);
            }
        }
        if (unproven.Count != 0 || unprovenPairs.Count != 0)
        {
            var dominators = _dominance.Analyze(graph, graph.Entry.Index, graph.ReachableBlocks);
            // A proven element is also proven non-null.
            foreach (var (block, offset, array, index) in unprovenPairs)
            {
                if (pairBlocks[(array, index)].Any(other => other != block && dominators[block].Contains(other)))
                {
                    checkedAccesses.Add(offset);
                }
            }
            foreach (var (block, offset, array) in unproven)
            {
                if (!checkedAccesses.Contains(offset) &&
                    accessedBlocks[array].Any(other => other != block && dominators[block].Contains(other)))
                {
                    nonNull.Add(offset);
                }
            }
        }
        return new(checkedAccesses.ToImmutable(), nonNull.ToImmutable()) { CheckedDimensions = dimensions.ToImmutable() };

        // An access whose reference this block already checked skips its null
        // check; one of a stable slot may yet be dominated by another block's.
        void Dereference(Slot slot, int offset, int block, bool checkedHere)
        {
            if (checkedHere)
            {
                nonNull.Add(offset);
            }
            else if (stable.Contains(slot))
            {
                unproven.Add((block, offset, slot));
            }
            if (stable.Contains(slot))
            {
                if (!accessedBlocks.TryGetValue(slot, out var blocks)) accessedBlocks[slot] = blocks = [];
                blocks.Add(block);
            }
        }
    }

    private static HashSet<Slot> ExposedSlots(CilMethodBody body) =>
    [
        .. body.Instructions
            .Where(static instruction => instruction.Operation is CilOperation.LoadLocalAddress or CilOperation.LoadArgumentAddress)
            .Select(static instruction => new Slot(instruction.Operation == CilOperation.LoadArgumentAddress,
                ((CilOperand.Index)instruction.Operand).Value)),
    ];

    // Loaded slots that hold one value for the whole method: never exposed by
    // address, and never stored after entry or stored once, in an entry block no
    // branch returns to, before the block reads it. Accesses decide which hold
    // references.
    private static HashSet<Slot> StableSlots(ControlFlowGraph graph, HashSet<Slot> exposed)
    {
        var body = graph.MethodBody;
        var slots = body.Instructions.Select(Loaded).OfType<Slot>()
            .Where(slot => !exposed.Contains(slot))
            .ToHashSet();
        var stores = body.Instructions.Select(Stored).OfType<Slot>().GroupBy(static slot => slot)
            .ToDictionary(static group => group.Key, static group => group.Count());
        var entry = graph.Entry;
        var reentered = !graph.Predecessors[entry.Index].IsEmpty;
        var storedOnEntry = new HashSet<Slot>();
        var readOnEntry = new HashSet<Slot>();
        foreach (var instruction in entry.Instructions)
        {
            if (Loaded(instruction) is { } read && !storedOnEntry.Contains(read)) readOnEntry.Add(read);
            if (Stored(instruction) is { } written) storedOnEntry.Add(written);
        }
        foreach (var (slot, count) in stores)
        {
            if (count != 1 || reentered || !storedOnEntry.Contains(slot) || readOnEntry.Contains(slot)) slots.Remove(slot);
        }
        return slots;
    }

    // The rank of a rectangular access of two or more dimensions.
    private static int? Rank(CilInstruction instruction) =>
        instruction is
        {
            Operation: CilOperation.LoadRectangularArrayElement or CilOperation.StoreRectangularArrayElement or
                CilOperation.LoadRectangularArrayElementAddress,
            Operand: CilOperand.TypeIdentity { Value: { Shape: CliTypeShape.Array, ArrayRank: >= 2 and var rank } },
        }
            ? rank
            : null;

    private static Slot? Loaded(CilInstruction instruction) => instruction switch
    {
        { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index local } => new(false, local.Value),
        { Operation: CilOperation.LoadArgument, Operand: CilOperand.Index argument } => new(true, argument.Value),
        _ => null,
    };

    private static Slot? Stored(CilInstruction instruction) => instruction switch
    {
        { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index local } => new(false, local.Value),
        { Operation: CilOperation.StoreArgument, Operand: CilOperand.Index argument } => new(true, argument.Value),
        _ => null,
    };


    // A local or argument plus a constant offset, or a constant.
    private readonly record struct Origin(Slot? Slot, int Offset, bool Constant)
    {
        public static Origin Unknown => default;
        public bool Known => Slot is not null || Constant;
    }

    private static void Track(CilInstruction instruction, List<Origin> origins)
    {
        switch (instruction.Operation)
        {
            case CilOperation.LoadLocal or CilOperation.LoadArgument:
                origins.Add(new(Loaded(instruction), 0, false));
                return;
            case CilOperation.StoreLocal or CilOperation.StoreArgument:
                Consume(origins, 1, false);
                var stored = Stored(instruction);
                // A value already on the stack still holds the old one.
                for (var i = 0; i < origins.Count; i++)
                    if (Nullable.Equals(origins[i].Slot, stored)) origins[i] = Origin.Unknown;
                return;
            case CilOperation.LoadInt32 when instruction.Operand is CilOperand.ConstantI4 constant:
                origins.Add(new(null, constant.Value, true));
                return;
            case CilOperation.Add or CilOperation.Subtract when origins.Count >= 2 &&
                origins[^2] is { Slot: not null } left && origins[^1] is { Constant: true } right:
                origins.RemoveRange(origins.Count - 2, 2);
                var sum = instruction.Operation == CilOperation.Add
                    ? (long)left.Offset + right.Offset
                    : (long)left.Offset - right.Offset;
                origins.Add(sum is >= int.MinValue and <= int.MaxValue ? left with { Offset = (int)sum } : Origin.Unknown);
                return;
            case CilOperation.Duplicate:
                origins.Add(origins[^1]);
                return;
            case CilOperation.Nop or CilOperation.Readonly or CilOperation.Volatile or CilOperation.Unaligned:
                return;
            case CilOperation.Negate or CilOperation.OnesComplement:
                Consume(origins, 1, true);
                return;
            case CilOperation.LoadArrayElement or CilOperation.LoadArrayElementReference or
                CilOperation.LoadArrayElementAddress:
            case >= CilOperation.Add and <= CilOperation.RemainderUnsigned:
            case >= CilOperation.CompareEqual and <= CilOperation.CompareLessThanUnsigned:
                Consume(origins, 2, true);
                return;
            case CilOperation.StoreArrayElement or CilOperation.StoreArrayElementReference:
                Consume(origins, 3, false);
                return;
            case CilOperation.LoadRectangularArrayElement or CilOperation.LoadRectangularArrayElementAddress
                when Rank(instruction) is { } loadedRank:
                Consume(origins, loadedRank + 1, true);
                return;
            case CilOperation.StoreRectangularArrayElement when Rank(instruction) is { } storedRank:
                Consume(origins, storedRank + 2, false);
                return;
            case CilOperation.LoadArrayLength or CilOperation.LoadField or CilOperation.LoadFieldAddress or
                CilOperation.LoadObject:
            case >= CilOperation.ConvertInt32 and <= CilOperation.ConvertNumeric:
                Consume(origins, 1, true);
                return;
            case CilOperation.Pop or CilOperation.StoreStaticField:
                Consume(origins, 1, false);
                return;
            case CilOperation.StoreField or CilOperation.StoreObject:
                Consume(origins, 2, false);
                return;
            case >= CilOperation.LoadInt32 and <= CilOperation.LoadFieldToken:
            case CilOperation.LoadLocalAddress or CilOperation.LoadArgumentAddress or CilOperation.LoadStaticField or
                CilOperation.LoadStaticFieldAddress or CilOperation.SizeOf:
                origins.Add(Origin.Unknown);
                return;
            default:
                // A call or branch ends what the tracked stack can say.
                origins.Clear();
                return;
        }
    }

    private static void Consume(List<Origin> origins, int count, bool produces)
    {
        origins.RemoveRange(origins.Count - count, count);
        if (produces) origins.Add(Origin.Unknown);
    }
}
