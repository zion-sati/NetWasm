using System;
using System.Linq;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class EnumEqualsEmitter(
    IEnumStorageResolver storages,
    IEnumNullCheckEmitter nullChecks,
    IEnumValuePairEmitter values,
    IReferenceComparisonEmitter references,
    ITargetLayout layouts) : IEnumEqualsEmitter
{
    public void EmitEquals(
        IWasmInstructionWriter code,
        CliValueKind leftKind,
        CliTypeIdentity? constrainedType,
        int left,
        int right,
        int result,
        int temporaryReference,
        int temporaryI4)
    {
        if (leftKind == CliValueKind.ManagedAddress)
        {
            EmitConstrained(
                code,
                constrainedType ?? throw new InvalidOperationException(
                    "constrained enum equality requires its closed receiver type"),
                left,
                right,
                result,
                temporaryReference,
                temporaryI4);
            return;
        }
        nullChecks.Emit(code, left);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(temporaryI4))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(right))));
        references.Emit(code, ReferenceComparison.EqualZero);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(left))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(2, (uint)(0))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(right))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(2, (uint)(0))));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        foreach (var storage in storages.Resolve())
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(left))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(2, (uint)(0))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(storage.Descriptor.TypeId)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            values.Emit(code, storage, left, right);
            if (storage.Layout.Size == sizeof(long)) code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Equal));
            else code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(temporaryI4))));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(temporaryI4))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(result))));
    }

    private void EmitConstrained(
        IWasmInstructionWriter code,
        CliTypeIdentity constrainedType,
        int left,
        int right,
        int result,
        int temporaryReference,
        int temporaryI4)
    {
        var storage = storages.Resolve().SingleOrDefault(candidate =>
            candidate.EnumType.Equals(constrainedType));
        if (storage is null)
        {
            throw new InvalidOperationException(
                $"no reachable enum descriptor exists for '{constrainedType.CanonicalName}'");
        }

        WriteLocal(code, WasmOpcodes.LocalGet, left);
        WriteLocal(code, WasmOpcodes.LocalSet, temporaryReference);
        WriteI32(code, 0);
        WriteLocal(code, WasmOpcodes.LocalSet, temporaryI4);
        WriteLocal(code, WasmOpcodes.LocalGet, right);
        references.Emit(code, ReferenceComparison.EqualZero);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        WriteLocal(code, WasmOpcodes.LocalGet, right);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(2, 0)));
        WriteI32(code, storage.Descriptor.TypeId);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        WriteLocal(code, WasmOpcodes.LocalGet, temporaryReference);
        ManagedMemoryEmitter.EmitLoadByType(
            code,
            layouts.Target,
            0,
            storage.UnderlyingType,
            storage.Layout.Size);
        WriteLocal(code, WasmOpcodes.LocalGet, right);
        ManagedMemoryEmitter.EmitLoadByType(
            code,
            layouts.Target,
            storage.PayloadOffset,
            storage.UnderlyingType,
            storage.Layout.Size);
        code.Write(WasmInstruction.NoOperand(
            storage.Layout.Size == sizeof(long)
                ? WasmOpcodes.I64Equal
                : WasmOpcodes.I32Equal));
        WriteLocal(code, WasmOpcodes.LocalSet, temporaryI4);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        WriteLocal(code, WasmOpcodes.LocalGet, temporaryI4);
        WriteLocal(code, WasmOpcodes.LocalSet, result);
    }

    private static void WriteI32(IWasmInstructionWriter code, int value) => code.Write(
        WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(value)));

    private static void WriteLocal(
        IWasmInstructionWriter code,
        byte opcode,
        int local) => code.Write(WasmInstruction.WithOperand(
        opcode,
        WasmInstructionOperand.Unsigned((uint)local)));
}
