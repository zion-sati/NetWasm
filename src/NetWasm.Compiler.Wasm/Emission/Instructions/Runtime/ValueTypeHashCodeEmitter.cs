using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class ValueTypeHashCodeEmitter(
    ITargetLayout layouts,
    IValueLayoutProvider values,
    ITypeDescriptorSource descriptors,
    ITypeRepository types,
    IMethodRepository methods,
    IValueTypeEqualityFieldPlanner fields) : IValueTypeHashCodeEmitter
{
    private readonly Lazy<EntityKey> _referenceHashCode = new(
        () => ResolveReferenceHashCode(descriptors, types, methods));

    public void Emit(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        CliTypeIdentity type,
        CliValueKind receiverKind)
    {
        var receiver = request.Local(0, receiverKind);
        var output = request.Local(0, CliValueKind.I4);
        var accumulator = request.Instruction.Context.NumericTemporaryI4;
        var fieldHash = request.Instruction.Context.NumericTemporaryI4Second;
        var temporaryI8 = request.Instruction.Context.NumericTemporaryI8;
        var functionIndices = request.FunctionIndices;
        var payloadOffset = receiverKind == CliValueKind.ManagedReference
            ? WasmTargetLayout.Align(
                layouts.Target.ObjectHeaderSize,
                values.GetValueLayout(type).Alignment)
            : 0;

        WriteI32(code, 17);
        SetLocal(code, accumulator);
        foreach (var field in fields.Plan(type))
        {
            EmitFieldHash(
                code,
                receiver,
                checked(payloadOffset + field.Offset),
                field.Type,
                fieldHash,
                temporaryI8,
                functionIndices.Resolve(_referenceHashCode.Value));
            GetLocal(code, accumulator);
            WriteI32(code, 31);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Multiply));
            GetLocal(code, fieldHash);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Add));
            SetLocal(code, accumulator);
        }
        GetLocal(code, accumulator);
        SetLocal(code, output);
    }

    private void EmitFieldHash(
        IWasmInstructionWriter code,
        int receiver,
        int offset,
        CliTypeIdentity type,
        int result,
        int temporaryI8,
        int referenceHashCode)
    {
        var size = values.GetValueLayout(type).Size;
        GetLocal(code, receiver);
        ManagedMemoryEmitter.EmitLoadByType(
            code,
            layouts.Target,
            offset,
            type,
            size);
        if (!type.IsValueType)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)referenceHashCode)));
            SetLocal(code, result);
            return;
        }
        if (type.StackKind == CliValueKind.F4)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32ReinterpretF32));
            SetLocal(code, result);
            NormalizeF32(code, receiver, offset, type, result);
            return;
        }
        if (type.StackKind == CliValueKind.F8)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ReinterpretF64));
            FoldI64(code, temporaryI8);
            SetLocal(code, result);
            NormalizeF64(code, receiver, offset, type, result);
            return;
        }
        if (ManagedMemoryEmitter.GetLoadValueType(
                type,
                size) == WasmValueType.I64)
        {
            FoldI64(code, temporaryI8);
        }
        SetLocal(code, result);
    }

    private void NormalizeF32(
        IWasmInstructionWriter code,
        int receiver,
        int offset,
        CliTypeIdentity type,
        int result)
    {
        EmitLoad(code, receiver, offset, type);
        EmitLoad(code, receiver, offset, type);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.F32Equal));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        WriteI32(code, unchecked((int)0x7fc00000));
        SetLocal(code, result);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        EmitLoad(code, receiver, offset, type);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.F32Constant,
            WasmInstructionOperand.Float32(0)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.F32Equal));
        NormalizeZero(code, result);
    }

    private void NormalizeF64(
        IWasmInstructionWriter code,
        int receiver,
        int offset,
        CliTypeIdentity type,
        int result)
    {
        EmitLoad(code, receiver, offset, type);
        EmitLoad(code, receiver, offset, type);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.F64Equal));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        WriteI32(code, unchecked((int)0x7ff80000));
        SetLocal(code, result);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        EmitLoad(code, receiver, offset, type);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.F64Constant,
            WasmInstructionOperand.Float64(0)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.F64Equal));
        NormalizeZero(code, result);
    }

    private static void NormalizeZero(
        IWasmInstructionWriter code,
        int result)
    {
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        WriteI32(code, 0);
        SetLocal(code, result);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitLoad(
        IWasmInstructionWriter code,
        int receiver,
        int offset,
        CliTypeIdentity type)
    {
        GetLocal(code, receiver);
        ManagedMemoryEmitter.EmitLoadByType(
            code,
            layouts.Target,
            offset,
            type,
            values.GetValueLayout(type).Size);
    }

    private static void FoldI64(
        IWasmInstructionWriter code,
        int temporaryI8)
    {
        SetLocal(code, temporaryI8);
        GetLocal(code, temporaryI8);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32WrapI64));
        GetLocal(code, temporaryI8);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I64Constant,
            WasmInstructionOperand.Signed64(32)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ShiftRightUnsigned));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32WrapI64));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Xor));
    }

    private static EntityKey ResolveReferenceHashCode(
        ITypeDescriptorSource descriptors,
        ITypeRepository types,
        IMethodRepository methods)
    {
        var objectType = descriptors.TypeDescriptors
            .Select(descriptor => types.GetTypeDefinition(descriptor.Type))
            .Single(type => type.FullName == "System.Object");
        return objectType.Methods
            .Select(methods.GetMethod)
            .Single(method =>
                method.Name == "GetValueHashCode" &&
                method.IsStatic &&
                method.Signature.ParameterTypes.Length == 1)
            .Key;
    }

    private static void GetLocal(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)local)));

    private static void SetLocal(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)local)));

    private static void WriteI32(IWasmInstructionWriter code, int value) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(value)));
}
