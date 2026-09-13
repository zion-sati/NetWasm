using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class EnumTypeCodeEmitter(
    IEnumStorageResolver storages,
    IEnumNullCheckEmitter nullChecks) : IEnumTypeCodeEmitter
{
    public void EmitTypeCode(
        IWasmInstructionWriter code,
        int receiver,
        int result,
        int temporaryI4,
        CliTypeIdentity? constrainedType = null,
        CliValueKind receiverKind = CliValueKind.ManagedReference)
    {
        var enumStorages = storages.Resolve();
        if (receiverKind == CliValueKind.ManagedAddress)
        {
            if (constrainedType is null)
            {
                throw new InvalidOperationException(
                    "constrained enum type-code dispatch requires its closed receiver type");
            }
            var storage = enumStorages.SingleOrDefault(candidate =>
                candidate.EnumType.Assembly.Equals(constrainedType.Assembly) &&
                candidate.EnumType.FullName == constrainedType.FullName);
            if (storage is null)
            {
                throw new InvalidOperationException(
                    $"enum type-code dispatch has no descriptor for '{constrainedType.CanonicalName}'");
            }
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(GetTypeCode(storage.UnderlyingType))));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned((uint)result)));
            return;
        }
        nullChecks.Emit(code, receiver);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(0)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)temporaryI4)));

        if (enumStorages.Length == 0)
        {
            throw new InvalidOperationException(
                "enum type-code dispatch was retained without a reachable enum descriptor");
        }

        foreach (var storage in enumStorages)
        {
            var typeCode = GetTypeCode(storage.UnderlyingType);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)receiver)));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Load,
                WasmInstructionOperand.Memory(2, 0)));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(storage.Descriptor.TypeId)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(typeCode)));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned((uint)temporaryI4)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }

        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)temporaryI4)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)result)));
    }

    private static int GetTypeCode(CliTypeIdentity underlyingType) =>
        underlyingType.CanonicalName switch
        {
            "primitive:i1" => 5,
            "primitive:u1" => 6,
            "primitive:i2" => 7,
            "primitive:u2" => 8,
            "primitive:i4" => 9,
            "primitive:u4" => 10,
            "primitive:i8" => 11,
            "primitive:u8" => 12,
            "primitive:char" => 4,
            _ => throw new InvalidOperationException(
                $"enum type-code dispatch does not support '{underlyingType.CanonicalName}'"),
        };
}
