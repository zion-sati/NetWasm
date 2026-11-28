// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.StructResults;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

// Struct temporaries built field by field only to be passed whole to a call that takes them by their fields
// (the scalar replacer's whole load of a split local into an argument): `ldloca T; initobj`, then `ldloca T; <value>;
// stfld f` per field, then `ldloc T` as the argument and T's last use, all in one block, with nothing that could collect
// or take the lanes before the call. T lives in lanes of its own; the call pushes them.
internal sealed record ArgumentLanePlan(
    ImmutableDictionary<int, StructResultShape> Locals,
    ImmutableHashSet<int> Addresses,
    ImmutableDictionary<int, (int Local, int Lane)> Stores,
    ImmutableDictionary<int, int> Clears,
    ImmutableDictionary<int, int> WholeLoads,
    ImmutableDictionary<(int Call, int Argument), int> Arguments)
{
    public static ArgumentLanePlan Empty { get; } = new(ImmutableDictionary<int, StructResultShape>.Empty, [],
        ImmutableDictionary<int, (int, int)>.Empty, ImmutableDictionary<int, int>.Empty, ImmutableDictionary<int, int>.Empty,
        ImmutableDictionary<(int, int), int>.Empty);
}

internal static class ArgumentLanes
{
    public static ArgumentLanePlan Find(StructuredMethod method, Func<CliTypeIdentity, StructResultShape?> classify,
        Func<CilInstruction, int?> fieldOffset, Func<MethodInstanceModel, bool> callsBody, Func<int, bool> captured)
    {
        var locals = ImmutableDictionary.CreateBuilder<int, StructResultShape>();
        var addresses = ImmutableHashSet.CreateBuilder<int>();
        var stores = ImmutableDictionary.CreateBuilder<int, (int, int)>();
        var clears = ImmutableDictionary.CreateBuilder<int, int>();
        var wholeLoads = ImmutableDictionary.CreateBuilder<int, int>();
        var arguments = ImmutableDictionary.CreateBuilder<(int, int), int>();
        var referenceBlocks = new Dictionary<int, HashSet<StructuredBlockId>>();
        foreach (var block in method.Blocks.Values)
        {
            foreach (var instruction in block.Instructions)
            {
                if (instruction.Operation is CilOperation.LoadLocal or CilOperation.StoreLocal or CilOperation.LoadLocalAddress &&
                    instruction.Operand is CilOperand.Index { Value: var local })
                {
                    if (!referenceBlocks.TryGetValue(local, out var blocks)) referenceBlocks[local] = blocks = [];
                    blocks.Add(block.Id);
                }
            }
        }
        foreach (var block in method.Blocks.Values)
        {
            var instructions = block.Instructions;
            for (var call = 0; call < instructions.Length; call++)
            {
                if (instructions[call] is not
                    {
                        Operation: CilOperation.Call or CilOperation.CallVirtual,
                        Operand: CilOperand.MethodInstance { Value: var called }
                    } ||
                    !callsBody(called))
                {
                    continue;
                }
                var parameters = called.Signature.ParameterSignatureTypes;
                var receiver = !called.Definition.IsStatic && instructions[call].Operation != CilOperation.NewObject ? 1 : 0;
                var count = parameters.Length + receiver;
                // Each argument's producer, found by walking back through instructions whose stack effects are known.
                for (var argument = receiver; argument < count; argument++)
                {
                    if (Producer(instructions, call, count - 1 - argument) is not { } at) continue;
                    var producer = instructions[at];
                    if (producer is { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var local } } &&
                        classify(parameters[argument - receiver]) is { } shape &&
                        method.Header.LocalSignatureTypes[local].CanonicalName == parameters[argument - receiver].CanonicalName &&
                        !locals.ContainsKey(local) && !captured(local) &&
                        referenceBlocks.TryGetValue(local, out var blocks) && blocks.Count == 1 && blocks.Contains(block.Id) &&
                        Plan(instructions, local, at, call, shape, fieldOffset) is { } planned)
                    {
                        locals[local] = shape;
                        addresses.UnionWith(planned.Addresses);
                        foreach (var (store, lane) in planned.Stores) stores[store] = (local, lane);
                        clears[planned.Clear] = local;
                        wholeLoads[producer.Offset] = local;
                        arguments[(instructions[call].Offset, argument)] = local;
                    }
                }
            }
        }
        return locals.Count == 0
            ? ArgumentLanePlan.Empty
            : new(locals.ToImmutable(), addresses.ToImmutable(), stores.ToImmutable(), clears.ToImmutable(),
                wholeLoads.ToImmutable(), arguments.ToImmutable());
    }

    // The instruction that pushed the value `depth` slots below the top of the stack the call finds, walking back through
    // instructions whose stack effects are known and that neither branch nor call, or null.
    private static int? Producer(ImmutableArray<CilInstruction> instructions, int call, int depth)
    {
        var need = depth;
        for (var index = call - 1; index >= 0; index--)
        {
            var instruction = instructions[index];
            if (!CallResultLanes.KeepsLanes(instruction.Operation) || CilStackEffects.Of(instruction) is not { } effect ||
                instruction.Operation == CilOperation.Duplicate)
            {
                return null;
            }
            if (need < effect.Pushes) return effect.Pushes == 1 ? index : null;
            need += effect.Pops - effect.Pushes;
        }
        return null;
    }

    private static bool IsSinglePush(CilOperation operation) => operation is CilOperation.LoadLocal or
        CilOperation.LoadArgument or CilOperation.LoadLocalAddress or CilOperation.LoadArgumentAddress or
        CilOperation.LoadInt32 or CilOperation.LoadInt64 or CilOperation.LoadFloat32 or CilOperation.LoadFloat64 or
        CilOperation.LoadNull;

    private static (List<int> Addresses, List<(int, int)> Stores, int Clear)? Plan(ImmutableArray<CilInstruction> instructions,
        int local, int load, int call, StructResultShape shape, Func<CilInstruction, int?> fieldOffset)
    {
        var first = -1;
        for (var index = 0; index < load; index++)
        {
            if (Refers(instructions[index], local))
            {
                first = index;
                break;
            }
        }
        if (first < 0 || instructions[first].Operation != CilOperation.LoadLocalAddress || first + 1 >= load ||
            instructions[first + 1].Operation != CilOperation.InitializeObject)
        {
            return null;
        }
        var addresses = new List<int> { instructions[first].Offset };
        var stores = new List<(int, int)>();
        for (var index = first + 2; index < call; index++)
        {
            var instruction = instructions[index];
            if (index != load && !CallResultLanes.KeepsLanes(instruction.Operation)) return null;
            if (!Refers(instruction, local) || index == load) continue;
            if (instruction.Operation != CilOperation.LoadLocalAddress) return null;
            // The address's store, as the scalar replacer writes it: one push of the value, then the stfld.
            var store = index + 2;
            if (store >= load || !IsSinglePush(instructions[index + 1].Operation) || Refers(instructions[index + 1], local) ||
                instructions[store].Operation != CilOperation.StoreField || fieldOffset(instructions[store]) is not { } offset)
            {
                return null;
            }
            var lane = instructions[store].Operand is CilOperand.FieldInstance stored ? shape.LaneOf(offset, stored.Value.FieldType) : -1;
            if (lane < 0) return null;
            addresses.Add(instruction.Offset);
            stores.Add((instructions[store].Offset, lane));
        }
        // The call's argument is the temporary's last use: lanes have no storage a later read could find (a span built
        // for one call and passed whole to another, as IPAddress's span is after SocketAddressPal fills it).
        for (var index = call + 1; index < instructions.Length; index++)
        {
            if (Refers(instructions[index], local)) return null;
        }
        return (addresses, stores, instructions[first + 1].Offset);
    }

    private static bool Refers(CilInstruction instruction, int local) =>
        instruction.Operation is CilOperation.LoadLocal or CilOperation.StoreLocal or CilOperation.LoadLocalAddress &&
        instruction.Operand is CilOperand.Index { Value: var index } && index == local;
}
