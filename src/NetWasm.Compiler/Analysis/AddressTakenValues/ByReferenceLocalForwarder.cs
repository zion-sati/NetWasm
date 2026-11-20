// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.AddressTakenValues;

internal interface IByReferenceLocalForwarder
{
    CilMethodBody Rewrite(CilMethodBody body);
}

// A by-reference local that holds a System.Numerics vector variable's address, as `ref var r = ref v` and an
// inlined call's spill of a receiver or a ref argument leave it, is that address: each load of it becomes the
// variable's ldloca or ldarga, and the store goes. The address of a variable never changes, so the loads read what
// they read before; the vector's lane and whole accesses through it can then become accesses to its value. The local
// is stored once, from the address just before the store with no other path into the store, and loaded only after
// it; a pinned or addressed local keeps its storage. Other variables keep their by-reference locals: one that stays
// in memory would compute its address at every use instead of loading it once.
internal sealed class ByReferenceLocalForwarder(bool enabled) : IByReferenceLocalForwarder
{
    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!enabled) return body;
        var instructions = body.Instructions;
        var candidates = new Dictionary<int, (List<int> Stores, List<int> Loads, bool Addressed)>();
        for (var index = 0; index < instructions.Length; index++)
        {
            if (instructions[index] is not
                {
                    Operation: CilOperation.StoreLocal or CilOperation.LoadLocal or CilOperation.LoadLocalAddress,
                    Operand: CilOperand.Index { Value: var local },
                } instruction ||
                (uint)local >= (uint)body.LocalSignatureTypes.Length ||
                body.LocalSignatureTypes[local].Shape != CliTypeShape.ManagedByReference ||
                body.PinnedLocals.Contains(local))
            {
                continue;
            }
            if (!candidates.TryGetValue(local, out var uses))
            {
                uses = ([], [], false);
            }
            if (instruction.Operation == CilOperation.StoreLocal) uses.Stores.Add(index);
            else if (instruction.Operation == CilOperation.LoadLocal) uses.Loads.Add(index);
            else uses.Addressed = true;
            candidates[local] = uses;
        }
        if (candidates.Count == 0) return body;
        var entries = InstructionEntries.Of(body);
        var rewritten = instructions.ToBuilder();
        var changed = false;
        foreach (var (_, (stores, loads, addressed)) in candidates)
        {
            if (addressed || stores.Count != 1 || stores[0] == 0) continue;
            var store = stores[0];
            var address = instructions[store - 1];
            if (address.Operation is not (CilOperation.LoadLocalAddress or CilOperation.LoadArgumentAddress) ||
                !IsVector(body, address) ||
                entries.Contains(instructions[store].Offset) || loads.Any(load => load < store))
            {
                continue;
            }
            rewritten[store - 1] = address with { Operation = CilOperation.Nop, Operand = new CilOperand.None() };
            rewritten[store] = instructions[store] with { Operation = CilOperation.Nop, Operand = new CilOperand.None() };
            foreach (var load in loads)
            {
                rewritten[load] = instructions[load] with { Operation = address.Operation, Operand = address.Operand };
            }
            changed = true;
        }
        return changed ? body with { Instructions = rewritten.ToImmutable() } : body;
    }

    // Whether the ldloca or ldarga addresses a System.Numerics vector local or argument.
    private static bool IsVector(CilMethodBody body, CilInstruction address)
    {
        var index = ((CilOperand.Index)address.Operand).Value;
        // A local a rewrite added without its signature type is not known to be one.
        if (address.Operation == CilOperation.LoadLocalAddress)
        {
            return (uint)index < (uint)body.LocalSignatureTypes.Length &&
                VectorLaneFields.Lanes(body.LocalSignatureTypes[index]) is not null;
        }
        var parameter = body.Method.IsStatic ? index : index - 1;
        var parameters = (body.MethodInstance?.Signature ?? body.Method.Signature).ParameterSignatureTypes;
        return (uint)parameter < (uint)parameters.Length && VectorLaneFields.Lanes(parameters[parameter]) is not null;
    }
}
