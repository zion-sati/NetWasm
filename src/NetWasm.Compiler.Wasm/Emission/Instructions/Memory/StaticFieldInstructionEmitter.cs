using NetWasm.Compiler.Wasm.Encoding;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Memory;

internal sealed class StaticFieldInstructionEmitter(
    IFieldRepository fieldRepository,
    ITargetLayout layouts,
    IStaticFieldLayoutProvider fields,
    IValueLayoutProvider values,
    IAddressInstructionEmitter addresses,
    IStaticInitializationEmitter initialization) : InstructionCommandProvider
{
    public override ImmutableArray<InstructionCommand> Commands =>
    [
        Command(CilOperation.LoadStaticField, EmitLoad),
        Command(CilOperation.LoadStaticFieldAddress, EmitLoadAddress),
        Command(CilOperation.StoreStaticField, EmitStore),
    ];

    private static InstructionCommand Command(
        CilOperation operation,
        Action<InstructionEmissionRequest, IWasmInstructionWriter, IFunctionIndexResolver> emit) => new(
        operation,
        InstructionFamily.ArraysFieldsStatics,
        emit);

    private void EmitLoad(InstructionEmissionRequest request, IWasmInstructionWriter code, IFunctionIndexResolver functionIndices)
    {
        EmitEnsureInitialized(request, code, functionIndices);
        var (fieldType, address) = Resolve(request.Instruction);
        var storage = values.GetValueLayout(fieldType);
        if (fieldType.StackKind == CliValueKind.ValueType)
        {
            var offset = request.Context.ValueLayout.TemporaryOffsets[
                request.Instruction.Offset];
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(request.Context.ValueFrame))));
            if (offset != 0)
            {
                addresses.Emit(code, offset);
                addresses.Emit(code, AddressOperation.Add);
            }
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalTee, WasmInstructionOperand.Unsigned((uint)(request.Context.ObjectTemporary))));
            addresses.Emit(code, address);
            addresses.Emit(code, storage.Size);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Prefixed, WasmInstructionOperand.PrefixedTriple(WasmOpcodes.MemoryCopy, 0, 0)));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(request.Context.ObjectTemporary))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
                request.Context,
                request.Stack.Count,
                CliValueKind.ValueType)))));
            request.Stack.Add(CliValueKind.ValueType);
            return;
        }
        addresses.Emit(code, address);
        ManagedMemoryEmitter.EmitLoadByType(
            code,
            layouts.Target,
            0,
            fieldType,
            storage.Size);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
            request.Context,
            request.Stack.Count,
            fieldType.StackKind)))));
        request.Stack.Add(fieldType.StackKind);
    }

    private void EmitStore(InstructionEmissionRequest request, IWasmInstructionWriter code, IFunctionIndexResolver functionIndices)
    {
        var slot = request.Stack.Count - 1;
        EmitEnsureInitialized(request, code, functionIndices);
        var (fieldType, address) = Resolve(request.Instruction);
        var storage = values.GetValueLayout(fieldType);
        addresses.Emit(code, address);
        if (fieldType.StackKind == CliValueKind.ValueType)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
                request.Context,
                slot,
                CliValueKind.ValueType)))));
            addresses.Emit(code, storage.Size);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Prefixed, WasmInstructionOperand.PrefixedTriple(WasmOpcodes.MemoryCopy, 0, 0)));
            request.Stack.RemoveAt(slot);
            return;
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
            request.Context,
            slot,
            fieldType.StackKind)))));
        ManagedMemoryEmitter.EmitStoreByType(
            code,
            layouts.Target,
            0,
            fieldType,
            storage.Size);
        request.Stack.RemoveAt(slot);
    }

    private void EmitLoadAddress(InstructionEmissionRequest request, IWasmInstructionWriter code, IFunctionIndexResolver functionIndices)
    {
        EmitEnsureInitialized(request, code, functionIndices);
        var (_, address) = Resolve(request.Instruction);
        addresses.Emit(code, address);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
            request.Context,
            request.Stack.Count,
            CliValueKind.ManagedAddress)))));
        request.Stack.Add(CliValueKind.ManagedAddress);
    }

    private (CliTypeIdentity Type, int Address) Resolve(CilInstruction instruction)
    {
        if (instruction.Operand is CilOperand.FieldInstance instance)
        {
            return (
                instance.Value.FieldType,
                fields.GetStaticFieldLayout(instance.Value).Address);
        }
        var key = CilOperandReader.GetEntity(instruction);
        var field = fieldRepository.GetField(key);
        return (field.SignatureType, fields.GetStaticFieldLayout(key).Address);
    }

    private void EmitEnsureInitialized(InstructionEmissionRequest request, IWasmInstructionWriter code, IFunctionIndexResolver functionIndices)
    {
        var instruction = request.Instruction;
        FieldDefinitionModel field;
        CliTypeIdentity? declaringType = null;
        if (instruction.Operand is CilOperand.FieldInstance instance)
        {
            field = instance.Value.Definition;
            declaringType = instance.Value.DeclaringType;
        }
        else
        {
            field = fieldRepository.GetField(CilOperandReader.GetEntity(instruction));
        }
        initialization.Emit(new(field.DeclaringType, declaringType,
            request.Target.ModuleData), code, functionIndices);
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
