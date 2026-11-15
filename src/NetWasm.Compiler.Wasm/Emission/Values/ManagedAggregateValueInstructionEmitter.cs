// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Values;

internal sealed class ManagedAggregateValueInstructionEmitter(
    ITargetLayout layouts,
    IValueFrameAddressEmitter valueFrameAddresses) :
    IManagedAggregateValueInstructionEmitter
{
    private readonly ITargetLayout _layouts = layouts ??
        throw new ArgumentNullException(nameof(layouts));
    private readonly IValueFrameAddressEmitter _valueFrameAddresses =
        valueFrameAddresses ?? throw new ArgumentNullException(nameof(valueFrameAddresses));

    public bool TryEmit(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(code);

        FlushMaterializations(request, code);
        if (!request.Context.ManagedAggregateValues.Plan.Accesses.TryGetValue(
                request.Instruction.Offset,
                out var access))
        {
            return false;
        }
        var kind = access.Access.StackKind;
        var lanes = access.Reads.IsEmpty ? access.Writes : access.Reads;
        if (!access.Access.LaneOffsets.IsDefaultOrEmpty)
        {
            EmitLaneAccess(request, code, access, lanes);
            return true;
        }
        if ((access.Access.Size, kind) is not
                ((sizeof(int), CliValueKind.I4) or (sizeof(long), CliValueKind.I8)) ||
            lanes.Length != access.Access.Size / sizeof(int) ||
            (!access.Reads.IsEmpty && !access.Writes.IsEmpty))
        {
            throw new InvalidOperationException(
                "A selected aggregate access is outside the supported integer lane shape.");
        }

        if (!access.Reads.IsEmpty)
        {
            var receiver = request.Stack.Count - 1;
            WriteLocalGet(code, request.Context.ManagedAggregateValues.LaneLocals[lanes[0].Lane]);
            if (kind == CliValueKind.I8)
            {
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
                WriteLocalGet(code, request.Context.ManagedAggregateValues.LaneLocals[lanes[1].Lane]);
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(32)));
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ShiftLeft));
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Or));
            }
            WriteLocalSet(code, GetStackLocal(request, receiver, kind));
            request.Stack[receiver] = kind;
            return true;
        }

        var destination = request.Stack.Count - 2;
        var valueLocal = GetStackLocal(request, destination + 1, kind);
        WriteLocalGet(code, valueLocal);
        if (kind == CliValueKind.I8)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32WrapI64));
        }
        WriteLocalSet(
            code,
            request.Context.ManagedAggregateValues.LaneLocals[lanes[0].Lane]);
        if (kind == CliValueKind.I8)
        {
            WriteLocalGet(code, valueLocal);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(32)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ShiftRightUnsigned));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32WrapI64));
            WriteLocalSet(code, request.Context.ManagedAggregateValues.LaneLocals[lanes[1].Lane]);
        }
        request.Stack.RemoveRange(destination, 2);
        return true;
    }

    // A reference or byref field is one lane of its own kind; initobj zeroes
    // every lane and consumes the address.
    private void EmitLaneAccess(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        ManagedAggregateSsaAccess access,
        ImmutableArray<ManagedAggregateSsaLaneValue> lanes)
    {
        var values = request.Context.ManagedAggregateValues;
        if (access.Access.StackKind == CliValueKind.ValueType)
        {
            foreach (var lane in lanes)
            {
                var shape = values.Plan.Storages[lane.Lane.Storage].Lane(lane.Lane.ByteOffset);
                code.Write(shape.Size == sizeof(long)
                    ? WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(0))
                    : WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)));
                WriteLocalSet(code, values.LaneLocals[lane.Lane]);
            }
            request.Stack.RemoveAt(request.Stack.Count - 1);
            return;
        }
        var kind = access.Access.StackKind;
        if (!access.Reads.IsEmpty)
        {
            var receiver = request.Stack.Count - 1;
            WriteLocalGet(code, values.LaneLocals[lanes[0].Lane]);
            WriteLocalSet(code, GetStackLocal(request, receiver, kind));
            request.Stack[receiver] = kind;
            return;
        }
        var destination = request.Stack.Count - 2;
        WriteLocalGet(code, GetStackLocal(request, destination + 1, kind));
        WriteLocalSet(code, values.LaneLocals[lanes[0].Lane]);
        request.Stack.RemoveRange(destination, 2);
    }

    private void FlushMaterializations(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code)
    {
        foreach (var materialization in request.Context.ManagedAggregateValues.Plan
                     .Materializations.Where(materialization =>
                         materialization.Materialization.InstructionOffset ==
                         request.Instruction.Offset))
        {
            var storage = materialization.Materialization.Storage;
            var offsets = storage.Kind == ManagedAggregateStorageKind.Argument
                ? request.Context.ValueLayout.ArgumentOffsets
                : request.Context.ValueLayout.LocalOffsets;
            if (!offsets.TryGetValue(
                    storage.Index,
                    out var storageOffset))
            {
                throw new InvalidOperationException(
                    "A selected aggregate materialization has no value-frame backing.");
            }
            foreach (var lane in materialization.Values
                         .OrderBy(value => value.Lane.ByteOffset))
            {
                _valueFrameAddresses.Emit(
                    code,
                    request.Context,
                    checked(storageOffset + lane.Lane.ByteOffset));
                WriteLocalGet(
                    code,
                    request.Context.ManagedAggregateValues.LaneLocals[lane.Lane]);
                ManagedMemoryEmitter.EmitStoreBySize(
                    code,
                    _layouts.Target,
                    0,
                    request.Context.ManagedAggregateValues.Plan.Storages[storage].Lane(lane.Lane.ByteOffset).Size);
            }
        }
    }

    private int GetStackLocal(
        InstructionEmissionRequest request,
        int slot,
        CliValueKind type) => WasmLocalLayoutPlanner.GetEvaluationStackLocal(
        request.Context.StackLocals,
        slot,
        type,
        _layouts.Target);

    private static void WriteLocalGet(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)local)));

    private static void WriteLocalSet(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)local)));
}
