using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Arrays;

internal sealed class ArrayMetadataCachingPlanner(
    IArgumentSignatureTypeResolver arguments) : IArrayMetadataCachingPlanner
{
    private readonly IArgumentSignatureTypeResolver _arguments = arguments ??
        throw new ArgumentNullException(nameof(arguments));

    public ArrayMetadataCachingPlan Plan(StructuredMethod method, bool enabled, bool hoistNullChecks = false)
    {
        ArgumentNullException.ThrowIfNull(method);
        if (!enabled || !method.ExceptionGroups.IsEmpty)
        {
            return ArrayMetadataCachingPlan.Empty;
        }

        var loopOffsets = new HashSet<int>();
        var loopEntries = new Dictionary<StructuredBlockId, HashSet<int>>();
        var preTested = new HashSet<StructuredBlockId>();
        var nestedLoops = new HashSet<StructuredBlockId>();
        CollectLoopOffsets(method.Body, method, false, false, loopOffsets, loopEntries, preTested, nestedLoops);
        if (loopOffsets.Count == 0)
        {
            return ArrayMetadataCachingPlan.Empty;
        }

        var candidates = new HashSet<ArrayReferenceSlot>();
        foreach (var instruction in method.Header.Instructions)
        {
            if (instruction.Operation is not (CilOperation.LoadLocal or CilOperation.LoadArgument))
            {
                continue;
            }
            var slot = new ArrayReferenceSlot(
                instruction.Operation == CilOperation.LoadArgument,
                CilOperandReader.GetIndex(instruction));
            var type = slot.IsArgument
                ? _arguments.Resolve(method.Header, slot.Index)
                : method.Header.LocalSignatureTypes[slot.Index];
            if (type.Shape == CliTypeShape.SzArray)
            {
                candidates.Add(slot);
            }
        }
        foreach (var instruction in method.Header.Instructions)
        {
            if (instruction.Operation is CilOperation.LoadLocalAddress or CilOperation.LoadArgumentAddress ||
                loopOffsets.Contains(instruction.Offset) &&
                instruction.Operation is CilOperation.StoreLocal or CilOperation.StoreArgument)
            {
                candidates.Remove(new(
                    instruction.Operation is CilOperation.LoadArgumentAddress or CilOperation.StoreArgument,
                    CilOperandReader.GetIndex(instruction)));
            }
        }

        var accesses = new Dictionary<int, ArrayReferenceSlot>();
        foreach (var block in method.Blocks.Values)
        {
            var origins = new List<ArrayReferenceSlot?>();
            foreach (var instruction in block.Instructions)
            {
                if (!method.InstructionEntryStacks.TryGetValue(instruction.Offset, out var stack))
                {
                    origins.Clear();
                    continue;
                }
                if (origins.Count != stack.Length)
                {
                    origins.Clear();
                    origins.AddRange(Enumerable.Repeat<ArrayReferenceSlot?>(null, stack.Length));
                }
                Track(instruction, origins, candidates, accesses);
            }
        }

        var selected = accesses.Where(pair => loopOffsets.Contains(pair.Key))
            .Select(pair => pair.Value).ToImmutableHashSet();
        if (selected.IsEmpty)
        {
            return ArrayMetadataCachingPlan.Empty;
        }
        var selectedAccesses = accesses.Where(pair => selected.Contains(pair.Value)).ToImmutableDictionary();
        var entryChecks = hoistNullChecks
            ? EntryNullChecks(method, preTested, selectedAccesses)
            : [];
        var proof = method.Header.ArrayBoundsProof is { } candidate &&
            candidate.Describes(method.Header.Instructions, method.Header.Locals, method.Header.LocalSignatureTypes)
            ? candidate
            : null;
        // Method entry snapshots an argument, which holds while it is never stored.
        var unstoredArguments = selected.Where(slot => slot.IsArgument).ToHashSet();
        foreach (var instruction in method.Header.Instructions)
        {
            if (instruction.Operation == CilOperation.StoreArgument)
                unstoredArguments.Remove(new(true, CilOperandReader.GetIndex(instruction)));
        }
        return new(
            [.. selected.OrderBy(slot => slot.IsArgument).ThenBy(slot => slot.Index)],
            selectedAccesses)
        {
            // Candidates are never stored inside a loop, so the snapshot an
            // enclosing loop's entry takes holds for every loop inside it.
            LoopEntries = loopEntries.Where(pair => !nestedLoops.Contains(pair.Key)).ToImmutableDictionary(
                pair => pair.Key,
                pair => accesses.Where(access => pair.Value.Contains(access.Key) && selected.Contains(access.Value) &&
                        !unstoredArguments.Contains(access.Value))
                    .Select(access => access.Value).Distinct()
                    .OrderBy(slot => slot.IsArgument).ThenBy(slot => slot.Index).ToImmutableArray()),
            // An earlier access already proves some of these arrays non-null.
            EntryNullChecks = entryChecks
                .Select(pair => KeyValuePair.Create(pair.Key, pair.Value
                    .Where(check => proof?.ProvesNonNull(check.Offset) != true)
                    .Select(check => check.Slot).ToImmutableArray()))
                .Where(pair => !pair.Value.IsEmpty)
                .ToImmutableDictionary(),
            // Candidates are never stored inside a loop, so a slot checked or
            // proven non-null on entry stays non-null for every access in that loop.
            CheckedAccesses = [.. entryChecks.SelectMany(entry => selectedAccesses
                .Where(access => loopEntries[entry.Key].Contains(access.Key) &&
                    entry.Value.Any(check => check.Slot == access.Value))
                .Select(access => access.Key))],
        };
    }

    // Instructions with no effect a caller could observe once a null check
    // ahead of them throws: the method has no handlers, so a store to a local
    // is lost with the frame.
    private static readonly FrozenSet<CilOperation> Unobservable = new[]
    {
        CilOperation.Nop, CilOperation.LoadLocal, CilOperation.LoadArgument, CilOperation.StoreLocal,
        CilOperation.StoreArgument, CilOperation.LoadInt32, CilOperation.LoadInt64, CilOperation.LoadFloat32,
        CilOperation.LoadFloat64, CilOperation.LoadNull, CilOperation.Duplicate, CilOperation.Pop,
        CilOperation.Add, CilOperation.Subtract, CilOperation.Multiply, CilOperation.BitwiseAnd,
        CilOperation.BitwiseOr, CilOperation.BitwiseXor, CilOperation.ShiftLeft, CilOperation.ShiftRightSigned,
        CilOperation.ShiftRightUnsigned, CilOperation.Negate, CilOperation.OnesComplement,
        CilOperation.CompareEqual, CilOperation.CompareGreaterThanSigned, CilOperation.CompareGreaterThanUnsigned,
        CilOperation.CompareLessThanSigned, CilOperation.CompareLessThanUnsigned, CilOperation.ConvertInt32,
        CilOperation.ConvertInt32Unsigned, CilOperation.ConvertInt64, CilOperation.ConvertInt64Unsigned,
        CilOperation.ConvertNativeInt, CilOperation.ConvertNativeUInt,
    }.ToFrozenSet();

    // A pre-test loop evaluates its condition first on entry. When the
    // condition's first effect is the null check of a cached array, the loop
    // entry can make that check instead: it throws exactly when the first
    // evaluation would. A length read has no other effect, so the next one
    // can move too; an element access may throw something else first.
    private static ImmutableDictionary<StructuredBlockId, ImmutableArray<(ArrayReferenceSlot Slot, int Offset)>> EntryNullChecks(
        StructuredMethod method,
        HashSet<StructuredBlockId> preTested,
        ImmutableDictionary<int, ArrayReferenceSlot> accesses)
    {
        var checks = ImmutableDictionary.CreateBuilder<StructuredBlockId, ImmutableArray<(ArrayReferenceSlot Slot, int Offset)>>();
        foreach (var condition in preTested)
        {
            // Each array with the offset of its first access.
            var slots = ImmutableArray.CreateBuilder<(ArrayReferenceSlot Slot, int Offset)>();
            foreach (var instruction in method.Blocks[condition].Instructions)
            {
                if (accesses.TryGetValue(instruction.Offset, out var slot))
                {
                    if (!slots.Any(check => check.Slot == slot)) slots.Add((slot, instruction.Offset));
                    if (instruction.Operation == CilOperation.LoadArrayLength) continue;
                    break;
                }
                if (!Unobservable.Contains(instruction.Operation)) break;
            }
            if (slots.Count != 0) checks.Add(condition, slots.ToImmutable());
        }
        return checks.ToImmutable();
    }

    private static void Track(
        CilInstruction instruction,
        List<ArrayReferenceSlot?> origins,
        HashSet<ArrayReferenceSlot> candidates,
        Dictionary<int, ArrayReferenceSlot> accesses)
    {
        var operation = instruction.Operation;
        switch (operation)
        {
            case CilOperation.LoadLocal:
            case CilOperation.LoadArgument:
                var slot = new ArrayReferenceSlot(
                    operation == CilOperation.LoadArgument, CilOperandReader.GetIndex(instruction));
                origins.Add(candidates.Contains(slot) ? slot : null);
                return;
            case CilOperation.StoreLocal:
            case CilOperation.StoreArgument:
                var assigned = new ArrayReferenceSlot(
                    operation == CilOperation.StoreArgument, CilOperandReader.GetIndex(instruction));
                Consume(origins, 1, false);
                // A reference already on the stack still denotes the old array.
                for (var index = 0; index < origins.Count; index++)
                {
                    if (origins[index] == assigned) origins[index] = null;
                }
                return;
            case CilOperation.Duplicate:
                origins.Add(origins[^1]);
                return;
            case CilOperation.Nop:
            case CilOperation.Readonly:
            case CilOperation.Volatile:
            case CilOperation.Unaligned:
                return;
            case CilOperation.LoadArrayLength:
                RecordAccess(1);
                Consume(origins, 1, true);
                return;
            case CilOperation.LoadArrayElement:
            case CilOperation.LoadArrayElementReference:
            case CilOperation.LoadArrayElementAddress:
                RecordAccess(2);
                Consume(origins, 2, true);
                return;
            case CilOperation.StoreArrayElement:
            case CilOperation.StoreArrayElementReference:
                RecordAccess(3);
                Consume(origins, 3, false);
                return;
            case CilOperation.Pop:
            case CilOperation.StoreStaticField:
                Consume(origins, 1, false);
                return;
            case CilOperation.StoreField:
            case CilOperation.StoreObject:
                Consume(origins, 2, false);
                return;
            case CilOperation.LoadField:
            case CilOperation.LoadFieldAddress:
            case CilOperation.LoadObject:
            case CilOperation.Negate:
            case CilOperation.OnesComplement:
            case >= CilOperation.ConvertInt32 and <= CilOperation.ConvertNumeric:
                Consume(origins, 1, true);
                return;
            case >= CilOperation.Add and <= CilOperation.RemainderUnsigned:
            case >= CilOperation.CompareEqual and <= CilOperation.CompareLessThanUnsigned:
                Consume(origins, 2, true);
                return;
            case >= CilOperation.LoadInt32 and <= CilOperation.LoadFieldToken:
            case CilOperation.LoadLocalAddress:
            case CilOperation.LoadArgumentAddress:
            case CilOperation.LoadStaticField:
            case CilOperation.LoadStaticFieldAddress:
            case CilOperation.SizeOf:
                origins.Add(null);
                return;
            default:
                // Unknown stack effects cannot establish an array origin.
                origins.Clear();
                return;
        }

        void RecordAccess(int consumed)
        {
            if (origins.Count >= consumed && origins[^consumed] is { } origin)
            {
                accesses.Add(instruction.Offset, origin);
            }
        }
    }

    private static void Consume(List<ArrayReferenceSlot?> origins, int count, bool produces)
    {
        origins.RemoveRange(origins.Count - count, count);
        if (produces) origins.Add(null);
    }

    // `enclosed` is whether a loop with an entry encloses the sequence; loops
    // inside one are collected into `nestedLoops`.
    private static void CollectLoopOffsets(
        StructuredSequence sequence,
        StructuredMethod method,
        bool repeating,
        bool enclosed,
        HashSet<int> offsets,
        Dictionary<StructuredBlockId, HashSet<int>> loopEntries,
        HashSet<StructuredBlockId> preTested,
        HashSet<StructuredBlockId> nestedLoops)
    {
        foreach (var region in sequence.Regions)
        {
            switch (region)
            {
                case StructuredCode code:
                    Add(code.Occurrence, repeating);
                    break;
                case StructuredIf conditional:
                    Add(conditional.Condition, repeating);
                    CollectLoopOffsets(conditional.WhenTrue, method, repeating, enclosed, offsets, loopEntries, preTested, nestedLoops);
                    CollectLoopOffsets(conditional.WhenFalse, method, repeating, enclosed, offsets, loopEntries, preTested, nestedLoops);
                    break;
                case StructuredLoop { Condition: { } condition } loop:
                    preTested.Add(condition.Block);
                    AddLoop(condition, loop.Body, loop.ContinueBody);
                    CollectLoopOffsets(loop.ExitBody, method, repeating, enclosed, offsets, loopEntries, preTested, nestedLoops);
                    break;
                case StructuredPostTestLoop loop:
                    AddLoop(loop.Condition, loop.Body, loop.ContinueBody);
                    CollectLoopOffsets(loop.ExitBody, method, repeating, enclosed, offsets, loopEntries, preTested, nestedLoops);
                    break;
                case StructuredLoop loop:
                    // Like a dispatcher, an unconditional loop has no entry
                    // condition to snapshot metadata before; its body repeats.
                    CollectLoopOffsets(loop.Body, method, true, enclosed, offsets, loopEntries, preTested, nestedLoops);
                    break;
                case StructuredLabeledBlock labeled:
                    CollectLoopOffsets(labeled.Body, method, repeating, enclosed, offsets, loopEntries, preTested, nestedLoops);
                    break;
                case StructuredLabeledLoop labeled:
                    // Like an unconditional loop, its body repeats.
                    CollectLoopOffsets(labeled.Body, method, true, enclosed, offsets, loopEntries, preTested, nestedLoops);
                    break;
                case StructuredSequence nested:
                    CollectLoopOffsets(nested, method, repeating, enclosed, offsets, loopEntries, preTested, nestedLoops);
                    break;
                case StructuredDispatcher dispatcher:
                    foreach (var block in dispatcher.Blocks) Add(block.Occurrence, true);
                    foreach (var exit in dispatcher.Exits)
                        CollectLoopOffsets(exit.Body, method, repeating, enclosed, offsets, loopEntries, preTested, nestedLoops);
                    break;
            }
        }

        void Add(StructuredBlockOccurrence occurrence, bool inLoop)
        {
            if (inLoop)
                offsets.UnionWith(method.Blocks[occurrence.Block].Instructions.Select(value => value.Offset));
        }

        void AddLoop(
            StructuredBlockOccurrence condition,
            StructuredSequence body,
            StructuredSequence continuation)
        {
            if (enclosed) nestedLoops.Add(condition.Block);
            var current = method.Blocks[condition.Block].Instructions.Select(value => value.Offset).ToHashSet();
            CollectLoopOffsets(body, method, true, true, current, loopEntries, preTested, nestedLoops);
            CollectLoopOffsets(continuation, method, true, true, current, loopEntries, preTested, nestedLoops);
            offsets.UnionWith(current);
            if (loopEntries.TryGetValue(condition.Block, out var existing)) existing.UnionWith(current);
            else loopEntries.Add(condition.Block, current);
        }
    }
}
