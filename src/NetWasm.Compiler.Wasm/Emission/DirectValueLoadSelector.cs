using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission;

internal interface IDirectValueLoadSelector
{
    ImmutableHashSet<int> Select(StructuredMethod method);
}

internal sealed class DirectValueLoadSelector : IDirectValueLoadSelector
{
    public ImmutableHashSet<int> Select(StructuredMethod method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var instructions = method.Header.Instructions
            .Where(instruction =>
                method.InstructionEntryStacks.ContainsKey(instruction.Offset))
            .ToArray();
        var selected = ImmutableHashSet.CreateBuilder<int>();
        for (var index = 0; index + 1 < instructions.Length; index++)
        {
            var load = instructions[index];
            var next = instructions[index + 1];
            if (load.Operation is (CilOperation.LoadArgument or
                    CilOperation.LoadLocal or CilOperation.LoadArrayElement or
                    CilOperation.LoadRectangularArrayElement or CilOperation.LoadObject) &&
                load.NextOffset == next.Offset &&
                next.Operation is (CilOperation.Return or
                    CilOperation.StoreLocal or CilOperation.StoreArgument))
            {
                // These adjacent consumers finish the by-value transfer before
                // another instruction can observe or mutate the source. Keep
                // the consumer's copy; the load needs no separate snapshot.
                selected.Add(load.Offset);
            }
        }
        if (method.ExceptionGroups.IsEmpty)
        {
            SelectStackTransfers(method, selected);
        }
        return selected.ToImmutable();
    }

    private static void SelectStackTransfers(
        StructuredMethod method,
        ImmutableHashSet<int>.Builder selected)
    {
        // A local or argument whose address is never taken changes only by its
        // own stores, so a call can read it in place: the callee owns no copy
        // it does not make, and one that writes its parameter copies it first.
        var addressedLocals = method.Header.Instructions
            .Where(instruction => instruction.Operation == CilOperation.LoadLocalAddress)
            .Select(CilOperandReader.GetIndex)
            .ToHashSet();
        var addressedArguments = method.Header.Instructions
            .Where(instruction => instruction.Operation == CilOperation.LoadArgumentAddress)
            .Select(CilOperandReader.GetIndex)
            .ToHashSet();
        bool Unaddressed(CilInstruction load) =>
            !(load.Operation == CilOperation.LoadLocal ? addressedLocals : addressedArguments)
                .Contains(CilOperandReader.GetIndex(load));

        foreach (var block in method.Blocks.Values)
        {
            var origins = new List<CilInstruction?>();
            int? nextOffset = null;
            foreach (var instruction in block.Instructions)
            {
                if (!method.InstructionEntryStacks.TryGetValue(instruction.Offset, out var entry))
                {
                    origins.Clear();
                    nextOffset = null;
                    continue;
                }
                if (nextOffset != instruction.Offset || origins.Count != entry.Length)
                {
                    origins.Clear();
                    origins.AddRange(Enumerable.Repeat<CilInstruction?>(null, entry.Length));
                }
                nextOffset = instruction.NextOffset;

                switch (instruction.Operation)
                {
                    case CilOperation.LoadLocal:
                    case CilOperation.LoadArgument:
                        origins.Add(instruction);
                        break;
                    case CilOperation.LoadInt32:
                    case CilOperation.LoadInt64:
                    case CilOperation.LoadFloat32:
                    case CilOperation.LoadFloat64:
                    case CilOperation.LoadNull:
                        origins.Add(null);
                        break;
                    case CilOperation.Nop:
                        break;
                    case CilOperation.StoreLocal:
                    case CilOperation.StoreArgument:
                        if (origins.Count > 0)
                        {
                            if (origins[^1] is { } origin)
                            {
                                selected.Add(origin.Offset);
                            }
                            origins.RemoveAt(origins.Count - 1);
                        }
                        var load = instruction.Operation == CilOperation.StoreLocal
                            ? CilOperation.LoadLocal : CilOperation.LoadArgument;
                        var index = CilOperandReader.GetIndex(instruction);
                        for (var slot = 0; slot < origins.Count; slot++)
                        {
                            if (origins[slot] is { } pending &&
                                pending.Operation == load &&
                                CilOperandReader.GetIndex(pending) == index)
                            {
                                origins[slot] = null;
                            }
                        }
                        break;
                    case CilOperation.LoadArrayElement:
                    case CilOperation.LoadArrayElementReference:
                        ConsumeArrayInputs(origins, 2);
                        break;
                    case CilOperation.LoadRectangularArrayElement:
                        var array = ((CilOperand.TypeIdentity)instruction.Operand).Value;
                        ConsumeArrayInputs(origins, array.ArrayRank + 1);
                        break;
                    case CilOperation.Call or CilOperation.CallVirtual or CilOperation.NewObject
                        when instruction.Operand is CilOperand.MethodInstance { Value: var called }:
                        var parameters = called.Signature.ParameterTypes.Length;
                        var consumed = parameters +
                            (instruction.Operation != CilOperation.NewObject && !called.Definition.IsStatic ? 1 : 0);
                        if (origins.Count < consumed)
                        {
                            origins.Clear();
                            break;
                        }
                        // The receiver is an address or an object; the arguments
                        // after it are passed by value.
                        for (var argument = origins.Count - parameters; argument < origins.Count; argument++)
                        {
                            if (origins[argument] is { } origin && Unaddressed(origin))
                            {
                                selected.Add(origin.Offset);
                            }
                        }
                        origins.RemoveRange(origins.Count - consumed, consumed);
                        if (instruction.Operation == CilOperation.NewObject ||
                            called.Signature.ReturnType != CliValueKind.Void)
                        {
                            origins.Add(null);
                        }
                        break;
                    default:
                        // Calls, indirect writes, escaping addresses and unknown
                        // evaluations require a snapshot. No origin crosses a
                        // block boundary or an explicit exception region.
                        origins.Clear();
                        break;
                }
            }
        }
    }

    private static void ConsumeArrayInputs(List<CilInstruction?> origins, int count)
    {
        // Array reads cannot mutate a suspended local/argument value. Their
        // existing null/bounds checks either return a value or unwind; the
        // consumer still owns its copy before any later call can mutate it.
        if (origins.Count < count)
        {
            origins.Clear();
            return;
        }
        origins.RemoveRange(origins.Count - count, count);
        origins.Add(null);
    }
}
