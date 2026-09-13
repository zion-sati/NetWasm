using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class EnumHasFlagEmitter(
    IEnumStorageResolver storages,
    IEnumNullCheckEmitter nullChecks,
    IEnumValuePairEmitter values,
    IImplicitExceptionEmitter exceptions,
    ITargetLayout layouts) : IEnumHasFlagEmitter
{
    public void EmitHasFlag(
        IWasmInstructionWriter code,
        int receiver,
        int flag,
        int result,
        int temporaryI4,
        CliValueKind receiverKind = CliValueKind.ManagedReference,
        CliTypeIdentity? constrainedType = null)
    {
        if (receiverKind == CliValueKind.ManagedAddress)
        {
            EmitConstrained(
                code,
                constrainedType ?? throw new InvalidOperationException(
                    "constrained enum HasFlag requires its closed receiver type"),
                receiver,
                flag,
                result,
                temporaryI4);
            return;
        }

        nullChecks.Emit(code, receiver);
        nullChecks.Emit(code, flag);
        EmitConstant(code, 0);
        SetLocal(code, temporaryI4);

        EmitTypeId(code, receiver);
        EmitTypeId(code, flag);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        foreach (var storage in storages.Resolve())
        {
            EmitTypeId(code, receiver);
            EmitConstant(code, storage.Descriptor.TypeId);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            values.Emit(code, storage, receiver, flag);
            if (storage.Layout.Size == sizeof(long))
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64And));
            else
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32And));
            EmitValue(code, storage, flag);
            if (storage.Layout.Size == sizeof(long))
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Equal));
            else
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            SetLocal(code, temporaryI4);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        exceptions.Emit(code, ManagedExceptionKind.Argument);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        GetLocal(code, temporaryI4);
        SetLocal(code, result);
    }

    private void EmitConstrained(
        IWasmInstructionWriter code,
        CliTypeIdentity constrainedType,
        int receiver,
        int flag,
        int result,
        int temporaryI4)
    {
        var storage = storages.Resolve().SingleOrDefault(candidate =>
            candidate.EnumType.Equals(constrainedType));
        if (storage is null)
        {
            throw new InvalidOperationException(
                $"no reachable enum descriptor exists for '{constrainedType.CanonicalName}'");
        }

        nullChecks.Emit(code, flag);
        EmitConstant(code, 0);
        SetLocal(code, temporaryI4);

        EmitTypeId(code, flag);
        EmitConstant(code, storage.Descriptor.TypeId);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));

        GetLocal(code, receiver);
        ManagedMemoryEmitter.EmitLoadByType(
            code,
            layouts.Target,
            0,
            storage.UnderlyingType,
            storage.Layout.Size);
        EmitValue(code, storage, flag);
        if (storage.Layout.Size == sizeof(long))
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64And));
        else
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32And));
        EmitValue(code, storage, flag);
        if (storage.Layout.Size == sizeof(long))
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Equal));
        else
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        SetLocal(code, temporaryI4);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        exceptions.Emit(code, ManagedExceptionKind.Argument);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));

        GetLocal(code, temporaryI4);
        SetLocal(code, result);
    }

    private static void EmitTypeId(IWasmInstructionWriter code, int receiver)
    {
        GetLocal(code, receiver);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(2, 0)));
    }

    private static void EmitConstant(IWasmInstructionWriter code, int value) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(value)));

    private static void GetLocal(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)local)));

    private static void SetLocal(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)local)));

    private void EmitValue(
        IWasmInstructionWriter code,
        EnumStorage storage,
        int value)
    {
        GetLocal(code, value);
        ManagedMemoryEmitter.EmitLoadByType(
            code,
            layouts.Target,
            storage.PayloadOffset,
            storage.UnderlyingType,
            storage.Layout.Size);
    }
}
