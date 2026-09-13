using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

/// <summary>
/// Emits a bounded decimal parser over a managed UTF-16 string.
/// </summary>
/// <remarks>
/// This is deliberately a leaf Strategy. It only writes a value and an
/// <see cref="EnumNumericParseStatus"/> to the locals named by the request;
/// Parse and TryParse callers decide how each status is surfaced.
/// </remarks>
internal sealed class EnumNumericParseEmitter : IEnumNumericParseEmitter
{
    private const int NoDigitsPositive = 0;
    private const int NoDigitsNegative = 1;
    private const int DigitsPositive = 2;
    private const int DigitsNegative = 3;
    private const int TrailingWhitespacePositive = 4;
    private const int Nonnumeric = 5;
    private const int Invalid = 6;
    private const int Overflow = 7;
    private const int NoDigitsPositiveSign = 8;
    private const int TrailingWhitespaceNegative = 9;

    public EnumNumericParseEmitter(
        IRuntimeObjectLayout objects,
        IAddressInstructionEmitter addresses,
        ITargetLayout layouts)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(addresses);
        ArgumentNullException.ThrowIfNull(layouts);
        Objects = objects;
        Addresses = addresses;
        Layouts = layouts;
    }

    private IRuntimeObjectLayout Objects { get; } = null!;
    private IAddressInstructionEmitter Addresses { get; } = null!;
    private ITargetLayout Layouts { get; } = null!;

    public void Emit(EnumNumericParseEmissionRequest request, IWasmInstructionWriter code)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(code);
        var domain = ResolveDomain(request.UnderlyingType);
        ValidateLocals(request);

        WriteIntegerConstant(code, domain, 0);
        Set(code, request.Output.ValueLocal);
        WriteI32(code, Invalid);
        Set(code, request.Output.StatusLocal);
        WriteI32(code, 0);
        Set(code, request.IndexLocal);

        Get(code, request.TextLocal);
        Addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        EmitScan(code, request, domain);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));

        EmitCompletion(code, request, domain);
    }

    private void EmitScan(
        IWasmInstructionWriter code,
        EnumNumericParseEmissionRequest request,
        NumericDomain domain)
    {
        WriteI32(code, NoDigitsPositive);
        Set(code, request.Output.StatusLocal);

        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Loop,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));

        Get(code, request.IndexLocal);
        Get(code, request.TextLocal);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(
                2,
                (uint)(request.IsArray
                    ? Objects.ArrayLengthOffset
                    : Objects.StringLengthOffset))));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32LessThanUnsigned));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));

        EmitCharacter(code, request);
        Set(code, request.DigitLocal);
        Get(code, request.Output.StatusLocal);
        WriteI32(code, Nonnumeric);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        Get(code, request.Output.StatusLocal);
        WriteI32(code, Invalid);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32And));
        Get(code, request.Output.StatusLocal);
        WriteI32(code, Overflow);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32And));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        EmitClassification(code, request, domain);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));

        Get(code, request.IndexLocal);
        WriteI32(code, 1);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Add));
        Set(code, request.IndexLocal);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Branch,
            WasmInstructionOperand.Unsigned(1)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Branch,
            WasmInstructionOperand.Unsigned(2)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private static void EmitClassification(
        IWasmInstructionWriter code,
        EnumNumericParseEmissionRequest request,
        NumericDomain domain)
    {
        Get(code, request.DigitLocal);
        WriteI32(code, '0');
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32GreaterThanOrEqualUnsigned));
        Get(code, request.DigitLocal);
        WriteI32(code, '9' + 1);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32LessThanUnsigned));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32And));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        EmitDigit(code, request, domain);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));

        EmitWhitespacePredicate(code, request.DigitLocal);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        EmitWhitespace(code, request);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));

        Get(code, request.DigitLocal);
        WriteI32(code, '+');
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        Get(code, request.DigitLocal);
        WriteI32(code, '-');
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Or));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        EmitSign(code, request);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        WriteI32(code, Nonnumeric);
        Set(code, request.Output.StatusLocal);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private static void EmitDigit(
        IWasmInstructionWriter code,
        EnumNumericParseEmissionRequest request,
        NumericDomain domain)
    {
        // Classification leaves the UTF-16 code unit in DigitLocal. Convert
        // '0'..'9' to its numeric digit before every arithmetic or limit test.
        Get(code, request.DigitLocal);
        WriteI32(code, '0');
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Subtract));
        Set(code, request.DigitLocal);

        Get(code, request.Output.StatusLocal);
        WriteI32(code, TrailingWhitespacePositive);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        Get(code, request.Output.StatusLocal);
        WriteI32(code, TrailingWhitespaceNegative);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Or));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        WriteI32(code, Invalid);
        Set(code, request.Output.StatusLocal);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));

        Get(code, request.Output.StatusLocal);
        WriteI32(code, NoDigitsNegative);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        Get(code, request.Output.StatusLocal);
        WriteI32(code, DigitsNegative);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Or));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        if (domain.IsSigned)
            EmitCheckedDigit(code, request, domain, negative: true);
        else
        {
            WriteI32(code, Overflow);
            Set(code, request.Output.StatusLocal);
        }
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        EmitCheckedDigit(code, request, domain, negative: false);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));

        Get(code, request.Output.StatusLocal);
        WriteI32(code, Overflow);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        Get(code, request.Output.StatusLocal);
        WriteI32(code, NoDigitsNegative);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        Get(code, request.Output.StatusLocal);
        WriteI32(code, DigitsNegative);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Or));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        WriteI32(code, DigitsNegative);
        Set(code, request.Output.StatusLocal);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        WriteI32(code, DigitsPositive);
        Set(code, request.Output.StatusLocal);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private static void EmitCheckedDigit(
        IWasmInstructionWriter code,
        EnumNumericParseEmissionRequest request,
        NumericDomain domain,
        bool negative)
    {
        var limit = negative ? domain.NegativeLimit : domain.PositiveLimit;
        var quotient = limit / 10;
        var remainder = limit % 10;

        Get(code, request.Output.ValueLocal);
        WriteIntegerConstant(code, domain, quotient);
        code.Write(WasmInstruction.NoOperand(
            domain.IsWide
                ? WasmOpcodes.I64GreaterThanUnsigned
                : WasmOpcodes.I32GreaterThanUnsigned));
        Get(code, request.Output.ValueLocal);
        WriteIntegerConstant(code, domain, quotient);
        code.Write(WasmInstruction.NoOperand(
            domain.IsWide ? WasmOpcodes.I64Equal : WasmOpcodes.I32Equal));
        Get(code, request.DigitLocal);
        if (domain.IsWide)
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
        WriteIntegerConstant(code, domain, remainder);
        code.Write(WasmInstruction.NoOperand(
            domain.IsWide
                ? WasmOpcodes.I64GreaterThanUnsigned
                : WasmOpcodes.I32GreaterThanUnsigned));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32And));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Or));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        WriteI32(code, Overflow);
        Set(code, request.Output.StatusLocal);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        Get(code, request.Output.ValueLocal);
        WriteIntegerConstant(code, domain, 10);
        code.Write(WasmInstruction.NoOperand(
            domain.IsWide ? WasmOpcodes.I64Multiply : WasmOpcodes.I32Multiply));
        Get(code, request.DigitLocal);
        if (domain.IsWide)
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
        code.Write(WasmInstruction.NoOperand(
            domain.IsWide ? WasmOpcodes.I64Add : WasmOpcodes.I32Add));
        Set(code, request.Output.ValueLocal);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private static void EmitWhitespacePredicate(IWasmInstructionWriter code, int digit)
    {
        EmitRange(code, digit, 0x0009, 0x000E);
        EmitEquals(code, digit, 0x0020, WasmOpcodes.I32Or);
        EmitEquals(code, digit, 0x0085, WasmOpcodes.I32Or);
        EmitEquals(code, digit, 0x00A0, WasmOpcodes.I32Or);
        EmitEquals(code, digit, 0x1680, WasmOpcodes.I32Or);
        EmitRange(code, digit, 0x2000, 0x200B, WasmOpcodes.I32Or);
        EmitEquals(code, digit, 0x2028, WasmOpcodes.I32Or);
        EmitEquals(code, digit, 0x2029, WasmOpcodes.I32Or);
        EmitEquals(code, digit, 0x202F, WasmOpcodes.I32Or);
        EmitEquals(code, digit, 0x205F, WasmOpcodes.I32Or);
        EmitEquals(code, digit, 0x3000, WasmOpcodes.I32Or);
    }

    private static void EmitWhitespace(
        IWasmInstructionWriter code,
        EnumNumericParseEmissionRequest request)
    {
        Get(code, request.Output.StatusLocal);
        WriteI32(code, DigitsPositive);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        WriteI32(code, TrailingWhitespacePositive);
        Set(code, request.Output.StatusLocal);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        Get(code, request.Output.StatusLocal);
        WriteI32(code, DigitsNegative);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        Get(code, request.Output.StatusLocal);
        WriteI32(code, TrailingWhitespaceNegative);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Or));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        WriteI32(code, TrailingWhitespaceNegative);
        Set(code, request.Output.StatusLocal);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));

        Get(code, request.Output.StatusLocal);
        WriteI32(code, NoDigitsNegative);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        WriteI32(code, Invalid);
        Set(code, request.Output.StatusLocal);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));

        Get(code, request.Output.StatusLocal);
        WriteI32(code, NoDigitsPositiveSign);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        WriteI32(code, Invalid);
        Set(code, request.Output.StatusLocal);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private static void EmitSign(
        IWasmInstructionWriter code,
        EnumNumericParseEmissionRequest request)
    {
        Get(code, request.Output.StatusLocal);
        WriteI32(code, NoDigitsPositive);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        Get(code, request.DigitLocal);
        WriteI32(code, '-');
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        WriteI32(code, NoDigitsNegative);
        Set(code, request.Output.StatusLocal);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        WriteI32(code, NoDigitsPositiveSign);
        Set(code, request.Output.StatusLocal);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        WriteI32(code, Invalid);
        Set(code, request.Output.StatusLocal);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private static void EmitCompletion(
        IWasmInstructionWriter code,
        EnumNumericParseEmissionRequest request,
        NumericDomain domain)
    {
        Get(code, request.Output.StatusLocal);
        WriteI32(code, DigitsNegative);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        Get(code, request.Output.StatusLocal);
        WriteI32(code, TrailingWhitespaceNegative);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Or));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        WriteIntegerConstant(code, domain, 0);
        Get(code, request.Output.ValueLocal);
        code.Write(WasmInstruction.NoOperand(
            domain.IsWide ? WasmOpcodes.I64Subtract : WasmOpcodes.I32Subtract));
        Set(code, request.Output.ValueLocal);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));

        Get(code, request.Output.StatusLocal);
        WriteI32(code, DigitsPositive);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        Get(code, request.Output.StatusLocal);
        WriteI32(code, DigitsNegative);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Or));
        Get(code, request.Output.StatusLocal);
        WriteI32(code, TrailingWhitespacePositive);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Or));
        Get(code, request.Output.StatusLocal);
        WriteI32(code, TrailingWhitespaceNegative);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Or));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        WriteI32(code, (int)EnumNumericParseStatus.Success);
        Set(code, request.Output.StatusLocal);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        WriteIntegerConstant(code, domain, 0);
        Set(code, request.Output.ValueLocal);

        Get(code, request.Output.StatusLocal);
        WriteI32(code, Nonnumeric);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        WriteI32(code, (int)EnumNumericParseStatus.NonNumeric);
        Set(code, request.Output.StatusLocal);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        Get(code, request.Output.StatusLocal);
        WriteI32(code, Overflow);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        WriteI32(code, (int)EnumNumericParseStatus.Overflow);
        Set(code, request.Output.StatusLocal);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        WriteI32(code, (int)EnumNumericParseStatus.Invalid);
        Set(code, request.Output.StatusLocal);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitCharacter(
        IWasmInstructionWriter code,
        EnumNumericParseEmissionRequest request)
    {
        Get(code, request.TextLocal);
        if (request.IsArray)
        {
            ManagedMemoryEmitter.EmitReferenceLoad(
                code,
                Layouts.Target,
                Objects.ArrayDataPointerOffset);
        }
        Get(code, request.IndexLocal);
        if (Layouts.Target.UsesMemory64)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I64Constant,
                WasmInstructionOperand.Signed64(sizeof(char))));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Multiply));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Add));
        }
        else
        {
            WriteI32(code, sizeof(char));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Multiply));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Add));
        }
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Load16Unsigned,
            WasmInstructionOperand.Memory(
                1,
                request.IsArray ? 0u : (uint)Objects.StringDataOffset)));
    }

    private static NumericDomain ResolveDomain(CliTypeIdentity type) =>
        type.CanonicalName switch
        {
            "primitive:i1" => new(true, false, 127, 128),
            "primitive:u1" => new(false, false, 255, 0),
            "primitive:i2" => new(true, false, 32767, 32768),
            "primitive:u2" or "primitive:char" =>
                new(false, false, 65535, 0),
            "primitive:i4" => new(true, false, 2147483647, 2147483648),
            "primitive:u4" => new(false, false, uint.MaxValue, 0),
            "primitive:i8" => new(true, true, 9223372036854775807, 9223372036854775808),
            "primitive:u8" => new(false, true, ulong.MaxValue, 0),
            _ => throw new ArgumentException(
                $"Unsupported enum underlying type '{type.CanonicalName}'.",
                nameof(type)),
        };

    private static void ValidateLocals(EnumNumericParseEmissionRequest request)
    {
        var locals = new[]
        {
            request.TextLocal,
            request.Output.ValueLocal,
            request.Output.StatusLocal,
            request.IndexLocal,
            request.DigitLocal,
        };
        foreach (var local in locals)
            ArgumentOutOfRangeException.ThrowIfNegative(local);
        for (var index = 0; index < locals.Length; index++)
        {
            for (var next = index + 1; next < locals.Length; next++)
            {
                if (locals[index] == locals[next])
                    throw new ArgumentException(
                        $"Enum numeric parser locals must be distinct: {string.Join(',', locals)}.",
                        nameof(request));
            }
        }
    }

    private static void WriteIntegerConstant(IWasmInstructionWriter code, NumericDomain domain, ulong value)
    {
        code.Write(WasmInstruction.WithOperand(
            domain.IsWide ? WasmOpcodes.I64Constant : WasmOpcodes.I32Constant,
            domain.IsWide
                ? WasmInstructionOperand.Signed64(unchecked((long)value))
                : WasmInstructionOperand.Signed(unchecked((int)value))));
    }

    private static void WriteI32(IWasmInstructionWriter code, int value) => code.Write(
        WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(value)));

    private static void Get(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)local)));

    private static void Set(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)local)));

    private static void EmitEquals(
        IWasmInstructionWriter code,
        int local,
        int value,
        byte combine)
    {
        Get(code, local);
        WriteI32(code, value);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        code.Write(WasmInstruction.NoOperand(combine));
    }

    private static void EmitRange(
        IWasmInstructionWriter code,
        int local,
        int lower,
        int upper,
        byte combine = 0)
    {
        Get(code, local);
        WriteI32(code, lower);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32GreaterThanOrEqualUnsigned));
        Get(code, local);
        WriteI32(code, upper);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32LessThanUnsigned));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32And));
        if (combine != 0)
            code.Write(WasmInstruction.NoOperand(combine));
    }

    private readonly record struct NumericDomain(
        bool IsSigned,
        bool IsWide,
        ulong PositiveLimit,
        ulong NegativeLimit);
}
