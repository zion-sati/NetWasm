// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.AddressTakenValues;

internal sealed class AddressTakenAggregateProjectionEmitter(
    ITargetLayout layouts) : IAddressTakenAggregateProjectionEmitter
{
    private readonly ITargetLayout _layouts = layouts ??
        throw new ArgumentNullException(nameof(layouts));

    public bool TryEmit(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(code);

        if (request.Context.AddressTakenAggregatePromotions.Plan.Projections
            .TryGetValue(request.Instruction.Offset, out var localProjection))
        {
            EmitLocalProjection(request, code, localProjection);
            return true;
        }
        if (!request.Context.ManagedByRefAggregatePromotions.Plan.Projections
            .TryGetValue(request.Instruction.Offset, out var byRefProjection))
        {
            return false;
        }

        EmitManagedByRefProjection(request, code, byRefProjection);
        return true;
    }

    private void EmitLocalProjection(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        AddressTakenAggregateProjection projection)
    {
        var localPlan = request.Context.AddressTakenAggregatePromotions.Plan
            .PromotedLocals[projection.LocalIndex];
        var storageLocals = request.Context.AddressTakenAggregatePromotions.StorageLocals[
            projection.LocalIndex];
        switch (request.Instruction.Operation)
        {
            case CilOperation.LoadField:
                EmitLoad(request, code, projection, localPlan, storageLocals);
                return;
            case CilOperation.StoreField:
                EmitStore(request, code, projection, localPlan, storageLocals);
                return;
            default:
                throw new InvalidOperationException(
                    "An aggregate projection must be a field load or store.");
        }
    }

    private void EmitManagedByRefProjection(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        ManagedByRefAggregateProjection projection)
    {
        var parameter = request.Context.ManagedByRefAggregatePromotions.Plan
            .PromotedParameters[projection.ParameterIndex];
        var storageLocals = request.Context.ManagedByRefAggregatePromotions.StorageLocals[
            projection.ParameterIndex];
        switch (request.Instruction.Operation)
        {
            case CilOperation.LoadField:
            case CilOperation.LoadObject:
                EmitManagedByRefLoad(
                    request,
                    code,
                    projection,
                    parameter.StorageSlots,
                    storageLocals);
                return;
            case CilOperation.StoreField:
            case CilOperation.StoreObject:
                EmitManagedByRefStore(
                    request,
                    code,
                    projection,
                    parameter.StorageSlots,
                    storageLocals);
                return;
            default:
                throw new InvalidOperationException(
                    "A managed-byref aggregate projection must load or store a field value.");
        }
    }

    private void EmitManagedByRefLoad(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        ManagedByRefAggregateProjection projection,
        ImmutableArray<AddressTakenAggregateStorageSlot> storage,
        ImmutableArray<int> storageLocals)
    {
        var receiverSlot = request.Stack.Count - 1;
        EmitLaneLoad(
            code,
            projection.ByteOffset,
            projection.Size,
            projection.StackKind,
            storage,
            storageLocals);
        WriteLocalSet(code, GetStackLocal(request, receiverSlot, projection.StackKind));
        request.Stack[receiverSlot] = projection.StackKind;
    }

    private void EmitManagedByRefStore(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        ManagedByRefAggregateProjection projection,
        ImmutableArray<AddressTakenAggregateStorageSlot> storage,
        ImmutableArray<int> storageLocals)
    {
        var receiverSlot = request.Stack.Count - 2;
        var receiverLocal = GetStackLocal(
            request,
            receiverSlot,
            request.Stack[receiverSlot]);
        var valueLocal = GetStackLocal(
            request,
            receiverSlot + 1,
            projection.StackKind);

        WriteLocalGet(code, receiverLocal);
        WriteLocalGet(code, valueLocal);
        ManagedMemoryEmitter.EmitStoreBySize(
            code,
            _layouts.Target,
            projection.AddressAlreadyProjected ? 0 : projection.ByteOffset,
            projection.Size);
        EmitLaneStore(
            code,
            projection.ByteOffset,
            projection.Size,
            projection.StackKind,
            storage,
            storageLocals,
            valueLocal);
        request.Stack.RemoveRange(receiverSlot, 2);
    }

    private void EmitLoad(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        AddressTakenAggregateProjection projection,
        AddressTakenAggregateLocalPlan localPlan,
        System.Collections.Immutable.ImmutableArray<int> storageLocals)
    {
        var receiverSlot = request.Stack.Count - 1;
        var targetLocal = GetStackLocal(
            request,
            receiverSlot,
            projection.StackKind);
        EmitLaneLoad(
            code,
            projection.ByteOffset,
            projection.Size,
            projection.StackKind,
            localPlan.StorageSlots,
            storageLocals);
        WriteLocalSet(code, targetLocal);
        request.Stack[receiverSlot] = projection.StackKind;
    }

    private void EmitStore(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        AddressTakenAggregateProjection projection,
        AddressTakenAggregateLocalPlan localPlan,
        System.Collections.Immutable.ImmutableArray<int> storageLocals)
    {
        var receiverSlot = request.Stack.Count - 2;
        var valueLocal = WasmLocalLayoutPlanner.GetEvaluationStackLocal(
            request.Context.StackLocals,
            receiverSlot + 1,
            projection.StackKind,
            _layouts.Target);
        EmitLaneStore(
            code,
            projection.ByteOffset,
            projection.Size,
            projection.StackKind,
            localPlan.StorageSlots,
            storageLocals,
            valueLocal);
        request.Stack.RemoveRange(receiverSlot, 2);
    }

    private static void EmitLaneLoad(
        IWasmInstructionWriter code,
        int byteOffset,
        int size,
        CliValueKind stackKind,
        ImmutableArray<AddressTakenAggregateStorageSlot> storage,
        ImmutableArray<int> storageLocals)
    {
        var directIndex = FindDirectStorage(storage, byteOffset, size, stackKind);
        if (directIndex >= 0)
        {
            WriteLocalGet(code, storageLocals[directIndex]);
            return;
        }

        var lowIndex = FindI4Storage(storage, byteOffset);
        var highIndex = FindI4Storage(storage, byteOffset + sizeof(int));
        WriteLocalGet(code, storageLocals[lowIndex]);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
        WriteLocalGet(code, storageLocals[highIndex]);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I64Constant,
            WasmInstructionOperand.Signed64(32)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ShiftLeft));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Or));
    }

    private static void EmitLaneStore(
        IWasmInstructionWriter code,
        int byteOffset,
        int size,
        CliValueKind stackKind,
        ImmutableArray<AddressTakenAggregateStorageSlot> storage,
        ImmutableArray<int> storageLocals,
        int valueLocal)
    {
        var directIndex = FindDirectStorage(storage, byteOffset, size, stackKind);
        WriteLocalGet(code, valueLocal);
        if (directIndex >= 0)
        {
            WriteLocalSet(code, storageLocals[directIndex]);
            return;
        }

        var lowIndex = FindI4Storage(storage, byteOffset);
        var highIndex = FindI4Storage(storage, byteOffset + sizeof(int));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32WrapI64));
        WriteLocalSet(code, storageLocals[lowIndex]);
        WriteLocalGet(code, valueLocal);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I64Constant,
            WasmInstructionOperand.Signed64(32)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ShiftRightUnsigned));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32WrapI64));
        WriteLocalSet(code, storageLocals[highIndex]);
    }

    private static int FindDirectStorage(
        ImmutableArray<AddressTakenAggregateStorageSlot> storage,
        int byteOffset,
        int size,
        CliValueKind stackKind)
    {
        for (var index = 0; index < storage.Length; index++)
        {
            var slot = storage[index];
            if (slot.ByteOffset == byteOffset &&
                slot.Size == size &&
                slot.StackKind == stackKind)
            {
                return index;
            }
        }
        return -1;
    }

    private static int FindI4Storage(
        ImmutableArray<AddressTakenAggregateStorageSlot> storage,
        int byteOffset)
    {
        for (var index = 0; index < storage.Length; index++)
        {
            var slot = storage[index];
            if (slot.ByteOffset == byteOffset &&
                slot.StackKind == CliValueKind.I4)
            {
                return index;
            }
        }
        throw new InvalidOperationException(
            "An aggregate projection has no compatible storage slot.");
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
