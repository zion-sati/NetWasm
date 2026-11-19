// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Inlining;

// A method with .locals init finds every local zero where it is entered (ECMA-335 II.24.4.4). Inlined, its locals are
// its caller's, zeroed at most once on the caller's entry, and not at all when the caller skips initialization: a
// second run in a loop would see the first run's values, as RyuJIT avoids by zeroing an inlinee's locals where it is
// entered. Only the locals the callee may read before it writes them need it: one read, or whose address is taken, on
// some path from its entry before any store. An address taken for initobj, or for a constructor of the local's own type
// that receives it as `this` after only plain argument pushes, initializes the local whole, as C#'s `default(T)` and
// `new T(...)` into a local do.
internal static class InlineeLocalInitialization
{
    // The callee's locals, by index, to zero where it is entered.
    public static ImmutableArray<int> MaybeUnassigned(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!body.InitializesLocals || body.Locals.IsEmpty || body.Instructions.IsEmpty) return [];
        var instructions = body.Instructions;
        var positions = new Dictionary<int, int>();
        for (var position = 0; position < instructions.Length; position++) positions[instructions[position].Offset] = position;
        // The locals assigned on every path to each position; null where no path has arrived yet.
        var assigned = new HashSet<int>?[instructions.Length];
        assigned[0] = [];
        var pending = new Stack<int>([0]);
        var unassigned = new SortedSet<int>();
        while (pending.TryPop(out var start))
        {
            var state = new HashSet<int>(assigned[start]!);
            for (var position = start; position < instructions.Length; position++)
            {
                if (position != start)
                {
                    // Arriving at an instruction others branch to merges with what they brought.
                    if (!Merge(assigned, position, state)) break;
                    state = new HashSet<int>(assigned[position]!);
                }
                var instruction = instructions[position];
                switch (instruction)
                {
                    case { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var read } }:
                        if (!state.Contains(read)) unassigned.Add(read);
                        break;
                    case { Operation: CilOperation.LoadLocalAddress, Operand: CilOperand.Index { Value: var reached } }:
                        if (!state.Contains(reached) && !Initializes(body, position, reached)) unassigned.Add(reached);
                        state.Add(reached);
                        break;
                    case { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var stored } }:
                        state.Add(stored);
                        break;
                }
                if (instruction.Operand is CilOperand.BranchTarget target && positions.TryGetValue(target.Offset, out var taken) &&
                    Merge(assigned, taken, state))
                {
                    pending.Push(taken);
                }
                if (instruction.Operation is CilOperation.Branch or CilOperation.Return or CilOperation.Throw or
                    CilOperation.Rethrow)
                {
                    break;
                }
            }
        }
        return [.. unassigned];
    }

    // The instructions that zero the given callee locals, numbered from calleeLocalStart in the caller.
    public static IEnumerable<(CilOperation Operation, CilOperand Operand)> Zeroing(
        CilMethodBody body, IEnumerable<int> locals, int calleeLocalStart)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(locals);
        foreach (var local in locals)
        {
            var slot = new CilOperand.Index(calleeLocalStart + local);
            var store = (CilOperation.StoreLocal, (CilOperand)slot);
            switch (body.Locals[local])
            {
                case CliValueKind.ValueType or CliValueKind.V128:
                    yield return (CilOperation.LoadLocalAddress, slot);
                    yield return (CilOperation.InitializeObject, new CilOperand.TypeIdentity(body.LocalSignatureTypes[local]));
                    break;
                case CliValueKind.I4:
                    yield return (CilOperation.LoadInt32, new CilOperand.ConstantI4(0));
                    yield return store;
                    break;
                case CliValueKind.I8:
                    yield return (CilOperation.LoadInt64, new CilOperand.ConstantI8(0));
                    yield return store;
                    break;
                case CliValueKind.F4:
                    yield return (CilOperation.LoadFloat32, new CilOperand.ConstantF4(0));
                    yield return store;
                    break;
                case CliValueKind.F8:
                    yield return (CilOperation.LoadFloat64, new CilOperand.ConstantF8(0));
                    yield return store;
                    break;
                case CliValueKind.NativeInt:
                    yield return (CilOperation.LoadInt32, new CilOperand.ConstantI4(0));
                    yield return (CilOperation.ConvertNativeInt, new CilOperand.None());
                    yield return store;
                    break;
                case CliValueKind.ManagedReference:
                    yield return (CilOperation.LoadNull, new CilOperand.None());
                    yield return store;
                    break;
            }
        }
    }

    // Whether the next path from position is unconditionally `initobj` on this address, or a constructor of the local's
    // type taking it as `this` after only plain pushes of its other arguments, none of them reaching the local.
    private static bool Initializes(CilMethodBody body, int position, int local)
    {
        var instructions = body.Instructions;
        if (position + 1 < instructions.Length && instructions[position + 1].Operation == CilOperation.InitializeObject)
            return true;
        var pushed = 0;
        for (var next = position + 1; next < instructions.Length; next++)
        {
            switch (instructions[next])
            {
                case { Operation: CilOperation.Call, Operand: CilOperand.MethodInstance { Value: var method } }:
                    return method.Definition.Name == ".ctor" && !method.Definition.IsStatic &&
                        method.DeclaringType.Equals(body.LocalSignatureTypes[local]) &&
                        method.Signature.ParameterTypes.Length == pushed;
                case { Operation: CilOperation.LoadLocal or CilOperation.LoadLocalAddress, Operand: CilOperand.Index { Value: var other } }
                    when other == local:
                    return false;
                case
                {
                    Operation: CilOperation.LoadArgument or CilOperation.LoadLocal or CilOperation.LoadInt32 or
                    CilOperation.LoadInt64 or CilOperation.LoadFloat32 or CilOperation.LoadFloat64 or CilOperation.LoadNull or
                    CilOperation.LoadString or CilOperation.LoadStaticField
                }:
                    pushed++;
                    break;
                default:
                    return false;
            }
        }
        return false;
    }

    // Narrows what position has on arrival to what state also brings; whether it changed or arrived first.
    private static bool Merge(HashSet<int>?[] assigned, int position, HashSet<int> state)
    {
        if (assigned[position] is not { } existing)
        {
            assigned[position] = new HashSet<int>(state);
            return true;
        }
        var before = existing.Count;
        existing.IntersectWith(state);
        return existing.Count != before;
    }
}
