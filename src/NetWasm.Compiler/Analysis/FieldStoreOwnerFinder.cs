// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using Slot = (bool IsArgument, int Index);

namespace NetWasm.Compiler.Analysis;

internal interface IFieldStoreOwnerFinder
{
    // The local or argument whose object each instance field store writes to, by the store's instruction index.
    // Instructions at the offsets in blockStarts start blocks.
    IReadOnlyDictionary<int, Slot> Find(ImmutableArray<CilInstruction> instructions, HashSet<int> blockStarts);
}

// A store's object is a slot's when an ldloc or ldarg in the store's own block pushed it, directly or as the first
// copy of a dup that follows it, and nothing stored to that slot before the store took it:
// `this.length = this.length + 1` and `node.Count++` write to the slot's object. An object that came from anywhere
// else, or from a slot stored in between, has no owner.
internal sealed class FieldStoreOwnerFinder(IStackValueConsumerFinder consumers) : IFieldStoreOwnerFinder
{
    private readonly IStackValueConsumerFinder _consumers = consumers ?? throw new ArgumentNullException(nameof(consumers));

    public IReadOnlyDictionary<int, Slot> Find(ImmutableArray<CilInstruction> instructions, HashSet<int> blockStarts)
    {
        ArgumentNullException.ThrowIfNull(blockStarts);
        var owners = new Dictionary<int, Slot>();
        for (var index = 0; index < instructions.Length; index++)
        {
            if (instructions[index] is not
                {
                    Operation: CilOperation.LoadLocal or CilOperation.LoadArgument, Operand: CilOperand.Index loaded,
                } instruction ||
                _consumers.Find(instructions, blockStarts, index) is not { } taken ||
                (instructions[taken].Operation == CilOperation.Duplicate
                    ? _consumers.Find(instructions, blockStarts, taken, above: 1)
                    : taken) is not { } consumer ||
                instructions[consumer].Operation != CilOperation.StoreField)
            {
                continue;
            }
            var slot = new Slot(instruction.Operation == CilOperation.LoadArgument, loaded.Value);
            if (!StoredBetween(instructions, index, consumer, slot)) owners[consumer] = slot;
        }
        return owners;
    }

    private static bool StoredBetween(ImmutableArray<CilInstruction> instructions, int from, int to, Slot slot)
    {
        for (var index = from + 1; index < to; index++)
        {
            if (instructions[index] is
                {
                    Operation: CilOperation.StoreLocal or CilOperation.StoreArgument, Operand: CilOperand.Index stored,
                } store &&
                new Slot(store.Operation == CilOperation.StoreArgument, stored.Value) == slot)
            {
                return true;
            }
        }
        return false;
    }
}
