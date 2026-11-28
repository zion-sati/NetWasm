// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.GarbageCollection;
using NetWasm.Compiler.Wasm.Emission.StructResults;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

// Calls whose value-type result, returned in results, stays in the shape's scratch locals (lanes): the call
// stores it into a local only read field by field right after, in the same block, before any other call.
internal sealed record CallResultLanePlan(
    ImmutableDictionary<int, StructResultShape> Calls,
    ImmutableHashSet<int> Stores,
    ImmutableHashSet<int> Addresses,
    ImmutableDictionary<int, (StructResultShape Shape, int Lane)> Loads,
    ImmutableHashSet<int> Locals)
{
    public static CallResultLanePlan Empty { get; } = new([], [], [], ImmutableDictionary<int, (StructResultShape, int)>.Empty, []);
}

internal static class CallResultLanes
{
    public static CallResultLanePlan Find(StructuredMethod method, MethodRootMap roots,
        Func<CliTypeIdentity, StructResultShape?> classify, Func<CilInstruction, (int Offset, CliTypeIdentity Type)?> field,
        Func<MethodInstanceModel, bool> callsBody, Func<int, bool> captured)
    {
        var calls = ImmutableDictionary.CreateBuilder<int, StructResultShape>();
        var stores = ImmutableHashSet.CreateBuilder<int>();
        var addresses = ImmutableHashSet.CreateBuilder<int>();
        var loads = ImmutableDictionary.CreateBuilder<int, (StructResultShape, int)>();
        var locals = ImmutableHashSet.CreateBuilder<int>();
        var rooted = roots.Safepoints.Values.SelectMany(point => point.Roots.Concat(point.ConstructorCallRoots))
            .Where(source => source.Kind == RootSourceKind.Local).Select(source => source.Index).ToHashSet();
        var references = new Dictionary<int, int>();
        foreach (var block in method.Blocks.Values)
        {
            foreach (var instruction in block.Instructions)
            {
                if (instruction.Operation is CilOperation.LoadLocal or CilOperation.StoreLocal or CilOperation.LoadLocalAddress &&
                    instruction.Operand is CilOperand.Index { Value: var local })
                {
                    references[local] = references.GetValueOrDefault(local) + 1;
                }
            }
        }
        foreach (var block in method.Blocks.Values)
        {
            var instructions = block.Instructions;
            for (var index = 0; index + 1 < instructions.Length; index++)
            {
                if (instructions[index] is not
                    {
                        Operation: CilOperation.Call or CilOperation.CallVirtual,
                        Operand: CilOperand.MethodInstance { Value: var called }
                    } ||
                    called.Signature.ReturnSignatureType.StackKind != CliValueKind.ValueType ||
                    // An intrinsic or an import has an emitter of its own, which may use the call's temporary.
                    !callsBody(called) ||
                    classify(called.Signature.ReturnSignatureType) is not { } shape ||
                    instructions[index + 1] is not { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var local } } ||
                    method.Header.LocalSignatureTypes[local].CanonicalName != called.Signature.ReturnSignatureType.CanonicalName ||
                    locals.Contains(local) || captured(local))
                {
                    continue;
                }
                var found = new List<(int Address, int Load, int Lane)>();
                var valid = true;
                var seen = 1;
                for (var next = index + 2; next < instructions.Length && valid; next++)
                {
                    var instruction = instructions[next];
                    if (instruction.Operand is CilOperand.Index { Value: var referenced } && referenced == local &&
                        instruction.Operation is CilOperation.LoadLocal or CilOperation.StoreLocal or CilOperation.LoadLocalAddress)
                    {
                        seen++;
                        if (instruction.Operation != CilOperation.LoadLocalAddress ||
                            AddressConsumer.Find(instructions, next) is not { } consumer ||
                            instructions[consumer].Operation != CilOperation.LoadField ||
                            field(instructions[consumer]) is not { } accessed)
                        {
                            valid = false;
                            break;
                        }
                        var lane = shape.LaneOf(accessed.Offset, accessed.Type);
                        if (lane < 0)
                        {
                            valid = false;
                            break;
                        }
                        found.Add((instruction.Offset, instructions[consumer].Offset, lane));
                    }
                    else if (found.Count == 0 || seen < references[local])
                    {
                        // Until the last read, no call may take the lanes.
                        if (!KeepsLanes(instruction.Operation))
                        {
                            valid = false;
                        }
                    }
                }
                if (!valid || found.Count == 0 || seen != references[local]) continue;
                calls[instructions[index].Offset] = shape;
                stores.Add(instructions[index + 1].Offset);
                locals.Add(local);
                foreach (var (address, load, lane) in found)
                {
                    addresses.Add(address);
                    loads[load] = (shape, lane);
                }
            }
        }
        return calls.Count == 0
            ? CallResultLanePlan.Empty
            : new(calls.ToImmutable(), stores.ToImmutable(), addresses.ToImmutable(), loads.ToImmutable(), locals.ToImmutable());
    }

    // Whether the instruction can neither collect, nor run other code that could take the lanes: no call, allocation,
    // throw or class constructor (a static field access may run one), only locals, arguments, constants, instance fields
    // and arithmetic.
    public static bool KeepsLanes(CilOperation operation) => operation is
        CilOperation.Nop or CilOperation.LoadArgument or CilOperation.LoadArgumentAddress or CilOperation.StoreArgument or
        CilOperation.LoadLocal or CilOperation.LoadLocalAddress or CilOperation.StoreLocal or CilOperation.LoadInt32 or
        CilOperation.LoadInt64 or CilOperation.LoadFloat32 or CilOperation.LoadFloat64 or CilOperation.LoadNull or
        CilOperation.Duplicate or CilOperation.Pop or CilOperation.LoadField or CilOperation.LoadFieldAddress or
        CilOperation.StoreField or CilOperation.InitializeObject or CilOperation.DefaultValue or
        CilOperation.Add or CilOperation.Subtract or CilOperation.Multiply or CilOperation.BitwiseAnd or
        CilOperation.BitwiseOr or CilOperation.BitwiseXor or CilOperation.ShiftLeft or CilOperation.ShiftRightSigned or
        CilOperation.ShiftRightUnsigned or CilOperation.Negate or CilOperation.OnesComplement or
        CilOperation.ConvertInt32 or CilOperation.ConvertInt32Unsigned or CilOperation.ConvertInt64 or
        CilOperation.ConvertInt64Unsigned or CilOperation.ConvertNativeInt or CilOperation.ConvertNativeUInt or
        CilOperation.ConvertFloat32 or CilOperation.ConvertFloat64 or CilOperation.ConvertFloatUnsigned or
        CilOperation.ConvertNumeric or CilOperation.CompareEqual or CilOperation.CompareGreaterThanSigned or
        CilOperation.CompareGreaterThanUnsigned or CilOperation.CompareLessThanSigned or
        CilOperation.CompareLessThanUnsigned or CilOperation.LoadArrayLength or CilOperation.Select;

    // Takes the shape's results off the stack into its lanes.
    public static void Take(IWasmInstructionWriter code, ImmutableArray<int> lanes)
    {
        for (var index = lanes.Length - 1; index >= 0; index--)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)lanes[index])));
        }
    }
}
