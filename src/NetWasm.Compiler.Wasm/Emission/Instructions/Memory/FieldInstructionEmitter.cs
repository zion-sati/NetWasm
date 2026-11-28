// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime.SimdVectors;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.AddressTakenValues;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Memory;

internal sealed class FieldInstructionEmitter(
    IFieldRepository fieldRepository,
    ITargetLayout layouts,
    IInstanceFieldLayoutProvider fields,
    IValueLayoutProvider values,
    INullDereferenceCheckEmitter nullChecks,
    IAddressInstructionEmitter addresses,
    IAddressTakenAggregateProjectionEmitter aggregateProjections,
    IReferenceWriteBarrierEmitter writeBarrier) :
    InstructionCommandProvider
{
    public override ImmutableArray<InstructionCommand> Commands =>
    [
        Command(CilOperation.LoadField, EmitLoad),
        Command(CilOperation.LoadFieldAddress, EmitLoadAddress),
        Command(CilOperation.StoreField, EmitStore),
    ];

    private static InstructionCommand Command(
        CilOperation operation,
        Action<InstructionEmissionRequest, IWasmInstructionWriter> emit) => new(
        operation,
        InstructionFamily.ArraysFieldsStatics,
        emit);

    private void EmitLoad(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        // A returned local's field in its lane.
        if (request.Context.FieldArgumentLoads.TryGetValue(request.Instruction.Offset, out var parameterField))
        {
            var fieldReceiver = request.Stack.Count - 1;
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)parameterField.Parameter)));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned((uint)GetStackLocal(request.Context, fieldReceiver, parameterField.Kind))));
            request.Stack[fieldReceiver] = parameterField.Kind;
            return;
        }
        if (request.Context.CallResultLanes.Loads.TryGetValue(request.Instruction.Offset, out var callLane))
        {
            var laneKind = callLane.Shape.Fields[callLane.Lane].Kind;
            var laneReceiver = request.Stack.Count - 1;
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)request.Context.StructResultLocals.For(callLane.Shape)[callLane.Lane])));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned((uint)GetStackLocal(request.Context, laneReceiver, laneKind))));
            request.Stack[laneReceiver] = laneKind;
            return;
        }
        if (request.Context.ReturnLanes is { } lanes && lanes.Loads.TryGetValue(request.Instruction.Offset, out var loaded))
        {
            var kind = lanes.Shape.Fields[loaded].Kind;
            var receiver = request.Stack.Count - 1;
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)request.Context.StructResultLocals.For(lanes.Shape)[loaded])));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned((uint)GetStackLocal(request.Context, receiver, kind))));
            request.Stack[receiver] = kind;
            return;
        }
        if (aggregateProjections.TryEmit(request, code))
        {
            return;
        }
        var (layout, fieldType) = Resolve(request.Instruction);
        var slot = request.Stack.Count - 1;
        if (request.Context.TransientAggregatePromotions.Plan.Projections.TryGetValue(
                request.Instruction.Offset,
                out var projection))
        {
            var aggregate = request.Context.TransientAggregatePromotions.ProducerLocals[
                projection.ProducerOffset];
            var source = projection.ElementIndex == 0
                ? aggregate.First
                : aggregate.Second;
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)source)));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned((uint)GetStackLocal(
                    request.Context,
                    slot,
                    fieldType.StackKind))));
            request.Stack[slot] = fieldType.StackKind;
            return;
        }
        if (request.Stack[slot] == CliValueKind.V128)
        {
            EmitVectorFieldLoad(request, code, slot, layout, fieldType);
            return;
        }
        var objectLocal = GetStackLocal(request.Context, slot, request.Stack[slot]);
        nullChecks.Emit(request, code, objectLocal, request.Stack[slot]);
        if (fieldType.StackKind == CliValueKind.ValueType)
        {
            var targetOffset = request.Context.ValueLayout.TemporaryOffsets[
                request.Instruction.Offset];
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(request.Context.ValueFrame))));
            if (targetOffset != 0)
            {
                addresses.Emit(code, targetOffset);
                addresses.Emit(code, AddressOperation.Add);
            }
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalTee, WasmInstructionOperand.Unsigned((uint)(request.Context.ObjectTemporary))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(objectLocal))));
            if (layout.Offset != 0)
            {
                addresses.Emit(code, layout.Offset);
                addresses.Emit(code, AddressOperation.Add);
            }
            SmallBlockEmitter.EmitKnownSizeCopy(code, request.Context, layout.Size,
                SmallBlockEmitter.WidestChunk(values.GetValueLayout(fieldType)),
                writer => addresses.Emit(writer, layout.Size));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(request.Context.ObjectTemporary))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
                request.Context,
                slot,
                CliValueKind.ValueType)))));
            request.Stack[slot] = fieldType.StackKind;
            return;
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(objectLocal))));
        ManagedMemoryEmitter.EmitLoadByType(
            code,
            layouts.Target,
            layout.Offset,
            fieldType,
            layout.Size, scratch: request.Context.VectorScratch);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(request.Context, slot, fieldType.StackKind)))));
        request.Stack[slot] = fieldType.StackKind;
    }

    // A field of a vector value, such as Vector<T>'s ulongs, is a lane of the
    // v128 value. A value-type field goes to the load's temporary, whose address
    // the stack then holds. A vector field at the start, as Plane's Normal or
    // Vector128<T>'s lower Vector64, is the value itself: its own lanes are the
    // vector's low lanes, and the lanes above them are undefined. The upper
    // Vector64 moves down into the low lanes.
    private void EmitVectorFieldLoad(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        int slot,
        FieldLayout layout,
        CliTypeIdentity fieldType)
    {
        var vector = GetStackLocal(request.Context, slot, CliValueKind.V128);
        if (fieldType.StackKind == CliValueKind.V128)
        {
            if (layout.Offset == sizeof(long) && SimdVectorStorage.Width(fieldType) == sizeof(long))
            {
                SimdVectorCode.Get(code, vector);
                SimdVectorCode.Get(code, vector);
                SimdVectorCode.Shuffle(code, SimdByteShuffles.UpperWords);
                SimdVectorCode.Set(code, vector);
            }
            else if (layout.Offset != 0)
            {
                throw new CompilerException(new CompilerDiagnostic(
                    DiagnosticCode.UnsupportedCil, "a vector's vector field does not start the vector"));
            }
            return;
        }
        var storage = fieldType.StackStorageType ?? fieldType;
        var floating = storage.StackKind is CliValueKind.F4 or CliValueKind.F8;
        var signed = storage.CanonicalName is "primitive:i1" or "primitive:i2";
        void Extract()
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)vector)));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.SimdPrefixed,
                WasmInstructionOperand.PrefixedPair((layout.Size, floating, signed) switch
                {
                    (16, _, _) => throw new CompilerException(new CompilerDiagnostic(
                        DiagnosticCode.UnsupportedCil, "a vector's field spans the whole vector")),
                    (8, true, _) => WasmSimdOpcodes.F64x2ExtractLane,
                    (8, false, _) => WasmSimdOpcodes.I64x2ExtractLane,
                    (4, true, _) => WasmSimdOpcodes.F32x4ExtractLane,
                    (4, false, _) => WasmSimdOpcodes.I32x4ExtractLane,
                    (2, _, true) => WasmSimdOpcodes.I16x8ExtractLaneSigned,
                    (2, _, false) => WasmSimdOpcodes.I16x8ExtractLaneUnsigned,
                    (_, _, true) => WasmSimdOpcodes.I8x16ExtractLaneSigned,
                    _ => WasmSimdOpcodes.I8x16ExtractLaneUnsigned,
                }, (uint)(layout.Offset / layout.Size))));
        }

        if (fieldType.StackKind != CliValueKind.ValueType)
        {
            Extract();
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)GetStackLocal(
                request.Context, slot, fieldType.StackKind))));
            request.Stack[slot] = fieldType.StackKind;
            return;
        }

        var targetOffset = request.Context.ValueLayout.TemporaryOffsets[request.Instruction.Offset];
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)request.Context.ValueFrame)));
        if (targetOffset != 0)
        {
            addresses.Emit(code, targetOffset);
            addresses.Emit(code, AddressOperation.Add);
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)request.Context.ObjectTemporary)));
        // A struct within the vector copies out in the widest lanes its offset
        // and size allow: a Vector64 half as one 64-bit lane, Plane's Normal as
        // three 32-bit lanes.
        if (layout.Offset % sizeof(int) != 0 || layout.Size % sizeof(int) != 0)
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.UnsupportedCil, "a vector's struct field is not made of 32-bit lanes"));
        }
        var lane = layout.Offset % sizeof(long) == 0 && layout.Size % sizeof(long) == 0 ? sizeof(long) : sizeof(int);
        for (var word = 0; word < layout.Size / lane; word++)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)request.Context.ObjectTemporary)));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)vector)));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.SimdPrefixed,
                WasmInstructionOperand.PrefixedPair(
                    lane == sizeof(long) ? WasmSimdOpcodes.I64x2ExtractLane : WasmSimdOpcodes.I32x4ExtractLane,
                    (uint)(layout.Offset / lane + word))));
            ManagedMemoryEmitter.EmitStoreBySize(code, layouts.Target, word * lane, lane);
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)request.Context.ObjectTemporary)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)GetStackLocal(
            request.Context, slot, CliValueKind.ValueType))));
        request.Stack[slot] = CliValueKind.ValueType;
    }

    private void EmitLoadAddress(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var (layout, _) = Resolve(request.Instruction);
        var slot = request.Stack.Count - 1;
        var objectLocal = GetStackLocal(request.Context, slot, request.Stack[slot]);
        nullChecks.Emit(request, code, objectLocal, request.Stack[slot]);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(objectLocal))));
        if (layout.Offset != 0)
        {
            addresses.Emit(code, layout.Offset);
            addresses.Emit(code, AddressOperation.Add);
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
            request.Context,
            slot,
            CliValueKind.ManagedAddress)))));
        request.Stack[slot] = CliValueKind.ManagedAddress;
    }

    private void EmitStore(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        // A returned local's or an argument temporary's field into its lane, narrowed as a store to the
        // field would be.
        var argumentStore = request.Context.ArgumentLanes.Stores.TryGetValue(request.Instruction.Offset, out var argumentLane);
        if (argumentStore || request.Context.ReturnLanes is { } lanes && lanes.Stores.ContainsKey(request.Instruction.Offset))
        {
            var shapeStored = argumentStore ? request.Context.ArgumentLanes.Locals[argumentLane.Local] : request.Context.ReturnLanes!.Shape;
            var stored = argumentStore ? argumentLane.Lane : request.Context.ReturnLanes!.Stores[request.Instruction.Offset];
            var laneLocal = argumentStore
                ? request.Context.ArgumentLaneLocals[argumentLane.Local][stored]
                : request.Context.StructResultLocals.For(shapeStored)[stored];
            var field = shapeStored.Fields[stored];
            var value = request.Stack.Count - 1;
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)GetStackLocal(request.Context, value, request.Stack[value]))));
            if (field.Kind == CliValueKind.I4 && field.Size < 4)
            {
                // As ManagedMemoryEmitter reads the field: an enum by its storage type, a primitive by its canonical name.
                var signed = (field.Type.StackStorageType ?? field.Type).CanonicalName is "primitive:i1" or "primitive:i2";
                if (signed)
                {
                    code.Write(WasmInstruction.NoOperand(field.Size == 1 ? WasmOpcodes.I32Extend8Signed : WasmOpcodes.I32Extend16Signed));
                }
                else
                {
                    code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant,
                        WasmInstructionOperand.Signed(field.Size == 1 ? 0xFF : 0xFFFF)));
                    code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32And));
                }
            }
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)laneLocal)));
            request.Stack.RemoveRange(request.Stack.Count - 2, 2);
            return;
        }
        if (aggregateProjections.TryEmit(request, code))
        {
            return;
        }
        var (layout, fieldType) = Resolve(request.Instruction);
        var objectSlot = request.Stack.Count - 2;
        var objectLocal = GetStackLocal(
            request.Context,
            objectSlot,
            request.Stack[objectSlot]);
        var valueLocal = GetStackLocal(
            request.Context,
            objectSlot + 1,
            request.Stack[objectSlot + 1]);
        nullChecks.Emit(request, code, objectLocal, request.Stack[objectSlot]);
        if (fieldType.StackKind == CliValueKind.ValueType)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(objectLocal))));
            if (layout.Offset != 0)
            {
                addresses.Emit(code, layout.Offset);
                addresses.Emit(code, AddressOperation.Add);
            }
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(valueLocal))));
            SmallBlockEmitter.EmitKnownSizeCopy(code, request.Context, layout.Size,
                SmallBlockEmitter.WidestChunk(values.GetValueLayout(fieldType)),
                writer => addresses.Emit(writer, layout.Size));
            writeBarrier.Emit(request, code, objectLocal, fieldType);
            request.Stack.RemoveRange(objectSlot, 2);
            return;
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(objectLocal))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(valueLocal))));
        ManagedMemoryEmitter.EmitStoreByType(
            code,
            layouts.Target,
            layout.Offset,
            fieldType,
            layout.Size, scratch: request.Context.VectorScratch);
        writeBarrier.Emit(request, code, objectLocal, fieldType);
        request.Stack.RemoveRange(objectSlot, 2);
    }

    private (FieldLayout Layout, CliTypeIdentity FieldType) Resolve(CilInstruction instruction)
    {
        if (instruction.Operand is CilOperand.FieldInstance instance)
        {
            return (fields.GetFieldLayout(instance.Value), instance.Value.FieldType);
        }
        var fieldKey = CilOperandReader.GetEntity(instruction);
        return (fields.GetFieldLayout(fieldKey), fieldRepository.GetField(fieldKey).SignatureType);
    }

    private int GetStackLocal(
        MethodEmissionContext context,
        int slot,
        CliValueKind type) => WasmLocalLayoutPlanner.GetEvaluationStackLocal(
        context.StackLocals,
        slot,
        type,
        layouts.Target);
}
