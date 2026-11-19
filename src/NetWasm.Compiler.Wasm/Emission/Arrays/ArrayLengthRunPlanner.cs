// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Arrays;

internal interface IArrayLengthRunPlanner
{
    ArrayLengthRunPlan Plan(StructuredMethod method, ArrayMetadataCachingPlan cached, bool enabled);
}

// The accesses that read an SZ array's length into its slot's length local, and the later ones that take it from
// there, by offset.
internal sealed record ArrayLengthRunPlan(
    ImmutableDictionary<int, ArrayReferenceSlot> Loads,
    ImmutableDictionary<int, ArrayReferenceSlot> Reuses)
{
    public static ArrayLengthRunPlan Empty { get; } = new([], []);
}

// A bounds check or ldlen reads the array's length from its object, and neither Binaryen nor an engine drops that
// load when the same array's length was read a moment before: as far as they can tell, a store into the array's
// elements in between could have changed it. A length never changes, so a run of ldlens and element accesses within
// one block to the array a local or argument holds, with no store to that slot between them, reads it once: the
// first keeps it in the slot's length local, and the others take it from there and make no null check, as the first
// made it on the same array. Accesses an earlier analysis proved in bounds read no length and stay out of runs, as do slots a
// loop's snapshot caches, slots whose address is taken, and methods with exception handlers.
internal sealed class ArrayLengthRunPlanner(IArgumentSignatureTypeResolver arguments) : IArrayLengthRunPlanner
{
    private readonly IArgumentSignatureTypeResolver _arguments = arguments ??
        throw new ArgumentNullException(nameof(arguments));

    public ArrayLengthRunPlan Plan(StructuredMethod method, ArrayMetadataCachingPlan cached, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(cached);
        if (!enabled || !method.ExceptionGroups.IsEmpty) return ArrayLengthRunPlan.Empty;
        var candidates = new HashSet<ArrayReferenceSlot>();
        var exposed = new HashSet<ArrayReferenceSlot>();
        foreach (var instruction in method.Header.Instructions)
        {
            if (instruction.Operation is CilOperation.LoadLocalAddress or CilOperation.LoadArgumentAddress)
            {
                exposed.Add(Slot(instruction, CilOperation.LoadArgumentAddress));
            }
            else if (instruction.Operation is CilOperation.LoadLocal or CilOperation.LoadArgument &&
                Slot(instruction, CilOperation.LoadArgument) is var slot &&
                (slot.IsArgument ? _arguments.Resolve(method.Header, slot.Index) : method.Header.LocalSignatureTypes[slot.Index])
                    .Shape == CliTypeShape.SzArray)
            {
                candidates.Add(slot);
            }
        }
        candidates.ExceptWith(exposed);
        candidates.ExceptWith(cached.Slots);
        if (candidates.Count == 0) return ArrayLengthRunPlan.Empty;
        var accesses = ArrayAccessOrigins.Collect(method, candidates);
        var proof = method.Header.ArrayBoundsProof is { } candidate &&
            candidate.Describes(method.Header.Instructions, method.Header.Locals, method.Header.LocalSignatureTypes)
            ? candidate
            : null;
        var loads = ImmutableDictionary.CreateBuilder<int, ArrayReferenceSlot>();
        var reuses = ImmutableDictionary.CreateBuilder<int, ArrayReferenceSlot>();
        foreach (var block in method.Blocks.Values)
        {
            var runs = new Dictionary<ArrayReferenceSlot, List<int>>();
            foreach (var instruction in block.Instructions)
            {
                if (instruction.Operation is CilOperation.StoreLocal or CilOperation.StoreArgument)
                {
                    var stored = Slot(instruction, CilOperation.StoreArgument);
                    if (runs.Remove(stored, out var ended)) End(ended, stored);
                    continue;
                }
                if (!ReadsLength(instruction) || !accesses.TryGetValue(instruction.Offset, out var accessed) ||
                    proof is not null && proof.Contains(instruction) && proof.AccessOffsets.Contains(instruction.Offset))
                {
                    continue;
                }
                if (!runs.TryGetValue(accessed, out var run)) runs[accessed] = run = [];
                run.Add(instruction.Offset);
            }
            foreach (var (slot, run) in runs) End(run, slot);
        }
        return new(loads.ToImmutable(), reuses.ToImmutable());

        void End(List<int> run, ArrayReferenceSlot slot)
        {
            if (run.Count < 2) return;
            loads.Add(run[0], slot);
            foreach (var offset in run.Skip(1)) reuses.Add(offset, slot);
        }
    }

    // ldlen and the SZ element accesses, each of which checks its index against the length.
    private static bool ReadsLength(CilInstruction instruction) => instruction.Operation is CilOperation.LoadArrayLength or
        CilOperation.LoadArrayElement or CilOperation.LoadArrayElementReference or CilOperation.LoadArrayElementAddress or
        CilOperation.StoreArrayElement or CilOperation.StoreArrayElementReference;

    private static ArrayReferenceSlot Slot(CilInstruction instruction, CilOperation argumentOperation) =>
        new(instruction.Operation == argumentOperation, CilOperandReader.GetIndex(instruction));
}
