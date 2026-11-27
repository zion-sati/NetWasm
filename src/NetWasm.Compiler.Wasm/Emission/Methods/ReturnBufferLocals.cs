// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

using System.Collections.Immutable;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

// A value-type local a method builds and returns, kept in the caller's return buffer rather than a frame of its own: the
// local, its load before the ret, and the ret.
internal sealed record ReturnBufferLocal(int Local, int Load, int Return);

// A returned local kept in the return shape's lanes (ReturnBufferLocals.FindLanes).
internal sealed record ReturnLanePlan(ImmutableHashSet<int> Locals, ImmutableHashSet<int> WholeLoads,
    ImmutableHashSet<int> Returns, StructResults.StructResultShape Shape,
    ImmutableHashSet<int> Addresses, ImmutableDictionary<int, int> Stores, ImmutableDictionary<int, int> Loads,
    ImmutableHashSet<int> Clears);

// A method returning a value type writes it through the buffer its caller passes first. An inlined constructor builds the
// value in a local (`ldloca V; initobj`, then field stores through `ldloca V`) that `ldloc V; ret` copies out, so the
// local needs a value frame, zeroed on entry, then zeroed again by initobj. Where every reference to V lies in the
// returning block, the first is that initobj, every other `ldloca V` reaches only a field store or load or initobj, the
// only load of V is the one the ret takes, and no safepoint holds V (a collection never reads it), V lives in the buffer:
// its address is the function's first parameter, it needs no frame, and the ret copies nothing.
internal static class ReturnBufferLocals
{
    public static ReturnBufferLocal? Find(StructuredMethod method, MethodRootMap roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        if (FindShape(method) is not { } found) return null;
        // A collection never reads V.
        if (roots.Safepoints.Values.Any(point => point.Roots.Concat(point.ConstructorCallRoots)
                .Any(source => source.Kind == RootSourceKind.Local && source.Index == found.Local)))
        {
            return null;
        }
        return found;
    }

    // The local's shape alone, without asking whether a collection reads it.
    private static ReturnBufferLocal? FindShape(StructuredMethod method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var signature = method.Header.MethodInstance?.Signature ?? method.Header.Method.Signature;
        if (signature.ReturnSignatureType.StackKind != CliValueKind.ValueType) return null;
        ReturnBufferLocal? found = null;
        foreach (var block in method.Blocks.Values)
        {
            if (block.Exit is not StructuredTerminalExit { Instruction: { Operation: CilOperation.Return } ret }) continue;
            if (found is not null || block.Instructions.IsEmpty ||
                block.Instructions[^1] is not { Operation: CilOperation.LoadLocal } load ||
                !method.InstructionEntryStacks.TryGetValue(ret.Offset, out var stack) || stack is not [CliValueKind.ValueType])
            {
                return null;
            }
            var local = CilOperandReader.GetIndex(load);
            if (method.Header.LocalSignatureTypes[local].StackKind != CliValueKind.ValueType) return null;
            found = new(local, load.Offset, ret.Offset);
        }
        if (found is null) return null;
        var home = method.Blocks.Values.Single(block => block.Exit is StructuredTerminalExit { Instruction.Offset: var at } &&
            at == found.Return);
        if (method.Blocks.Values.Any(block => block != home && block.Instructions.Any(instruction => Refers(instruction, found.Local))))
            return null;
        var instructions = home.Instructions;
        var first = instructions.IndexOf(instructions.First(instruction => Refers(instruction, found.Local)));
        if (instructions[first].Operation != CilOperation.LoadLocalAddress ||
            first + 1 >= instructions.Length || instructions[first + 1].Operation != CilOperation.InitializeObject)
        {
            return null;
        }
        for (var index = first; index < instructions.Length - 1; index++)
        {
            var instruction = instructions[index];
            if (!Refers(instruction, found.Local)) continue;
            if (instruction.Operation != CilOperation.LoadLocalAddress ||
                Consumer(method, instructions, index) is not (CilOperation.StoreField or CilOperation.LoadField or
                    CilOperation.InitializeObject))
            {
                return null;
            }
        }
        return found;
    }

    // Where the method returns the value in results, each returned local of that shape lives in the return
    // shape's scratch locals (lanes): its address pushes push nothing, its field stores and loads use the lanes, its
    // initobj clears them, and the ret returns them. Each returning block is taken alone: its local is referenced only
    // there, first cleared (`ldloca V; initobj`), then only through field accesses to the shape's fields, and nothing
    // between its first reference and the ret may collect or take the lanes.
    public static ReturnLanePlan? FindLanes(StructuredMethod method, MethodRootMap roots, StructResults.StructResultShape shape,
        Func<CilInstruction, (int Offset, CliTypeIdentity Type)?> field, Func<int, bool> captured)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(field);
        var signature = method.Header.MethodInstance?.Signature ?? method.Header.Method.Signature;
        if (signature.ReturnSignatureType.StackKind != CliValueKind.ValueType) return null;
        var locals = ImmutableHashSet.CreateBuilder<int>();
        var wholeLoads = ImmutableHashSet.CreateBuilder<int>();
        var returns = ImmutableHashSet.CreateBuilder<int>();
        var addresses = ImmutableHashSet.CreateBuilder<int>();
        var stores = ImmutableDictionary.CreateBuilder<int, int>();
        var loads = ImmutableDictionary.CreateBuilder<int, int>();
        var clears = ImmutableHashSet.CreateBuilder<int>();
        foreach (var home in method.Blocks.Values)
        {
            if (home.Exit is not StructuredTerminalExit { Instruction: { Operation: CilOperation.Return } ret }) continue;
            var instructions = home.Instructions;
            if (instructions.IsEmpty || instructions[^1] is not { Operation: CilOperation.LoadLocal } load ||
                !method.InstructionEntryStacks.TryGetValue(ret.Offset, out var stack) || stack is not [CliValueKind.ValueType])
            {
                continue;
            }
            var local = CilOperandReader.GetIndex(load);
            if (method.Header.LocalSignatureTypes[local].StackKind != CliValueKind.ValueType || locals.Contains(local) || captured(local) ||
                method.Blocks.Values.Any(block => block != home && block.Instructions.Any(instruction => Refers(instruction, local))))
            {
                continue;
            }
            var first = instructions.IndexOf(instructions.First(instruction => Refers(instruction, local)));
            if (instructions[first].Operation != CilOperation.LoadLocalAddress || first + 1 >= instructions.Length ||
                instructions[first + 1].Operation != CilOperation.InitializeObject)
            {
                continue;
            }
            var blockAddresses = new List<int>();
            var blockStores = new Dictionary<int, int>();
            var blockLoads = new Dictionary<int, int>();
            var blockClears = new List<int>();
            var valid = true;
            for (var index = first; index < instructions.Length - 1 && valid; index++)
            {
                var instruction = instructions[index];
                // Nothing between may collect or take the lanes: they hold references no root map covers.
                if (!CallResultLanes.KeepsLanes(instruction.Operation))
                {
                    valid = false;
                    break;
                }
                if (!Refers(instruction, local)) continue;
                if (instruction.Operation != CilOperation.LoadLocalAddress || AddressConsumer.Find(instructions, index) is not { } at)
                {
                    valid = false;
                    break;
                }
                blockAddresses.Add(instruction.Offset);
                var taken = instructions[at];
                switch (taken.Operation)
                {
                    case CilOperation.InitializeObject:
                        blockClears.Add(taken.Offset);
                        break;
                    case CilOperation.StoreField or CilOperation.LoadField:
                        var lane = -1;
                        if (field(taken) is { } accessed) lane = shape.LaneOf(accessed.Offset, accessed.Type);
                        if (lane < 0)
                        {
                            valid = false;
                            break;
                        }
                        (taken.Operation == CilOperation.StoreField ? blockStores : blockLoads)[taken.Offset] = lane;
                        break;
                    default:
                        valid = false;
                        break;
                }
            }
            if (!valid) continue;
            locals.Add(local);
            wholeLoads.Add(load.Offset);
            returns.Add(ret.Offset);
            addresses.UnionWith(blockAddresses);
            foreach (var (at, lane) in blockStores) stores[at] = lane;
            foreach (var (at, lane) in blockLoads) loads[at] = lane;
            clears.UnionWith(blockClears);
        }
        return locals.Count == 0 ? null : new(locals.ToImmutable(), wholeLoads.ToImmutable(), returns.ToImmutable(), shape,
            addresses.ToImmutable(), stores.ToImmutable(), loads.ToImmutable(), clears.ToImmutable());
    }

    private static bool Refers(CilInstruction instruction, int local) =>
        instruction.Operation is CilOperation.LoadLocal or CilOperation.StoreLocal or CilOperation.LoadLocalAddress &&
        CilOperandReader.GetIndex(instruction) == local;

    // The instruction that pops what the one at index pushed: the first after it whose exit height falls to its entry's.
    private static CilOperation? Consumer(StructuredMethod method, System.Collections.Immutable.ImmutableArray<CilInstruction> instructions, int index)
    {
        if (!method.InstructionEntryStacks.TryGetValue(instructions[index].Offset, out var entry)) return null;
        for (var next = index + 1; next < instructions.Length; next++)
        {
            var after = next + 1 < instructions.Length &&
                method.InstructionEntryStacks.TryGetValue(instructions[next + 1].Offset, out var stack)
                    ? stack.Length : -1;
            if (after < 0) return null;
            if (after <= entry.Length) return instructions[next].Operation;
        }
        return null;
    }
}
