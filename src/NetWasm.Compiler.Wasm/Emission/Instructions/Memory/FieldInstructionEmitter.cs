using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Memory;

internal sealed class FieldInstructionEmitter(
    ITypeRepository types,
    IFieldRepository fieldRepository,
    ITargetLayout layouts,
    IInstanceFieldLayoutProvider fields,
    IImplicitExceptionEmitter exceptions,
    IAddressInstructionEmitter addresses) : InstructionCommandProvider
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
        var (layout, fieldType, receiverType) = Resolve(request.Instruction);
        var slot = request.Stack.Count - 1;
        var objectLocal = GetStackLocal(request.Context, slot, request.Stack[slot]);
        EmitNullCheckIfRequired(code, objectLocal, receiverType);
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
            addresses.Emit(code, layout.Size);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Prefixed, WasmInstructionOperand.PrefixedTriple(WasmOpcodes.MemoryCopy, 0, 0)));
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

    private void EmitLoadAddress(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var (layout, _, receiverType) = Resolve(request.Instruction);
        var slot = request.Stack.Count - 1;
        var objectLocal = GetStackLocal(request.Context, slot, request.Stack[slot]);
        EmitNullCheckIfRequired(code, objectLocal, receiverType);
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
        EmitNullCheckIfRequired(code, objectLocal, receiverType);
        if (fieldType.StackKind == CliValueKind.ValueType)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(objectLocal))));
            if (layout.Offset != 0)
            {
                addresses.Emit(code, layout.Offset);
                addresses.Emit(code, AddressOperation.Add);
            }
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(valueLocal))));
            addresses.Emit(code, layout.Size);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Prefixed, WasmInstructionOperand.PrefixedTriple(WasmOpcodes.MemoryCopy, 0, 0)));
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
        CliValueKind receiverType)
    {
        if (receiverType != CliValueKind.ManagedReference)
        {
            return;
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(objectLocal))));
        addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.NullReference);
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
