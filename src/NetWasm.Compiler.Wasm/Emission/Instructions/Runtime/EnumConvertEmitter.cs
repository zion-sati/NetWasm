using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class EnumConvertEmitter(
    IEnumStorageResolver storages,
    IImplicitExceptionEmitter exceptions,
    ITargetLayout layouts) : IEnumConvertEmitter
{
    public void EmitConvert(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var name = NormalizeName(request.Method.Definition.Name);
        if (name == "InternalToDateTime")
        {
            exceptions.Emit(code, ManagedExceptionKind.InvalidCast, request.Instruction.Context.ManagedThrowFunction);
            return;
        }
        if (name == "InternalToDecimal")
        {
            EmitDecimal(request, code);
            return;
        }

        // A numeric result can share the receiver's evaluation-stack local.
        // Keep the boxed address intact while dispatch checks remaining enum types.
        var receiver = request.Instruction.Context.ObjectTemporary;
        Get(code, request.Local(0, CliValueKind.ManagedReference));
        Set(code, receiver);
        var resultKind = request.Method.Signature.ReturnType;
        var result = request.Local(0, resultKind);
        var temporary = request.Instruction.Context.NumericTemporaryI4;
        WriteI32(code, 0);
        Set(code, temporary);
        foreach (var storage in storages.Resolve())
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)receiver)));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load,
                WasmInstructionOperand.Memory(2, 0)));
            WriteI32(code, storage.TypeId);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            Get(code, receiver);
            ManagedMemoryEmitter.EmitLoadByType(
                code, layouts.Target, storage.PayloadOffset,
                storage.UnderlyingType, storage.Layout.Size);
            EmitIntegerBounds(request, code, name, storage.UnderlyingType);
            EmitConversion(code, name, storage.UnderlyingType, resultKind);
            if (resultKind == CliValueKind.I8)
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
                    WasmInstructionOperand.Unsigned((uint)request.Local(0, CliValueKind.I8))));
            else
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
                    WasmInstructionOperand.Unsigned((uint)result)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
        if (name == "InternalToBoolean")
        {
            Get(code, result);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
            Set(code, result);
        }
    }

    private void EmitIntegerBounds(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        string name,
        CliTypeIdentity underlying)
    {
        (long Minimum, ulong Maximum)? bounds = name switch
        {
            "InternalToChar" or "InternalToUInt16" => (0, ushort.MaxValue),
            "InternalToSByte" => (sbyte.MinValue, (ulong)sbyte.MaxValue),
            "InternalToByte" => (0, byte.MaxValue),
            "InternalToInt16" => (short.MinValue, (ulong)short.MaxValue),
            "InternalToInt32" => (int.MinValue, int.MaxValue),
            "InternalToUInt32" => (0, uint.MaxValue),
            "InternalToInt64" => (long.MinValue, long.MaxValue),
            "InternalToUInt64" => (0, ulong.MaxValue),
            _ => null,
        };
        if (bounds is not { } range)
            return;

        var unsigned = IsUnsigned(underlying);
        var temporary = request.Instruction.Context.NumericTemporaryI8;
        if (underlying.StackKind == CliValueKind.I4)
            code.Write(WasmInstruction.NoOperand(unsigned
                ? WasmOpcodes.I64ExtendI32Unsigned
                : WasmOpcodes.I64ExtendI32Signed));
        Set(code, temporary);
        if (!unsigned)
        {
            Get(code, temporary);
            WriteI64(code, range.Minimum);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64LessThanSigned));
            EmitOverflowIf(code, request.Instruction.Context.ManagedThrowFunction);
        }
        if (unsigned || range.Maximum != ulong.MaxValue)
        {
            Get(code, temporary);
            WriteI64(code, unchecked((long)range.Maximum));
            code.Write(WasmInstruction.NoOperand(unsigned
                ? WasmOpcodes.I64GreaterThanUnsigned
                : WasmOpcodes.I64GreaterThanSigned));
            EmitOverflowIf(code, request.Instruction.Context.ManagedThrowFunction);
        }
        Get(code, temporary);
        if (underlying.StackKind == CliValueKind.I4)
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32WrapI64));
    }

    private void EmitOverflowIf(IWasmInstructionWriter code, OptionalFunctionIndex throwFunction)
    {
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.Overflow, throwFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitDecimal(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var receiver = request.Local(0, CliValueKind.ManagedReference);
        var receiverTemporary = request.Instruction.Context.ObjectTemporary;
        var result = request.Local(0, CliValueKind.ValueType);
        var temporaryI4 = request.Instruction.Context.NumericTemporaryI4;
        var temporaryI8 = request.Instruction.Context.NumericTemporaryI8;

        // On wasm32, managed references and value-type addresses both use the
        // i32 evaluation-stack local family. Preserve the receiver before the
        // result address is assigned to that same logical slot.
        Get(code, receiver);
        Set(code, receiverTemporary);
        EmitResultAddress(request, code, result);
        StoreI32(code, result, 0, 0);
        StoreI32(code, result, 4, 0);
        StoreI32(code, result, 8, 0);
        StoreI32(code, result, 12, 0);

        foreach (var storage in storages.Resolve())
        {
            EmitTypeId(code, receiverTemporary);
            WriteI32(code, storage.TypeId);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            EmitDecimalStorage(
                code, receiverTemporary, result, temporaryI4, temporaryI8,
                storage);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
    }

    private void EmitDecimalStorage(
        IWasmInstructionWriter code,
        int receiver,
        int result,
        int temporaryI4,
        int temporaryI8,
        EnumStorage storage)
    {
        Get(code, receiver);
        ManagedMemoryEmitter.EmitLoadByType(
            code,
            layouts.Target,
            storage.PayloadOffset,
            storage.UnderlyingType,
            storage.Layout.Size);
        EmitDecimalValue(
            code,
            result,
            temporaryI4,
            temporaryI8,
            storage.UnderlyingType);
    }

    private static void EmitTypeId(
        IWasmInstructionWriter code,
        int receiver)
    {
        Get(code, receiver);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(2, 0)));
    }

    private void EmitResultAddress(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        int result)
    {
        var context = request.Instruction.Context;
        var offset = context.ValueLayout.TemporaryOffsets[
            request.Instruction.Instruction.Offset];
        Get(code, context.ValueFrame);
        if (offset != 0)
        {
            ManagedMemoryEmitter.EmitAddressConstant(code, layouts.Target, offset);
            ManagedMemoryEmitter.EmitAddressAdd(code, layouts.Target);
        }
        Set(code, result);
    }

    private static void EmitDecimalValue(
        IWasmInstructionWriter code,
        int result,
        int temporaryI4,
        int temporaryI8,
        CliTypeIdentity underlying)
    {
        switch (underlying.StackKind)
        {
            case CliValueKind.I4:
                Set(code, temporaryI4);
                EmitDecimalI32(code, result, temporaryI4, IsUnsigned(underlying));
                return;
            case CliValueKind.I8:
                Set(code, temporaryI8);
                EmitDecimalI64(code, result, temporaryI8, IsUnsigned(underlying));
                return;
            default:
                throw new InvalidOperationException(
                    $"Enum decimal conversion does not support '{underlying.CanonicalName}'.");
        }
    }

    private static void EmitDecimalI32(
        IWasmInstructionWriter code,
        int result,
        int temporary,
        bool unsigned)
    {
        if (!unsigned)
        {
            Get(code, result);
            Get(code, temporary);
            WriteI32(code, 31);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32ShiftRightSigned));
            WriteI32(code, int.MinValue);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32And));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Store,
                WasmInstructionOperand.Memory(2, 0)));

            Get(code, result);
            Get(code, temporary);
            Get(code, result);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Load,
                WasmInstructionOperand.Memory(2, 0)));
            WriteI32(code, 31);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32ShiftRightSigned));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Xor));
            Get(code, result);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Load,
                WasmInstructionOperand.Memory(2, 0)));
            WriteI32(code, 31);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32ShiftRightSigned));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Subtract));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Store,
                WasmInstructionOperand.Memory(2, 8)));
        }
        else
        {
            StoreFromI32(code, result, 8, temporary);
        }

        StoreI32(code, result, 4, 0);
        StoreI32(code, result, 12, 0);
        if (unsigned)
        {
            StoreI32(code, result, 0, 0);
        }
    }

    private static void EmitDecimalI64(
        IWasmInstructionWriter code,
        int result,
        int temporary,
        bool unsigned)
    {
        if (!unsigned)
        {
            Get(code, result);
            Get(code, temporary);
            WriteI64(code, 63);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ShiftRightSigned));
            WriteI64(code, 0x80000000L);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64And));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32WrapI64));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Store,
                WasmInstructionOperand.Memory(2, 0)));

            Get(code, temporary);
            Get(code, result);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Load,
                WasmInstructionOperand.Memory(2, 0)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Signed));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Xor));
            Get(code, result);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Load,
                WasmInstructionOperand.Memory(2, 0)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Signed));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Subtract));
            Set(code, temporary);
        }

        StoreFromI64Low(code, result, 8, temporary);
        StoreFromI64High(code, result, 12, temporary);
        StoreI32(code, result, 4, 0);
        if (unsigned)
        {
            StoreI32(code, result, 0, 0);
        }
    }

    private static void StoreFromI32(
        IWasmInstructionWriter code,
        int result,
        int offset,
        int source)
    {
        Get(code, result);
        Get(code, source);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Store,
            WasmInstructionOperand.Memory(2, (uint)offset)));
    }

    private static void StoreFromI64Low(
        IWasmInstructionWriter code,
        int result,
        int offset,
        int source)
    {
        Get(code, result);
        Get(code, source);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32WrapI64));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Store,
            WasmInstructionOperand.Memory(2, (uint)offset)));
    }

    private static void StoreFromI64High(
        IWasmInstructionWriter code,
        int result,
        int offset,
        int source)
    {
        Get(code, result);
        Get(code, source);
        WriteI64(code, 32);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ShiftRightUnsigned));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32WrapI64));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Store,
            WasmInstructionOperand.Memory(2, (uint)offset)));
    }

    private static void StoreI32(
        IWasmInstructionWriter code,
        int result,
        int offset,
        int value)
    {
        Get(code, result);
        WriteI32(code, value);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Store,
            WasmInstructionOperand.Memory(2, (uint)offset)));
    }

    private static void EmitConversion(
        IWasmInstructionWriter code,
        string name,
        CliTypeIdentity underlying,
        CliValueKind resultKind)
    {
        if (name == "InternalToBoolean")
        {
            code.Write(WasmInstruction.NoOperand(
                underlying.StackKind == CliValueKind.I8
                    ? WasmOpcodes.I64EqualZero
                    : WasmOpcodes.I32EqualZero));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
            return;
        }
        if (resultKind == CliValueKind.I8 && underlying.StackKind != CliValueKind.I8)
        {
            code.Write(WasmInstruction.NoOperand(
                IsUnsigned(underlying) ? WasmOpcodes.I64ExtendI32Unsigned : WasmOpcodes.I64ExtendI32Signed));
            return;
        }
        if (resultKind == CliValueKind.I4 && underlying.StackKind == CliValueKind.I8)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32WrapI64));
            return;
        }
        if (resultKind == CliValueKind.F4)
        {
            code.Write(WasmInstruction.NoOperand(underlying.StackKind == CliValueKind.I8
                ? (IsUnsigned(underlying) ? WasmOpcodes.F32ConvertI64Unsigned : WasmOpcodes.F32ConvertI64Signed)
                : (IsUnsigned(underlying) ? WasmOpcodes.F32ConvertI32Unsigned : WasmOpcodes.F32ConvertI32Signed)));
            return;
        }
        if (resultKind == CliValueKind.F8)
        {
            code.Write(WasmInstruction.NoOperand(underlying.StackKind == CliValueKind.I8
                ? (IsUnsigned(underlying) ? WasmOpcodes.F64ConvertI64Unsigned : WasmOpcodes.F64ConvertI64Signed)
                : (IsUnsigned(underlying) ? WasmOpcodes.F64ConvertI32Unsigned : WasmOpcodes.F64ConvertI32Signed)));
        }
    }

    private static bool IsUnsigned(CliTypeIdentity type) => type.CanonicalName is
        "primitive:u1" or "primitive:u2" or "primitive:u4" or "primitive:u8" or "primitive:char";
    private static string NormalizeName(string name) => name.StartsWith(
        "System.IConvertible.",
        StringComparison.Ordinal)
        ? $"Internal{name["System.IConvertible.".Length..]}"
        : name;
    private static void Get(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)local)));
    private static void Set(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)local)));
    private static void WriteI32(IWasmInstructionWriter code, int value) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(value)));
    private static void WriteI64(IWasmInstructionWriter code, long value) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(value)));
}
