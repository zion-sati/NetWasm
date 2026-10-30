using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.AddressTakenValues;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Memory;

internal sealed class FieldInstructionEmitter(
    ITypeRepository types,
    IFieldRepository fieldRepository,
    ITargetLayout layouts,
    IInstanceFieldLayoutProvider fields,
    IImplicitExceptionEmitter exceptions,
    IAddressInstructionEmitter addresses,
    IAddressTakenAggregateProjectionEmitter aggregateProjections) :
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
        if (aggregateProjections.TryEmit(request, code))
        {
            return;
        }
        var (layout, fieldType, receiverType) = Resolve(request.Instruction);
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
        EmitNullCheckIfRequired(code, objectLocal, receiverType, request.Context.ManagedThrowFunction);
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
            layout.Size);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(request.Context, slot, fieldType.StackKind)))));
        request.Stack[slot] = fieldType.StackKind;
    }

    // A field of a vector value, such as Vector128<T>'s Vector64 halves or
    // Vector<T>'s ulongs, is a lane of the v128 value. A value-type field goes to
    // the load's temporary, whose address the stack then holds.
    private void EmitVectorFieldLoad(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        int slot,
        FieldLayout layout,
        CliTypeIdentity fieldType)
    {
        var vector = GetStackLocal(request.Context, slot, CliValueKind.V128);
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
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalTee, WasmInstructionOperand.Unsigned((uint)request.Context.ObjectTemporary)));
        Extract();
        ManagedMemoryEmitter.EmitStoreBySize(code, layouts.Target, 0, layout.Size);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)request.Context.ObjectTemporary)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)GetStackLocal(
            request.Context, slot, CliValueKind.ValueType))));
        request.Stack[slot] = CliValueKind.ValueType;
    }

    private void EmitLoadAddress(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var (layout, _, receiverType) = Resolve(request.Instruction);
        var slot = request.Stack.Count - 1;
        var objectLocal = GetStackLocal(request.Context, slot, request.Stack[slot]);
        EmitNullCheckIfRequired(code, objectLocal, receiverType, request.Context.ManagedThrowFunction);
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
        if (aggregateProjections.TryEmit(request, code))
        {
            return;
        }
        var (layout, fieldType, receiverType) = Resolve(request.Instruction);
        var objectSlot = request.Stack.Count - 2;
        var objectLocal = GetStackLocal(
            request.Context,
            objectSlot,
            request.Stack[objectSlot]);
        var valueLocal = GetStackLocal(
            request.Context,
            objectSlot + 1,
            request.Stack[objectSlot + 1]);
        EmitNullCheckIfRequired(code, objectLocal, receiverType, request.Context.ManagedThrowFunction);
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
                writer => addresses.Emit(writer, layout.Size));
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
            layout.Size);
        request.Stack.RemoveRange(objectSlot, 2);
    }

    private (FieldLayout Layout, CliTypeIdentity FieldType, CliValueKind ReceiverType)
        Resolve(CilInstruction instruction)
    {
        if (instruction.Operand is CilOperand.FieldInstance instance)
        {
            return (
                fields.GetFieldLayout(instance.Value),
                instance.Value.FieldType,
                instance.Value.DeclaringType.IsValueType
                    ? CliValueKind.ManagedAddress
                    : CliValueKind.ManagedReference);
        }
        var fieldKey = CilOperandReader.GetEntity(instruction);
        var field = fieldRepository.GetField(fieldKey);
        return (
            fields.GetFieldLayout(fieldKey),
            field.SignatureType,
            types.GetTypeDefinition(field.DeclaringType).IsValueType
                ? CliValueKind.ManagedAddress
                : CliValueKind.ManagedReference);
    }

    private void EmitNullCheckIfRequired(
        IWasmInstructionWriter code,
        int objectLocal,
        CliValueKind receiverType,
        OptionalFunctionIndex throwFunction)
    {
        if (receiverType != CliValueKind.ManagedReference)
        {
            return;
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(objectLocal))));
        addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.NullReference, throwFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
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
