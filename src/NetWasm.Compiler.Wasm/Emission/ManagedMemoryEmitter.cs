using System;
using System.Collections.Generic;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission;

internal static class ManagedMemoryEmitter
{
    public static void EmitRootSlotStore(
        IWasmInstructionWriter code,
        WasmTargetLayout target,
        int rootFrameLocal,
        int slot,
        int valueLocal)
    {
        WriteLocalGet(code, rootFrameLocal);
        if (slot != 0)
        {
            EmitAddressConstant(code, target, slot * target.ObjectReferenceSize);
            EmitAddressAdd(code, target);
        }
        WriteLocalGet(code, valueLocal);
        EmitReferenceStore(code, target, 0);
    }

    public static void EmitArrayElementAddress(
        IWasmInstructionWriter code,
        WasmTargetLayout target,
        int arrayLocal,
        int indexLocal,
        int elementSize = sizeof(int))
    {
        WriteLocalGet(code, arrayLocal);
        EmitReferenceLoad(code, target, WasmTargetLayout.Align(
            target.ObjectHeaderSize + WasmTargetLayout.SemanticLengthSize,
            target.AddressSize));
        WriteLocalGet(code, indexLocal);
        if (target.UsesMemory64)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
        }
        EmitAddressConstant(code, target, elementSize);
        EmitAddressMultiply(code, target);
        EmitAddressAdd(code, target);
    }

    public static void EmitLoadBySize(
        IWasmInstructionWriter code,
        WasmTargetLayout target,
        int offset,
        int size)
    {
        switch (size)
        {
            case 1: WriteMemory(code, WasmOpcodes.I32Load8Unsigned, 0, offset); break;
            case 2: WriteMemory(code, WasmOpcodes.I32Load16Unsigned, 1, offset); break;
            case 4: WriteMemory(code, WasmOpcodes.I32Load, 2, offset); break;
            case 8: WriteMemory(code, WasmOpcodes.I64Load, 3, offset); break;
            default:
                throw new CompilerException(new CompilerDiagnostic(
                    DiagnosticCode.RuntimeContract,
                    $"scalar load has unsupported size {size}"));
        }
    }

    public static void EmitStoreBySize(
        IWasmInstructionWriter code,
        WasmTargetLayout target,
        int offset,
        int size)
    {
        switch (size)
        {
            case 1: WriteMemory(code, WasmOpcodes.I32Store8, 0, offset); break;
            case 2: WriteMemory(code, WasmOpcodes.I32Store16, 1, offset); break;
            case 4: WriteMemory(code, WasmOpcodes.I32Store, 2, offset); break;
            case 8: WriteMemory(code, WasmOpcodes.I64Store, 3, offset); break;
            default:
                throw new CompilerException(new CompilerDiagnostic(
                    DiagnosticCode.RuntimeContract,
                    $"scalar store has unsupported size {size}"));
        }
    }

    public static void EmitLoadByType(
        IWasmInstructionWriter code,
        WasmTargetLayout target,
        int offset,
        CliTypeIdentity type,
        int size)
    {
        type = type.StackStorageType ?? type;
        switch (type.CanonicalName)
        {
            case "primitive:i1": WriteMemory(code, WasmOpcodes.I32Load8Signed, 0, offset); return;
            case "primitive:bool" or "primitive:u1": WriteMemory(code, WasmOpcodes.I32Load8Unsigned, 0, offset); return;
            case "primitive:i2": WriteMemory(code, WasmOpcodes.I32Load16Signed, 1, offset); return;
            case "primitive:char" or "primitive:u2": WriteMemory(code, WasmOpcodes.I32Load16Unsigned, 1, offset); return;
            case "primitive:f4": WriteMemory(code, WasmOpcodes.F32Load, 2, offset); return;
            case "primitive:f8": WriteMemory(code, WasmOpcodes.F64Load, 3, offset); return;
            case "primitive:i8" or "primitive:u8": WriteMemory(code, WasmOpcodes.I64Load, 3, offset); return;
            default: EmitLoadBySize(code, target, offset, size); return;
        }
    }

    public static WasmValueType GetLoadValueType(
        CliTypeIdentity type,
        int size)
    {
        type = type.StackStorageType ?? type;
        return type.CanonicalName switch
        {
            "primitive:f4" => WasmValueType.F32,
            "primitive:f8" => WasmValueType.F64,
            "primitive:i8" or "primitive:u8" => WasmValueType.I64,
            _ => size == 8 ? WasmValueType.I64 : WasmValueType.I32,
        };
    }

    public static void EmitStoreByType(
        IWasmInstructionWriter code,
        WasmTargetLayout target,
        int offset,
        CliTypeIdentity type,
        int size)
    {
        type = type.StackStorageType ?? type;
        switch (type.CanonicalName)
        {
            case "primitive:f4": WriteMemory(code, WasmOpcodes.F32Store, 2, offset); return;
            case "primitive:f8": WriteMemory(code, WasmOpcodes.F64Store, 3, offset); return;
            default: EmitStoreBySize(code, target, offset, size); return;
        }
    }

    public static void EmitLoad(
        IWasmInstructionWriter code,
        List<CliValueKind> stack,
        int stackBase,
        int sourceLocal,
        CliValueKind type)
    {
        WriteLocalGet(code, sourceLocal);
        WriteLocalSet(code, stackBase + stack.Count);
        stack.Add(type);
    }

    public static void EmitStore(
        IWasmInstructionWriter code,
        List<CliValueKind> stack,
        int stackBase,
        int targetLocal)
    {
        var slot = stack.Count - 1;
        WriteLocalGet(code, stackBase + slot);
        WriteLocalSet(code, targetLocal);
        stack.RemoveAt(slot);
    }

    internal static void EmitAddressConstant(
        IWasmInstructionWriter code,
        WasmTargetLayout target,
        int value)
    {
        if (target.UsesMemory64)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I64Constant,
                WasmInstructionOperand.Signed64(value)));
        }
        else
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(value)));
        }
    }

    internal static void EmitAddressAdd(IWasmInstructionWriter code, WasmTargetLayout target)
    {
        if (target.UsesMemory64)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Add));
        }
        else
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Add));
        }
    }

    private static void EmitAddressMultiply(
        IWasmInstructionWriter code,
        WasmTargetLayout target)
    {
        if (target.UsesMemory64)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Multiply));
        }
        else
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Multiply));
        }
    }

    public static void EmitReferenceLoad(
        IWasmInstructionWriter code,
        WasmTargetLayout target,
        int offset)
    {
        if (target.UsesMemory64)
        {
            WriteMemory(code, WasmOpcodes.I64Load, 3, offset);
        }
        else
        {
            WriteMemory(code, WasmOpcodes.I32Load, 2, offset);
        }
    }

    private static void EmitReferenceStore(
        IWasmInstructionWriter code,
        WasmTargetLayout target,
        int offset)
    {
        if (target.UsesMemory64)
        {
            WriteMemory(code, WasmOpcodes.I64Store, 3, offset);
        }
        else
        {
            WriteMemory(code, WasmOpcodes.I32Store, 2, offset);
        }
    }

    private static void WriteLocalGet(IWasmInstructionWriter code, int local)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(local);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)local)));
    }

    private static void WriteLocalSet(IWasmInstructionWriter code, int local)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(local);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)local)));
    }

    private static void WriteMemory(
        IWasmInstructionWriter code,
        byte opcode,
        uint alignment,
        int offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        code.Write(WasmInstruction.WithOperand(
            opcode,
            WasmInstructionOperand.Memory(alignment, (uint)offset)));
    }
}
