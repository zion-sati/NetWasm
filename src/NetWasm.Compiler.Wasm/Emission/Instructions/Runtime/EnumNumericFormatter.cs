using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

/// <summary>
/// Emits the decimal or fixed-width hexadecimal representation of an unnamed enum value.
/// </summary>
internal sealed class EnumNumericFormatter(
    IValueLayoutProvider values,
    IRuntimeObjectLayout objects,
    ITypeLayoutProvider typeLayouts,
    ITargetLayout layouts,
    IRuntimeImportResolver runtimeImports) : IEnumNumericFormatter
{
    public void Emit(
        IWasmInstructionWriter code,
        CliTypeIdentity underlying,
        int value,
        int payload,
        int? format,
        int result,
        int raw,
        int scratch)
    {
        if (format is null)
        {
            EmitDecimalFallback(code, underlying, value, payload, result, raw, scratch);
            return;
        }

        Get(code, format.Value);
        code.Write(WasmInstruction.NoOperand(
            layouts.Target.UsesMemory64
                ? WasmOpcodes.I64EqualZero
                : WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        EmitDecimalFallback(code, underlying, value, payload, result, raw, scratch);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        EmitFormatMatch(code, format.Value, 'X');
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        EmitHexFallback(code, underlying, value, payload, result, raw, scratch);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        EmitDecimalFallback(code, underlying, value, payload, result, raw, scratch);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitDecimalFallback(
        IWasmInstructionWriter code,
        CliTypeIdentity underlying,
        int value,
        int payload,
        int result,
        int raw,
        int scratch)
    {
        var maxLength = DecimalWidth(underlying);

        EmitRawValue(code, underlying, value, payload, raw);
        WriteI32(code, 0);
        Set(code, scratch);
        if (IsSigned(underlying))
        {
            Get(code, raw);
            WriteI64(code, 0);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64LessThanSigned));
            Set(code, scratch);
            Get(code, scratch);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            WriteI64(code, 0);
            Get(code, raw);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Subtract));
            Set(code, raw);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }

        EmitAllocateString(code, result, maxLength);
        EmitDecimalDigits(code, raw, result);

        Get(code, scratch);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        DecrementStringLength(code, result);
        EmitStringCharacterAddress(code, result);
        WriteI32(code, '-');
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Store16,
            WasmInstructionOperand.Memory(1, (uint)objects.StringDataOffset)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));

        EmitCompactDecimalString(code, result, raw, maxLength);
    }

    private void EmitCompactDecimalString(
        IWasmInstructionWriter code,
        int result,
        int start,
        int maxLength)
    {
        Get(code, result);
        EmitStringLength(code);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
        Set(code, start);
        Get(code, result);
        WriteI32(code, maxLength);
        Get(code, start);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32WrapI64));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Subtract));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Store,
            WasmInstructionOperand.Memory(2, (uint)objects.StringLengthOffset)));

        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Loop,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        Get(code, result);
        EmitStringLength(code);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.BranchIf,
            WasmInstructionOperand.Unsigned(1)));
        DecrementStringLength(code, result);

        Get(code, result);
        WriteI32(code, maxLength - 1);
        Get(code, result);
        EmitStringLength(code);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Subtract));
        Get(code, start);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32WrapI64));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Subtract));
        EmitStringIndexAddress(code, layouts.Target);
        Get(code, result);
        WriteI32(code, maxLength - 1);
        Get(code, result);
        EmitStringLength(code);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Subtract));
        EmitStringIndexAddress(code, layouts.Target);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Load16Unsigned,
            WasmInstructionOperand.Memory(1, (uint)objects.StringDataOffset)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Store16,
            WasmInstructionOperand.Memory(1, (uint)objects.StringDataOffset)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Branch,
            WasmInstructionOperand.Unsigned(0)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));

        Get(code, result);
        WriteI32(code, maxLength);
        Get(code, start);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32WrapI64));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Subtract));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Store,
            WasmInstructionOperand.Memory(2, (uint)objects.StringLengthOffset)));
    }

    private void EmitHexFallback(
        IWasmInstructionWriter code,
        CliTypeIdentity underlying,
        int value,
        int payload,
        int result,
        int raw,
        int scratch)
    {
        var width = HexWidth(underlying);

        EmitRawValue(code, underlying, value, payload, raw);
        EmitAllocateString(code, result, width);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Loop,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        Get(code, result);
        EmitStringLength(code);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.BranchIf,
            WasmInstructionOperand.Unsigned(1)));
        DecrementStringLength(code, result);
        EmitStringCharacterAddress(code, result);
        Get(code, raw);
        WriteI64(code, 0xF);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64And));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32WrapI64));
        Set(code, scratch);
        Get(code, scratch);
        WriteI32(code, 10);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32GreaterThanOrEqualUnsigned));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        Get(code, scratch);
        WriteI32(code, 7);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Add));
        Set(code, scratch);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        Get(code, scratch);
        WriteI32(code, '0');
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Add));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Store16,
            WasmInstructionOperand.Memory(1, (uint)objects.StringDataOffset)));
        Get(code, raw);
        WriteI64(code, 4);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ShiftRightUnsigned));
        Set(code, raw);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Branch,
            WasmInstructionOperand.Unsigned(0)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        Get(code, result);
        WriteI32(code, width);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Store,
            WasmInstructionOperand.Memory(2, (uint)objects.StringLengthOffset)));
    }

    private void EmitDecimalDigits(
        IWasmInstructionWriter code,
        int raw,
        int result)
    {
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Loop,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        DecrementStringLength(code, result);
        EmitStringCharacterAddress(code, result);
        Get(code, raw);
        WriteI64(code, 10);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64RemainderUnsigned));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32WrapI64));
        WriteI32(code, '0');
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Add));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Store16,
            WasmInstructionOperand.Memory(1, (uint)objects.StringDataOffset)));
        Get(code, raw);
        WriteI64(code, 10);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64DivideUnsigned));
        Set(code, raw);
        Get(code, raw);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64EqualZero));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.BranchIf,
            WasmInstructionOperand.Unsigned(1)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Branch,
            WasmInstructionOperand.Unsigned(0)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitRawValue(
        IWasmInstructionWriter code,
        CliTypeIdentity underlying,
        int value,
        int payload,
        int raw)
    {
        var layout = values.GetValueLayout(underlying);
        Get(code, value);
        ManagedMemoryEmitter.EmitLoadByType(
            code, layouts.Target, payload, underlying, layout.Size);
        if (underlying.StackKind != CliValueKind.I8)
        {
            code.Write(WasmInstruction.NoOperand(
                IsSigned(underlying)
                    ? WasmOpcodes.I64ExtendI32Signed
                    : WasmOpcodes.I64ExtendI32Unsigned));
        }
        Set(code, raw);
    }

    private void EmitAllocateString(
        IWasmInstructionWriter code,
        int result,
        int length)
    {
        WriteI32(code, 0);
        WriteI32(code, length);
        WriteI32(code, typeLayouts.StringTypeId);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                RuntimeImportSymbol.AllocateString))));
        Set(code, result);
    }

    private void EmitStringCharacterAddress(
        IWasmInstructionWriter code,
        int result)
    {
        Get(code, result);
        Get(code, result);
        EmitStringLength(code);
        if (layouts.Target.UsesMemory64)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
            WriteI64(code, sizeof(char));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Multiply));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Add));
        }
        else
        {
            WriteI32(code, sizeof(char));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Multiply));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Add));
        }
    }

    private static void EmitStringIndexAddress(
        IWasmInstructionWriter code,
        WasmTargetLayout target)
    {
        if (target.UsesMemory64)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
            WriteI64(code, sizeof(char));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Multiply));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Add));
        }
        else
        {
            WriteI32(code, sizeof(char));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Multiply));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Add));
        }
    }

    private void DecrementStringLength(
        IWasmInstructionWriter code,
        int result)
    {
        Get(code, result);
        Get(code, result);
        EmitStringLength(code);
        WriteI32(code, 1);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Subtract));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Store,
            WasmInstructionOperand.Memory(2, (uint)objects.StringLengthOffset)));
    }

    private void EmitStringLength(
        IWasmInstructionWriter code) => code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(2, (uint)objects.StringLengthOffset)));

    private static int DecimalWidth(CliTypeIdentity type) => type.CanonicalName switch
    {
        "primitive:i1" => 4,
        "primitive:u1" => 3,
        "primitive:i2" => 6,
        "primitive:u2" or "primitive:char" => 5,
        "primitive:i4" => 11,
        "primitive:u4" => 10,
        _ => 20,
    };

    private static int HexWidth(CliTypeIdentity type) => type.CanonicalName switch
    {
        "primitive:i1" or "primitive:u1" => 2,
        "primitive:i2" or "primitive:u2" or "primitive:char" => 4,
        "primitive:i4" or "primitive:u4" => 8,
        _ => 16,
    };

    private static bool IsSigned(CliTypeIdentity type) => type.CanonicalName is
        "primitive:i1" or "primitive:i2" or "primitive:i4" or "primitive:i8";

    private void EmitFormatMatch(
        IWasmInstructionWriter code,
        int format,
        char character)
    {
        Get(code, format);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load16Unsigned,
            WasmInstructionOperand.Memory(1, (uint)objects.StringDataOffset)));
        WriteI32(code, character);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        Get(code, format);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load16Unsigned,
            WasmInstructionOperand.Memory(1, (uint)objects.StringDataOffset)));
        WriteI32(code, char.ToLowerInvariant(character));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Or));
    }

    private static void Get(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)local)));
    private static void Set(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)local)));
    private static void WriteI32(IWasmInstructionWriter code, int value) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(value)));
    private static void WriteI64(IWasmInstructionWriter code, long value) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(value)));
}
