using NetWasm.Compiler.Wasm.Encoding;
using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class EnumHashCodeEmitter(
    IEnumStorageResolver storages,
    IEnumNullCheckEmitter nullChecks,
    IValueLayoutProvider values,
    ITargetLayout layouts) : IEnumHashCodeEmitter
{
    public void EmitHashCode(
        IWasmInstructionWriter code,
        CliValueKind receiverKind,
        CliTypeIdentity? receiverType,
        int receiver,
        int result,
        int temporaryI4,
        int temporaryI8)
    {
        if (receiverKind == CliValueKind.ManagedAddress)
        {
            if (receiverType is null)
            {
                throw new InvalidOperationException(
                    "constrained enum hashing requires its closed receiver type");
            }
            var storage = values.GetValueLayout(receiverType);
            var enumStorage = storages.Resolve()
                .FirstOrDefault(candidate => candidate.EnumType.Equals(receiverType));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(receiver))));
            EmitHashLoad(
                code,
                enumStorage?.UnderlyingType ?? storage.Type,
                storage,
                0,
                temporaryI8);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(result))));
            return;
        }
        nullChecks.Emit(code, receiver);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(temporaryI4))));
        var enumStorages = storages.Resolve();
        if (enumStorages.Length == 0)
        {
            throw new InvalidOperationException(
                "enum hashing was retained without a reachable enum descriptor");
        }
        foreach (var storage in enumStorages)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(receiver))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(2, (uint)(0))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(storage.Descriptor.TypeId)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(receiver))));
            EmitHashLoad(code, storage.UnderlyingType, storage.Layout, storage.PayloadOffset, temporaryI8);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(temporaryI4))));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(temporaryI4))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(result))));
    }

    private void EmitHashLoad(
        IWasmInstructionWriter code,
        CliTypeIdentity type,
        ValueLayout storage,
        int offset,
        int temporaryI8)
    {
        if (storage.Size == sizeof(long))
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I64Load, WasmInstructionOperand.Memory(3, (uint)(offset))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(temporaryI8))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(temporaryI8))));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32WrapI64));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(temporaryI8))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(32)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ShiftRightUnsigned));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32WrapI64));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Xor));
        }
        else
        {
            ManagedMemoryEmitter.EmitLoadByType(
                code, layouts.Target, offset, type, storage.Size);
        }
    }
}
