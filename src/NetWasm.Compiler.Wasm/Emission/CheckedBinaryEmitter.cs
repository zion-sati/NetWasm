using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission;

internal sealed class CheckedBinaryEmitter(
    WasmTargetLayout target,
    IImplicitExceptionEmitter exceptions) : ICheckedBinaryEmitter
{
    public void Emit(
        IWasmInstructionWriter code,
        CilOperation operation,
        CliValueKind type,
        int leftLocal,
        int rightLocal,
        int resultLocal)
    {
        var usesI64 = type == CliValueKind.I8 ||
                      type == CliValueKind.NativeInt && target.UsesMemory64;
        if (operation == CilOperation.MultiplyCheckedUnsigned)
        {
            EmitUnsignedMultiply(code, usesI64, leftLocal, rightLocal, resultLocal);
            return;
        }
        if (operation is CilOperation.AddCheckedUnsigned or
            CilOperation.SubtractCheckedUnsigned)
        {
            EmitUnsignedAddOrSubtract(
                code,
                operation,
                usesI64,
                leftLocal,
                rightLocal,
                resultLocal);
            return;
        }

        WriteLocalGet(code, leftLocal);
        WriteLocalGet(code, rightLocal);
        switch (operation)
        {
            case CilOperation.AddChecked:
                WriteOperation(code, usesI64 ? WasmOpcodes.I64Add : WasmOpcodes.I32Add);
                break;
            case CilOperation.SubtractChecked:
                WriteOperation(code, usesI64 ? WasmOpcodes.I64Subtract : WasmOpcodes.I32Subtract);
                break;
            case CilOperation.MultiplyChecked:
                WriteOperation(code, usesI64 ? WasmOpcodes.I64Multiply : WasmOpcodes.I32Multiply);
                break;
            default:
                throw new InvalidOperationException(
                    $"{operation} is not a checked binary operation.");
        }
        WriteLocalSet(code, resultLocal);
        if (operation == CilOperation.MultiplyChecked)
        {
            EmitSignedMultiplyCheck(code, usesI64, leftLocal, rightLocal, resultLocal);
            return;
        }
        EmitSignedAddOrSubtractCheck(
            code,
            operation,
            usesI64,
            leftLocal,
            rightLocal,
            resultLocal);
    }

    private void EmitUnsignedAddOrSubtract(
        IWasmInstructionWriter code,
        CilOperation operation,
        bool usesI64,
        int leftLocal,
        int rightLocal,
        int resultLocal)
    {
        WriteLocalGet(code, leftLocal);
        WriteLocalGet(code, rightLocal);
        if (operation == CilOperation.AddCheckedUnsigned)
        {
            WriteOperation(code, usesI64 ? WasmOpcodes.I64Add : WasmOpcodes.I32Add);
        }
        else
        {
            WriteOperation(code, usesI64 ? WasmOpcodes.I64Subtract : WasmOpcodes.I32Subtract);
        }
        WriteLocalSet(code, resultLocal);
        WriteLocalGet(code, operation == CilOperation.AddCheckedUnsigned
            ? resultLocal
            : leftLocal);
        WriteLocalGet(code, operation == CilOperation.AddCheckedUnsigned
            ? leftLocal
            : rightLocal);
        WriteOperation(code, usesI64
            ? WasmOpcodes.I64LessThanUnsigned
            : WasmOpcodes.I32LessThanUnsigned);
        WriteBlockInstruction(code, WasmOpcodes.If);
        exceptions.Emit(code, ManagedExceptionKind.Overflow);
        WriteOperation(code, WasmOpcodes.End);
    }

    private void EmitSignedAddOrSubtractCheck(
        IWasmInstructionWriter code,
        CilOperation operation,
        bool usesI64,
        int leftLocal,
        int rightLocal,
        int resultLocal)
    {
        WriteLocalGet(code, leftLocal);
        WriteLocalGet(code, operation == CilOperation.AddChecked ? resultLocal : rightLocal);
        WriteOperation(code, usesI64 ? WasmOpcodes.I64Xor : WasmOpcodes.I32Xor);
        WriteLocalGet(code, operation == CilOperation.AddChecked ? rightLocal : leftLocal);
        WriteLocalGet(code, resultLocal);
        if (usesI64)
        {
            WriteOperation(code, WasmOpcodes.I64Xor);
            WriteOperation(code, WasmOpcodes.I64And);
            WriteConstant(code, 0, true);
            WriteOperation(code, WasmOpcodes.I64LessThanSigned);
        }
        else
        {
            WriteOperation(code, WasmOpcodes.I32Xor);
            WriteOperation(code, WasmOpcodes.I32And);
            WriteConstant(code, 0, false);
            WriteOperation(code, WasmOpcodes.I32LessThanSigned);
        }
        WriteBlockInstruction(code, WasmOpcodes.If);
        exceptions.Emit(code, ManagedExceptionKind.Overflow);
        WriteOperation(code, WasmOpcodes.End);
    }

    private void EmitSignedMultiplyCheck(
        IWasmInstructionWriter code,
        bool usesI64,
        int leftLocal,
        int rightLocal,
        int resultLocal)
    {
        WriteLocalGet(code, rightLocal);
        WriteOperation(code, usesI64 ? WasmOpcodes.I64EqualZero : WasmOpcodes.I32EqualZero);
        WriteBlockInstruction(code, WasmOpcodes.If);
        WriteOperation(code, WasmOpcodes.Else);
        WriteLocalGet(code, leftLocal);
        if (usesI64)
        {
            WriteConstant(code, long.MinValue, true);
            WriteOperation(code, WasmOpcodes.I64Equal);
        }
        else
        {
            WriteConstant(code, int.MinValue, false);
            WriteOperation(code, WasmOpcodes.I32Equal);
        }
        WriteLocalGet(code, rightLocal);
        if (usesI64)
        {
            WriteConstant(code, -1L, true);
            WriteOperation(code, WasmOpcodes.I64Equal);
        }
        else
        {
            WriteConstant(code, -1, false);
            WriteOperation(code, WasmOpcodes.I32Equal);
        }
        WriteOperation(code, WasmOpcodes.I32And);
        WriteBlockInstruction(code, WasmOpcodes.If);
        exceptions.Emit(code, ManagedExceptionKind.Overflow);
        WriteOperation(code, WasmOpcodes.End);
        WriteLocalGet(code, resultLocal);
        WriteLocalGet(code, rightLocal);
        WriteOperation(code, usesI64 ? WasmOpcodes.I64DivideSigned : WasmOpcodes.I32DivideSigned);
        WriteLocalGet(code, leftLocal);
        WriteOperation(code, usesI64 ? WasmOpcodes.I64Equal : WasmOpcodes.I32Equal);
        WriteOperation(code, WasmOpcodes.I32EqualZero);
        WriteBlockInstruction(code, WasmOpcodes.If);
        exceptions.Emit(code, ManagedExceptionKind.Overflow);
        WriteOperation(code, WasmOpcodes.End);
        WriteOperation(code, WasmOpcodes.End);
    }

    private void EmitUnsignedMultiply(
        IWasmInstructionWriter code,
        bool usesI64,
        int leftLocal,
        int rightLocal,
        int resultLocal)
    {
        WriteLocalGet(code, leftLocal);
        WriteLocalGet(code, rightLocal);
        WriteOperation(code, usesI64 ? WasmOpcodes.I64Multiply : WasmOpcodes.I32Multiply);
        WriteLocalSet(code, resultLocal);
        WriteLocalGet(code, rightLocal);
        WriteOperation(code, usesI64 ? WasmOpcodes.I64EqualZero : WasmOpcodes.I32EqualZero);
        WriteBlockInstruction(code, WasmOpcodes.If);
        WriteOperation(code, WasmOpcodes.Else);
        WriteLocalGet(code, resultLocal);
        WriteLocalGet(code, rightLocal);
        WriteOperation(code, usesI64 ? WasmOpcodes.I64DivideUnsigned : WasmOpcodes.I32DivideUnsigned);
        WriteLocalGet(code, leftLocal);
        WriteOperation(code, usesI64 ? WasmOpcodes.I64Equal : WasmOpcodes.I32Equal);
        WriteOperation(code, WasmOpcodes.I32EqualZero);
        WriteBlockInstruction(code, WasmOpcodes.If);
        exceptions.Emit(code, ManagedExceptionKind.Overflow);
        WriteOperation(code, WasmOpcodes.End);
        WriteOperation(code, WasmOpcodes.End);
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

    private static void WriteOperation(IWasmInstructionWriter code, byte opcode)
    {
        code.Write(WasmInstruction.NoOperand(opcode));
    }

    private static void WriteBlockInstruction(
        IWasmInstructionWriter code,
        byte opcode)
    {
        code.Write(WasmInstruction.WithOperand(
            opcode,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
    }

    private static void WriteConstant(
        IWasmInstructionWriter code,
        long value,
        bool usesI64)
    {
        code.Write(usesI64
            ? WasmInstruction.WithOperand(
                WasmOpcodes.I64Constant,
                WasmInstructionOperand.Signed64(value))
            : WasmInstruction.WithOperand(
                WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed((int)value)));
    }
}
