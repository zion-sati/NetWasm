// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Arrays;

// The slot whose array each array access reads, by the access's offset: an ldloc or ldarg of one of the candidate
// slots, followed along its block's stack while nothing stores to the slot.
internal static class ArrayAccessOrigins
{
    public static Dictionary<int, ArrayReferenceSlot> Collect(StructuredMethod method, HashSet<ArrayReferenceSlot> candidates)
    {
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
        return accesses;
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
            // The array, then an index for each dimension.
            case CilOperation.LoadRectangularArrayElement:
            case CilOperation.LoadRectangularArrayElementAddress:
                RecordAccess(Rank() + 1);
                Consume(origins, Rank() + 1, true);
                return;
            case CilOperation.StoreRectangularArrayElement:
                RecordAccess(Rank() + 2);
                Consume(origins, Rank() + 2, false);
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

        int Rank() => ((CilOperand.TypeIdentity)instruction.Operand).Value.ArrayRank;

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
}
